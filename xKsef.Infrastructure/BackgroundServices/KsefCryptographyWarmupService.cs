using KSeF.Client.Core.Interfaces.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class KsefCryptographyWarmupService : BackgroundService
{
    private readonly ICryptographyService _cryptographyService;
    private readonly ILogger<KsefCryptographyWarmupService> _logger;

    public KsefCryptographyWarmupService(
        ICryptographyService cryptographyService,
        ILogger<KsefCryptographyWarmupService> logger)
    {
        _cryptographyService = cryptographyService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                 await _cryptographyService.WarmupAsync(stoppingToken);
                if (_cryptographyService.IsWarmedUp())
                {
                    _logger.LogInformation(
                        "Certyfikaty KSeF zostały zainicjalizowane.");

                    return;
                }

                _logger.LogWarning(
                    "Inicjalizacja certyfikatów KSeF nie została zakończona.");
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Nie udało się pobrać certyfikatów KSeF. Ponowna próba za 10 sekund.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(10),
                stoppingToken);
        }
    }
}