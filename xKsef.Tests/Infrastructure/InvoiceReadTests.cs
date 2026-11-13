using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using xKsef.Api.Controllers;
using xKsef.Application.Invoices;
using xKsef.Contracts.Invoices;
using xKsef.Domain.Invoices;
using xKsef.Infrastructure.Ksef.Invoices;
using xKsef.Wpf.Http;
using xKsef.Wpf.Services;
using Direction = xKsef.Domain.Invoices.InvoiceDirection;

namespace xKsef.Tests.Infrastructure;

public sealed class InvoiceReadTests
{
    [Theory]
    [InlineData("values")]
    [InlineData("zero")]
    [InlineData("null")]
    public async Task ServiceAndControllerPreserveAllFieldsAndReadParameters(string variant)
    {
        Guid tenantId = Guid.NewGuid();
        var stored = InvoiceReadTestData.Create(tenantId, "READ", variant);
        var repository = new ReadRepository(stored);
        var service = new KsefInvoiceService(null!, null!, null!, null!, null!, null!, null!, repository);
        DateOnly from = new(2026, 11, 1);
        DateOnly to = new(2026, 11, 30);
        using var cancellation = new CancellationTokenSource();
        var domain = Assert.Single(await service.GetInvoicesAsync(tenantId, from, to, Direction.Sales, cancellation.Token));
        InvoiceReadTestData.AssertJson(JsonSerializer.SerializeToElement(domain, JsonSerializerOptions.Web), stored);
        Assert.Equal((tenantId, from, to, Direction.Sales, cancellation.Token), repository.LastRequest);

        var controller = new InvoicesController(service, repository) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.HttpContext.Items["TenantId"] = tenantId;
        var response = Assert.IsType<OkObjectResult>(await controller.GetInvoices(from, to, xKsef.Contracts.Invoices.InvoiceDirection.Sales, cancellation.Token));
        var dto = Assert.Single(Assert.IsAssignableFrom<IEnumerable<InvoiceDto>>(response.Value));
        InvoiceReadTestData.AssertJson(JsonSerializer.SerializeToElement(dto, JsonSerializerOptions.Web), stored);
        Assert.Equal((tenantId, from, to, Direction.Sales, cancellation.Token), repository.LastRequest);
    }

    [Fact]
    public async Task ExistingWpfClientAcceptsTheOldResponseWithoutOptionalFields()
    {
        const string json = "[{\"ksefNumber\":\"OLD\",\"invoiceNumber\":\"FV/1\",\"sellerName\":\"Seller\",\"sellerNip\":\"1234567890\",\"issueDate\":\"2026-11-12\",\"grossAmount\":123.45,\"currency\":\"EUR\"}]";
        using var handler = new JsonHandler(json);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var client = new InvoiceApiClient(http);
        var context = new ApiRequestContext(1, 1, Guid.NewGuid(), "test-token", CancellationToken.None, ApiRequestScope.TenantHeader);
        var invoice = Assert.Single(await client.GetInvoicesAsync(context, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), xKsef.Contracts.Invoices.InvoiceDirection.Sales));

