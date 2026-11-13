using InvoiceDirection = xKsef.Domain.Invoices.InvoiceDirection;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Invoices;
using KSeF.Client.Core.Models.Sessions.BatchSession;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using xKsef.Application.Exceptions;
using xKsef.Application.Invoices;
using xKsef.Contracts.Invoices;
using xKsef.Domain.Invoices;
using xKsef.Infrastructure.Invoices;
using xKsef.Infrastructure.Ksef.Authentication;


namespace xKsef.Infrastructure.Ksef.Invoices;

public sealed class KsefInvoiceService : IInvoiceService
{
    private readonly IKSeFClient _ksefClient;
    private readonly HttpClient _downloadClient;
    private readonly KsefTokenProvider _tokenProvider;
    private readonly KsefRateLimitState _rateLimitState;
    private readonly ICryptographyService _cryptographyService;

    private readonly InvoiceImportService _invoiceImportService;

    private readonly IInvoiceImportTransaction _invoiceImportTransaction;
    private readonly IInvoiceRepository _invoiceRepository;

    public KsefInvoiceService(IKSeFClient ksefClient, KsefTokenProvider tokenProvider, KsefRateLimitState rateLimitState, ICryptographyService cryptographyService, HttpClient downloadClient, InvoiceImportService invoiceImportService, IInvoiceImportTransaction invoiceImportTransaction, IInvoiceRepository invoiceRepository)
    {
        _ksefClient = ksefClient;
        _tokenProvider = tokenProvider;
        _rateLimitState = rateLimitState;
        _cryptographyService = cryptographyService;
        _downloadClient = downloadClient;
        _invoiceImportService = invoiceImportService;
        _invoiceImportTransaction = invoiceImportTransaction;
        _invoiceRepository = invoiceRepository;
    }

