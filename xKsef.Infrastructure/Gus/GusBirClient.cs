using System.Globalization;
using System.Net;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.Contractors;
using xKsef.Application.Gus;

namespace xKsef.Infrastructure.Gus;

public sealed class GusBirClient : IGusBirClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GusBirOptions _options;
    private readonly TimeProvider _clock;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private string? _sid;
    private DateTimeOffset _loggedInAt;

    public GusBirClient(IHttpClientFactory httpClientFactory, IOptions<GusBirOptions> options, TimeProvider clock, IServiceScopeFactory scopeFactory)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _clock = clock;
        _scopeFactory = scopeFactory;
        var validation = new GusBirOptionsValidator().Validate(null, _options);
        if (validation.Failed)
            throw new OptionsValidationException(Options.DefaultName, typeof(GusBirOptions), validation.Failures);
    }

    public async Task LoginAsync(CancellationToken cancellationToken = default)
    {
        var budget = new GusWaitBudget(timeProvider: _clock);
        await WaitForSessionAsync(budget, cancellationToken);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await EnsureSessionAsync(budget, scope.ServiceProvider.GetRequiredService<IGusRateLimiter>(), cancellationToken);
        }
        finally { _sessionGate.Release(); }
    }

    public Task<IReadOnlyList<GusEntity>> SearchByNipAsync(string nip, CancellationToken cancellationToken = default) => SearchAsync("Nip", Required(ContractorIdentifierValidator.NormalizeAndValidateNip(nip)), cancellationToken);
    public Task<IReadOnlyList<GusEntity>> SearchByRegonAsync(string regon, CancellationToken cancellationToken = default) => SearchAsync("Regon", Required(ContractorIdentifierValidator.NormalizeAndValidateRegon(regon)), cancellationToken);
    public Task<IReadOnlyList<GusEntity>> SearchByKrsAsync(string krsNumber, CancellationToken cancellationToken = default) => SearchAsync("Krs", Required(ContractorIdentifierValidator.NormalizeAndValidateKrsNumber(krsNumber)), cancellationToken);

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var budget = new GusWaitBudget(timeProvider: _clock);
        await WaitForSessionAsync(budget, cancellationToken);
        try
        {
            if (_sid is null) return;
            await using var scope = _scopeFactory.CreateAsyncScope();
            var limiter = scope.ServiceProvider.GetRequiredService<IGusRateLimiter>();
            var sid = _sid;
            try
            {
                var result = await SendAsync("Wyloguj", new XElement(GusBirSoap.Service + "pIdentyfikatorSesji", sid), sid, budget, limiter, cancellationToken);
                if (result.Trim() is not ("true" or "1"))
                    throw new GusBirException(GusBirError.ServiceRejected, "Wyloguj");
            }
            finally { _sid = null; }
        }
        finally { _sessionGate.Release(); }
    }

    private async Task EnsureSessionAsync(GusWaitBudget budget, IGusRateLimiter limiter, CancellationToken cancellationToken)
    {
        if (_sid is not null && _clock.GetUtcNow() - _loggedInAt < TimeSpan.FromMinutes(55))
            return;
        _sid = null;
        var sid = await SendAsync("Zaloguj", new XElement(GusBirSoap.Service + "pKluczUzytkownika", _options.EffectiveApiKey), null, budget, limiter, cancellationToken);
        if (string.IsNullOrWhiteSpace(sid))
            throw new GusBirException(GusBirError.Authentication, "Zaloguj");
        if (sid.Length != 20 || sid.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new GusBirException(GusBirError.InvalidResponse, "Zaloguj");
        _sid = sid;
        _loggedInAt = _clock.GetUtcNow();
    }

    private async Task<IReadOnlyList<GusEntity>> SearchAsync(string field, string identifier, CancellationToken cancellationToken)
    {
        var budget = new GusWaitBudget(timeProvider: _clock);
        await WaitForSessionAsync(budget, cancellationToken);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var limiter = scope.ServiceProvider.GetRequiredService<IGusRateLimiter>();
            await EnsureSessionAsync(budget, limiter, cancellationToken);
            try
            {
                return await SearchOnceAsync(field, identifier, budget, limiter, cancellationToken);
            }
            catch (GusBirException exception) when (exception.Error == GusBirError.SessionExpired &&
                (exception.ServiceCode == 7 || exception.Operation == "GetValue" && exception.ServiceCode is null))
            {
                // The entire sequence stays under _sessionGate. No older operation can invalidate a new SID.
                // This catch wraps only the first search: rejection of the renewed SID is never retried.
                _sid = null;
                budget.ThrowIfExhausted(cancellationToken);
                await EnsureSessionAsync(budget, limiter, cancellationToken);
                return await SearchOnceAsync(field, identifier, budget, limiter, cancellationToken);
            }
        }
        catch (GusBirException exception) when (exception.Error is GusBirError.SessionExpired or GusBirError.Authentication)
        {
            _sid = null;
            throw;
        }
        catch (XmlException)
        {
            throw new GusBirException(GusBirError.InvalidResponse, "DaneSzukajPodmioty");
        }
        finally { _sessionGate.Release(); }
    }

    private async Task<IReadOnlyList<GusEntity>> SearchOnceAsync(string field, string identifier, GusWaitBudget budget, IGusRateLimiter limiter, CancellationToken cancellationToken)
    {
        var result = await SendAsync("DaneSzukajPodmioty", new XElement(GusBirSoap.Service + "pParametryWyszukiwania", new XElement(GusBirSoap.Data + field, identifier)), _sid, budget, limiter, cancellationToken);
        if (!string.IsNullOrWhiteSpace(result)) return ParseEntities(result);
        var diagnostic = await SendAsync("GetValue", new XElement(GusBirSoap.Diagnostic + "pNazwaParametru", "KomunikatKod"), _sid, budget, limiter, cancellationToken);
        // BIR 1.2 section 3.4 explicitly defines an empty KomunikatKod result and code 7 as no session.
        // Whitespace/malformed replies, authentication errors and generic SOAP faults are not proof of it.
        if (diagnostic.Length == 0)
            throw new GusBirException(GusBirError.SessionExpired, "GetValue");
        if (!int.TryParse(diagnostic.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var code))
            throw new GusBirException(GusBirError.InvalidResponse, "GetValue");
        CheckServiceCode(code, "GetValue");
        return [];
    }

    private async Task WaitForSessionAsync(GusWaitBudget budget, CancellationToken cancellationToken)
    {
        using var waiting = budget.BeginWaiting(cancellationToken);
        var acquired = false;
        try
        {
            await _sessionGate.WaitAsync(waiting.CancellationToken);
            acquired = true;
            waiting.ThrowIfCancellationRequested();
        }
        catch
        {
            // A deadline/cancellation may race successful acquisition. Never leave the gate held.
            if (acquired) _sessionGate.Release();
            waiting.ThrowIfCancellationRequested();
            throw;
        }
    }

    private static IReadOnlyList<GusEntity> ParseEntities(string xml)
    {
        const string operation = "DaneSzukajPodmioty";
        var root = GusBirSoap.Parse(xml).Root;
        if (root?.Name != "root" || root.Elements().Any(element => element.Name != "dane"))
            throw new GusBirException(GusBirError.InvalidResponse, operation);
        var result = new List<GusEntity>();
        foreach (var row in root.Elements("dane"))
        {
            if (Value(row, "ErrorCode") is { } error)
            {
                if (!int.TryParse(error, NumberStyles.None, CultureInfo.InvariantCulture, out var code))
                    throw new GusBirException(GusBirError.InvalidResponse, operation);
                CheckServiceCode(code);
                continue;
            }
            var regon = Value(row, "Regon");
            var name = Value(row, "Nazwa");
            if (regon is null || name is null)
                throw new GusBirException(GusBirError.InvalidResponse, operation);
            int? silo = null;
            if (Value(row, "SilosID") is { } siloText)
            {
                if (!int.TryParse(siloText, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                    throw new GusBirException(GusBirError.InvalidResponse, operation);
                silo = id;
            }
            result.Add(new GusEntity
            {
                Regon = regon, Nip = Value(row, "Nip"), FullName = name, NipStatus = Value(row, "StatusNip"),
                EntityType = Value(row, "Typ"), SiloId = silo,
                Address = new GusAddress
                {
                    Voivodeship = Value(row, "Wojewodztwo"), County = Value(row, "Powiat"), Municipality = Value(row, "Gmina"),
                    City = Value(row, "Miejscowosc"), PostalCode = Value(row, "KodPocztowy"), Street = Value(row, "Ulica"),
                    BuildingNumber = Value(row, "NrNieruchomosci"), ApartmentNumber = Value(row, "NrLokalu"), PostOfficeCity = Value(row, "MiejscowoscPoczty")
                }
            });
        }
        return result;
    }

    private static void CheckServiceCode(int code, string operation = "DaneSzukajPodmioty")
    {
        if (code is 0 or 4) return;
        throw new GusBirException(code == 7 ? GusBirError.SessionExpired : GusBirError.ServiceRejected, operation, code);
    }

    private async Task<string> SendAsync(string operation, XElement parameter, string? sid, GusWaitBudget budget, IGusRateLimiter limiter, CancellationToken cancellationToken)
    {
        using var http = _httpClientFactory.CreateClient(GusBirServiceCollectionExtensions.HttpClientName);
        using var request = GusBirSoap.CreateRequest(_options.Endpoint, operation, parameter, sid);
        var environment = _options.Environment == GusBirEnvironment.Test ? GusRateLimitEnvironment.Test : GusRateLimitEnvironment.Production;
        while (true)
        {
            budget.ThrowIfExhausted(cancellationToken);
            var permit = await limiter.AcquireAsync(environment, budget, cancellationToken);
            budget.ThrowIfExhausted(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            if (!permit.IsUsable || !permit.TryUse()) continue;
            // No await, serialization or client creation between consuming the permit and sending.
            return await SendHttpAsync(http, request, operation, timeout.Token, cancellationToken);
        }
    }

    private static async Task<string> SendHttpAsync(HttpClient http, HttpRequestMessage request, string operation, CancellationToken timeoutToken, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutToken);
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
                throw new GusBirException(GusBirError.Unavailable, operation);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new GusBirException(GusBirError.Authentication, operation);
            var result = await GusBirSoap.ReadResultAsync(response.Content, operation, timeoutToken);
            if (!response.IsSuccessStatusCode)
                throw new GusBirException(GusBirError.Communication, operation);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GusBirException(GusBirError.Timeout, operation);
        }
        catch (HttpRequestException)
        {
            throw new GusBirException(GusBirError.Communication, operation);
        }
        catch (XmlException)
        {
            throw new GusBirException(GusBirError.InvalidResponse, operation);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException or ArgumentException)
        {
            throw new GusBirException(GusBirError.InvalidResponse, operation);
        }
    }

    private static string Required(string? value) => value ?? throw new ArgumentException("Identyfikator wyszukiwania GUS jest wymagany.");
    private static string? Value(XElement row, string name) => ContractorIdentifierNormalizer.NormalizeOptional(row.Element(name)?.Value);
}
