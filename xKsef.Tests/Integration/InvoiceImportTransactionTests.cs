using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using xKsef.Application.Exceptions;
using xKsef.Application.Invoices;
using xKsef.Infrastructure.Invoices;
using xKsef.Tests.Infrastructure;
using xKsef.Domain.Invoices;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

[Collection(LocalDbIntegrationCollection.Name)]
public sealed class InvoiceImportTransactionTests
{
    [Fact]
    public async Task ExecuteAsync_WhenOperationThrows_RollsBackTransaction()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Brak connection stringa DefaultConnection.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var testTenant = await TestTenant.CreateAsync(options);

        string ksefNumber = $"TEST-ROLLBACK-{Guid.NewGuid():N}";

        await using var dbContext = new AppDbContext(options);

        var transaction = new InvoiceImportTransaction(dbContext);

        var invoice = new StoredInvoice
        {
            TenantId = testTenant.TenantId,
            KsefNumber = ksefNumber,
            InvoiceNumber = "TEST/ROLLBACK/001",
            IssueDate = DateOnly.FromDateTime(DateTime.Today),
            Direction = InvoiceDirection.Purchase,
            GrossAmount = 123.45m,
            Currency = "PLN",
            SellerName = "Testowy sprzedawca",
            SellerNip = "1234567890",
            ImportedAt = DateTimeOffset.UtcNow,
            XmlBytes = System.Text.Encoding.UTF8.GetBytes(
                "<Invoice>Test</Invoice>")
        };

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
            {
                await transaction.ExecuteAsync(
                    async ct =>
                    {
                        dbContext.Invoices.Add(invoice);

                        await dbContext.SaveChangesAsync(ct);

                        throw new InvalidOperationException(
                            "Symulowany błąd importu.");
                    });
            });

        // Assert
        await using var verificationContext = new AppDbContext(options);

        bool exists = await verificationContext.Invoices
            .AnyAsync(x => x.KsefNumber == ksefNumber);

        Assert.False(exists);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationSucceeds_CommitsTransaction()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Brak connection stringa DefaultConnection.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var testTenant = await TestTenant.CreateAsync(options);

        string ksefNumber = $"TEST-COMMIT-{Guid.NewGuid():N}";

        await using var dbContext = new AppDbContext(options);

        var transaction = new InvoiceImportTransaction(dbContext);

        var invoice = new StoredInvoice
        {
            TenantId = testTenant.TenantId,
            KsefNumber = ksefNumber,
            InvoiceNumber = "TEST/COMMIT/001",
            IssueDate = DateOnly.FromDateTime(DateTime.Today),
            Direction = InvoiceDirection.Purchase,
            GrossAmount = 123.45m,
            Currency = "PLN",
            SellerName = "Testowy sprzedawca",
            SellerNip = "1234567890",
            ImportedAt = DateTimeOffset.UtcNow,
            XmlBytes = System.Text.Encoding.UTF8.GetBytes(
                "<Invoice>Test</Invoice>")
        };

        try
        {
            // Act
            await transaction.ExecuteAsync(async ct =>
            {
                dbContext.Invoices.Add(invoice);
                await dbContext.SaveChangesAsync(ct);
            });

            // Assert
            await using var verificationContext = new AppDbContext(options);

            bool exists = await verificationContext.Invoices
                .AnyAsync(x => x.KsefNumber == ksefNumber);

            Assert.True(exists);
        }
        finally
        {
            // Cleanup - usuwamy wyłącznie fakturę testową
            await using var cleanupContext = new AppDbContext(options);

            var savedInvoice = await cleanupContext.Invoices
                .SingleOrDefaultAsync(x => x.KsefNumber == ksefNumber);

            if (savedInvoice is not null)
            {
                cleanupContext.Invoices.Remove(savedInvoice);
                await cleanupContext.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenDuplicateOccurs_SkipsDuplicateAndCommits()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Brak connection stringa DefaultConnection.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var testTenant = await TestTenant.CreateAsync(options);

        string ksefNumberA = $"TEST-DUP-A-{Guid.NewGuid():N}";
        string ksefNumberB = $"TEST-DUP-B-{Guid.NewGuid():N}";

        StoredInvoice CreateInvoice(string ksefNumber) => new()
        {
            TenantId = testTenant.TenantId,
            KsefNumber = ksefNumber,
            InvoiceNumber = "TEST/DUP/001",
            IssueDate = DateOnly.FromDateTime(DateTime.Today),
            Direction = InvoiceDirection.Purchase,
            GrossAmount = 123.45m,
            Currency = "PLN",
            SellerName = "Testowy sprzedawca",
            SellerNip = "1234567890",
            ImportedAt = DateTimeOffset.UtcNow,
            XmlBytes = System.Text.Encoding.UTF8.GetBytes(
                "<Invoice>Test</Invoice>")
        };

        try
        {
            await using var dbContext = new AppDbContext(options);

            var repository = new InvoiceRepository(dbContext);
            var transaction = new InvoiceImportTransaction(dbContext);

            int skippedCount = 0;

            // Act
            await transaction.ExecuteAsync(async ct =>
            {
                await repository.SaveAsync(CreateInvoice(ksefNumberA), ct);

                try
                {
                    await repository.SaveAsync(CreateInvoice(ksefNumberA), ct);
                }
                catch (DuplicateInvoiceException)
                {
                    skippedCount++;
                }

                await repository.SaveAsync(CreateInvoice(ksefNumberB), ct);
            });

            // Assert
            await using var verificationContext = new AppDbContext(options);

            int count = await verificationContext.Invoices.CountAsync(
                x => x.KsefNumber == ksefNumberA ||
                     x.KsefNumber == ksefNumberB);

            Assert.Equal(2, count);
            Assert.Equal(1, skippedCount);
        }
        finally
        {
            // Cleanup
            await using var cleanupContext = new AppDbContext(options);

            var invoices = await cleanupContext.Invoices
                .Where(x => x.KsefNumber == ksefNumberA ||
                            x.KsefNumber == ksefNumberB)
                .ToListAsync();

            cleanupContext.Invoices.RemoveRange(invoices);
            await cleanupContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenSecondInvoiceFails_RollsBackAllInvoices()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Brak connection stringa DefaultConnection.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var testTenant = await TestTenant.CreateAsync(options);

        string ksefNumberA = $"TEST-ROLLBACK-A-{Guid.NewGuid():N}";
        string ksefNumberB = $"TEST-ROLLBACK-B-{Guid.NewGuid():N}";

        StoredInvoice CreateInvoice(string number) => new()
        {
            TenantId = testTenant.TenantId,
            KsefNumber = number,
            InvoiceNumber = "TEST/ROLLBACK/001",
            IssueDate = DateOnly.FromDateTime(DateTime.Today),
            Direction = InvoiceDirection.Purchase,
            GrossAmount = 123.45m,
            Currency = "PLN",
            SellerName = "Testowy sprzedawca",
            SellerNip = "1234567890",
            ImportedAt = DateTimeOffset.UtcNow,
            XmlBytes = System.Text.Encoding.UTF8.GetBytes(
                "<Invoice>Test</Invoice>")
        };

        await using var dbContext = new AppDbContext(options);

        var repository = new InvoiceRepository(dbContext);
        var transaction = new InvoiceImportTransaction(dbContext);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transaction.ExecuteAsync(async ct =>
            {
                await repository.SaveAsync(
                    CreateInvoice(ksefNumberA), ct);

                await repository.SaveAsync(
                    CreateInvoice(ksefNumberB), ct);

                throw new InvalidOperationException(
                    "Symulowany błąd podczas importu paczki.");
            }));

        // Assert
        await using var verificationContext = new AppDbContext(options);

        int count = await verificationContext.Invoices.CountAsync(
            x => x.KsefNumber == ksefNumberA ||
                 x.KsefNumber == ksefNumberB);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTwoImportsRun_ExecutesThemSequentially()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Brak connection stringa DefaultConnection.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var contextA = new AppDbContext(options);
        await using var contextB = new AppDbContext(options);

        var transactionA = new InvoiceImportTransaction(contextA);
        var transactionB = new InvoiceImportTransaction(contextB);

        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var secondStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var taskA = transactionA.ExecuteAsync(async ct =>
        {
            firstStarted.SetResult();

            await releaseFirst.Task.WaitAsync(ct);
        });

        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var taskB = transactionB.ExecuteAsync(ct =>
        {
            secondStarted.SetResult();
            return Task.CompletedTask;
        });

        try
        {
            // Drugi import nie powinien wejść do operacji,
            // dopóki pierwszy utrzymuje blokadę.
            await Assert.ThrowsAsync<TimeoutException>(
                () => secondStarted.Task.WaitAsync(
                    TimeSpan.FromMilliseconds(500)));
        }
        finally
        {
            // Zwalniamy pierwszy import nawet po nieudanej asercji.
            releaseFirst.TrySetResult();
        }

        await Task.WhenAll(taskA, taskB);

        // Assert
        Assert.True(secondStarted.Task.IsCompletedSuccessfully);
    }
    [Fact]
    public async Task ExecuteAsync_WhenLockTimeoutExpires_ThrowsException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Brak connection stringa DefaultConnection.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var contextA = new AppDbContext(options);
        await using var contextB = new AppDbContext(options);

        var transactionA = new InvoiceImportTransaction(contextA);

        var transactionB = new InvoiceImportTransaction(
            contextB,
            lockTimeoutMilliseconds: 1000);

        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        bool secondOperationExecuted = false;

        // Act
        var taskA = transactionA.ExecuteAsync(async ct =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task.WaitAsync(ct);
        });

        try
        {
            // Czekamy, aż pierwszy import uzyska blokadę.
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Drugi import powinien przekroczyć timeout blokady.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => transactionB.ExecuteAsync(ct =>
                {
                    secondOperationExecuted = true;
                    return Task.CompletedTask;
                }));

            // Assert
            Assert.Contains("Kod: -1", exception.Message);
            Assert.False(secondOperationExecuted);
        }
        finally
        {
            // Zawsze zwalniamy pierwszą transakcję.
            releaseFirst.TrySetResult();
            await taskA;
        }
    }
    [Fact]
    public async Task ExecuteAsync_WhenFirstImportFails_ReleasesLock()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Brak connection stringa DefaultConnection.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var contextA = new AppDbContext(options);
        await using var contextB = new AppDbContext(options);

        var transactionA = new InvoiceImportTransaction(contextA);

        var transactionB = new InvoiceImportTransaction(
            contextB,
            lockTimeoutMilliseconds: 3000);

        bool secondOperationExecuted = false;

        // Act - pierwszy import kończy się błędem
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transactionA.ExecuteAsync(ct =>
            {
                throw new InvalidOperationException(
                    "Symulowany błąd pierwszego importu.");
            }));

        // Drugi import próbuje uzyskać tę samą blokadę
        await transactionB.ExecuteAsync(ct =>
        {
            secondOperationExecuted = true;
            return Task.CompletedTask;
        });

        // Assert
        Assert.True(secondOperationExecuted);
    }

    [Theory]
    [InlineData("PLN", false, false)]
    [InlineData("PLN", false, true)]
    [InlineData("EUR", false, false)]
    [InlineData("EUR", false, true)]
    [InlineData("USD", true, false)]
    [InlineData("USD", true, true)]
    public async Task DirectoryImportPersistsXmlAndOptionalSdkMetadata(string currency, bool correction, bool withSdkMetadata)
    {
        var options = CreateImportTestOptions();
        await using var tenant = await TestTenant.CreateAsync(options);
        string number = "TEST-METADATA-" + Guid.NewGuid().ToString("N");
        string directory = Path.Combine(Path.GetTempPath(), "xksef-import-" + Guid.NewGuid().ToString("N"));
        byte[] xml = InvoiceImportTestData.Xml(currency, correction);
        decimal? vatAmountPln = withSdkMetadata ? (correction ? -99.12m : 99.12m) : null;
        DateTimeOffset? invoicingDate = withSdkMetadata ? InvoiceImportTestData.InvoicingDate : null;
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, number + ".xml"), xml);
            await File.WriteAllTextAsync(Path.Combine(directory, "_metadata.json"), InvoiceImportTestData.Metadata(number, xml, vatAmountPln, invoicingDate));
            await using var context = new AppDbContext(options);
            var importer = new InvoiceImportService(new InvoiceRepository(context), new KsefInvoiceMetadataParser());
            var packageImporter = new InvoicePackageImportService(importer);
            var transaction = new InvoiceImportTransaction(context);
            InvoiceImportResult? result = null;
            await transaction.ExecuteAsync(async ct => result = await packageImporter.ImportFromDirectoryAsync(tenant.TenantId, directory, InvoiceDirection.Purchase, ct));

            Assert.Equal(1, result!.ImportedCount);
            Assert.Equal(0, result.SkippedCount);
            await using var verification = new AppDbContext(options);
            var saved = await verification.Invoices.AsNoTracking().SingleAsync(x => x.TenantId == tenant.TenantId && x.KsefNumber == number);
            InvoiceImportTestData.AssertValues(saved, xml, currency, correction, vatAmountPln, invoicingDate);
            Assert.Equal(InvoiceDirection.Purchase, saved.Direction);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ImporterSkipsDuplicatesWithoutOverwritingAndIsolatesTheSameNumberAcrossTenants()
    {
        var options = CreateImportTestOptions();
        await using var tenantA = await TestTenant.CreateAsync(options);
        await using var tenantB = await TestTenant.CreateAsync(options);
        string number = "TEST-METADATA-DUP-" + Guid.NewGuid().ToString("N");
        byte[] originalXml = InvoiceImportTestData.Xml("EUR");
        var original = new ValidatedInvoice(number, originalXml, 99.12m, InvoiceImportTestData.InvoicingDate);
        byte[] otherXml = InvoiceImportTestData.Xml("USD", true);
        var other = new ValidatedInvoice(number, otherXml, -200m, InvoiceImportTestData.InvoicingDate.AddDays(1));
        await using var context = new AppDbContext(options);
        var importer = new InvoiceImportService(new InvoiceRepository(context), new KsefInvoiceMetadataParser());
        var transaction = new InvoiceImportTransaction(context);
        await transaction.ExecuteAsync(async ct =>
        {
            await importer.ImportAsync(tenantA.TenantId, original, InvoiceDirection.Sales, ct);
            var result = await importer.ImportManyAsync(tenantA.TenantId, [other], InvoiceDirection.Purchase, ct);
            Assert.Equal(0, result.ImportedCount);
            Assert.Equal(1, result.SkippedCount);
            await importer.ImportAsync(tenantB.TenantId, other, InvoiceDirection.Purchase, ct);
        });

        await using var verification = new AppDbContext(options);
        var repository = new InvoiceRepository(verification);
        var first = await repository.GetByKsefNumberAsync(tenantA.TenantId, number);
        var second = await repository.GetByKsefNumberAsync(tenantB.TenantId, number);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(tenantA.TenantId, first.TenantId);
        Assert.Equal(tenantB.TenantId, second.TenantId);
        Assert.Equal(InvoiceDirection.Sales, first.Direction);
        Assert.Equal(InvoiceDirection.Purchase, second.Direction);
        InvoiceImportTestData.AssertValues(first, originalXml, "EUR", false, 99.12m, InvoiceImportTestData.InvoicingDate);
        InvoiceImportTestData.AssertValues(second, otherXml, "USD", true, -200m, InvoiceImportTestData.InvoicingDate.AddDays(1));
        Assert.False(await repository.ExistsAsync(Guid.NewGuid(), number));
        Assert.Null(await repository.GetByKsefNumberAsync(Guid.NewGuid(), number));
    }

    [Fact]
    public async Task ImporterRollsBackNewMetadataWhenALaterXmlCannotBeParsed()
    {
        var options = CreateImportTestOptions();
        await using var tenant = await TestTenant.CreateAsync(options);
        string number = "TEST-METADATA-ROLLBACK-" + Guid.NewGuid().ToString("N");
        var original = new ValidatedInvoice(number, InvoiceImportTestData.Xml(), 99.12m, InvoiceImportTestData.InvoicingDate);
        var invalid = new ValidatedInvoice(number + "-INVALID", System.Text.Encoding.UTF8.GetBytes("<Faktura/>"));
        await using var context = new AppDbContext(options);
        var importer = new InvoiceImportService(new InvoiceRepository(context), new KsefInvoiceMetadataParser());
        var transaction = new InvoiceImportTransaction(context);
        await Assert.ThrowsAsync<FormatException>(() => transaction.ExecuteAsync(ct => importer.ImportManyAsync(tenant.TenantId, [original, invalid], InvoiceDirection.Sales, ct)));

        await using var verification = new AppDbContext(options);
        Assert.False(await verification.Invoices.AnyAsync(x => x.TenantId == tenant.TenantId));
    }

    private static DbContextOptions<AppDbContext> CreateImportTestOptions()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Brak connection stringa DefaultConnection."))
            .Options;
    }

    private sealed class TestTenant : IAsyncDisposable
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly Guid _taxEntityId;

        private TestTenant(DbContextOptions<AppDbContext> options, Tenant tenant)
        {
            _options = options;
            TenantId = tenant.Id;
            _taxEntityId = tenant.TaxEntityId;
        }

        public Guid TenantId { get; }

        public static async Task<TestTenant> CreateAsync(DbContextOptions<AppDbContext> options)
        {
            var taxEntity = new TaxEntity
            {
                Id = Guid.NewGuid(),
                Nip = Random.Shared.NextInt64(1_000_000_000L, 10_000_000_000L).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = "InvoiceImportTransactionTests"
            };
            var tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = "InvoiceImportTransactionTests",
                TaxEntityId = taxEntity.Id,
                TaxEntity = taxEntity
            };

            // Persist the fixture before the transaction under test starts.
            await using var dbContext = new AppDbContext(options);
            dbContext.Tenants.Add(tenant);
            await dbContext.SaveChangesAsync();

            return new TestTenant(options, tenant);
        }

        public async ValueTask DisposeAsync()
        {
            // Use a fresh context, also when the test failed or rolled back.
            await using var dbContext = new AppDbContext(_options);
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            await dbContext.Invoices.Where(x => x.TenantId == TenantId).ExecuteDeleteAsync();
            await dbContext.Tenants.Where(x => x.Id == TenantId).ExecuteDeleteAsync();
            await dbContext.TaxEntities.Where(x => x.Id == _taxEntityId).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
    }
}