    public async Task<string> GetInvoiceXmlAsync(string userId, Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ksefNumber))
            throw new ArgumentException( "Numer KSeF nie może być pusty.", nameof(ksefNumber));

        var accessToken = await _tokenProvider.GetAccessTokenAsync(userId, tenantId, cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Nie udało się uzyskać tokena KSeF.");

        return await ExecuteKsefRequestAsync(() => _ksefClient.GetInvoiceAsync(ksefNumber, accessToken, cancellationToken));
    }

    public async Task<InvoicePackageReadResult> ImportInvoicesAsync(string userId, Guid tenantId, DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));

        List<string>? decryptedFiles = null;
        string? archivePath = null;

        try
        {
            // 1. Rozpoczęcie eksportu z KSeF
            var session = await StartInvoiceImportAsync(userId, tenantId, from, to, direction, cancellationToken);

            // 2. Oczekiwanie na zakończenie eksportu
            var status = await WaitForInvoiceImportAsync(userId, tenantId, session, cancellationToken);

            // 3. Pobranie i odszyfrowanie części paczki
            decryptedFiles = await DownloadAndDecryptPackageAsync(status, session, cancellationToken);

            // 4. Połączenie części w archiwum TAR.GZ
            archivePath = await MergePackagePartsAsync( decryptedFiles,cancellationToken);

            // 5. Pierwszy przebieg - weryfikacja archiwum
            // Bez zapisywania faktur do bazy danych
            var readCount = await ProcessInvoiceXmlsFromArchiveAsync(archivePath, (invoice, ct) => Task.CompletedTask, cancellationToken);

            var declaredCount = status.Package.InvoiceCount;

            if (declaredCount != readCount)
            {
                throw new InvalidDataException($"Niezgodna liczba faktur. KSeF: {declaredCount}, odczytane XML: {readCount}.");
            }

            // 6. Drugi przebieg - zapis faktur w jednej transakcji
            int importedCount = 0;
            int skippedCount = 0;
            int secondReadCount = 0;

            await _invoiceImportTransaction.ExecuteAsync(
                async ct =>
                {
                    secondReadCount = await ProcessInvoiceXmlsFromArchiveAsync(archivePath,async (invoice, invoiceCt) =>
                        {
                            try
                            {
                                await _invoiceImportService.ImportAsync(tenantId, invoice, direction, invoiceCt);
                                importedCount++;
                            }
                            catch (DuplicateInvoiceException)
                            {
                                skippedCount++;
                            }
                        },
                        ct);

                    // 7. Sprawdzenie poprawności drugiego przebiegu
                    if (secondReadCount != readCount || importedCount + skippedCount != secondReadCount)
                    {
                        throw new InvalidDataException(
                            "Niezgodna liczba faktur podczas zapisu paczki KSeF. Pierwszy odczyt: {readCount}, drugi odczyt: {secondReadCount}, zapisane: {importedCount}, pominięte: {skippedCount}.");
                    }

                    // Po zakończeniu delegata ExecuteAsync
                    // zatwierdzi transakcję.
                },
                cancellationToken);

            // 8. Wynik importu - transakcja została zatwierdzona
            return new InvoicePackageReadResult
            {
                ReferenceNumber = session.ReferenceNumber,
                DeclaredInvoiceCount = declaredCount,
                ReadInvoiceCount = readCount,
                ImportedCount = importedCount,
                SkippedCount = skippedCount
            };
        }
        finally
        {
            // 9. Usunięcie tymczasowego archiwum
            if (archivePath is not null && File.Exists(archivePath))
            {
                try
                {
                    File.Delete(archivePath);
                }
                catch (IOException)
                {
                    // Nie przerywamy obsługi pierwotnego wyjątku.
                }
                catch (UnauthorizedAccessException)
                {
                    // Nie przerywamy obsługi pierwotnego wyjątku.
                }
            }

            // 10. Usunięcie odszyfrowanych części paczki
            if (decryptedFiles is not null)
            {
                foreach (var file in decryptedFiles)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                        }
                    }
                    catch (IOException)
                    {
                        // Nie przerywamy czyszczenia pozostałych plików.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Nie przerywamy czyszczenia pozostałych plików.
                    }
                }
            }
        }
    }

    internal async Task<InvoiceExportStatusResponse> GetInvoiceImportStatusAsync(string userId, Guid tenantId, InvoiceImportSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var accessToken = await _tokenProvider.GetAccessTokenAsync(userId, tenantId, cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Nie udało się uzyskać tokena KSeF.");

        return await ExecuteKsefRequestAsync(() => _ksefClient.GetInvoiceExportStatusAsync(session.ReferenceNumber,accessToken,cancellationToken));
    }

    private async Task<string> DownloadInvoicePackagePartAsync(InvoiceExportPackagePart part, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(part);

        if (!string.Equals(part.Method, "GET", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Nieobsługiwana metoda pobierania paczki: {part.Method}");

        var uri = new Uri(part.Url, UriKind.Absolute);

        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Adres pobierania paczki musi używać HTTPS.");

        if (part.ExpirationDate <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Adres pobierania części paczki wygasł.");

        var filePath = Path.Combine(Path.GetTempPath(),$"ksef-{Guid.NewGuid():N}.encrypted");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            using var response = await _downloadClient.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);

            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            var fileInfo = new FileInfo(filePath);

            if (fileInfo.Length != part.EncryptedPartSize)
                throw new InvalidDataException("Rozmiar pobranej części paczki nie zgadza się z deklarowanym.");

            await using var file = File.OpenRead(filePath);

            var hash = await SHA256.HashDataAsync(file, cancellationToken);
            var calculatedHash = Convert.ToBase64String(hash);

            if (calculatedHash != part.EncryptedPartHash)
                throw new InvalidDataException(
                    "Hash pobranej części paczki nie zgadza się z deklarowanym.");

            return filePath;
        }
        catch
        {
            if (File.Exists(filePath))
                File.Delete(filePath);

            throw;
        }
    }
    private async Task<string> DecryptInvoicePackagePartAsync(string encryptedFilePath, InvoiceExportPackagePart part, InvoiceImportSession session, CancellationToken cancellationToken = default)
    {
        var decryptedFilePath = Path.Combine(
            Path.GetTempPath(),
            $"ksef-{Guid.NewGuid():N}.decrypted");

        try
        {
            using var aes = Aes.Create();

            aes.Key = session.CipherKey;
            aes.IV = session.CipherIv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();

            await using var source = File.OpenRead(encryptedFilePath);

            using var cryptoStream = new CryptoStream(
                source,
                decryptor,
                CryptoStreamMode.Read);

            await using (var destination = new FileStream(
                decryptedFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await cryptoStream.CopyToAsync(
                    destination,
                    cancellationToken);
            }

            var fileInfo = new FileInfo(decryptedFilePath);

            if (fileInfo.Length != part.PartSize)
                throw new InvalidDataException(
                    "Rozmiar odszyfrowanej części paczki jest nieprawidłowy.");

            await using var file = File.OpenRead(decryptedFilePath);

            var hash = await SHA256.HashDataAsync(
                file,
                cancellationToken);

            if (Convert.ToBase64String(hash) != part.PartHash)
                throw new InvalidDataException(
                    "Hash odszyfrowanej części paczki jest nieprawidłowy.");

            return decryptedFilePath;
        }
        catch
        {
            if (File.Exists(decryptedFilePath))
                File.Delete(decryptedFilePath);

            throw;
        }
    }

    private async Task<List<string>> DownloadAndDecryptPackageAsync(
    InvoiceExportStatusResponse exportStatus,
    InvoiceImportSession session,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exportStatus);
        ArgumentNullException.ThrowIfNull(session);

        var package = exportStatus.Package
            ?? throw new InvalidOperationException(
                "KSeF nie zwrócił informacji o paczce.");

        if (package.IsTruncated)
            throw new InvalidOperationException(
                "Paczka została obcięta. Import wymaga obsługi kontynuacji.");

        if (package.Parts is null)
            throw new InvalidOperationException(
                "KSeF nie zwrócił listy części paczki.");

        var decryptedFiles = new List<string>();

        try
        {
            foreach (var part in package.Parts.OrderBy(p => p.OrdinalNumber))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? encryptedFile = null;

                try
                {
                    encryptedFile = await DownloadInvoicePackagePartAsync(
                        part,
                        cancellationToken);

                    var decryptedFile = await DecryptInvoicePackagePartAsync(
                        encryptedFile,
                        part,
                        session,
                        cancellationToken);

                    decryptedFiles.Add(decryptedFile);
                }
                finally
                {
                    if (encryptedFile is not null && File.Exists(encryptedFile))
                        File.Delete(encryptedFile);
                }
            }

            return decryptedFiles;
        }
        catch
        {
            foreach (var file in decryptedFiles)
            {
                if (File.Exists(file))
                    File.Delete(file);
            }

            throw;
        }
    }

    private async Task<InvoiceExportStatusResponse> WaitForInvoiceImportAsync(string userId, Guid tenantId, InvoiceImportSession session, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 30;
        var delay = TimeSpan.FromSeconds(10);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var response = await GetInvoiceImportStatusAsync(userId, tenantId, session, cancellationToken);

            var status = response.Status
                ?? throw new InvalidOperationException(
                    "KSeF nie zwrócił statusu eksportu.");

            if (status.Code == 200)
            {
                if (response.Package is null)
                    throw new InvalidOperationException(
                        "Eksport zakończony, ale KSeF nie zwrócił paczki.");

                return response;
            }

            if (status.Code != 100)
            {
                throw new InvalidOperationException(
                    $"Eksport KSeF zakończył się nieoczekiwanym statusem " +
                    $"{status.Code}: {status.Description}");
            }

            if (attempt < maxAttempts)
                await Task.Delay(delay, cancellationToken);
        }

        throw new TimeoutException(
            "Przekroczono czas oczekiwania na przygotowanie paczki KSeF.");
    }

    internal async Task<InvoiceImportSession> StartInvoiceImportAsync(string userId, Guid tenantId, DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default)
    {
        if (from > to)
            throw new ArgumentException("Data początkowa nie może być późniejsza od końcowej.");

        var accessToken = await _tokenProvider.GetAccessTokenAsync(userId, tenantId, cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Nie udało się uzyskać tokena KSeF.");

        var filters = new InvoiceQueryFilters
        {
            SubjectType = direction switch
            {
                InvoiceDirection.Sales => InvoiceSubjectType.Subject1,
                InvoiceDirection.Purchase => InvoiceSubjectType.Subject2,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            },
            DateRange = new DateRange
            {
                From = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                To = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                DateType = DateType.Issue
            }
        };

        var encryptionData = _cryptographyService.GetEncryptionData();

        var request = new InvoiceExportRequest
        {
            Encryption = encryptionData.EncryptionInfo,
            CompressionType = CompressionType.TarGz,
            OnlyMetadata = false,
            Filters = filters
        };

        var response = await ExecuteKsefRequestAsync(
            () => _ksefClient.ExportInvoicesAsync(
                request,
                accessToken,
                cancellationToken: cancellationToken));

        return new InvoiceImportSession
        {
            ReferenceNumber = response.ReferenceNumber,
            CipherKey = encryptionData.CipherKey,
            CipherIv = encryptionData.CipherIv
        };
    }

    public async Task<int> GetInvoiceCountAsync(string userId, Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (from > to)
            throw new ArgumentException(
                "Data początkowa nie może być późniejsza od końcowej.");

        var accessToken = await _tokenProvider.GetAccessTokenAsync(userId, tenantId, cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Nie udało się uzyskać tokena KSeF.");

        var filters = new InvoiceQueryFilters
        {
            SubjectType = InvoiceSubjectType.Subject1,
            DateRange = new DateRange
            {
                From = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                To = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                DateType = DateType.Issue
            }
        };

        const int pageSize = 50;

        int pageOffset = 0;
        int totalCount = 0;

        while (true)
        {
            var response = await QueryInvoicePageAsync(
                filters,
                accessToken,
                pageOffset,
                pageSize,
                cancellationToken);
            if (response.IsTruncated)
            {
                throw new InvalidOperationException("Wynik zapytania KSeF został ograniczony do 10 000 faktur. " + "Wybierz krótszy zakres dat.");
            }
            // Dotychczasowa obsługa odpowiedzi
            int count = response.Invoices?.Count ?? 0;

            totalCount += count;

            if (!response.HasMore)
                return totalCount;

            pageOffset++;
        }
    }

    public async Task<IReadOnlyList<InvoiceSummary>> GetInvoicesMetadataAsync(string userId, Guid tenantId, DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default)
    {
        if (from > to)
            throw new ArgumentException("Data początkowa nie może być późniejsza od końcowej.");
        var remainingDelay = _rateLimitState.GetRemainingDelay();

        if (remainingDelay.HasValue)
        {
            throw new ExternalServiceRateLimitException("Limit żądań KSeF nadal obowiązuje.", retryAfter: remainingDelay.Value);
        }

        var accessToken = await _tokenProvider.GetAccessTokenAsync(userId, tenantId, cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Nie udało się uzyskać tokena KSeF.");

        var filters = new InvoiceQueryFilters
        {
            SubjectType = direction switch
            {
                InvoiceDirection.Sales => InvoiceSubjectType.Subject1,
                InvoiceDirection.Purchase => InvoiceSubjectType.Subject2,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            },
            DateRange = new DateRange
            {
                From = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                To = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                DateType = DateType.Issue
            }
        };

        const int pageSize = 50;
        int pageOffset = 0;

        var invoices = new List<InvoiceSummary>();

        while (true)
        {
            var response = await QueryInvoicePageAsync(
                filters,
                accessToken,
                pageOffset,
                pageSize,
                cancellationToken);

            if (response.IsTruncated)
            {
                throw new InvalidOperationException(
                    "Wynik zapytania KSeF został ograniczony do 10 000 faktur. " +
                    "Wybierz krótszy zakres dat.");
            }

            if (response.Invoices is not null)
                invoices.AddRange(response.Invoices);

            if (!response.HasMore)
                break;

            pageOffset++;
        }

        return invoices;
    }

    public async Task<IReadOnlyList<Invoice>> GetInvoicesAsync(Guid tenantId, DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));

        if (from > to)
            throw new ArgumentException("Niepoprawny zakres dat.");

        var storedInvoices = await _invoiceRepository.GetInvoicesAsync(tenantId, from, to, direction, cancellationToken);

        return storedInvoices.Select(invoice => new Invoice
        {
            KsefNumber = invoice.KsefNumber,
            InvoiceNumber = invoice.InvoiceNumber,
            IssueDate = invoice.IssueDate,
            GrossAmount = invoice.GrossAmount,
            BuyerNip = invoice.BuyerNip,
            BuyerName = invoice.BuyerName,
            NetAmount = invoice.NetAmount,
            VatAmount = invoice.VatAmount,
            VatAmountPln = invoice.VatAmountPln,
            InvoicingDate = invoice.InvoicingDate,
            Currency = invoice.Currency,
            SellerName = invoice.SellerName,
            SellerNip = invoice.SellerNip
        }).ToList();
    }

    private Task<PagedInvoiceResponse> QueryInvoicePageAsync(InvoiceQueryFilters filters, string accessToken, int pageOffset, int pageSize, CancellationToken cancellationToken)
    {
        return ExecuteKsefRequestAsync(
            () => _ksefClient.QueryInvoiceMetadataAsync(
                filters,
                accessToken,
                pageOffset: pageOffset,
                pageSize: pageSize,
                cancellationToken: cancellationToken));
    }

    private async Task<T> ExecuteKsefRequestAsync<T>(Func<Task<T>> operation)
    {
        var remaining = _rateLimitState.GetRemainingDelay();

        if (remaining.HasValue)
        {
            throw new ExternalServiceRateLimitException(
                "Limit żądań KSeF nadal obowiązuje.",
                retryAfter: remaining.Value);
        }

        try
        {
            return await operation();
        }
        catch (KsefRateLimitException ex)
        {
            TimeSpan? retryAfter = null;

            if (ex.RetryAfterSeconds.HasValue)
            {
                retryAfter = TimeSpan.FromSeconds(
                    Math.Max(0, ex.RetryAfterSeconds.Value));
            }
            else if (ex.RetryAfterDate.HasValue)
            {
                retryAfter = ex.RetryAfterDate.Value - DateTimeOffset.UtcNow;

                if (retryAfter < TimeSpan.Zero)
                    retryAfter = TimeSpan.Zero;
            }

            if (retryAfter.HasValue)
                _rateLimitState.BlockFor(retryAfter.Value);

            throw new ExternalServiceRateLimitException(
                "Przekroczono limit żądań KSeF.",
                retryAfter: retryAfter,
                innerException: ex);
        }
    }

    private static async Task<IReadOnlyList<ExportedInvoiceMetadata>> ReadPackageMetadataAsync( string archivePath, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(archivePath);

        await using var gzip = new GZipStream(file,CompressionMode.Decompress);

        using var tar = new TarReader(gzip, leaveOpen: true);
        IReadOnlyList<ExportedInvoiceMetadata>? metadata = null;
        TarEntry? entry;

        while ((entry = await tar.GetNextEntryAsync(
            cancellationToken: cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.EntryType != TarEntryType.RegularFile)
                continue;

            var fileName = entry.Name
                .Replace('\\', '/')
                .Split('/')
                .Last();

            if (!string.Equals(
                fileName,
                "_metadata.json",
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.DataStream is null)
            {
                throw new InvalidDataException(
                    "Brak danych pliku _metadata.json.");
            }

            using var reader = new StreamReader(entry.DataStream);

            var json = await reader.ReadToEndAsync(
                cancellationToken);

            if (metadata is not null)
            {
                throw new InvalidDataException(
                    "Archiwum KSeF zawiera więcej niż jeden plik _metadata.json.");
            }

            metadata = InvoicePackageMetadataReader.ReadInvoices(json);

        }

        return metadata ?? throw new InvalidDataException(
    "Archiwum KSeF nie zawiera pliku _metadata.json.");
    }

    private async Task<int> ProcessInvoiceXmlsFromArchiveAsync(string archivePath, Func<ValidatedInvoice, CancellationToken, Task> processInvoiceAsync, CancellationToken cancellationToken = default)
    {
        var invoiceCount = 0;
        var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var invoices = await ReadPackageMetadataAsync(archivePath, cancellationToken);
        var metadataFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadataKsefNumbers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var invoice in invoices)
        {
            if (string.IsNullOrWhiteSpace(invoice.KsefNumber))
            {
                throw new InvalidDataException($"Brak numeru KSeF dla pliku: {invoice.FileName}");
            }
            if (string.IsNullOrWhiteSpace(invoice.InvoiceHash))
            {
                throw new InvalidDataException($"Brak hash SHA-256 dla faktury KSeF: {invoice.KsefNumber}");
            }

            byte[] hashBytes;

            try
            {
                hashBytes = Convert.FromBase64String(invoice.InvoiceHash);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException($"Nieprawidłowy format hash dla faktury KSeF: {invoice.KsefNumber}",ex);
            }

            if (hashBytes.Length != 32)
            {
                throw new InvalidDataException($"Nieprawidłowa długość hash SHA-256 dla faktury KSeF: {invoice.KsefNumber}");
            }
            if (string.IsNullOrWhiteSpace(invoice.FileName) || invoice.FileName != Path.GetFileName(invoice.FileName) || invoice.FileName.Contains('\\'))
            {
                throw new InvalidDataException($"Nieprawidłowa nazwa pliku w metadanych KSeF: {invoice.FileName}");
            }
            if (!invoice.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Nieprawidłowe rozszerzenie pliku faktury KSeF: {invoice.FileName}");
            }
            if (!metadataFileNames.Add(invoice.FileName))
            {
                throw new InvalidDataException($"Zduplikowany wpis w metadanych KSeF: {invoice.FileName}");
            }
            if (!metadataKsefNumbers.Add(invoice.KsefNumber))
            {
                throw new InvalidDataException($"Zduplikowany numer KSeF w metadanych: {invoice.KsefNumber}");
            }
        }
        await using var file = File.OpenRead(archivePath);

        await using var gzip = new GZipStream(file, CompressionMode.Decompress);

        using var tar = new TarReader(gzip, leaveOpen: true);

        TarEntry? entry;

        while ((entry = await tar.GetNextEntryAsync(cancellationToken: cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.EntryType != TarEntryType.RegularFile)
                continue;

            if (string.Equals(entry.Name.Replace('\\', '/').Split('/').Last(), "_metadata.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Nieoczekiwany plik w archiwum KSeF: {entry.Name}");
            }

            if (entry.DataStream is null)
                throw new InvalidDataException($"Brak danych pliku XML: {entry.Name}");

            var fileName = entry.Name.Replace('\\', '/').Split('/').Last();

            if (!processedFiles.Add(fileName))
            {
                throw new InvalidDataException($"Zduplikowany plik XML w paczce KSeF: {fileName}");
            }

            var invoiceMetadata = invoices.SingleOrDefault(x => string.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase));

            if (invoiceMetadata is null)
            {
                throw new InvalidDataException($"Brak metadanych KSeF dla pliku XML: {fileName}");
            }

            using var memoryStream = new MemoryStream();

            await entry.DataStream.CopyToAsync( memoryStream, cancellationToken);

            var xmlBytes = memoryStream.ToArray();

            var calculatedHash = Convert.ToBase64String(SHA256.HashData(xmlBytes));

            if (!string.Equals(calculatedHash, invoiceMetadata.InvoiceHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Nieprawidłowy hash faktury KSeF: {invoiceMetadata.KsefNumber}");
            }

            var validatedInvoice = new ValidatedInvoice(invoiceMetadata.KsefNumber, xmlBytes, invoiceMetadata.VatAmountPln, invoiceMetadata.InvoicingDate);

            await processInvoiceAsync(validatedInvoice, cancellationToken);

            invoiceCount++;
        }
        if (processedFiles.Count != invoices.Count)
        {
            throw new InvalidDataException($"Niezgodna liczba plików XML i metadanych. Metadane: {invoices.Count}, pliki XML: {processedFiles.Count}.");
        }
        return invoiceCount;
    }

    private static async Task<bool> IsGzipFileAsync(
    string filePath,
    CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);

        var header = new byte[2];

        var bytesRead = await stream.ReadAsync(
            header,
            cancellationToken);

        return bytesRead == 2
            && header[0] == 0x1F
            && header[1] == 0x8B;
    }

    //private static async Task ValidatePackagePartsAsync(
    //IReadOnlyList<string> decryptedFiles,
    //CancellationToken cancellationToken = default)
    //{
    //    if (decryptedFiles.Count == 0)
    //        throw new InvalidDataException(
    //            "Paczka KSeF nie zawiera żadnych części.");

    //    for (int i = 0; i < decryptedFiles.Count; i++)
    //    {
    //        bool isGzip = await IsGzipFileAsync(
    //            decryptedFiles[i],
    //            cancellationToken);

    //        if (!isGzip)
    //        {
    //            throw new InvalidDataException(
    //                $"Część paczki nr {i + 1} nie rozpoczyna się nagłówkiem GZIP.");
    //        }
    //    }
    //}

    private static async Task<string> MergePackagePartsAsync(
    IReadOnlyList<string> decryptedFiles,
    CancellationToken cancellationToken = default)
    {
        if (decryptedFiles.Count == 0)
            throw new InvalidDataException("Brak części paczki.");

        var archivePath = Path.Combine(
            Path.GetTempPath(),
            $"ksef-{Guid.NewGuid():N}.tar.gz");

        try
        {
            await using var destination = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            foreach (var filePath in decryptedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using var source = File.OpenRead(filePath);

                await source.CopyToAsync(
                    destination,
                    cancellationToken);
            }

            return archivePath;
        }
        catch
        {
            if (File.Exists(archivePath))
                File.Delete(archivePath);

            throw;
        }
    }

}