using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using xKsef.Application.Exceptions;
using xKsef.Domain.Invoices;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

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

        string ksefNumber = $"TEST-ROLLBACK-{Guid.NewGuid():N}";

        await using var dbContext = new AppDbContext(options);

        var transaction = new InvoiceImportTransaction(dbContext);

        var invoice = new StoredInvoice
        {
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

        string ksefNumber = $"TEST-COMMIT-{Guid.NewGuid():N}";

        await using var dbContext = new AppDbContext(options);

        var transaction = new InvoiceImportTransaction(dbContext);

        var invoice = new StoredInvoice
        {
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

        string ksefNumberA = $"TEST-DUP-A-{Guid.NewGuid():N}";
        string ksefNumberB = $"TEST-DUP-B-{Guid.NewGuid():N}";

        StoredInvoice CreateInvoice(string ksefNumber) => new()
        {
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

        string ksefNumberA = $"TEST-ROLLBACK-A-{Guid.NewGuid():N}";
        string ksefNumberB = $"TEST-ROLLBACK-B-{Guid.NewGuid():N}";

        StoredInvoice CreateInvoice(string number) => new()
        {
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
}