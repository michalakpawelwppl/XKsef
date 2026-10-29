using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using xKsef.Application.Invoices;

namespace xKsef.Infrastructure.Persistence;

public sealed class InvoiceImportTransaction
    : IInvoiceImportTransaction
{
    private readonly AppDbContext _dbContext;
    private readonly int _lockTimeoutMilliseconds;
    private const string ImportLockName = "xKsef.InvoiceImport";

    public InvoiceImportTransaction(
        AppDbContext dbContext,
        int lockTimeoutMilliseconds = 60000)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            lockTimeoutMilliseconds);

        _dbContext = dbContext;
        _lockTimeoutMilliseconds = lockTimeoutMilliseconds;
    }

    public async Task ExecuteAsync(
    Func<CancellationToken, Task> operation,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var connection = _dbContext.Database.GetDbConnection();

        await using (var command = connection.CreateCommand())
        {

            command.Transaction = transaction.GetDbTransaction();
            command.CommandTimeout =
        Math.Max(30, _lockTimeoutMilliseconds / 1000 + 10);

            command.CommandText = """
            DECLARE @result int;

            EXEC @result = sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @lockTimeout;

            SELECT @result;
            """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@resource";
            var timeoutParameter = command.CreateParameter();
            timeoutParameter.ParameterName = "@lockTimeout";
            timeoutParameter.Value = _lockTimeoutMilliseconds;
            command.Parameters.Add(timeoutParameter);
            parameter.Value = ImportLockName;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(
                cancellationToken);

            int lockResult = Convert.ToInt32(result);

            if (lockResult < 0)
            {
                throw new InvalidOperationException(
                    $"Nie udało się uzyskać blokady importu KSeF. " +
                    $"Kod: {lockResult}.");
            }
        }

        await operation(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT XACT_STATE();";

            var result = await command.ExecuteScalarAsync(
                cancellationToken);

            int transactionState = Convert.ToInt32(result);

            if (transactionState != 1)
            {
                throw new InvalidOperationException(
                    $"Nie można zatwierdzić transakcji importu KSeF. " +
                    $"XACT_STATE: {transactionState}.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }
}