        Assert.Equal("OLD", invoice.KsefNumber);
        Assert.Equal("FV/1", invoice.InvoiceNumber);
        Assert.Equal("Seller", invoice.SellerName);
        Assert.Equal("1234567890", invoice.SellerNip);
        Assert.Equal(new DateOnly(2026, 11, 12), invoice.IssueDate);
        Assert.Equal(123.45m, invoice.GrossAmount);
        Assert.Equal("EUR", invoice.Currency);
        Assert.Null(invoice.BuyerNip);
        Assert.Null(invoice.BuyerName);
        Assert.Null(invoice.NetAmount);
        Assert.Null(invoice.VatAmount);
        Assert.Null(invoice.VatAmountPln);
        Assert.Null(invoice.InvoicingDate);
    }

    [Theory]
    [InlineData("values")]
    [InlineData("zero")]
    [InlineData("null")]
    public async Task ExistingWpfClientReadsTheExtendedResponse(string variant)
    {
        var stored = InvoiceReadTestData.Create(Guid.NewGuid(), "NEW", variant);
        var repository = new ReadRepository(stored);
        var service = new KsefInvoiceService(null!, null!, null!, null!, null!, null!, null!, repository);
        var controller = new InvoicesController(service, repository) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.HttpContext.Items["TenantId"] = stored.TenantId;
        var response = Assert.IsType<OkObjectResult>(await controller.GetInvoices(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), xKsef.Contracts.Invoices.InvoiceDirection.Sales, CancellationToken.None));
        string json = JsonSerializer.Serialize(response.Value, JsonSerializerOptions.Web);
        using var handler = new JsonHandler(json);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var client = new InvoiceApiClient(http);
        var context = new ApiRequestContext(1, 1, stored.TenantId, "test-token", CancellationToken.None, ApiRequestScope.TenantHeader);
        var invoice = Assert.Single(await client.GetInvoicesAsync(context, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), xKsef.Contracts.Invoices.InvoiceDirection.Sales));
        InvoiceReadTestData.AssertJson(JsonSerializer.SerializeToElement(invoice, JsonSerializerOptions.Web), stored);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private sealed class ReadRepository(StoredInvoice invoice) : IInvoiceRepository
    {
        public (Guid, DateOnly, DateOnly, Direction, CancellationToken) LastRequest { get; private set; }
        public Task<IReadOnlyList<StoredInvoice>> GetInvoicesAsync(Guid tenantId, DateOnly from, DateOnly to, Direction direction, CancellationToken cancellationToken = default)
        {
            LastRequest = (tenantId, from, to, direction, cancellationToken);
            return Task.FromResult<IReadOnlyList<StoredInvoice>>([invoice]);
        }
        public Task SaveAsync(StoredInvoice invoice, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredInvoice?> GetByKsefNumberAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

internal static class InvoiceReadTestData
{
    private static readonly string[] PropertyNames = ["ksefNumber", "invoiceNumber", "sellerName", "sellerNip", "issueDate", "grossAmount", "currency", "buyerNip", "buyerName", "netAmount", "vatAmount", "vatAmountPln", "invoicingDate"];

    public static StoredInvoice Create(Guid tenantId, string number, string variant = "values") => new()
    {
        TenantId = tenantId, KsefNumber = number, InvoiceNumber = "FV/READ/1", SellerName = "Seller", SellerNip = "1234567890",
        IssueDate = new DateOnly(2026, 11, 12), Direction = Direction.Sales, GrossAmount = 123.45m, Currency = "EUR",
        BuyerNip = variant == "null" ? null : variant == "zero" ? "" : "9876543210",
        BuyerName = variant == "null" ? null : variant == "zero" ? "" : "Nabywca Łódź",
        NetAmount = variant == "null" ? null : variant == "zero" ? 0m : -100.12m,
        VatAmount = variant == "null" ? null : variant == "zero" ? 0m : -23.33m,
        VatAmountPln = variant == "null" ? null : variant == "zero" ? 0m : -101.23m,
        InvoicingDate = variant == "null" ? null : new DateTimeOffset(2030, 2, 3, 15, 20, 30, variant == "zero" ? TimeSpan.FromHours(-3.5) : TimeSpan.FromHours(5.75)).AddTicks(1234567),
        ImportedAt = DateTimeOffset.UtcNow, XmlBytes = [1, 2, 3, 4]
    };

    public static void AssertJson(JsonElement json, StoredInvoice expected)
    {
        Assert.Equal(PropertyNames.OrderBy(x => x), json.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
        Assert.Equal(expected.KsefNumber, json.GetProperty("ksefNumber").GetString());
        Assert.Equal(expected.InvoiceNumber, json.GetProperty("invoiceNumber").GetString());
        Assert.Equal(expected.SellerName, json.GetProperty("sellerName").GetString());
        Assert.Equal(expected.SellerNip, json.GetProperty("sellerNip").GetString());
        Assert.Equal(expected.IssueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), json.GetProperty("issueDate").GetString());
        Assert.Equal(expected.GrossAmount, json.GetProperty("grossAmount").GetDecimal());
        Assert.Equal(expected.Currency, json.GetProperty("currency").GetString());
        Assert.Equal(expected.BuyerNip, json.GetProperty("buyerNip").GetString());
        Assert.Equal(expected.BuyerName, json.GetProperty("buyerName").GetString());
        AssertAmount(json.GetProperty("netAmount"), expected.NetAmount);
        AssertAmount(json.GetProperty("vatAmount"), expected.VatAmount);
        AssertAmount(json.GetProperty("vatAmountPln"), expected.VatAmountPln);
        var date = json.GetProperty("invoicingDate");
        if (expected.InvoicingDate is DateTimeOffset expectedDate)
        {
            Assert.Equal(expectedDate, date.GetDateTimeOffset());
            Assert.Equal(expectedDate.Offset, date.GetDateTimeOffset().Offset);
            Assert.Equal(expectedDate.Ticks, date.GetDateTimeOffset().Ticks);
        }
        else Assert.Equal(JsonValueKind.Null, date.ValueKind);
    }

    private static void AssertAmount(JsonElement value, decimal? expected)
    {
        if (expected.HasValue) Assert.Equal(expected.Value, value.GetDecimal());
        else Assert.Equal(JsonValueKind.Null, value.ValueKind);
    }
}
