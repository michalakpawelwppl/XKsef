using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KSeF.Client.Core.Models.Invoices;
using xKsef.Application.Exceptions;
using xKsef.Application.Invoices;
using xKsef.Domain.Invoices;
using xKsef.Infrastructure.Invoices;
using xKsef.Infrastructure.Ksef.Invoices;

namespace xKsef.Tests.Infrastructure;

public sealed class InvoiceImportMetadataTests
{
    [Fact]
    public void SdkMapperKeepsVatInPlnAndInvoicingDate()
    {
        var source = new InvoiceSummary
        {
            KsefNumber = "SDK-1", InvoiceHash = "hash", VatAmount = 99.12m,
            InvoicingDate = InvoiceImportTestData.InvoicingDate
        };
        var result = InvoiceMetadataMapper.Map(source);
        Assert.Equal(99.12m, result.VatAmountPln);
        Assert.Equal(InvoiceImportTestData.InvoicingDate, result.InvoicingDate);
        Assert.Equal("SDK-1.xml", result.FileName);
        Assert.Equal("hash", result.InvoiceHash);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MetadataReaderDistinguishesMissingFieldsFromAnExplicitZero(bool hasVat, bool hasDate)
    {
        byte[] xml = InvoiceImportTestData.Xml();
        string json = InvoiceImportTestData.Metadata("READER", xml, hasVat ? 0m : null, hasDate ? InvoiceImportTestData.InvoicingDate : null);
        // The existing reader accepts property names without regard to case.
        json = json.Replace("vatAmount", "VatAmount").Replace("invoicingDate", "InvoicingDate").Replace("invoices", "Invoices");
        var result = Assert.Single(InvoicePackageMetadataReader.ReadInvoices(json));
        Assert.Equal(hasVat ? 0m : (decimal?)null, result.VatAmountPln);
        Assert.Equal(hasDate ? InvoiceImportTestData.InvoicingDate : (DateTimeOffset?)null, result.InvoicingDate);
    }

    [Theory]
    [InlineData("PLN", false)]
    [InlineData("EUR", false)]
    [InlineData("USD", true)]
    public async Task ImporterStoresXmlAndSdkValuesWithoutChangingExistingFields(string currency, bool correction)
    {
        var repository = new RecordingRepository();
        var importer = new InvoiceImportService(repository, new KsefInvoiceMetadataParser());
        byte[] xml = InvoiceImportTestData.Xml(currency, correction);
        var invoice = new ValidatedInvoice("IMPORT", xml, 99.12m, InvoiceImportTestData.InvoicingDate);
        Guid tenantId = Guid.NewGuid();
        await importer.ImportAsync(tenantId, invoice, InvoiceDirection.Purchase);
        var saved = Assert.Single(repository.Invoices);
        InvoiceImportTestData.AssertValues(saved, xml, currency, correction, 99.12m, InvoiceImportTestData.InvoicingDate);
        Assert.Equal(tenantId, saved.TenantId);
        Assert.Equal(InvoiceDirection.Purchase, saved.Direction);
        Assert.Equal("IMPORT", saved.KsefNumber);
    }

    [Fact]
    public async Task UnknownXmlAmountsRemainNullDespiteAvailableSdkMetadata()
    {
        var repository = new RecordingRepository();
        var importer = new InvoiceImportService(repository, new KsefInvoiceMetadataParser());
        byte[] xml = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(InvoiceImportTestData.Xml()).Replace("<P_14_1>23.00</P_14_1>", "<P_14_1>invalid</P_14_1>"));
        await importer.ImportAsync(Guid.NewGuid(), new ValidatedInvoice("UNKNOWN", xml, 99.12m, InvoiceImportTestData.InvoicingDate), InvoiceDirection.Sales);
        var saved = Assert.Single(repository.Invoices);
        Assert.Null(saved.VatAmount);
        Assert.Equal(99.12m, saved.VatAmountPln);
        Assert.Equal(123m, saved.GrossAmount);
    }

