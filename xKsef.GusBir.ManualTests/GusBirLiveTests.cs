using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using xKsef.Application.Contractors;
using xKsef.Application.Gus;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Gus;
using xKsef.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;
using Microsoft.EntityFrameworkCore;
using xKsef.Tests.Integration;

namespace xKsef.GusBir.ManualTests;

/// <summary>
/// Live HTTP is opt-in. Run only this test with GusBir.Test.runsettings explicitly selected.
/// Uses the real client and TEST transport with an owned isolated LocalDB database for quota permits.
/// Never starts xKsef.Api or uses the application database/configuration.
/// </summary>
public sealed class GusBirLiveTests(ITestOutputHelper output) : IAsyncLifetime
{
    private SqlServerTestDatabase? _database;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("XKSEF_RUN_GUS_BIR_LIVE_TEST") != "1") return;
        var database = new SqlServerTestDatabase();
        await database.InitializeAsync();
        _database = database;
    }

    public Task DisposeAsync() => _database?.DisposeAsync() ?? Task.CompletedTask;

    // Official GUS BIR 1.2 documentation, package 1.4:
    // https://api.stat.gov.pl/Content/files/regon/GUS-Regon-UslugaBIRver1.2-dokumentacjaVer1.4.zip
    // BIR12_Przyklady.pdf, section B, DaneSzukajPodmioty request.
    private const string DocumentedRegon = "000331501";
    // Used in the documentation's error example, but the live TEST registry contains this NIP.
    // The example does not guarantee that an identifier remains absent from the registry.
    private const string DocumentedNip = "9999999999";


    [ManualGusBirFact]
    [Trait("Category", "ManualGusBirLive")]
    public async Task TestEnvironment_LoginSearchMapMissingIdentifierAndLogout()
    {
        var database = _database ?? throw new InvalidOperationException("Manual test requires its own isolated LocalDB fixture.");
        var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.GusBir.json", optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.InstanceConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddGusBirInfrastructure(configuration);
        services.AddDatabaseInfrastructure(configuration);

        var guard = new TestEndpointGuard();
        services.AddHttpClient(GusBirServiceCollectionExtensions.HttpClientName).AddHttpMessageHandler(() => guard);

        await using var provider = services.BuildServiceProvider(validateScopes: true);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!db.Database.IsSqlServer())
                throw new InvalidOperationException("Manual test requires isolated SQL Server LocalDB.");
            await SqlServerTestDatabase.VerifyAsync(db.Database.GetConnectionString()!);
        }

        var options = provider.GetRequiredService<IOptions<GusBirOptions>>().Value;

        if (options.Environment != GusBirEnvironment.Test || options.Endpoint.AbsoluteUri != GusBirOptions.TestServiceUrl || options.EffectiveApiKey != GusBirOptions.PublicTestApiKey)
            throw new InvalidOperationException("Manual GUS test requires the official TEST endpoint and public TEST key. No requests sent.");

        Assert.Equal(DocumentedRegon, ContractorIdentifierValidator.NormalizeAndValidateRegon(DocumentedRegon));
        Assert.Equal(DocumentedNip, ContractorIdentifierValidator.NormalizeAndValidateNip(DocumentedNip));

        var client = provider.GetRequiredService<IGusBirClient>();

        output.WriteLine("Środowisko: TEST; adres: " + options.Endpoint.AbsoluteUri);
        output.WriteLine("Baza SQL: izolowana LocalDB / " + database.Name);
        output.WriteLine("Dane wejściowe pochodzą z oficjalnych przykładów GUS BIR 1.2, dokumentacja 1.4.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var loggedIn = false;
        Exception? failure = null;

        try
        {
            await client.LoginAsync(timeout.Token);
            loggedIn = true;
            output.WriteLine("Zaloguj: OK. SID pozostaje wewnątrz klienta i nie jest wyświetlany.");

            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);

            var entities = await client.SearchByRegonAsync(DocumentedRegon, timeout.Token);
            WriteResult("REGON " + DocumentedRegon, entities);

            Assert.NotEmpty(entities);
            Assert.Contains(entities, entity => entity.Regon == DocumentedRegon);
            Assert.All(entities, entity => Assert.False(string.IsNullOrWhiteSpace(entity.FullName)));

            output.WriteLine("Mapowanie SOAP/MTOM → GusEntity: OK.");

            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);

            var nipEntities = await client.SearchByNipAsync(DocumentedNip, timeout.Token);
            WriteResult("NIP " + DocumentedNip, nipEntities);

            Assert.NotEmpty(nipEntities);

            Assert.All(nipEntities, entity =>
            {
                Assert.Equal(DocumentedNip, entity.Nip);
                Assert.False(string.IsNullOrWhiteSpace(entity.FullName));
                Assert.Contains(entity.Regon.Length, new[] { 9, 14 });
                Assert.All(entity.Regon, digit => Assert.InRange(digit, '0', '9'));
                Assert.NotNull(entity.Address);
                //Assert.Contains(new[] { entity.Address.City, entity.Address.Street, entity.Address.BuildingNumber, entity.Address.PostalCode }, value => !string.IsNullOrWhiteSpace(value));
            });

            output.WriteLine("Wyszukiwanie po NIP i mapowanie danych podmiotu: OK.");
        }
        catch (Exception exception)
        {
            failure = exception;
            WriteFailure(exception);
            throw;
        }
        finally
        {
            if (loggedIn)
            {
                try
                {
                    using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds + 2));

                    await Task.Delay(TimeSpan.FromSeconds(1), cleanupTimeout.Token);
                    await client.LogoutAsync(cleanupTimeout.Token);

                    output.WriteLine("Wyloguj: OK.");
                }
                catch (Exception exception)
                {
                    WriteFailure(exception);
                    if (failure is null) throw;
                }
            }

            output.WriteLine("Liczba żądań HTTP: " + guard.Requests);
        }
    }


    private void WriteResult(string criterion, IReadOnlyList<GusEntity> entities)
    {
        output.WriteLine(criterion + ": znaleziono=" + (entities.Count > 0) + ", liczba=" + entities.Count);
        // Only the SOAP-independent result model is serialized; it contains neither API key nor SID.
        output.WriteLine(JsonSerializer.Serialize(entities, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void WriteFailure(Exception exception)
    {
        if (exception is GusBirException gus)
            output.WriteLine($"Błąd GUS: operacja={gus.Operation}, kategoria={gus.Error}, kod={gus.ServiceCode}.");
        else
            output.WriteLine("Błąd testu: " + exception.GetType().Name + ".");
    }

    private sealed class TestEndpointGuard : DelegatingHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Environment.GetEnvironmentVariable("XKSEF_RUN_GUS_BIR_LIVE_TEST") != "1")
                throw new InvalidOperationException("Live GUS requests require explicit manual opt-in.");
            if (request.Method != HttpMethod.Post || request.RequestUri?.AbsoluteUri != GusBirOptions.TestServiceUrl)
                throw new InvalidOperationException("Only SOAP POST to the official GUS TEST endpoint is permitted.");
            if (++Requests > 5)
                throw new InvalidOperationException("Manual test HTTP request budget exceeded.");
            return base.SendAsync(request, cancellationToken);
        }
    }
}

public sealed class ManualGusBirFactAttribute : FactAttribute
{
    public ManualGusBirFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("XKSEF_RUN_GUS_BIR_LIVE_TEST") != "1")
            Skip = "Manual TEST only: explicitly select GusBir.Test.runsettings and this test. No network calls in the default run.";
    }
}
