using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using xKsef.Application.Gus;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Gus;
using xKsef.Tests.Integration;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace xKsef.GusBir.ManualTests;

public sealed class GusBirLoginOnlyLiveTests(ITestOutputHelper output)
{
    [ManualGusBirLoginOnlyFact]
    [Trait("Category", "ManualGusBirLive")]
    public async Task TestEnvironment_LoginOnly()
    {
        var guard = new GusBirLoginOnlyGuard();
        SqlServerTestDatabase? database = null;
        ServiceProvider? provider = null;
        var loginSucceeded = false;
        var cleanupSucceeded = true;
        GusBirError? error = null;
        GusBirDiagnostics? diagnostics = null;
        try
        {
            if (!GusBirLoginOnlyOptIn.IsEnabled()) throw new InvalidOperationException("Login-only live test requires explicit opt-in.");

            database = new SqlServerTestDatabase();
            await database.InitializeAsync();
            var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.GusBir.json", optional: false)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = database.InstanceConnectionString
                }).Build();

            var services = new ServiceCollection();
            services.AddLogging(logging => logging.ClearProviders());
            services.AddGusBirInfrastructure(configuration);
            services.AddDatabaseInfrastructure(configuration);
            services.AddHttpClient(GusBirServiceCollectionExtensions.HttpClientName).AddHttpMessageHandler(() => guard);
            provider = services.BuildServiceProvider(validateScopes: true);

            var options = provider.GetRequiredService<IOptions<GusBirOptions>>().Value;
            if (options.Environment != GusBirEnvironment.Test || options.Endpoint.AbsoluteUri != GusBirOptions.TestServiceUrl || options.EffectiveApiKey != GusBirOptions.PublicTestApiKey)
                throw new InvalidOperationException("Only the official TEST endpoint and public TEST key are permitted.");

            var client = provider.GetRequiredService<IGusBirClient>();
            using var timeout = new CancellationTokenSource(GusWaitBudget.DefaultLimit + TimeSpan.FromSeconds(options.TimeoutSeconds));
            await client.LoginAsync(timeout.Token);
            // A fresh real client returns from LoginAsync only after validating and storing the SID internally.
            loginSucceeded = true;
        }
        catch (GusBirException exception)
        {
            error = exception.Error;
            diagnostics = exception.Diagnostics;
        }
        catch
        {
            // Setup, cancellation and guard failures must never expose original messages or exceptions to the runner.
        }
        finally
        {
            // Never call LogoutAsync: closing local resources must not send another HTTP request.
            try { if (provider is not null) await provider.DisposeAsync(); }
            catch { cleanupSucceeded = false; }
            try { guard.Dispose(); }
            catch { cleanupSucceeded = false; }
            try { if (database is not null) await database.DisposeAsync(); }
            catch { cleanupSucceeded = false; }

            GusBirLoginOnlyReport.Write(output, loginSucceeded, error, diagnostics, guard.Requests);
        }

        if (!loginSucceeded || guard.Requests != 1 || !cleanupSucceeded)
            throw new XunitException("Login-only GUS TEST did not complete successfully. See the controlled result fields.");
    }
}

public sealed class ManualGusBirLoginOnlyFactAttribute : FactAttribute
{
    public ManualGusBirLoginOnlyFactAttribute()
    {
        if (!GusBirLoginOnlyOptIn.IsEnabled())
            Skip = "Manual login-only TEST: explicitly select GusBir.LoginOnly.runsettings and TestEnvironment_LoginOnly.";
    }
}

internal static class GusBirLoginOnlyOptIn
{
    internal const string Variable = "XKSEF_RUN_GUS_BIR_LOGIN_ONLY_TEST";
    internal static bool IsEnabled() => Environment.GetEnvironmentVariable(Variable) == "1";
}

internal sealed class GusBirLoginOnlyGuard(Func<bool>? isEnabled = null) : DelegatingHandler
{
    private readonly Func<bool> _isEnabled = isEnabled ?? GusBirLoginOnlyOptIn.IsEnabled;
    private int _requests;
    internal int Requests => Volatile.Read(ref _requests);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_isEnabled()) throw new InvalidOperationException("Login-only live test requires explicit opt-in.");
        cancellationToken.ThrowIfCancellationRequested();
        var contentType = request.Content?.Headers.ContentType;
        var actions = contentType?.Parameters.Where(parameter => parameter.Name.Equals("action", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (request.Method != HttpMethod.Post || request.RequestUri?.AbsoluteUri != GusBirOptions.TestServiceUrl || request.Headers.Contains("sid") ||
            contentType?.MediaType != "application/soap+xml" || actions is not { Length: 1 } ||
            actions[0].Value?.Trim('"') != "http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/Zaloguj")
            throw new InvalidOperationException("Only a TEST login SOAP request without a session header is permitted.");
        if (Interlocked.CompareExchange(ref _requests, 1, 0) != 0)
            throw new InvalidOperationException("The single HTTP request budget has already been consumed.");
        return base.SendAsync(request, cancellationToken);
    }
}

internal static class GusBirLoginOnlyReport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    internal static void Write(ITestOutputHelper output, bool succeeded, GusBirError? error, GusBirDiagnostics? diagnostics, int requests)
    {
        output.WriteLine("Logowanie: " + (succeeded ? "powodzenie" : "niepowodzenie"));
        output.WriteLine("GusBirError: " + (error?.ToString() ?? "nie dotyczy"));
        output.WriteLine("Diagnostics: " + (diagnostics is null ? "brak" : JsonSerializer.Serialize(diagnostics, JsonOptions)));
        output.WriteLine("Liczba żądań HTTP: " + requests.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("Uzyskano SID: " + (succeeded ? "tak" : "nie"));
    }
}
