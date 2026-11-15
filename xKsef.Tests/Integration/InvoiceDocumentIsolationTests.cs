using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.Authentication;
using xKsef.Application.Invoices.Pdf;
using xKsef.Domain.Invoices;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    public static TheoryData<string, string> DocumentCases => new()
    {
        { "xml", "cookie" }, { "pdf", "cookie" }, { "xml", "bearer" }, { "pdf", "bearer" }
    };

    [Theory]
    [MemberData(nameof(DocumentCases))]
    public async Task InvoiceDocumentReturnsOwnDocumentWithCorrectContentTypeAndBytes(string format, string authentication)
    {
        await using var f = await Fixture.CreateAsync(); var documents = await SeedDocumentsAsync(f);
        foreach (string owner in new[] { "A", "B" })
        {
            var client = await DocumentClientAsync(f, authentication, owner);
            var document = documents.Single(row => row.Marker.EndsWith(owner + "-only", StringComparison.Ordinal));
            await AssertDocumentAsync(f, client, format, document, documents);
        }
    }

    [Theory]
    [MemberData(nameof(DocumentCases))]
    public async Task InvoiceDocumentRejectsForeignTenantHeaderBeforeReadingXmlOrGeneratingPdf(string format, string authentication)
    {
        await using var f = await Fixture.CreateAsync(); var documents = await SeedDocumentsAsync(f);
        foreach (string owner in new[] { "A", "B" })
        {
            var client = await DocumentClientAsync(f, authentication, owner);
            foreach (var foreign in documents.Where(row => !row.Marker.Contains("-" + owner + "-", StringComparison.Ordinal)))
            {
                ResetDocumentObservations(f);
                using var response = await client.Browser.SendAsync(HttpMethod.Get, DocumentPath(foreign.Invoice.KsefNumber, format),
                    authorization: client.Authorization, tenantId: foreign.Invoice.TenantId);
                await AssertDocumentFailureAsync(f, response, HttpStatusCode.Forbidden, documents);
            }
        }
    }

    [Theory]
    [MemberData(nameof(DocumentCases))]
    public async Task InvoiceDocumentReturns404ForNumbersExistingOnlyInAnotherTenant(string format, string authentication)
    {
        await using var f = await Fixture.CreateAsync(); var documents = await SeedDocumentsAsync(f);
        foreach (string owner in new[] { "A", "B" })
        {
            var client = await DocumentClientAsync(f, authentication, owner);
            var own = documents.Single(row => row.Marker.EndsWith(owner + "-only", StringComparison.Ordinal));
            var foreign = documents.Single(row => row.Marker.EndsWith((owner == "A" ? "B" : "A") + "-only", StringComparison.Ordinal));
            // A foreign-only number and a nonexistent number must both be unavailable in the authorized tenant.
            foreach (string number in new[] { foreign.Invoice.KsefNumber, f.Prefix + "-missing" })
            {
                ResetDocumentObservations(f);
                using var response = await client.Browser.SendAsync(HttpMethod.Get, DocumentPath(number, format),
                    authorization: client.Authorization, tenantId: own.Invoice.TenantId);
                await AssertDocumentFailureAsync(f, response, HttpStatusCode.NotFound, documents);
                AssertDocumentSql(f);
            }
        }
    }

    [Theory]
    [MemberData(nameof(DocumentCases))]
    public async Task InvoiceDocumentUsesTenantIdWhenTwoTenantsShareTheSameKsefNumber(string format, string authentication)
    {
        await using var f = await Fixture.CreateAsync(); var documents = await SeedDocumentsAsync(f);
        var shared = documents.Where(row => row.Marker.EndsWith("-shared", StringComparison.Ordinal)).ToArray();
        Assert.Equal(shared[0].Invoice.KsefNumber, shared[1].Invoice.KsefNumber);
        Assert.NotEqual(shared[0].Invoice.TenantId, shared[1].Invoice.TenantId);
        Assert.False(shared[0].Invoice.XmlBytes.SequenceEqual(shared[1].Invoice.XmlBytes));
        Assert.False(shared[0].Pdf.SequenceEqual(shared[1].Pdf));
        foreach (string owner in new[] { "A", "B" })
        {
            var client = await DocumentClientAsync(f, authentication, owner);
            await AssertDocumentAsync(f, client, format, shared.Single(row => row.Marker.EndsWith(owner + "-shared", StringComparison.Ordinal)), documents);
        }
    }

    [Theory]
    [InlineData("xml", "anonymous")]
    [InlineData("pdf", "anonymous")]
    [InlineData("xml", "expired-cookie")]
    [InlineData("pdf", "expired-cookie")]
    [InlineData("xml", "invalid-bearer-with-cookie")]
    [InlineData("pdf", "invalid-bearer-with-cookie")]
    public async Task InvoiceDocumentRejectsMissingOrInvalidAuthenticationBeforeReadingXmlOrGeneratingPdf(string format, string mode)
    {
        await using var f = await Fixture.CreateAsync(); var documents = await SeedDocumentsAsync(f);
        var own = documents.Single(row => row.Marker.EndsWith("A-only", StringComparison.Ordinal));
        var browser = f.Browser();
        if (mode != "anonymous")
        {
            using var login = await browser.LoginAsync(); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            await AssertDocumentAsync(f, (browser, null), format, own, documents);
            if (mode == "expired-cookie") f.Clock.Advance(TimeSpan.FromMinutes(31));
        }
        ResetDocumentObservations(f);
        using var response = await browser.SendAsync(HttpMethod.Get, DocumentPath(own.Invoice.KsefNumber, format),
            authorization: mode == "invalid-bearer-with-cookie" ? "Bearer malformed" : null, tenantId: own.Invoice.TenantId);
        await AssertDocumentFailureAsync(f, response, HttpStatusCode.Unauthorized, documents);
    }

    [Theory]
    [MemberData(nameof(DocumentCases))]
    public async Task InvoiceDocumentRejectsRevokedMembershipOnTheNextRequest(string format, string authentication)
    {
        await using var f = await Fixture.CreateAsync(); var documents = await SeedDocumentsAsync(f);
        var own = documents.Single(row => row.Marker.EndsWith("A-only", StringComparison.Ordinal));
        var other = documents.Single(row => row.Marker.EndsWith("B-only", StringComparison.Ordinal));
        var client = await DocumentClientAsync(f, authentication, "A");
        await AssertDocumentAsync(f, client, format, own, documents);
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.UserTenants.Where(row => row.TenantId == own.Invoice.TenantId && row.UserId == f.UserId).ExecuteDeleteAsync());
        }
        ResetDocumentObservations(f);
        using var response = await client.Browser.SendAsync(HttpMethod.Get, DocumentPath(own.Invoice.KsefNumber, format),
            authorization: client.Authorization, tenantId: own.Invoice.TenantId);
        await AssertDocumentFailureAsync(f, response, HttpStatusCode.Forbidden, documents);
        var otherClient = await DocumentClientAsync(f, authentication, "B");
        await AssertDocumentAsync(f, otherClient, format, other, documents);
    }

    private static string DocumentPath(string number, string format) => $"/api/invoices/{Uri.EscapeDataString(number)}/{format}";

    private static async Task<(Browser Browser, string? Authorization)> DocumentClientAsync(Fixture f, string authentication, string owner)
    {
        var browser = f.Browser(); string userId = owner == "A" ? f.UserId : f.OtherId; string login = owner == "A" ? f.Login : f.OtherLogin;
        if (authentication == "cookie")
        {
            using var response = await browser.LoginAsync(login); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (browser, null);
        }
        Assert.Equal("bearer", authentication);
        using var scope = f.Scope();
        return (browser, "Bearer " + scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken(userId, login));
    }

    private sealed record SyntheticDocument(StoredInvoice Invoice, string Marker, byte[] Pdf);

    private static async Task<SyntheticDocument[]> SeedDocumentsAsync(Fixture f)
    {
        Guid tenantA = await f.CreateTenantAsync(); Guid tenantB = await f.CreateTenantAsync(f.OtherId);
        var documents = new List<SyntheticDocument>();
        foreach (var (tenantId, owner) in new[] { (tenantA, "A"), (tenantB, "B") })
        foreach (string kind in new[] { "only", "shared" })
        {
            string marker = $"{f.Prefix}-{owner}-{kind}";
            string number = kind == "shared" ? f.Prefix + "-shared" : marker;
            byte[] xml = Encoding.UTF8.GetBytes(new XDocument(new XElement("SyntheticInvoice", new XElement("Marker", marker),
                new XElement("KsefNumber", number), new XElement("Description", "Zażółć gęślą jaźń"))).ToString());
            var invoice = new StoredInvoice
            {
                TenantId = tenantId, KsefNumber = number, InvoiceNumber = marker, IssueDate = new DateOnly(2026, 11, 12),
                Direction = owner == "A" ? xKsef.Domain.Invoices.InvoiceDirection.Sales : xKsef.Domain.Invoices.InvoiceDirection.Purchase,
                Currency = "PLN", GrossAmount = 123.45m, SellerName = marker, SellerNip = "1234567890", XmlBytes = xml, ImportedAt = DateTimeOffset.UtcNow
            };
            var document = new SyntheticDocument(invoice, marker, SyntheticPdf(marker));
            f.Pdf.Register(document); documents.Add(document);
        }
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Invoices.AddRange(documents.Select(row => row.Invoice)); await db.SaveChangesAsync();
        return documents.ToArray();
    }

    private static void ResetDocumentObservations(Fixture f) { f.InvoiceSql.Commands.Clear(); f.Pdf.Calls.Clear(); }

    private static void AssertDocumentSql(Fixture f)
    {
        string sql = Assert.Single(f.InvoiceSql.Commands);
        Assert.Contains("[TenantId] =", sql); Assert.Contains("[KsefNumber] =", sql); Assert.Contains("[XmlBytes]", sql);
    }

    private static async Task AssertDocumentAsync(Fixture f, (Browser Browser, string? Authorization) client, string format, SyntheticDocument expected, SyntheticDocument[] documents)
    {
        ResetDocumentObservations(f);
        using var response = await client.Browser.SendAsync(HttpMethod.Get, DocumentPath(expected.Invoice.KsefNumber, format),
            authorization: client.Authorization, tenantId: expected.Invoice.TenantId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Null(response.Headers.Location);
        if (client.Authorization is null) Assert.True(response.Headers.CacheControl?.NoStore == true);
        Assert.Equal(format == "xml" ? "application/xml" : "application/pdf", response.Content.Headers.ContentType!.MediaType);
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(format == "xml" ? expected.Invoice.XmlBytes : expected.Pdf, body);
        if (format == "xml")
        {
            Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
            Assert.Equal(expected.Marker, XDocument.Parse(Encoding.UTF8.GetString(body)).Root!.Element("Marker")!.Value);
            Assert.Empty(f.Pdf.Calls);
        }
        else
        {
            Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(body));
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
            Assert.Equal("invoice.pdf", response.Content.Headers.ContentDisposition.FileName!.Trim('"'));
            var call = Assert.Single(f.Pdf.Calls);
            Assert.Equal(expected.Invoice.XmlBytes, call.Xml); Assert.Equal(expected.Invoice.KsefNumber, call.Number);
        }
        foreach (var foreign in documents.Where(row => row.Invoice.TenantId != expected.Invoice.TenantId))
            Assert.DoesNotContain(foreign.Marker, Encoding.UTF8.GetString(body));
        AssertDocumentSql(f);
    }

    private static async Task AssertDocumentFailureAsync(Fixture f, HttpResponseMessage response, HttpStatusCode expected, SyntheticDocument[] documents)
    {
        Assert.Equal(expected, response.StatusCode); Assert.Null(response.Headers.Location);
        Assert.Null(response.Content.Headers.ContentDisposition); Assert.Empty(f.Pdf.Calls);
        if (expected != HttpStatusCode.NotFound) Assert.Empty(f.InvoiceSql.Commands);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("%PDF-", body); Assert.DoesNotContain("SyntheticInvoice", body);
        foreach (var document in documents)
        {
            Assert.DoesNotContain(document.Marker, body); Assert.DoesNotContain(document.Invoice.KsefNumber, body);
        }
    }

    private sealed class DocumentPdfGenerator : IInvoicePdfGenerator
    {
        private readonly ConcurrentDictionary<string, SyntheticDocument> _documents = new();
        public ConcurrentQueue<(byte[] Xml, string Number)> Calls { get; } = new();
        public void Register(SyntheticDocument document) => Assert.True(_documents.TryAdd(Convert.ToBase64String(document.Invoice.XmlBytes), document));
        public Task<byte[]> GenerateAsync(byte[] invoiceXml, string ksefNumber, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Enqueue((invoiceXml.ToArray(), ksefNumber));
            Assert.True(_documents.TryGetValue(Convert.ToBase64String(invoiceXml), out var document), "Unexpected XML passed to the PDF generator.");
            Assert.Equal(document.Invoice.KsefNumber, ksefNumber);
            return Task.FromResult(document.Pdf.ToArray());
        }
    }

    private static byte[] SyntheticPdf(string marker)
    {
        // A minimal one-page ASCII PDF, created entirely in memory; no Node.js or PDF renderer is invoked.
        string stream = $"BT /F1 12 Tf 50 750 Td ({marker}) Tj ET\n";
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {stream.Length} >>\nstream\n{stream}endstream"];
        var pdf = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        int xref = pdf.Length;
        pdf.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int offset in offsets) pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