    [Fact]
    public async Task DuplicateDoesNotOverwritePreviouslyImportedValues()
    {
        var repository = new RecordingRepository();
        var importer = new InvoiceImportService(repository, new KsefInvoiceMetadataParser());
        Guid tenantId = Guid.NewGuid();
        var original = new ValidatedInvoice("DUP", InvoiceImportTestData.Xml(), 99.12m, InvoiceImportTestData.InvoicingDate);
        await importer.ImportAsync(tenantId, original, InvoiceDirection.Sales);
        var replacement = new ValidatedInvoice("DUP", InvoiceImportTestData.Xml("USD", true), 999m, DateTimeOffset.UtcNow);
        var result = await importer.ImportManyAsync(tenantId, [replacement], InvoiceDirection.Purchase);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);
        var saved = Assert.Single(repository.Invoices);
        InvoiceImportTestData.AssertValues(saved, original.GetXmlBytes(), "EUR", false, 99.12m, InvoiceImportTestData.InvoicingDate);
    }

    [Fact]
    public void ValidatedInvoiceRetainsXmlCopyingAndSupportsMissingSdkMetadata()
    {
        byte[] xml = [1, 2, 3];
        var invoice = new ValidatedInvoice("LEGACY", xml);
        xml[0] = 9;
        byte[] returned = invoice.GetXmlBytes();
        returned[1] = 9;
        Assert.Equal(new byte[] { 1, 2, 3 }, invoice.GetXmlBytes());
        Assert.Null(invoice.VatAmountPln);
        Assert.Null(invoice.InvoicingDate);
    }

    [Fact]
    public async Task ArchivePassesKeepMetadataAssociatedWithTheCorrectXml()
    {
        byte[] first = InvoiceImportTestData.Xml("EUR");
        byte[] second = InvoiceImportTestData.Xml("USD", true);
        var entries = new[] { InvoiceImportTestData.MetadataEntry("FIRST", first, 99.12m, InvoiceImportTestData.InvoicingDate), InvoiceImportTestData.MetadataEntry("SECOND", second, -200m, InvoiceImportTestData.InvoicingDate.AddDays(1)) };
        string archive = WriteArchive(JsonSerializer.Serialize(new { invoices = entries }), [("SECOND.xml", second), ("FIRST.xml", first)]);
        var repository = new RecordingRepository();
        var importer = new InvoiceImportService(repository, new KsefInvoiceMetadataParser());
        Guid tenantId = Guid.NewGuid();
        try
        {
            int firstCount = await ReadArchive(archive, (_, _) => Task.CompletedTask);
            Assert.Empty(repository.Invoices);
            int secondCount = await ReadArchive(archive, (invoice, ct) => importer.ImportAsync(tenantId, invoice, InvoiceDirection.Sales, ct));
            Assert.Equal(2, firstCount);
            Assert.Equal(firstCount, secondCount);
            var firstSaved = repository.Invoices.Single(x => x.KsefNumber == "FIRST");
            var secondSaved = repository.Invoices.Single(x => x.KsefNumber == "SECOND");
            InvoiceImportTestData.AssertValues(firstSaved, first, "EUR", false, 99.12m, InvoiceImportTestData.InvoicingDate);
            InvoiceImportTestData.AssertValues(secondSaved, second, "USD", true, -200m, InvoiceImportTestData.InvoicingDate.AddDays(1));
        }
        finally { File.Delete(archive); }
    }

    [Theory]
    [InlineData("bad-hash")]
    [InlineData("missing-xml")]
    [InlineData("duplicate-xml")]
    [InlineData("unlisted-xml")]
    [InlineData("duplicate-metadata")]
    public async Task ArchiveIntegrityChecksRemainEffective(string failure)
    {
        byte[] xml = InvoiceImportTestData.Xml();
        var entry = InvoiceImportTestData.MetadataEntry("INTEGRITY", xml, 99.12m, InvoiceImportTestData.InvoicingDate);
        List<(string Name, byte[] Content)> files = [("INTEGRITY.xml", xml)];
        List<Dictionary<string, object>> entries = [entry];
        switch (failure)
        {
            case "bad-hash": files[0] = ("INTEGRITY.xml", [1, 2, 3]); break;
            case "missing-xml": files.Clear(); break;
            case "duplicate-xml": files.Add(files[0]); break;
            case "unlisted-xml": files.Add(("UNLISTED.xml", xml)); break;
            case "duplicate-metadata": entries.Add(entry); break;
        }
        string archive = WriteArchive(JsonSerializer.Serialize(new { invoices = entries }), files);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => ReadArchive(archive, (_, _) => Task.CompletedTask));
        }
        finally { File.Delete(archive); }
    }

    [Fact]
    public async Task DirectoryValidatorStillRejectsHashMismatch()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xksef-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] xml = InvoiceImportTestData.Xml();
            await File.WriteAllBytesAsync(Path.Combine(directory, "HASH.xml"), [1, 2, 3]);
            var metadata = Assert.Single(InvoicePackageMetadataReader.ReadInvoices(InvoiceImportTestData.Metadata("HASH", xml, null, null)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => InvoicePackageValidator.ValidateInvoiceAsync(directory, metadata));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static Task<int> ReadArchive(string path, Func<ValidatedInvoice, CancellationToken, Task> callback)
    {
        // Exercise the existing archive stage in isolation; no SDK calls or authentication are needed here.
        var service = new KsefInvoiceService(null!, null!, null!, null!, null!, null!, null!, null!);
        var method = typeof(KsefInvoiceService).GetMethod("ProcessInvoiceXmlsFromArchiveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task<int>)method.Invoke(service, [path, callback, CancellationToken.None])!;
    }

    private static string WriteArchive(string metadata, IEnumerable<(string Name, byte[] Content)> files)
    {
        string path = Path.Combine(Path.GetTempPath(), "xksef-import-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        using var file = new FileStream(path, FileMode.CreateNew);
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        using var tar = new TarWriter(gzip);
        foreach (var entry in files.Prepend((Name: "_metadata.json", Content: Encoding.UTF8.GetBytes(metadata))))
        {
            using var data = new MemoryStream(entry.Content);
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entry.Name) { DataStream = data });
        }
        return path;
    }

    private sealed class RecordingRepository : IInvoiceRepository
    {
        public List<StoredInvoice> Invoices { get; } = [];
        public Task SaveAsync(StoredInvoice invoice, CancellationToken cancellationToken = default)
        {
            Invoices.Add(invoice);
            return Task.CompletedTask;
        }
        public Task<bool> ExistsAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default) => Task.FromResult(Invoices.Any(x => x.TenantId == tenantId && x.KsefNumber == ksefNumber));
        public Task<StoredInvoice?> GetByKsefNumberAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StoredInvoice>> GetInvoicesAsync(Guid tenantId, DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

internal static class InvoiceImportTestData
{
    public static readonly DateTimeOffset InvoicingDate = new(2026, 11, 13, 15, 25, 30, TimeSpan.FromHours(2));

    public static byte[] Xml(string currency = "EUR", bool correction = false) => Encoding.UTF8.GetBytes($$"""
        <Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/">
          <Podmiot1><DaneIdentyfikacyjne><NIP>1234567890</NIP><Nazwa>Seller</Nazwa></DaneIdentyfikacyjne></Podmiot1>
          <Podmiot2><DaneIdentyfikacyjne><NIP>9876543210</NIP><Nazwa>Buyer</Nazwa></DaneIdentyfikacyjne></Podmiot2>
          <Fa><KodWaluty>{{currency}}</KodWaluty><P_1>2026-11-12</P_1><P_2>FV/IMPORT/1</P_2>
            <P_13_1>{{(correction ? "-100.00" : "100.00")}}</P_13_1><P_14_1>{{(correction ? "-23.00" : "23.00")}}</P_14_1>
            <P_15>{{(correction ? "-123.00" : "123.00")}}</P_15><RodzajFaktury>{{(correction ? "KOR" : "VAT")}}</RodzajFaktury>
          </Fa>
        </Faktura>
        """);

    public static Dictionary<string, object> MetadataEntry(string number, byte[] xml, decimal? vatAmountPln, DateTimeOffset? invoicingDate)
    {
        var entry = new Dictionary<string, object>
        {
            ["ksefNumber"] = number,
            ["invoiceHash"] = Convert.ToBase64String(SHA256.HashData(xml))
        };
        if (vatAmountPln.HasValue) entry["vatAmount"] = vatAmountPln.Value;
        if (invoicingDate.HasValue) entry["invoicingDate"] = invoicingDate.Value;
        return entry;
    }

    public static string Metadata(string number, byte[] xml, decimal? vatAmountPln, DateTimeOffset? invoicingDate) => JsonSerializer.Serialize(new { invoices = new[] { MetadataEntry(number, xml, vatAmountPln, invoicingDate) } });

    public static void AssertValues(StoredInvoice invoice, byte[] xml, string currency, bool correction, decimal? vatAmountPln, DateTimeOffset? invoicingDate)
    {
        Assert.Equal("9876543210", invoice.BuyerNip);
        Assert.Equal("Buyer", invoice.BuyerName);
        Assert.Equal(correction ? -100m : 100m, invoice.NetAmount);
        Assert.Equal(correction ? -23m : 23m, invoice.VatAmount);
        Assert.Equal(vatAmountPln, invoice.VatAmountPln);
        Assert.Equal(invoicingDate, invoice.InvoicingDate);
        Assert.Equal(invoicingDate?.Offset, invoice.InvoicingDate?.Offset);
        Assert.Equal(new DateOnly(2026, 11, 12), invoice.IssueDate);
        Assert.Equal(correction ? -123m : 123m, invoice.GrossAmount);
        Assert.Equal(currency, invoice.Currency);
        Assert.Equal("FV/IMPORT/1", invoice.InvoiceNumber);
        Assert.Equal("1234567890", invoice.SellerNip);
        Assert.Equal("Seller", invoice.SellerName);
        Assert.Equal(xml, invoice.XmlBytes);
    }
}
