using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Xml.Linq;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.Credentials;
using xKsef.Contracts.Authentication;
using xKsef.Contracts.Invoices;
using xKsef.Contracts.Tenants;
using xKsef.Wpf.Authentication;
using xKsef.Wpf.Http;
using xKsef.Wpf.Models;
using xKsef.Wpf.Services;
using xKsef.Wpf.Services.Certificates;
using xKsef.Wpf.ViewModels;

namespace xKsef.Tests.Wpf;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfTestCollection
{
    public const string Name = "WPF dispatcher tests";
}

[Collection(WpfTestCollection.Name)]
public sealed class InvoiceContextIsolationTests
{
    [Theory]
    [InlineData(InvoiceDateKind.IssueDate)]
    [InlineData(InvoiceDateKind.InvoicingDate)]
    public async Task QueryClientSendsCapturedCredentialsInvariantDatesAndTheRequestedZone(InvoiceDateKind kind)
    {
        Guid tenantId = Guid.NewGuid();
        var tokens = new TokenStore { AccessToken = "other-token" };
        var tenant = new TenantContext();
        tenant.SetTenant(new TenantDto(Guid.NewGuid(), "Other", "User"));
        string? query = null;
        var expected = QueryResult("HTTP", 777m);
        using var transport = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/api/invoices/query", request.RequestUri!.AbsolutePath);
            query = request.RequestUri.Query;
            return Task.FromResult(JsonResponse(System.Text.Json.JsonSerializer.Serialize(expected, System.Text.Json.JsonSerializerOptions.Web)));
        });
        using var http = CreatePipeline(tokens, tenant, transport);
        var client = new InvoiceApiClient(http);
        var context = CreateContext(tenantId, "token-A", 123);
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ar-SA");
            var result = await client.QueryInvoicesAsync(context, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), InvoiceDirection.Purchase, kind, "Europe/Warsaw");
            Assert.Contains("from=2026-03-01", query);
            Assert.Contains("to=2026-03-31", query);
            Assert.Contains("direction=Purchase", query);
            Assert.Contains("dateKind=" + kind, query);
            if (kind == InvoiceDateKind.InvoicingDate) Assert.Contains("timeZoneId=Europe%2FWarsaw", query);
            else Assert.DoesNotContain("timeZoneId", query);
            Assert.Equal(expected.Summaries[0], Assert.Single(result.Summaries));
            Assert.Equal("HTTP", Assert.Single(result.Items).KsefNumber);
            Assert.Equal("Bearer token-A", transport.LastAuthorization);
            Assert.Equal(tenantId.ToString(), transport.LastTenantId);
            Assert.Equal(context, transport.LastRequestContext);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public async Task QueryClientReportsApiTimeZoneRejectionWithoutRetryOrUtcFallback()
    {
        using var transport = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"detail\":\"Nieprawidłowa lub niedostępna strefa czasowa.\"}", Encoding.UTF8, "application/problem+json")
        }));
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://example.test/") };
        var client = new InvoiceApiClient(http);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.QueryInvoicesAsync(CreateContext(Guid.NewGuid(), "token", 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), InvoiceDirection.Sales, InvoiceDateKind.InvoicingDate, "Unavailable/Zone"));
        Assert.Contains("strefa czasowa", error.Message);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal(1, transport.RequestCount);
    }

    [Theory]
    [InlineData("jwt")]
    [InlineData("tenant")]
    [InlineData("scope")]
    [InlineData("cancelled")]
    [InlineData("zone")]
    public async Task QueryClientRejectsInvalidContextOrMissingZoneBeforeSending(string failure)
    {
        using var cancellation = new CancellationTokenSource();
        var context = CreateContext(Guid.NewGuid(), "token", 1);
        if (failure == "jwt") context = context with { AccessToken = null };
        if (failure == "tenant") context = context with { TenantId = null };
        if (failure == "scope") context = context with { Scope = ApiRequestScope.User };
        if (failure == "cancelled") { cancellation.Cancel(); context = context with { CancellationToken = cancellation.Token }; }
        using var transport = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("{}")));
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://example.test/") };
        var client = new InvoiceApiClient(http);
        Task<InvoiceQueryResultDto> Send() => client.QueryInvoicesAsync(context, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), InvoiceDirection.Sales, InvoiceDateKind.InvoicingDate, failure == "zone" ? null : "Europe/Warsaw");
        if (failure == "cancelled") await Assert.ThrowsAnyAsync<OperationCanceledException>(Send);
        else await Assert.ThrowsAsync<InvalidOperationException>(Send);
        Assert.Equal(0, transport.RequestCount);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"items\":null,\"summaries\":[]}")]
    public async Task QueryClientDoesNotPresentAnInvalidResponseAsAnEmptyResult(string json)
    {
        using var transport = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(json)));
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://example.test/") };
        var client = new InvoiceApiClient(http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.QueryInvoicesAsync(CreateContext(Guid.NewGuid(), "token", 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), InvoiceDirection.Sales, InvoiceDateKind.IssueDate, null));
    }

    [Fact]
    public Task QuerySelectionDefaultsToIssueDateAndKeepsSeparateApiSummariesWithoutRecalculation()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var (vm, api, tokens, tenant) = CreateQueryModel();
            api.QueryHandler = (_, _, _, direction, _, _) => Task.FromResult(direction == InvoiceDirection.Sales
                ? new InvoiceQueryResultDto([CreateInvoice("SALES")], [new("EUR", 1, 777m, 12m, 999m, 0, 0), new("PLN", 2, null, 0m, -5m, 1, 0)])
                : QueryResult("PURCHASE", 222m));
            try
            {
                Assert.Equal(InvoiceDateKind.IssueDate, vm.SelectedInvoiceDateKind);
                await vm.LoadInvoicesCommand.ExecuteAsync(null);
                Assert.Equal(2, vm.SalesSummaries.Count);
                Assert.Equal(777m, vm.SalesSummaries[0].NetTotal);
                Assert.Equal(999m, vm.SalesSummaries[0].GrossTotal);
                Assert.Null(vm.SalesSummaries[1].NetTotal);
                Assert.True(vm.HasSalesQueryResult);
                Assert.False(vm.HasPurchaseQueryResult);
                Assert.Equal(InvoiceDateKind.IssueDate, api.QueryFilters[0].DateKind);
                Assert.Null(api.QueryFilters[0].TimeZoneId);
                vm.SelectedSalesInvoice = vm.SalesInvoices[0];
                Assert.Equal(999m, vm.SalesSummaries[0].GrossTotal);
                vm.SelectedTabIndex = 1;
                await vm.LoadInvoicesCommand.ExecuteAsync(null);
                Assert.Equal(222m, Assert.Single(vm.PurchaseSummaries).NetTotal);
                Assert.Equal(777m, vm.SalesSummaries[0].NetTotal);
                Assert.True(vm.HasPurchaseQueryResult);
                vm.HasKsefToken = true;
                var tenantId = tenant.TenantId;
                vm.SelectedInvoiceDateKind = InvoiceDateKind.InvoicingDate;
                Assert.Empty(vm.SalesInvoices); Assert.Empty(vm.PurchaseInvoices);
                Assert.Empty(vm.SalesSummaries); Assert.Empty(vm.PurchaseSummaries);
                Assert.False(vm.HasSalesQueryResult); Assert.False(vm.HasPurchaseQueryResult);
                Assert.Null(vm.SelectedSalesInvoice); Assert.Null(vm.SelectedInvoicePdfPath);
                Assert.True(vm.HasKsefToken);
                Assert.Equal("token-A", tokens.AccessToken);
                Assert.Equal(tenantId, tenant.TenantId);
                await vm.LoadInvoicesCommand.ExecuteAsync(null);
                var filter = api.QueryFilters[^1];
                Assert.Equal(InvoiceDateKind.InvoicingDate, filter.DateKind);
                Assert.False(string.IsNullOrWhiteSpace(filter.TimeZoneId));
                var resolved = TimeZoneInfo.FindSystemTimeZoneById(filter.TimeZoneId!);
                Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow), resolved.GetUtcOffset(DateTime.UtcNow));
                _ = xKsef.Application.Invoices.InvoiceQueryFilter.Create(filter.From, filter.To, filter.Direction, filter.DateKind, filter.TimeZoneId);
            }
            finally { vm.Cleanup(); await WpfTestDispatcher.DrainAsync(); }
        });
    }

    [Fact]
    public Task DateKindComboUsesTwoWayBindingAndKeepsTheExistingTwoDatePickers()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var (vm, _, _, _) = CreateQueryModel();
            var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Wpf", "MainWindow.xaml"));
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            Assert.Equal(2, document.Descendants(ns + "DatePicker").Count());
            var element = new XElement(document.Descendants(ns + "ComboBox").Single(combo => ((string?)combo.Attribute("SelectedValue"))?.Contains("SelectedInvoiceDateKind", StringComparison.Ordinal) == true));
            AddWindowResources(element, document);
            var combo = (ComboBox)System.Windows.Markup.XamlReader.Parse(element.ToString());
            combo.DataContext = vm;
            try
            {
                await WpfTestDispatcher.DrainAsync();
                Assert.Equal(2, combo.Items.Count);
                Assert.Equal(InvoiceDateKind.IssueDate, combo.SelectedValue);
                combo.SelectedIndex = 1;
                Assert.Equal(InvoiceDateKind.InvoicingDate, vm.SelectedInvoiceDateKind);
                vm.SelectedInvoiceDateKind = InvoiceDateKind.IssueDate;
                await WpfTestDispatcher.DrainAsync();
                Assert.Equal(0, combo.SelectedIndex);
            }
            finally { combo.DataContext = null; vm.Cleanup(); await WpfTestDispatcher.DrainAsync(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task QuerySummaryRowsShowPolishValuesNullsZeroAndMissingCounts(bool purchase)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var (vm, api, _, _) = CreateQueryModel();
            api.QueryHandler = (_, _, _, _, _, _) => Task.FromResult(new InvoiceQueryResultDto([CreateInvoice("DISPLAY")],
                [new("EUR", 3, null, 0m, -1234.5m, 2, 0), new("PLN", 1, 1234.5m, null, 0m, 0, 1)]));
            vm.SelectedTabIndex = purchase ? 1 : 0;
            Grid? panels = null;
            try
            {
                await vm.LoadInvoicesCommand.ExecuteAsync(null);
                panels = CreateInvoicePanels(vm);
                panels.Measure(new Size(2200, 500));
                panels.Arrange(new Rect(0, 0, 2200, 500));
                panels.UpdateLayout();
                await WpfTestDispatcher.DrainAsync();
                var summaries = purchase ? vm.PurchaseSummaries : vm.SalesSummaries;
                var rows = VisualChildren<ItemsControl>(panels).Single(control => ReferenceEquals(control.ItemsSource, summaries));
                Assert.Equal(2, rows.Items.Count);
                var first = (ContentPresenter)rows.ItemContainerGenerator.ContainerFromIndex(0)!;
                var second = (ContentPresenter)rows.ItemContainerGenerator.ContainerFromIndex(1)!;
                var firstTexts = VisualChildren<TextBlock>(first).ToArray();
                var secondTexts = VisualChildren<TextBlock>(second).ToArray();
                string grouped = "1" + System.Globalization.CultureInfo.GetCultureInfo("pl-PL").NumberFormat.NumberGroupSeparator + "234,50";
                Assert.Equal(new[] { "EUR", "Brak danych", "0,00", "-" + grouped }, firstTexts.Select(text => text.Text));
                Assert.Equal(new[] { "PLN", grouped, "Brak danych", "0,00" }, secondTexts.Select(text => text.Text));
                var netTip = Assert.IsType<ToolTip>(firstTexts[1].ToolTip);
                var vatTip = Assert.IsType<ToolTip>(secondTexts[2].ToolTip);
                netTip.PlacementTarget = firstTexts[1];
                vatTip.PlacementTarget = secondTexts[2];
                await WpfTestDispatcher.DrainAsync();
                Assert.Equal("Brakujące wartości netto: 2", Assert.IsType<TextBlock>(netTip.Content).Text);
                Assert.Equal("Brakujące wartości VAT: 1", Assert.IsType<TextBlock>(vatTip.Content).Text);
            }
            finally { if (panels is not null) panels.DataContext = null; vm.Cleanup(); await WpfTestDispatcher.DrainAsync(); }
        });
    }

    [Fact]
    public Task EmptyQueryIsDistinguishedFromAnUnloadedResultInTheView()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var (vm, api, _, _) = CreateQueryModel();
            api.QueryHandler = (_, _, _, _, _, _) => Task.FromResult(new InvoiceQueryResultDto([], []));
            var panels = CreateInvoicePanels(vm);
            panels.Measure(new Size(1800, 500)); panels.Arrange(new Rect(0, 0, 1800, 500)); panels.UpdateLayout();
            try
            {
                await WpfTestDispatcher.DrainAsync();
                var label = VisualChildren<TextBlock>(panels).Single(text => text.Text == "Brak faktur");
                var footer = VisualChildren<Border>(panels).Single(border => border.Child is StackPanel stack && VisualChildren<TextBlock>(stack).Contains(label));
                Assert.Equal(Visibility.Collapsed, footer.Visibility);
                await vm.LoadInvoicesCommand.ExecuteAsync(null);
                await WpfTestDispatcher.DrainAsync();
                Assert.True(vm.HasSalesQueryResult);
                Assert.Empty(vm.SalesInvoices); Assert.Empty(vm.SalesSummaries);
                Assert.Equal(Visibility.Visible, label.Visibility);
                Assert.Equal(Visibility.Visible, footer.Visibility);
                vm.DateTo = vm.DateTo.AddDays(1);
                await WpfTestDispatcher.DrainAsync();
                Assert.False(vm.HasSalesQueryResult);
                Assert.Equal(Visibility.Collapsed, footer.Visibility);
            }
            finally { panels.DataContext = null; vm.Cleanup(); await WpfTestDispatcher.DrainAsync(); }
        });
    }

    [Theory]
    [InlineData("from", false)]
    [InlineData("to", false)]
    [InlineData("kind", false)]
    [InlineData("from", true)]
    [InlineData("to", true)]
    [InlineData("kind", true)]
    public Task AFilterChangeRejectsOldResultsAndErrorsWithoutReleasingNewLoading(string changedField, bool oldFails)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var (vm, api, _, _) = CreateQueryModel();
            var oldResponse = NewCompletionSource<InvoiceQueryResultDto>();
            var newResponse = NewCompletionSource<InvoiceQueryResultDto>();
            api.QueryHandler = (_, _, _, _, _, _) => api.QueryFilters.Count == 1 ? oldResponse.Task : newResponse.Task;
            try
            {
                vm.SalesInvoices.Add(CreateInvoice("PREVIOUS")); vm.PurchaseInvoices.Add(CreateInvoice("PREVIOUS-BUY"));
                vm.SalesSummaries.Add(QueryResult("A").Summaries[0]); vm.PurchaseSummaries.Add(QueryResult("B").Summaries[0]);
                Task oldLoad = vm.LoadInvoicesCommand.ExecuteAsync(null);
                Assert.True(vm.IsLoading);
                if (changedField == "from") vm.DateFrom = vm.DateFrom.AddDays(-1);
                else if (changedField == "to") vm.DateTo = vm.DateTo.AddDays(1);
                else vm.SelectedInvoiceDateKind = InvoiceDateKind.InvoicingDate;
                Assert.True(api.InvoiceContexts[0].CancellationToken.IsCancellationRequested);
                Assert.False(vm.IsLoading);
                Assert.Empty(vm.SalesInvoices); Assert.Empty(vm.PurchaseInvoices);
                Assert.Empty(vm.SalesSummaries); Assert.Empty(vm.PurchaseSummaries);
                Task newLoad = vm.LoadInvoicesCommand.ExecuteAsync(null);
                Assert.True(vm.IsLoading);
                if (oldFails) oldResponse.SetException(new InvalidOperationException("stale error"));
                else oldResponse.SetResult(QueryResult("STALE", 111m));
                await oldLoad;
                Assert.True(vm.IsLoading);
                Assert.Equal("Pobieranie faktur...", vm.Status);
                Assert.Empty(vm.SalesInvoices); Assert.Empty(vm.SalesSummaries);
                newResponse.SetResult(QueryResult("CURRENT", 222m));
                await newLoad;
                Assert.False(vm.IsLoading);
                Assert.Equal("CURRENT", Assert.Single(vm.SalesInvoices).KsefNumber);
                Assert.Equal(222m, Assert.Single(vm.SalesSummaries).NetTotal);
                Assert.True(vm.HasSalesQueryResult);
            }
            finally { oldResponse.TrySetResult(QueryResult("OLD")); newResponse.TrySetResult(QueryResult("NEW")); vm.Cleanup(); await WpfTestDispatcher.DrainAsync(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OldQueryCannotPublishSummariesAfterTenantOrSessionChanges(bool changeSession)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var (vm, api, tokens, tenant) = CreateQueryModel();
            var oldResponse = NewCompletionSource<InvoiceQueryResultDto>();
            var newResponse = NewCompletionSource<InvoiceQueryResultDto>();
            api.QueryHandler = (_, _, _, _, _, _) => api.QueryFilters.Count == 1 ? oldResponse.Task : newResponse.Task;
            try
            {
                Task oldLoad = vm.LoadInvoicesCommand.ExecuteAsync(null);
                if (changeSession) tokens.AccessToken = "token-B";
                else tenant.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant B", "User"));
                await WpfTestDispatcher.DrainAsync(); await WpfTestDispatcher.DrainAsync();
                Task newLoad = vm.LoadInvoicesCommand.ExecuteAsync(null);
                Assert.Equal(2, api.QueryFilters.Count);
                oldResponse.SetResult(QueryResult("OLD", 111m));
                await oldLoad;
                Assert.True(vm.IsLoading);
                Assert.Empty(vm.SalesInvoices); Assert.Empty(vm.SalesSummaries);
                newResponse.SetResult(QueryResult("NEW", 222m));
                await newLoad;
                Assert.Equal("NEW", Assert.Single(vm.SalesInvoices).KsefNumber);
                Assert.Equal(222m, Assert.Single(vm.SalesSummaries).NetTotal);
                Assert.False(vm.IsLoading);
            }
            finally { oldResponse.TrySetResult(QueryResult("OLD")); newResponse.TrySetResult(QueryResult("NEW")); vm.Cleanup(); await WpfTestDispatcher.DrainAsync(); }
        });
    }

    [Fact]
    public Task FilterChangeAlsoInvalidatesTheSelectedInvoiceAndPendingPdf()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var (vm, api, _, _) = CreateQueryModel();
            var response = NewCompletionSource<byte[]>();
            api.GetInvoicePdfHandler = (_, _) => response.Task;
            try
            {
                var invoice = CreateInvoice("OLD-PDF");
                vm.SalesInvoices.Add(invoice);
                vm.SelectedSalesInvoice = invoice;
                Task loading = vm.LoadInvoicePdfCommand.ExecutionTask!;
                vm.DateTo = vm.DateTo.AddDays(1);
                response.SetResult([1, 2, 3]);
                await loading;
                Assert.Null(vm.SelectedSalesInvoice);
                Assert.Null(vm.SelectedInvoicePdfPath);
                Assert.Empty(api.QueryFilters);
            }
            finally { response.TrySetResult([]); vm.Cleanup(); await WpfTestDispatcher.DrainAsync(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ImportKeepsItsOriginalPostRangeAndRefreshesTheCurrentLocalFilter(bool changeDuringRefresh)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var (vm, api, _, _) = CreateQueryModel();
            var post = NewCompletionSource<InvoicePackageReadResult>();
            var query = NewCompletionSource<InvoiceQueryResultDto>();
            DateOnly originalFrom = DateOnly.FromDateTime(vm.DateFrom), originalTo = DateOnly.FromDateTime(vm.DateTo);
            api.ImportHandler = (_, from, to, _) => { Assert.Equal(originalFrom, from); Assert.Equal(originalTo, to); return post.Task; };
            api.QueryHandler = (_, _, _, _, _, _) => query.Task;
            try
            {
                Task import = vm.ImportFromKsefCommand.ExecuteAsync(null);
                vm.DateFrom = vm.DateFrom.AddDays(-2); vm.DateTo = vm.DateTo.AddDays(1);
                vm.SelectedInvoiceDateKind = InvoiceDateKind.InvoicingDate;
                Assert.True(vm.IsLoading);
                Assert.False(Assert.Single(api.ImportContexts).CancellationToken.IsCancellationRequested);
                Assert.Empty(api.QueryFilters);
                post.SetResult(new InvoicePackageReadResult { ReferenceNumber = "REF", ImportedCount = 1, SkippedCount = 0, ReadInvoiceCount = 1, DeclaredInvoiceCount = 1 });
                await WpfTestDispatcher.DrainAsync();
                var filter = Assert.Single(api.QueryFilters);
                Assert.Equal(DateOnly.FromDateTime(vm.DateFrom), filter.From);
                Assert.Equal(DateOnly.FromDateTime(vm.DateTo), filter.To);
                Assert.Equal(InvoiceDateKind.InvoicingDate, filter.DateKind);
                Assert.False(string.IsNullOrWhiteSpace(filter.TimeZoneId));
                if (changeDuringRefresh) vm.DateTo = vm.DateTo.AddDays(1);
                Assert.True(vm.IsLoading);
                Assert.False(api.ImportContexts[0].CancellationToken.IsCancellationRequested);
                query.SetResult(QueryResult("IMPORTED", 456m));
                await import;
                Assert.Single(api.ImportContexts); Assert.Single(api.QueryFilters);
                Assert.False(vm.IsLoading);
                if (changeDuringRefresh) { Assert.Empty(vm.SalesInvoices); Assert.Empty(vm.SalesSummaries); Assert.Contains("Filtr zmienił się", vm.Status); }
                else { Assert.Equal("IMPORTED", Assert.Single(vm.SalesInvoices).KsefNumber); Assert.Equal(456m, Assert.Single(vm.SalesSummaries).NetTotal); }
            }
            finally { post.TrySetCanceled(); query.TrySetResult(QueryResult("END")); vm.Cleanup(); await WpfTestDispatcher.DrainAsync(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ImportFailureIsDistinguishedFromRefreshFailureAndNeverRepeatsPost(bool refreshFails)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var (vm, api, _, _) = CreateQueryModel();
            if (!refreshFails) api.ImportHandler = (_, _, _, _) => Task.FromException<InvoicePackageReadResult>(new InvalidOperationException("POST failed"));
            api.QueryHandler = (_, _, _, _, _, _) => Task.FromException<InvoiceQueryResultDto>(new InvalidOperationException("GET failed"));
            try
            {
                await vm.ImportFromKsefCommand.ExecuteAsync(null);
                Assert.Single(api.ImportContexts);
                Assert.Equal(refreshFails ? 1 : 0, api.QueryFilters.Count);
                Assert.Contains(refreshFails ? "Import zakończony poprawnie, ale odświeżenie listy nie powiodło się" : "Import nie powiódł się", vm.Status);
                Assert.False(vm.IsLoading);
                Assert.Empty(vm.SalesInvoices); Assert.Empty(vm.SalesSummaries);
            }
            finally { vm.Cleanup(); await WpfTestDispatcher.DrainAsync(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AnOldImportCannotReleaseLoadingOrRefreshTheNewTenant(bool postFails)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var (vm, api, _, tenant) = CreateQueryModel();
            var post = NewCompletionSource<InvoicePackageReadResult>();
            var query = NewCompletionSource<InvoiceQueryResultDto>();
            api.ImportHandler = (_, _, _, _) => post.Task;
            api.QueryHandler = (_, _, _, _, _, _) => query.Task;
            try
            {
                Task import = vm.ImportFromKsefCommand.ExecuteAsync(null);
                tenant.SetTenant(new TenantDto(Guid.NewGuid(), "New tenant", "User"));
                await WpfTestDispatcher.DrainAsync();
                Task load = vm.LoadInvoicesCommand.ExecuteAsync(null);
                if (postFails) post.SetException(new InvalidOperationException("Old POST failed"));
                else post.SetResult(new InvoicePackageReadResult { ReferenceNumber = "OLD", ImportedCount = 1, SkippedCount = 0, ReadInvoiceCount = 1, DeclaredInvoiceCount = 1 });
                await import;
                Assert.True(vm.IsLoading);
                Assert.Equal("Pobieranie faktur...", vm.Status);
                Assert.Single(api.QueryFilters);
                query.SetResult(QueryResult("NEW", 222m));
                await load;
                Assert.False(vm.IsLoading);
                Assert.Equal("NEW", Assert.Single(vm.SalesInvoices).KsefNumber);
                Assert.Equal(222m, Assert.Single(vm.SalesSummaries).NetTotal);
            }
            finally { post.TrySetCanceled(); query.TrySetResult(QueryResult("END")); vm.Cleanup(); await WpfTestDispatcher.DrainAsync(); }
        });
    }

    [Fact]
    public Task ReentrantFilterChangeDuringPublicationCannotLeaveOldSummariesOrRows()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var (vm, api, _, _) = CreateQueryModel();
            api.QueryHandler = (_, _, _, _, _, _) => Task.FromResult(new InvoiceQueryResultDto([CreateInvoice("A"), CreateInvoice("B")], QueryResult("A").Summaries));
            bool changed = false;
            System.Collections.Specialized.NotifyCollectionChangedEventHandler handler = (_, args) =>
            {
                if (!changed && args.NewItems?.Count > 0) { changed = true; vm.SelectedInvoiceDateKind = InvoiceDateKind.InvoicingDate; }
            };
            vm.SalesInvoices.CollectionChanged += handler;
            try
            {
                await vm.LoadInvoicesCommand.ExecuteAsync(null);
                Assert.True(changed);
                Assert.Empty(vm.SalesInvoices); Assert.Empty(vm.SalesSummaries);
                Assert.False(vm.HasSalesQueryResult);
                Assert.False(vm.IsLoading);
            }
            finally { vm.SalesInvoices.CollectionChanged -= handler; vm.Cleanup(); await WpfTestDispatcher.DrainAsync(); }
        });
    }

    private static InvoiceQueryResultDto QueryResult(string number, decimal marker = 777m) => new([CreateInvoice(number)], [new InvoiceCurrencySummaryDto("EUR", 1, marker, marker + 1m, marker + 2m, 0, 0)]);

    private static (MainViewModel Vm, FakeInvoiceApiClient Api, TokenStore Tokens, TenantContext Tenant) CreateQueryModel()
    {
        var tokens = new TokenStore { AccessToken = "token-A" };
        var tenant = new TenantContext();
        var selected = new TenantDto(Guid.NewGuid(), "Tenant A", "User");
        tenant.SetTenant(selected);
        var api = new FakeInvoiceApiClient();
        var vm = CreateViewModel(api, tokens, tenant, new FakeTenantApiClient { Tenants = [selected] });
        vm.IsLoggedIn = true;
        return (vm, api, tokens, tenant);
    }

    [Fact]
    public Task InvoicePanelsResizeWithOneSharedSplitterAndKeepTheRatioAcrossTabs()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), new TokenStore(), new TenantContext(), new FakeTenantApiClient());
            var panels = CreateInvoicePanels(viewModel);
            var window = new Window { Content = panels, Width = 1800, Height = 400, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            try
            {
                Assert.Equal(3, panels.ColumnDefinitions.Count);
                var left = panels.ColumnDefinitions[0];
                var right = panels.ColumnDefinitions[2];
                Assert.True(left.Width.IsStar);
                Assert.True(right.Width.IsStar);
                Assert.Equal(3, left.Width.Value);
                Assert.Equal(2, right.Width.Value);
                Assert.Equal(300, left.MinWidth);
                Assert.Equal(300, right.MinWidth);
                var tabs = panels.Children.OfType<TabControl>().Single();
                Assert.Equal(0, Grid.GetColumn(tabs));
                Assert.Equal(1, Grid.GetColumnSpan(tabs));
                var splitter = Assert.Single(panels.Children.OfType<GridSplitter>());
                Assert.Equal(1, Grid.GetColumn(splitter));
                Assert.Equal(GridResizeDirection.Columns, splitter.ResizeDirection);
                Assert.Equal(GridResizeBehavior.PreviousAndNext, splitter.ResizeBehavior);
                Assert.True(splitter.ShowsPreview);
                Assert.Equal(2, Grid.GetColumn(panels.Children.OfType<Border>().Single()));

                window.Show();
                await WpfTestDispatcher.DrainAsync();
                panels.UpdateLayout();
                double total = left.ActualWidth + right.ActualWidth;
                Assert.Equal(0.6, left.ActualWidth / total, 4);
                double initialLeft = left.ActualWidth;
                DragInvoiceSplitter(splitter, 80);
                panels.UpdateLayout();
                Assert.Equal(initialLeft + 80, left.ActualWidth, 2);
                Assert.Equal(total, left.ActualWidth + right.ActualWidth, 2);
                double resizedLeft = left.ActualWidth;
                double resizedRatio = resizedLeft / total;

                tabs.SelectedIndex = 1;
                await WpfTestDispatcher.DrainAsync();
                panels.UpdateLayout();
                Assert.Equal(resizedLeft, left.ActualWidth, 2);
                tabs.SelectedIndex = 0;
                await WpfTestDispatcher.DrainAsync();
                panels.UpdateLayout();
                Assert.Equal(resizedLeft, left.ActualWidth, 2);

                window.Width = 1500;
                await WpfTestDispatcher.DrainAsync();
                panels.UpdateLayout();
                Assert.Equal(resizedRatio, left.ActualWidth / (left.ActualWidth + right.ActualWidth), 4);
                DragInvoiceSplitter(splitter, 100000);
                panels.UpdateLayout();
                Assert.Equal(right.MinWidth, right.ActualWidth, 2);
                DragInvoiceSplitter(splitter, -100000);
                panels.UpdateLayout();
                Assert.Equal(left.MinWidth, left.ActualWidth, 2);

                window.Width = 660;
                await WpfTestDispatcher.DrainAsync();
                panels.UpdateLayout();
                Assert.True(left.ActualWidth >= left.MinWidth);
                Assert.True(right.ActualWidth >= right.MinWidth);
                Assert.True(panels.ColumnDefinitions.Sum(column => column.ActualWidth) <= panels.ActualWidth + 0.1);
            }
            finally
            {
                window.Close();
                panels.DataContext = null;
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InvoiceGridUsesCompactWidthsAndScrollsWhenItCannotFit(bool purchase)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), new TokenStore(), new TenantContext(), new FakeTenantApiClient());
            (purchase ? viewModel.PurchaseInvoices : viewModel.SalesInvoices).Add(CreateInvoice("SCROLL"));
            var grid = CreateInvoiceGrid(viewModel, purchase);
            try
            {
                Assert.Equal(110, grid.Columns[1].Width.Value);
                Assert.Equal(140, grid.Columns[3].Width.Value);
                double[] minima = [130, 80, 110, 100, 115, 155, 65, 95, 95, 95];
                Assert.Equal(minima, grid.Columns.Select(column => column.MinWidth));
                for (int index = 0; index < grid.Columns.Count; index++)
                    if (index is not (1 or 3)) Assert.True(grid.Columns[index].Width.IsSizeToCells);

                await WpfTestDispatcher.DrainAsync();
                var scroll = VisualChildren<ScrollViewer>(grid).First();
                Assert.Equal(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility);
                Assert.Equal(0, scroll.ScrollableWidth);
                Assert.Equal(Visibility.Collapsed, scroll.ComputedHorizontalScrollBarVisibility);

                grid.Measure(new Size(650, 400));
                grid.Arrange(new Rect(0, 0, 650, 400));
                grid.UpdateLayout();
                await WpfTestDispatcher.DrainAsync();
                Assert.True(scroll.ScrollableWidth > 0);
                Assert.Equal(Visibility.Visible, scroll.ComputedHorizontalScrollBarVisibility);

                grid.Measure(new Size(1600, 400));
                grid.Arrange(new Rect(0, 0, 1600, 400));
                grid.UpdateLayout();
                await WpfTestDispatcher.DrainAsync();
                Assert.Equal(0, scroll.ScrollableWidth);
                Assert.Equal(Visibility.Collapsed, scroll.ComputedHorizontalScrollBarVisibility);
            }
            finally
            {
                grid.DataContext = null;
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TrimmedInvoiceCellsKeepTheirFullTooltipAndNativeClipboardValues(bool purchase)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant A", "User"));
            var invoiceClient = new FakeInvoiceApiClient();
            var viewModel = CreateViewModel(invoiceClient, tokenStore, tenantContext, new FakeTenantApiClient());
            viewModel.IsLoggedIn = true;
            viewModel.SelectedTabIndex = purchase ? 1 : 0;
            string fullNumber = "1234567890-20261113-" + new string('A', 60);
            string fullName = "Pełna nazwa kontrahenta z Łodzi " + new string('B', 100);
            var invoice = CreateInvoice(fullNumber);
            invoice.BuyerName = fullName;
            invoice.SellerName = fullName;
            (purchase ? viewModel.PurchaseInvoices : viewModel.SalesInvoices).Add(invoice);
            var grid = CreateInvoiceGrid(viewModel, purchase);
            var previousClipboard = await ReadClipboardAsync();
            try
            {
                await WpfTestDispatcher.DrainAsync();
                foreach (int index in new[] { 1, 3 })
                {
                    var text = CellText(grid, invoice, index);
                    string fullText = index == 1 ? fullNumber : fullName;
                    Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
                    Assert.Equal(fullText, text.Text);
                    Assert.Equal(fullText, text.ToolTip);
                    var formatted = new FormattedText(fullText, System.Globalization.CultureInfo.GetCultureInfo("pl-PL"), FlowDirection.LeftToRight, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
                    Assert.True(formatted.WidthIncludingTrailingWhitespace > text.ActualWidth);
                }
                grid.CurrentCell = new DataGridCellInfo(invoice, grid.Columns[1]);
                await (viewModel.LoadInvoicePdfCommand.ExecutionTask ?? Task.CompletedTask);
                grid.SelectedCells.Add(new DataGridCellInfo(invoice, grid.Columns[1]));
                grid.SelectedCells.Add(new DataGridCellInfo(invoice, grid.Columns[3]));
                System.Windows.Input.ApplicationCommands.Copy.Execute(null, grid);
                Assert.Equal(fullNumber + "\t\t" + fullName + "\r\n", Clipboard.GetText(TextDataFormat.UnicodeText));
                grid.CurrentCell = new DataGridCellInfo(invoice, grid.Columns[3]);
                Assert.Single(invoiceClient.PdfContexts);
            }
            finally
            {
                try
                {
                    if (previousClipboard is null) Clipboard.Clear();
                    else await RestoreClipboardAsync(previousClipboard);
                }
                finally
                {
                    grid.DataContext = null;
                    viewModel.Cleanup();
                    await WpfTestDispatcher.DrainAsync();
                }
            }
        });
    }

    [Theory]
    [InlineData("2026-01-15T23:30:45+09:00")]
    [InlineData("2026-07-15T23:30:45-07:00")]
    public void LocalDateConverterPreservesSourceAndUsesTheUsersTimeZone(string input)
    {
        var original = DateTimeOffset.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        var converter = new xKsef.Wpf.Converters.LocalDateTimeOffsetConverter();
        var culture = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
        string expected = TimeZoneInfo.ConvertTimeFromUtc(original.UtcDateTime, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm", culture);
        Assert.Equal(expected, converter.Convert(original, typeof(string), null, culture));
        Assert.Equal(input, original.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(string.Empty, converter.Convert(null, typeof(string), null, culture));
        Assert.Same(System.Windows.Data.Binding.DoNothing, converter.ConvertBack("edited", typeof(DateTimeOffset), null, culture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InvoiceGridHasTheTenRequestedColumnsInOrder(bool purchase)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), new TokenStore(), new TenantContext(), new FakeTenantApiClient());
            var grid = CreateInvoiceGrid(viewModel, purchase);
            try
            {
                string[] paths = ["InvoiceNumber", "KsefNumber", purchase ? "SellerNip" : "BuyerNip", purchase ? "SellerName" : "BuyerName", "IssueDate", "InvoicingDate", "Currency", "NetAmount", "VatAmount", "GrossAmount"];
                string[] headers = ["Numer faktury", "Numer KSeF", purchase ? "NIP sprzedawcy" : "NIP nabywcy", purchase ? "Nazwa sprzedawcy" : "Nazwa nabywcy", "Data wystawienia", "Data przyjęcia do KSeF", "Waluta", "Netto", "VAT", "Brutto"];
                Assert.Equal(10, grid.Columns.Count);
                Assert.Equal(headers, grid.Columns.Select(column => (string)column.Header));
                Assert.Equal(paths, grid.Columns.Cast<System.Windows.Controls.DataGridTextColumn>().Select(column => ((System.Windows.Data.Binding)column.Binding).Path.Path));
                Assert.False(grid.AutoGenerateColumns);
                Assert.Equal(System.Windows.Controls.ScrollBarVisibility.Auto, System.Windows.Controls.ScrollViewer.GetHorizontalScrollBarVisibility(grid));
                Assert.All(grid.Columns, column => Assert.True(column.MinWidth >= 65));
                var dateBinding = (System.Windows.Data.Binding)((System.Windows.Controls.DataGridTextColumn)grid.Columns[5]).Binding;
                Assert.Equal(System.Windows.Data.BindingMode.OneWay, dateBinding.Mode);
                Assert.IsType<xKsef.Wpf.Converters.LocalDateTimeOffsetConverter>(dateBinding.Converter);
            }
            finally
            {
                grid.DataContext = null;
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task InvoiceGridDisplaysPolishAmountsLocalDatesAndEmptyNullCells(bool purchase, bool missing)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), new TokenStore(), new TenantContext(), new FakeTenantApiClient());
            var originalDate = new DateTimeOffset(2026, 7, 15, 23, 30, 45, TimeSpan.FromHours(9));
            var invoice = new InvoiceDto
            {
                InvoiceNumber = "FV/1", KsefNumber = "KSEF-1", SellerName = "Seller", SellerNip = "1234567890",
                BuyerName = missing ? null : "Buyer", BuyerNip = missing ? null : "9876543210",
                IssueDate = new DateOnly(2026, 11, 12), InvoicingDate = missing ? null : originalDate, Currency = "EUR",
                NetAmount = missing ? null : 0m, VatAmount = missing ? null : -1234.5m, GrossAmount = missing ? 0m : 1234.5m
            };
            (purchase ? viewModel.PurchaseInvoices : viewModel.SalesInvoices).Add(invoice);
            var grid = CreateInvoiceGrid(viewModel, purchase);
            try
            {
                await WpfTestDispatcher.DrainAsync();
                Assert.Equal("FV/1", CellText(grid, invoice, 0).Text);
                Assert.Equal("KSEF-1", CellText(grid, invoice, 1).Text);
                Assert.Equal(purchase ? "1234567890" : missing ? "" : "9876543210", CellText(grid, invoice, 2).Text);
                Assert.Equal(purchase ? "Seller" : missing ? "" : "Buyer", CellText(grid, invoice, 3).Text);
                Assert.Equal("2026-11-12", CellText(grid, invoice, 4).Text);
                string expectedDate = missing ? "" : TimeZoneInfo.ConvertTimeFromUtc(originalDate.UtcDateTime, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(expectedDate, CellText(grid, invoice, 5).Text);
                Assert.Equal("EUR", CellText(grid, invoice, 6).Text);
                string groupedAmount = "1" + System.Globalization.CultureInfo.GetCultureInfo("pl-PL").NumberFormat.NumberGroupSeparator + "234,50";
                Assert.Equal(missing ? "" : "0,00", CellText(grid, invoice, 7).Text);
                Assert.Equal(missing ? "" : "-" + groupedAmount, CellText(grid, invoice, 8).Text);
                Assert.Equal(missing ? "0,00" : groupedAmount, CellText(grid, invoice, 9).Text);
                for (int column = 7; column < 10; column++)
                    Assert.Equal(TextAlignment.Right, CellText(grid, invoice, column).TextAlignment);
                Assert.Equal(missing ? (DateTimeOffset?)null : originalDate, invoice.InvoicingDate);
                Assert.Equal(missing ? (TimeSpan?)null : originalDate.Offset, invoice.InvoicingDate?.Offset);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = originalCulture;
                grid.DataContext = null;
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InvoiceGridNativeCopyKeepsLocalDatePolishNumbersNullsAndZeros(bool purchase)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
            var polish = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
            System.Globalization.CultureInfo.CurrentCulture = polish;
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant A", "User"));
            var invoiceClient = new FakeInvoiceApiClient();
            var viewModel = CreateViewModel(invoiceClient, tokenStore, tenantContext, new FakeTenantApiClient());
            viewModel.IsLoggedIn = true;
            viewModel.SelectedTabIndex = purchase ? 1 : 0;
            var sourceDate = new DateTimeOffset(2026, 7, 15, 23, 30, 45, TimeSpan.FromHours(9));
            var first = new InvoiceDto { KsefNumber = "COPY-A", InvoicingDate = sourceDate, Currency = "EUR", NetAmount = 1234.5m, VatAmount = 0m, GrossAmount = 1234.5m };
            var second = new InvoiceDto { KsefNumber = "COPY-B", Currency = "EUR", GrossAmount = 0m };
            var invoices = purchase ? viewModel.PurchaseInvoices : viewModel.SalesInvoices;
            invoices.Add(first);
            invoices.Add(second);
            var grid = CreateInvoiceGrid(viewModel, purchase);
            var previousClipboard = await ReadClipboardAsync();
            try
            {
                grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(first, grid.Columns[5]);
                await (viewModel.LoadInvoicePdfCommand.ExecutionTask ?? Task.CompletedTask);
                foreach (var invoice in invoices)
                    for (int column = 5; column < 10; column++)
                        grid.SelectedCells.Add(new System.Windows.Controls.DataGridCellInfo(invoice, grid.Columns[column]));
                System.Windows.Input.ApplicationCommands.Copy.Execute(null, grid);
                string[] rows = Clipboard.GetText(TextDataFormat.UnicodeText).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(2, rows.Length);
                string[] values = rows[0].Split('\t');
                string[] nulls = rows[1].Split('\t');
                Assert.Equal(5, values.Length);
                Assert.Equal(5, nulls.Length);
                Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(sourceDate.UtcDateTime, TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm", polish), values[0]);
                Assert.Equal("EUR", values[1]);
                Assert.Contains(",", values[2]);
                Assert.DoesNotContain(".", values[2]);
                Assert.Equal(1234.5m, decimal.Parse(values[2], System.Globalization.NumberStyles.Number, polish));
                Assert.Equal(0m, decimal.Parse(values[3], System.Globalization.NumberStyles.Number, polish));
                Assert.Equal(1234.5m, decimal.Parse(values[4], System.Globalization.NumberStyles.Number, polish));
                Assert.Equal("", nulls[0]);
                Assert.Equal("EUR", nulls[1]);
                Assert.Equal("", nulls[2]);
                Assert.Equal("", nulls[3]);
                Assert.Equal(0m, decimal.Parse(nulls[4], System.Globalization.NumberStyles.Number, polish));
                Assert.Equal(sourceDate.Offset, first.InvoicingDate!.Value.Offset);
                Assert.Equal(sourceDate.Ticks, first.InvoicingDate.Value.Ticks);
                Assert.Single(invoiceClient.PdfContexts);
            }
            finally
            {
                try
                {
                    if (previousClipboard is null) Clipboard.Clear();
                    else await RestoreClipboardAsync(previousClipboard);
                }
                finally
                {
                    System.Globalization.CultureInfo.CurrentCulture = originalCulture;
                    grid.DataContext = null;
                    viewModel.Cleanup();
                    await WpfTestDispatcher.DrainAsync();
                }
            }
        });
    }

    private static System.Windows.Controls.TextBlock CellText(System.Windows.Controls.DataGrid grid, InvoiceDto invoice, int column)
    {
        grid.ScrollIntoView(invoice, grid.Columns[column]);
        grid.UpdateLayout();
        return Assert.IsType<System.Windows.Controls.TextBlock>(grid.Columns[column].GetCellContent(invoice));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InvoiceGridCurrentItemBindingLoadsPdfOnlyWhenInvoiceChanges(bool purchase)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant A", "User"));
            var invoiceClient = new FakeInvoiceApiClient { GetInvoicePdfHandler = (_, _) => Task.FromResult<byte[]>([1, 2, 3]) };
            var viewModel = CreateViewModel(invoiceClient, tokenStore, tenantContext, new FakeTenantApiClient());
            viewModel.IsLoggedIn = true;
            viewModel.SelectedTabIndex = purchase ? 1 : 0;
            var first = CreateInvoice("PDF-A");
            var second = CreateInvoice("PDF-B");
            var invoices = purchase ? viewModel.PurchaseInvoices : viewModel.SalesInvoices;
            invoices.Add(first);
            invoices.Add(second);
            var grid = CreateInvoiceGrid(viewModel, purchase);
            try
            {
                Assert.Equal(System.Windows.Data.BindingMode.TwoWay, System.Windows.Data.BindingOperations.GetBinding(grid, System.Windows.Controls.DataGrid.CurrentItemProperty)!.Mode);
                Assert.Equal(System.Windows.Controls.DataGridSelectionUnit.Cell, grid.SelectionUnit);
                Assert.Equal(System.Windows.Controls.DataGridSelectionMode.Extended, grid.SelectionMode);
                Assert.True(grid.IsReadOnly);
                for (int column = 0; column < grid.Columns.Count; column++)
                {
                    grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(first, grid.Columns[column]);
                    await (viewModel.LoadInvoicePdfCommand.ExecutionTask ?? Task.CompletedTask);
                    Assert.Same(first, purchase ? viewModel.SelectedPurchaseInvoice : viewModel.SelectedSalesInvoice);
                    Assert.Single(invoiceClient.PdfContexts);
                    Assert.NotNull(viewModel.SelectedInvoicePdfPath);
                }
                string firstPath = viewModel.SelectedInvoicePdfPath!;
                grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(second, grid.Columns[2]);
                await (viewModel.LoadInvoicePdfCommand.ExecutionTask ?? Task.CompletedTask);
                Assert.Same(second, purchase ? viewModel.SelectedPurchaseInvoice : viewModel.SelectedSalesInvoice);
                Assert.Equal(2, invoiceClient.PdfContexts.Count);
                Assert.NotEqual(firstPath, viewModel.SelectedInvoicePdfPath);
                Assert.Empty(grid.SelectedItems);

                if (purchase) viewModel.SelectedPurchaseInvoice = first;
                else viewModel.SelectedSalesInvoice = first;
                await (viewModel.LoadInvoicePdfCommand.ExecutionTask ?? Task.CompletedTask);
                await WpfTestDispatcher.DrainAsync();
                Assert.Same(first, grid.CurrentItem);
                Assert.Equal(3, invoiceClient.PdfContexts.Count);
            }
            finally
            {
                grid.DataContext = null;
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InvoiceGridNativeCopyPreservesCellLayoutWithoutHeadersOrExtraPdfRequests(bool purchase)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant A", "User"));
            var invoiceClient = new FakeInvoiceApiClient();
            var viewModel = CreateViewModel(invoiceClient, tokenStore, tenantContext, new FakeTenantApiClient());
            viewModel.IsLoggedIn = true;
            viewModel.SelectedTabIndex = purchase ? 1 : 0;
            var first = CreateInvoice("FV/A");
            first.SellerName = "Seller A";
            first.SellerNip = "1234567890";
            first.BuyerName = "Buyer A";
            first.BuyerNip = "9876543210";
            var second = CreateInvoice("FV/B");
            second.SellerName = "Seller B";
            second.SellerNip = "1234567891";
            second.BuyerName = "Buyer B";
            second.BuyerNip = "9876543211";
            var invoices = purchase ? viewModel.PurchaseInvoices : viewModel.SalesInvoices;
            invoices.Add(first);
            invoices.Add(second);
            var grid = CreateInvoiceGrid(viewModel, purchase);
            var previousClipboard = await ReadClipboardAsync();
            try
            {
                grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(first, grid.Columns[1]);
                await (viewModel.LoadInvoicePdfCommand.ExecutionTask ?? Task.CompletedTask);
                foreach (var invoice in invoices)
                    foreach (int column in new[] { 2, 3 })
                        grid.SelectedCells.Add(new System.Windows.Controls.DataGridCellInfo(invoice, grid.Columns[column]));

                Assert.Equal(System.Windows.Controls.DataGridClipboardCopyMode.ExcludeHeader, grid.ClipboardCopyMode);
                Assert.True(System.Windows.Input.ApplicationCommands.Copy.CanExecute(null, grid));
                System.Windows.Input.ApplicationCommands.Copy.Execute(null, grid);
                string expected = purchase ? "1234567890\tSeller A\r\n1234567891\tSeller B\r\n" : "9876543210\tBuyer A\r\n9876543211\tBuyer B\r\n";
                Assert.Equal(expected, Clipboard.GetText(TextDataFormat.UnicodeText));
                Assert.Single(invoiceClient.PdfContexts);
                Assert.Same(first, purchase ? viewModel.SelectedPurchaseInvoice : viewModel.SelectedSalesInvoice);
            }
            finally
            {
                if (previousClipboard is null) Clipboard.Clear();
                else await RestoreClipboardAsync(previousClipboard);
                grid.DataContext = null;
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task InvoiceGridIgnoresLatePdfAfterContextChanges(bool purchase, bool changeSession)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant A", "User"));
            var response = NewCompletionSource<byte[]>();
            var invoiceClient = new FakeInvoiceApiClient { GetInvoicePdfHandler = (_, _) => response.Task };
            var viewModel = CreateViewModel(invoiceClient, tokenStore, tenantContext, new FakeTenantApiClient());
            viewModel.IsLoggedIn = true;
            viewModel.SelectedTabIndex = purchase ? 1 : 0;
            var invoice = CreateInvoice("OLD-PDF");
            (purchase ? viewModel.PurchaseInvoices : viewModel.SalesInvoices).Add(invoice);
            var grid = CreateInvoiceGrid(viewModel, purchase);
            try
            {
                grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(invoice, grid.Columns[3]);
                var loading = viewModel.LoadInvoicePdfCommand.ExecutionTask!;
                Assert.Single(invoiceClient.PdfContexts);
                if (changeSession) tokenStore.AccessToken = "token-B";
                else tenantContext.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant B", "User"));
                await WpfTestDispatcher.DrainAsync();
                response.SetResult([1, 2, 3]);
                await loading;
                await WpfTestDispatcher.DrainAsync();
                Assert.Null(viewModel.SelectedInvoicePdfPath);
                Assert.True(invoiceClient.PdfContexts[0].CancellationToken.IsCancellationRequested);
                Assert.Single(invoiceClient.PdfContexts);
            }
            finally
            {
                response.TrySetResult([]);
                grid.DataContext = null;
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InvoiceGridNativeKeyboardNavigationKeepsPdfForTheCurrentRow(bool purchase)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant A", "User"));
            var invoiceClient = new FakeInvoiceApiClient();
            var viewModel = CreateViewModel(invoiceClient, tokenStore, tenantContext, new FakeTenantApiClient());
            viewModel.IsLoggedIn = true;
            viewModel.SelectedTabIndex = purchase ? 1 : 0;
            var first = CreateInvoice("PDF-A");
            var second = CreateInvoice("PDF-B");
            var invoices = purchase ? viewModel.PurchaseInvoices : viewModel.SalesInvoices;
            invoices.Add(first);
            invoices.Add(second);
            var grid = CreateInvoiceGrid(viewModel, purchase);
            var window = new Window { Content = grid, Width = 1600, Height = 400, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            try
            {
                window.Show();
                await WpfTestDispatcher.DrainAsync();
                grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(first, grid.Columns[0]);
                var cell = FindGridCell(grid.Columns[0].GetCellContent(first));
                Assert.True(cell.Focus());
                await (viewModel.LoadInvoicePdfCommand.ExecutionTask ?? Task.CompletedTask);
                Assert.Single(invoiceClient.PdfContexts);
                SendGridKey(grid, System.Windows.Input.Key.Right);
                Assert.Same(grid.Columns[1], grid.CurrentCell.Column);
                SendGridKey(grid, System.Windows.Input.Key.Tab);
                Assert.Same(grid.Columns[2], grid.CurrentCell.Column);
                Assert.True(((UIElement)System.Windows.Input.Keyboard.FocusedElement).MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Previous)));
                Assert.Same(grid.Columns[1], grid.CurrentCell.Column);
                Assert.Single(invoiceClient.PdfContexts);
                SendGridKey(grid, System.Windows.Input.Key.Down);
                await (viewModel.LoadInvoicePdfCommand.ExecutionTask ?? Task.CompletedTask);
                Assert.Same(second, grid.CurrentItem);
                Assert.Same(second, purchase ? viewModel.SelectedPurchaseInvoice : viewModel.SelectedSalesInvoice);
                Assert.Equal(2, invoiceClient.PdfContexts.Count);
            }
            finally
            {
                window.Close();
                grid.DataContext = null;
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    private static System.Windows.Controls.DataGridCell FindGridCell(DependencyObject element)
    {
        while (element is not System.Windows.Controls.DataGridCell)
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        return (System.Windows.Controls.DataGridCell)element;
    }

    private static void SendGridKey(System.Windows.Controls.DataGrid grid, System.Windows.Input.Key key)
    {
        var args = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid), Environment.TickCount, key)
        {
            RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent
        };
        ((UIElement)System.Windows.Input.Keyboard.FocusedElement).RaiseEvent(args);
        Assert.True(args.Handled);
    }

    private static async Task<IDataObject?> ReadClipboardAsync()
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return Clipboard.GetDataObject();
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x800401D0) && attempt < 9)
            {
                await Task.Delay(100);
            }
        }
    }

    private static async Task RestoreClipboardAsync(IDataObject previousClipboard)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(previousClipboard, true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 9)
            {
                await Task.Delay(100);
            }
        }
    }

    private static void AddWindowResources(XElement element, XDocument document)
    {
        // Load the production XAML and resources without constructing the native PDF viewer.
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        foreach (var attribute in document.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
        {
            string value = attribute.Value == "clr-namespace:xKsef.Wpf.Converters" ? attribute.Value + ";assembly=xKsef.Wpf" : attribute.Value;
            element.SetAttributeValue(attribute.Name, value);
        }
        var resources = document.Root.Element(ns + "Window.Resources");
        if (resources is not null)
            element.AddFirst(new XElement(ns + (element.Name.LocalName + ".Resources"), resources.Elements().Select(resource => new XElement(resource))));
        foreach (var resource in element.Descendants().Where(resource => resource.Name.NamespaceName == "clr-namespace:xKsef.Wpf.Converters"))
            resource.Name = XName.Get(resource.Name.LocalName, "clr-namespace:xKsef.Wpf.Converters;assembly=xKsef.Wpf");
    }

    private static Grid CreateInvoicePanels(MainViewModel viewModel)
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Wpf", "MainWindow.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var element = new XElement(document.Descendants(ns + "TabControl").Single().Parent!);
        foreach (var viewer in element.Descendants().Where(child => child.Name.LocalName == "PDFViewer").ToArray())
            viewer.ReplaceWith(new XElement(ns + "Border"));
        AddWindowResources(element, document);
        var grid = (Grid)System.Windows.Markup.XamlReader.Parse(element.ToString());
        grid.DataContext = viewModel;
        return grid;
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void DragInvoiceSplitter(GridSplitter splitter, double horizontalChange)
    {
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(horizontalChange, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new DragCompletedEventArgs(horizontalChange, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
    }

    private static System.Windows.Controls.DataGrid CreateInvoiceGrid(MainViewModel viewModel, bool purchase)
    {
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Wpf", "MainWindow.xaml"));
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        string source = purchase ? "{Binding PurchaseInvoices}" : "{Binding SalesInvoices}";
        var element = document.Descendants(ns + "DataGrid").Single(grid => (string?)grid.Attribute("ItemsSource") == source);
        element = new XElement(element);
        AddWindowResources(element, document);
        var grid = (System.Windows.Controls.DataGrid)System.Windows.Markup.XamlReader.Parse(element.ToString());
        grid.DataContext = viewModel;
        grid.Measure(new Size(2200, 400));
        grid.Arrange(new Rect(0, 0, 2200, 400));
        grid.UpdateLayout();
        return grid;
    }

    [Fact]
    public async Task InvoiceRequestUsesCapturedJwtAndTenantAfterSingletonsChange()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        var tokenStore = new TokenStore { AccessToken = "token-B" };
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(new TenantDto(tenantB, "Tenant B", "User"));
        using var transport = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("[]")));
        using var httpClient = CreatePipeline(tokenStore, tenantContext, transport);
        var client = new InvoiceApiClient(httpClient);
        var context = CreateContext(tenantA, "token-A", 10);

        await client.GetInvoicesAsync(context, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), InvoiceDirection.Sales);

        Assert.Equal("Bearer token-A", transport.LastAuthorization);
        Assert.Equal(tenantA.ToString("D"), transport.LastTenantId);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task InvoiceRequestWithoutJwtIsRejectedBeforeTransport()
    {
        await AssertInvoiceContextRejected(CreateContext(Guid.NewGuid(), null, 1));
    }

    [Fact]
    public async Task InvoiceRequestWithoutTenantIsRejectedBeforeTransport()
    {
        await AssertInvoiceContextRejected(CreateContext(null, "token-A", 1));
    }

    [Fact]
    public async Task InvoiceRequestWithWrongScopeIsRejectedBeforeTransport()
    {
        var context = CreateContext(Guid.NewGuid(), "token-A", 1) with { Scope = ApiRequestScope.User };
        var tokenStore = new TokenStore();
        var tenantContext = new TenantContext();
        using var transport = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("[]")));
        using var httpClient = CreatePipeline(tokenStore, tenantContext, transport);
        var client = new InvoiceApiClient(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetInvoicesAsync(context, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), InvoiceDirection.Sales));

        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public async Task CancelledContextIsRejectedBeforeTransport()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = CreateContext(Guid.NewGuid(), "token-A", 1) with { CancellationToken = cancellation.Token };
        var tokenStore = new TokenStore();
        var tenantContext = new TenantContext();
        using var transport = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("[]")));
        using var httpClient = CreatePipeline(tokenStore, tenantContext, transport);
        var client = new InvoiceApiClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetInvoicesAsync(context, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), InvoiceDirection.Sales));

        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public async Task UnauthorizedResponseForOldSessionDoesNotClearNewToken()
    {
        var tokenStore = new TokenStore { AccessToken = "token-old" };
        var oldSession = tokenStore.GetSessionSnapshot();
        tokenStore.AccessToken = "token-new";
        Guid tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        using var transport = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        using var httpClient = CreatePipeline(tokenStore, tenantContext, transport);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/invoices");
        request.SetApiRequestContext(CreateContext(tenantId, oldSession.AccessToken, oldSession.SessionVersion));

        using HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("token-new", tokenStore.AccessToken);
        Assert.NotEqual(oldSession.SessionVersion, tokenStore.SessionVersion);
    }

    [Fact]
    public Task ConstructorObservedSessionStillProcessesPendingTokenChangedUi()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            Guid tenantId = Guid.NewGuid();
            var tokenStore = new ManualTokenStore("token-A", 1);
            var tenantContext = new TenantContext();
            var tenantApiClient = new FakeTenantApiClient
            {
                Tenants = [new TenantDto(tenantId, "Tenant A", "User")]
            };
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, tenantApiClient);

            try
            {
                tokenStore.RaiseTokenChanged(1);
                await WpfTestDispatcher.DrainAsync();

                Assert.True(viewModel.IsLoggedIn);
                Assert.Equal(1, tenantApiClient.GetMyTenantsCallCount);
                Assert.Equal(tenantId, tenantContext.TenantId);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task DelayedInvoiceResponseForOldTenantCannotOverwriteNewTenantState()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenantA = new(Guid.NewGuid(), "Tenant A", "User");
            TenantDto tenantB = new(Guid.NewGuid(), "Tenant B", "User");
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenantA);
            var oldResponse = NewCompletionSource<IReadOnlyList<InvoiceDto>>();
            var newResponse = NewCompletionSource<IReadOnlyList<InvoiceDto>>();
            var invoiceClient = new FakeInvoiceApiClient();
            int requestNumber = 0;
            invoiceClient.GetInvoicesHandler = (_, _, _, _) =>
                Interlocked.Increment(ref requestNumber) == 1 ? oldResponse.Task : newResponse.Task;
            var viewModel = CreateViewModel(invoiceClient, tokenStore, tenantContext, new FakeTenantApiClient());
            viewModel.IsLoggedIn = true;

            try
            {
                Task oldLoad = viewModel.LoadInvoicesCommand.ExecuteAsync(null);
                Assert.Equal(1, requestNumber);

                viewModel.SelectedTenant = tenantB;
                await WpfTestDispatcher.DrainAsync();

                Task newLoad = viewModel.LoadInvoicesCommand.ExecuteAsync(null);
                Assert.Equal(2, requestNumber);

                oldResponse.SetResult([CreateInvoice("A-1")]);
                await oldLoad;

                Assert.Empty(viewModel.SalesInvoices);
                Assert.True(viewModel.IsLoading);
                Assert.Equal("Pobieranie faktur...", viewModel.Status);

                newResponse.SetResult([CreateInvoice("B-1")]);
                await newLoad;

                Assert.Collection(viewModel.SalesInvoices, invoice => Assert.Equal("B-1", invoice.KsefNumber));
                Assert.False(viewModel.IsLoading);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task DelayedInvoiceResponseForOldSessionCannotOverwriteCurrentStatusOrLoading()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenantA = new(Guid.NewGuid(), "Tenant A", "User");
            TenantDto tenantB = new(Guid.NewGuid(), "Tenant B", "User");
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenantA);
            var oldResponse = NewCompletionSource<IReadOnlyList<InvoiceDto>>();
            var newResponse = NewCompletionSource<IReadOnlyList<InvoiceDto>>();
            var invoiceClient = new FakeInvoiceApiClient();
            int requestNumber = 0;
            invoiceClient.GetInvoicesHandler = (_, _, _, _) =>
                Interlocked.Increment(ref requestNumber) == 1 ? oldResponse.Task : newResponse.Task;
            var tenantApiClient = new FakeTenantApiClient { Tenants = [tenantB] };
            var viewModel = CreateViewModel(invoiceClient, tokenStore, tenantContext, tenantApiClient);
            viewModel.IsLoggedIn = true;

            try
            {
                Task oldLoad = viewModel.LoadInvoicesCommand.ExecuteAsync(null);
                Assert.Equal(1, requestNumber);

                tokenStore.AccessToken = "token-B";
                await WpfTestDispatcher.DrainAsync();
                await WpfTestDispatcher.DrainAsync();

                Assert.Equal(tenantB.Id, tenantContext.TenantId);
                Task newLoad = viewModel.LoadInvoicesCommand.ExecuteAsync(null);
                Assert.Equal(2, requestNumber);

                oldResponse.SetResult([CreateInvoice("OLD-SESSION")]);
                await oldLoad;

                Assert.Empty(viewModel.SalesInvoices);
                Assert.True(viewModel.IsLoading);
                Assert.Equal("Pobieranie faktur...", viewModel.Status);

                newResponse.SetResult([CreateInvoice("NEW-SESSION")]);
                await newLoad;

                Assert.Collection(viewModel.SalesInvoices, invoice => Assert.Equal("NEW-SESSION", invoice.KsefNumber));
                Assert.False(viewModel.IsLoading);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task ImportAndRefreshUseTheSameSessionAndTenantWithIndependentReadCancellation()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenant = new(Guid.NewGuid(), "Tenant A", "User");
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenant);
            var invoiceClient = new FakeInvoiceApiClient
            {
                GetInvoicesHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<InvoiceDto>>([CreateInvoice("INV-1")])
            };
            var viewModel = CreateViewModel(invoiceClient, tokenStore, tenantContext, new FakeTenantApiClient());
            viewModel.IsLoggedIn = true;

            try
            {
                await viewModel.ImportFromKsefCommand.ExecuteAsync(null);

                Assert.Single(invoiceClient.ImportContexts);
                Assert.Single(invoiceClient.InvoiceContexts);
                Assert.Equal(invoiceClient.ImportContexts[0] with { CancellationToken = invoiceClient.InvoiceContexts[0].CancellationToken }, invoiceClient.InvoiceContexts[0]);
                Assert.NotEqual(invoiceClient.ImportContexts[0].CancellationToken, invoiceClient.InvoiceContexts[0].CancellationToken);
                Assert.Collection(viewModel.SalesInvoices, invoice => Assert.Equal("INV-1", invoice.KsefNumber));
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task ChangingInvoiceSelectionDoesNotDisplayOrLeaveTheOldPdf()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenant = new(Guid.NewGuid(), "Tenant A", "User");
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenant);
            var oldPdf = NewCompletionSource<byte[]>();
            var newPdf = NewCompletionSource<byte[]>();
            var invoiceClient = new FakeInvoiceApiClient
            {
                GetInvoicePdfHandler = (_, ksefNumber) => ksefNumber == "PDF-A" ? oldPdf.Task : newPdf.Task
            };
            var viewModel = CreateViewModel(invoiceClient, tokenStore, tenantContext, new FakeTenantApiClient());
            viewModel.IsLoggedIn = true;
            var loadedPath = NewCompletionSource<string>();
            PropertyChangedEventHandler propertyChanged = (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedInvoicePdfPath)
                    && viewModel.SelectedInvoicePdfPath is string path)
                    loadedPath.TrySetResult(path);
            };
            viewModel.PropertyChanged += propertyChanged;
            var existingPdfFiles = Directory.GetFiles(Path.GetTempPath(), "*.pdf").ToHashSet(StringComparer.OrdinalIgnoreCase);

            try
            {
                viewModel.SelectedSalesInvoice = CreateInvoice("PDF-A");
                viewModel.SelectedSalesInvoice = CreateInvoice("PDF-B");

                oldPdf.SetResult([1, 2, 3]);
                await WpfTestDispatcher.DrainAsync();

                Assert.Null(viewModel.SelectedInvoicePdfPath);
                Assert.DoesNotContain(Directory.GetFiles(Path.GetTempPath(), "*.pdf"), path => !existingPdfFiles.Contains(path));

                newPdf.SetResult([4, 5, 6]);
                string path = await loadedPath.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(File.Exists(path));
                Assert.Equal("Wyświetlono PDF faktury PDF-B.", viewModel.Status);

                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
                Assert.False(File.Exists(path));
            }
            finally
            {
                viewModel.PropertyChanged -= propertyChanged;
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public async Task KsefCredentialsClientUsesCapturedContextForEveryMethod()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Guid credentialId = Guid.NewGuid();
        var tokenStore = new TokenStore { AccessToken = "token-B" };
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(new TenantDto(tenantB, "Tenant B", "User"));
        using var transport = new RecordingHandler((request, _) =>
        {
            string json = request.Method == HttpMethod.Post
                ? System.Text.Json.JsonSerializer.Serialize(new { CredentialId = credentialId })
                : System.Text.Json.JsonSerializer.Serialize(new KsefTokenInfo(true, credentialId));
            return Task.FromResult(JsonResponse(json));
        });
        using var httpClient = CreatePipeline(tokenStore, tenantContext, transport);
        var client = new KsefCredentialsApiClient(httpClient);
        ApiRequestContext context = CreateContext(tenantA, "token-A", 14);

        KsefTokenInfo tokenInfo = await client.GetTokenInfoAsync(context);
        Assert.Equal(new KsefTokenInfo(true, credentialId), tokenInfo);
        AssertCapturedCredentialsContext(transport, tenantA);

        KsefTokenInfo certificateInfo = await client.GetCertificateInfoAsync(context);
        Assert.Equal(new KsefTokenInfo(true, credentialId), certificateInfo);
        AssertCapturedCredentialsContext(transport, tenantA);

        Guid savedCredentialId = await client.SaveTokenAsync(context, "secret-token");
        Assert.Equal(credentialId, savedCredentialId);
        AssertCapturedCredentialsContext(transport, tenantA);
        Assert.Equal(3, transport.RequestCount);
    }

    [Fact]
    public async Task KsefCredentialsClientRejectsInvalidContextsBeforeTransport()
    {
        var tokenStore = new TokenStore();
        var tenantContext = new TenantContext();
        using var transport = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("{}")));
        using var httpClient = CreatePipeline(tokenStore, tenantContext, transport);
        var client = new KsefCredentialsApiClient(httpClient);
        ApiRequestContext[] invalidContexts =
        [
            CreateContext(Guid.NewGuid(), null, 1),
            CreateContext(null, "token-A", 1),
            CreateContext(Guid.NewGuid(), "token-A", 1) with { Scope = ApiRequestScope.User }
        ];

        foreach (ApiRequestContext context in invalidContexts)
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetTokenInfoAsync(context));

        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public async Task AuthenticationApiClientUsesAnonymousAndTenantContexts()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Guid credentialId = Guid.NewGuid();
        var tokenStore = new TokenStore { AccessToken = "token-B" };
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(new TenantDto(tenantB, "Tenant B", "User"));
        using var transport = new RecordingHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/api/auth/login", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse(System.Text.Json.JsonSerializer.Serialize(new LoginResponse("login-token", "Bearer"))));
            if (path.EndsWith("/api/ksef/authentication/certificate", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse(System.Text.Json.JsonSerializer.Serialize(new { CredentialId = credentialId })));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var httpClient = CreatePipeline(tokenStore, tenantContext, transport);
        var client = new AuthenticationApiClient(httpClient);
        var anonymousContext = new ApiRequestContext(1, tokenStore.SessionVersion, null, null, CancellationToken.None, ApiRequestScope.Anonymous);

        LoginResponse? login = await client.LoginAsync(anonymousContext, "user", "password");
        Assert.NotNull(login);
        Assert.Null(transport.LastAuthorization);
        Assert.Null(transport.LastTenantId);

        ApiRequestContext tenantContextA = CreateContext(tenantA, "token-A", 22);
        await client.AuthenticateKsefTokenAsync(tenantContextA);
        AssertCapturedCredentialsContext(transport, tenantA);

        Guid savedCredential = await client.SaveKsefCertificateAsync(tenantContextA, "certificate", "private-key", "password");
        Assert.Equal(credentialId, savedCredential);
        AssertCapturedCredentialsContext(transport, tenantA);

        await client.AuthenticateKsefCertificateAsync(tenantContextA);
        AssertCapturedCredentialsContext(transport, tenantA);
        Assert.Equal(4, transport.RequestCount);
    }

    [Fact]
    public async Task AuthenticationApiClientRejectsWrongScopeOrMissingTenantDataBeforeTransport()
    {
        var tokenStore = new TokenStore();
        var tenantContext = new TenantContext();
        using var transport = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = CreatePipeline(tokenStore, tenantContext, transport);
        var client = new AuthenticationApiClient(httpClient);

        var wrongLoginScope = CreateContext(Guid.NewGuid(), "token-A", 1) with { Scope = ApiRequestScope.User };
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LoginAsync(wrongLoginScope, "user", "password"));

        var missingJwt = CreateContext(Guid.NewGuid(), null, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.AuthenticateKsefTokenAsync(missingJwt));

        var missingTenant = CreateContext(null, "token-A", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.AuthenticateKsefCertificateAsync(missingTenant));

        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public async Task LoginAsyncRejectsResponseAfterSessionVersionChanges()
    {
        var tokenStore = new TokenStore { AccessToken = "previous-token" };
        var response = NewCompletionSource<LoginResponse?>();
        var authenticationClient = new FakeAuthenticationApiClient
        {
            LoginHandler = (_, _, _) => response.Task
        };
        var viewModel = new LoginViewModel(authenticationClient, tokenStore, new FakeWindowNavigationService());
        viewModel.Login = "user";

        Task<bool> loginTask = viewModel.LoginAsync("password");
        ApiRequestContext requestContext = Assert.Single(authenticationClient.LoginContexts);
        Assert.Equal(ApiRequestScope.Anonymous, requestContext.Scope);
        Assert.Null(requestContext.AccessToken);
        Assert.Null(requestContext.TenantId);

        tokenStore.AccessToken = "newer-session-token";
        response.SetResult(new LoginResponse("stale-login-token", "Bearer"));

        Assert.False(await loginTask);
        Assert.Equal("newer-session-token", tokenStore.AccessToken);
    }

    [Fact]
    public async Task OlderParallelLoginAttemptCannotReplaceTokenOrErrorState()
    {
        var tokenStore = new TokenStore();
        var oldResponse = NewCompletionSource<LoginResponse?>();
        var currentResponse = NewCompletionSource<LoginResponse?>();
        int callNumber = 0;
        var authenticationClient = new FakeAuthenticationApiClient
        {
            LoginHandler = (_, _, _) => Interlocked.Increment(ref callNumber) == 1 ? oldResponse.Task : currentResponse.Task
        };
        var viewModel = new LoginViewModel(authenticationClient, tokenStore, new FakeWindowNavigationService())
        {
            Login = "user"
        };

        Task oldAttempt = viewModel.ExecuteLoginCommand.ExecuteAsync("password");
        Task currentAttempt = viewModel.ExecuteLoginCommand.ExecuteAsync("password");
        Assert.Equal(2, authenticationClient.LoginContexts.Count);
        Assert.Equal(authenticationClient.LoginContexts[0].SessionVersion, authenticationClient.LoginContexts[1].SessionVersion);
        Assert.NotEqual(authenticationClient.LoginContexts[0].Generation, authenticationClient.LoginContexts[1].Generation);

        currentResponse.SetResult(new LoginResponse("current-login-token", "Bearer"));
        await currentAttempt;
        oldResponse.SetResult(null);
        await oldAttempt;

        Assert.Equal("current-login-token", tokenStore.AccessToken);
        Assert.Equal(string.Empty, viewModel.ErrorMessage);
    }

    [Fact]
    public Task CredentialReadsShareContextAndIgnoreDelayedTokenInfoFromOldTenant()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenantA = new(Guid.NewGuid(), "Tenant A", "User");
            TenantDto tenantB = new(Guid.NewGuid(), "Tenant B", "User");
            Guid tenantBCredentialId = Guid.NewGuid();
            var oldTenantTokenInfo = NewCompletionSource<KsefTokenInfo>();
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            var credentialsClient = new FakeKsefCredentialsApiClient
            {
                GetTokenInfoHandler = context => context.TenantId == tenantA.Id
                    ? oldTenantTokenInfo.Task
                    : Task.FromResult(new KsefTokenInfo(true, tenantBCredentialId)),
                GetCertificateInfoHandler = _ => Task.FromResult(new KsefTokenInfo(false, null))
            };
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(), credentialsClient);
            viewModel.IsLoggedIn = true;

            try
            {
                viewModel.SelectedTenant = tenantA;
                await WpfTestDispatcher.DrainAsync();
                Assert.Single(credentialsClient.TokenInfoContexts);
                Assert.Empty(credentialsClient.CertificateInfoContexts);

                viewModel.SelectedTenant = tenantB;
                await WpfTestDispatcher.DrainAsync();

                Assert.Equal(2, credentialsClient.TokenInfoContexts.Count);
                Assert.Single(credentialsClient.CertificateInfoContexts);
                Assert.Equal(tenantB.Id, credentialsClient.TokenInfoContexts[1].TenantId);
                Assert.Equal(credentialsClient.TokenInfoContexts[1], credentialsClient.CertificateInfoContexts[0]);
                Assert.True(viewModel.HasKsefToken);
                Assert.Equal(tenantBCredentialId, viewModel.KsefCredentialId);

                viewModel.Status = "Aktualny status tenant B";
                oldTenantTokenInfo.SetResult(new KsefTokenInfo(false, null));
                await WpfTestDispatcher.DrainAsync();

                Assert.True(viewModel.HasKsefToken);
                Assert.Equal(tenantBCredentialId, viewModel.KsefCredentialId);
                Assert.Equal("Aktualny status tenant B", viewModel.Status);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task DelayedCertificateInfoErrorFromOldTenantCannotChangeCurrentStateOrStatus()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenantA = new(Guid.NewGuid(), "Tenant A", "User");
            TenantDto tenantB = new(Guid.NewGuid(), "Tenant B", "User");
            Guid tenantBCertificateId = Guid.NewGuid();
            var oldTenantCertificateInfo = NewCompletionSource<KsefTokenInfo>();
            var credentialsClient = new FakeKsefCredentialsApiClient
            {
                GetTokenInfoHandler = _ => Task.FromResult(new KsefTokenInfo(false, null)),
                GetCertificateInfoHandler = context => context.TenantId == tenantA.Id
                    ? oldTenantCertificateInfo.Task
                    : Task.FromResult(new KsefTokenInfo(true, tenantBCertificateId))
            };
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(), credentialsClient);
            viewModel.IsLoggedIn = true;

            try
            {
                viewModel.SelectedTenant = tenantA;
                await WpfTestDispatcher.DrainAsync();
                Assert.Single(credentialsClient.CertificateInfoContexts);

                viewModel.SelectedTenant = tenantB;
                await WpfTestDispatcher.DrainAsync();

                Assert.Equal(2, credentialsClient.TokenInfoContexts.Count);
                Assert.Equal(2, credentialsClient.CertificateInfoContexts.Count);
                Assert.Equal(credentialsClient.TokenInfoContexts[1], credentialsClient.CertificateInfoContexts[1]);
                Assert.True(viewModel.HasKsefCertificate);
                Assert.Equal(tenantBCertificateId, viewModel.KsefCertificateCredentialId);

                viewModel.Status = "Aktualny status tenant B";
                oldTenantCertificateInfo.SetException(new InvalidOperationException("stary tenant"));
                await WpfTestDispatcher.DrainAsync();

                Assert.True(viewModel.HasKsefCertificate);
                Assert.Equal(tenantBCertificateId, viewModel.KsefCertificateCredentialId);
                Assert.Equal("Aktualny status tenant B", viewModel.Status);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task TenantChangeWhileTokenDialogIsOpenDoesNotSendSaveRequest()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenantA = new(Guid.NewGuid(), "Tenant A", "User");
            TenantDto tenantB = new(Guid.NewGuid(), "Tenant B", "User");
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenantA);
            var credentialsClient = new FakeKsefCredentialsApiClient();
            MainViewModel? viewModel = null;
            var tokenWindow = new FakeKsefTokenWindowService
            {
                ShowTokenDialogHandler = () =>
                {
                    viewModel!.SelectedTenant = tenantB;
                    return "token-for-A";
                }
            };
            viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(), credentialsClient, tokenWindow);
            viewModel.IsLoggedIn = true;
            viewModel.Status = "Status przed dialogiem";

            try
            {
                await viewModel.ConfigureKsefTokenCommand.ExecuteAsync(null);
                await WpfTestDispatcher.DrainAsync();

                Assert.Empty(credentialsClient.SaveTokenContexts);
                Assert.Null(viewModel.KsefCredentialId);
                Assert.Equal("Status przed dialogiem", viewModel.Status);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task LogoutWhileTokenDialogIsOpenDoesNotSendSaveRequest()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenant = new(Guid.NewGuid(), "Tenant A", "User");
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenant);
            var credentialsClient = new FakeKsefCredentialsApiClient();
            var tokenWindow = new FakeKsefTokenWindowService
            {
                ShowTokenDialogHandler = () =>
                {
                    tokenStore.AccessToken = null;
                    return "token-from-logged-out-session";
                }
            };
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(), credentialsClient, tokenWindow);
            viewModel.IsLoggedIn = true;
            viewModel.Status = "Status przed dialogiem";

            try
            {
                await viewModel.ConfigureKsefTokenCommand.ExecuteAsync(null);
                await WpfTestDispatcher.DrainAsync();

                Assert.Empty(credentialsClient.SaveTokenContexts);
                Assert.Null(viewModel.KsefCredentialId);
                Assert.Equal("Status przed dialogiem", viewModel.Status);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task DelayedTokenSaveAfterTenantChangeCannotUpdateCredentialState()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenantA = new(Guid.NewGuid(), "Tenant A", "User");
            TenantDto tenantB = new(Guid.NewGuid(), "Tenant B", "User");
            Guid tenantBCredentialId = Guid.NewGuid();
            Guid tenantASavedCredentialId = Guid.NewGuid();
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenantA);
            var saveResponse = NewCompletionSource<Guid>();
            var credentialsClient = new FakeKsefCredentialsApiClient
            {
                SaveTokenHandler = (_, _) => saveResponse.Task,
                GetTokenInfoHandler = context => Task.FromResult(
                    context.TenantId == tenantB.Id
                        ? new KsefTokenInfo(true, tenantBCredentialId)
                        : new KsefTokenInfo(false, null))
            };
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(), credentialsClient,
                new FakeKsefTokenWindowService { ShowTokenDialogHandler = () => "token-for-A" });
            viewModel.IsLoggedIn = true;

            try
            {
                Task saveTask = viewModel.ConfigureKsefTokenCommand.ExecuteAsync(null);
                Assert.Single(credentialsClient.SaveTokenContexts);
                Assert.Equal(tenantA.Id, credentialsClient.SaveTokenContexts[0].TenantId);

                viewModel.SelectedTenant = tenantB;
                await WpfTestDispatcher.DrainAsync();

                Assert.True(viewModel.HasKsefToken);
                Assert.Equal(tenantBCredentialId, viewModel.KsefCredentialId);
                viewModel.Status = "Aktualny status tenant B";

                saveResponse.SetResult(tenantASavedCredentialId);
                await saveTask;

                Assert.True(viewModel.HasKsefToken);
                Assert.Equal(tenantBCredentialId, viewModel.KsefCredentialId);
                Assert.DoesNotContain(viewModel.AvailableKsefCredentials, option => option.CredentialId == tenantASavedCredentialId);
                Assert.Equal("Aktualny status tenant B", viewModel.Status);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task CancelledTokenDialogDoesNotSendRequestOrChangeStatus()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenant = new(Guid.NewGuid(), "Tenant A", "User");
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenant);
            var credentialsClient = new FakeKsefCredentialsApiClient();
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(), credentialsClient,
                new FakeKsefTokenWindowService { ShowTokenDialogHandler = () => null });
            viewModel.IsLoggedIn = true;
            viewModel.Status = "Poprzedni status";

            try
            {
                await viewModel.ConfigureKsefTokenCommand.ExecuteAsync(null);

                Assert.Empty(credentialsClient.SaveTokenContexts);
                Assert.Equal("Poprzedni status", viewModel.Status);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task SuccessfulTokenSaveUsesCapturedContextAndPreservesPreferredCredential()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenant = new(Guid.NewGuid(), "Tenant A", "User");
            Guid preferredCredentialId = Guid.NewGuid();
            Guid savedCredentialId = Guid.NewGuid();
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenant);
            var credentialsClient = new FakeKsefCredentialsApiClient
            {
                SaveTokenHandler = (_, _) => Task.FromResult(savedCredentialId)
            };
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(), credentialsClient,
                new FakeKsefTokenWindowService { ShowTokenDialogHandler = () => "token-new" });
            viewModel.IsLoggedIn = true;
            viewModel.HasKsefCertificate = true;
            viewModel.KsefCertificateCredentialId = preferredCredentialId;
            viewModel.PreferredKsefCredentialId = preferredCredentialId;

            try
            {
                await viewModel.ConfigureKsefTokenCommand.ExecuteAsync(null);

                Assert.Single(credentialsClient.SaveTokenContexts);
                Assert.Equal(tenant.Id, credentialsClient.SaveTokenContexts[0].TenantId);
                Assert.Equal("token-A", credentialsClient.SaveTokenContexts[0].AccessToken);
                Assert.True(viewModel.HasKsefToken);
                Assert.Equal(savedCredentialId, viewModel.KsefCredentialId);
                Assert.Equal(preferredCredentialId, viewModel.PreferredKsefCredentialId);
                Assert.Equal(preferredCredentialId, viewModel.SelectedKsefCredential?.CredentialId);
                Assert.Contains(viewModel.AvailableKsefCredentials, option => option.CredentialId == savedCredentialId);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task TenantChangeDuringCertificateDialogDoesNotReadOrSendSelectedFiles()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenantA = new(Guid.NewGuid(), "Tenant A", "User");
            TenantDto tenantB = new(Guid.NewGuid(), "Tenant B", "User");
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenantA);
            var authenticationClient = new FakeAuthenticationApiClient();
            MainViewModel? viewModel = null;
            var certificateSelection = new FakeCertificateSelectionWindowService
            {
                SelectHandler = () =>
                {
                    viewModel!.SelectedTenant = tenantB;
                    return new KsefCertificateFiles
                    {
                        CertificatePath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.crt"),
                        PrivateKeyPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.key"),
                        Password = "password"
                    };
                }
            };
            viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(),
                authenticationApiClient: authenticationClient, certificateSelectionWindowService: certificateSelection);
            viewModel.IsLoggedIn = true;
            viewModel.Status = "Status przed dialogiem";

            try
            {
                await viewModel.ConfigureKsefCertificateCommand.ExecuteAsync(null);
                await WpfTestDispatcher.DrainAsync();

                Assert.Empty(authenticationClient.SaveCertificateContexts);
                Assert.Equal("Status przed dialogiem", viewModel.Status);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task LogoutDuringCertificateDialogDoesNotSendSelectedFiles()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenant = new(Guid.NewGuid(), "Tenant A", "User");
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenant);
            var authenticationClient = new FakeAuthenticationApiClient();
            var certificateSelection = new FakeCertificateSelectionWindowService
            {
                SelectHandler = () =>
                {
                    tokenStore.AccessToken = null;
                    return new KsefCertificateFiles
                    {
                        CertificatePath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.crt"),
                        PrivateKeyPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.key"),
                        Password = "password"
                    };
                }
            };
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(),
                authenticationApiClient: authenticationClient, certificateSelectionWindowService: certificateSelection);
            viewModel.IsLoggedIn = true;
            viewModel.Status = "Status przed dialogiem";

            try
            {
                await viewModel.ConfigureKsefCertificateCommand.ExecuteAsync(null);
                await WpfTestDispatcher.DrainAsync();

                Assert.Empty(authenticationClient.SaveCertificateContexts);
                Assert.Equal("Status przed dialogiem", viewModel.Status);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task CancelledCertificateDialogDoesNotSendRequestOrChangeStatus()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenant = new(Guid.NewGuid(), "Tenant A", "User");
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenant);
            var authenticationClient = new FakeAuthenticationApiClient();
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(),
                authenticationApiClient: authenticationClient,
                certificateSelectionWindowService: new FakeCertificateSelectionWindowService { SelectHandler = () => null });
            viewModel.IsLoggedIn = true;
            viewModel.Status = "Poprzedni status";

            try
            {
                await viewModel.ConfigureKsefCertificateCommand.ExecuteAsync(null);

                Assert.Empty(authenticationClient.SaveCertificateContexts);
                Assert.Equal("Poprzedni status", viewModel.Status);
                Assert.False(viewModel.IsLoading);
            }
            finally
            {
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task DelayedCertificateSaveAfterTenantChangeCannotUpdateCertificateStateOrLoading()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenantA = new(Guid.NewGuid(), "Tenant A", "User");
            TenantDto tenantB = new(Guid.NewGuid(), "Tenant B", "User");
            Guid tenantBCertificateId = Guid.NewGuid();
            Guid tenantASavedCertificateId = Guid.NewGuid();
            KsefCertificateFiles files = CreateCertificateFiles();
            var saveResponse = NewCompletionSource<Guid>();
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenantA);
            var authenticationClient = new FakeAuthenticationApiClient
            {
                SaveCertificateHandler = (_, _, _, _) => saveResponse.Task
            };
            var certificateSelection = new FakeCertificateSelectionWindowService { SelectHandler = () => files };
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(),
                authenticationApiClient: authenticationClient, certificateSelectionWindowService: certificateSelection);
            viewModel.IsLoggedIn = true;

            try
            {
                Task saveTask = viewModel.ConfigureKsefCertificateCommand.ExecuteAsync(null);
                Assert.Single(authenticationClient.SaveCertificateContexts);
                Assert.Equal(tenantA.Id, authenticationClient.SaveCertificateContexts[0].TenantId);

                viewModel.SelectedTenant = tenantB;
                await WpfTestDispatcher.DrainAsync();

                viewModel.HasKsefCertificate = true;
                viewModel.KsefCertificateCredentialId = tenantBCertificateId;
                viewModel.PreferredKsefCredentialId = tenantBCertificateId;
                viewModel.SelectedKsefCredential = new KsefCredentialOption { CredentialId = tenantBCertificateId, Name = "Certyfikat KSeF" };
                viewModel.AvailableKsefCredentials.Add(viewModel.SelectedKsefCredential);
                viewModel.IsLoading = true;
                viewModel.Status = "Aktualny stan tenant B";

                saveResponse.SetResult(tenantASavedCertificateId);
                await saveTask;

                Assert.True(viewModel.HasKsefCertificate);
                Assert.Equal(tenantBCertificateId, viewModel.KsefCertificateCredentialId);
                Assert.Equal(tenantBCertificateId, viewModel.PreferredKsefCredentialId);
                Assert.Equal(tenantBCertificateId, viewModel.SelectedKsefCredential?.CredentialId);
                Assert.Equal("Aktualny stan tenant B", viewModel.Status);
                Assert.True(viewModel.IsLoading);
            }
            finally
            {
                DeleteCertificateFiles(files);
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task SuccessfulCertificateSavePreservesPreferredCredential()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenant = new(Guid.NewGuid(), "Tenant A", "User");
            Guid preferredCredentialId = Guid.NewGuid();
            Guid savedCertificateId = Guid.NewGuid();
            KsefCertificateFiles files = CreateCertificateFiles();
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenant);
            var authenticationClient = new FakeAuthenticationApiClient
            {
                SaveCertificateHandler = (_, _, _, _) => Task.FromResult(savedCertificateId)
            };
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(),
                authenticationApiClient: authenticationClient,
                certificateSelectionWindowService: new FakeCertificateSelectionWindowService { SelectHandler = () => files });
            viewModel.IsLoggedIn = true;
            viewModel.HasKsefToken = true;
            viewModel.KsefCredentialId = preferredCredentialId;
            viewModel.PreferredKsefCredentialId = preferredCredentialId;
            viewModel.AvailableKsefCredentials.Add(new KsefCredentialOption { CredentialId = preferredCredentialId, Name = "Token KSeF" });
            viewModel.SelectedKsefCredential = viewModel.AvailableKsefCredentials[0];

            try
            {
                await viewModel.ConfigureKsefCertificateCommand.ExecuteAsync(null);

                Assert.Single(authenticationClient.SaveCertificateContexts);
                Assert.Equal(tenant.Id, authenticationClient.SaveCertificateContexts[0].TenantId);
                Assert.True(viewModel.HasKsefCertificate);
                Assert.Equal(savedCertificateId, viewModel.KsefCertificateCredentialId);
                Assert.Equal(preferredCredentialId, viewModel.PreferredKsefCredentialId);
                Assert.Equal(preferredCredentialId, viewModel.SelectedKsefCredential?.CredentialId);
                Assert.Contains(viewModel.AvailableKsefCredentials, option => option.CredentialId == savedCertificateId);
            }
            finally
            {
                DeleteCertificateFiles(files);
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(true, "tenant", "success")]
    [InlineData(true, "tenant", "error")]
    [InlineData(true, "tenant", "cancel")]
    [InlineData(true, "session", "success")]
    [InlineData(true, "session", "error")]
    [InlineData(true, "session", "cancel")]
    [InlineData(true, "logout", "success")]
    [InlineData(true, "logout", "error")]
    [InlineData(true, "logout", "cancel")]
    [InlineData(true, "tenant-return", "success")]
    [InlineData(true, "tenant-return", "error")]
    [InlineData(true, "tenant-return", "cancel")]
    [InlineData(false, "tenant", "success")]
    [InlineData(false, "tenant", "error")]
    [InlineData(false, "tenant", "cancel")]
    [InlineData(false, "session", "success")]
    [InlineData(false, "session", "error")]
    [InlineData(false, "session", "cancel")]
    [InlineData(false, "logout", "success")]
    [InlineData(false, "logout", "error")]
    [InlineData(false, "logout", "cancel")]
    [InlineData(false, "tenant-return", "success")]
    [InlineData(false, "tenant-return", "error")]
    [InlineData(false, "tenant-return", "cancel")]
    public Task DelayedKsefAuthenticationCannotChangeNewContextStateOrLoading(bool useToken, string contextChange, string outcome)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            TenantDto tenantA = new(Guid.NewGuid(), "Tenant A", "User");
            TenantDto tenantB = new(Guid.NewGuid(), "Tenant B", "User");
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenantA);
            var oldResponse = NewCompletionSource<bool>();
            var newResponse = NewCompletionSource<bool>();
            var authenticationClient = new FakeAuthenticationApiClient
            {
                AuthenticateTokenHandler = _ => useToken ? oldResponse.Task : newResponse.Task,
                AuthenticateCertificateHandler = _ => useToken ? newResponse.Task : oldResponse.Task
            };
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(),
                authenticationApiClient: authenticationClient);
            viewModel.IsLoggedIn = true;
            viewModel.HasKsefToken = true;
            viewModel.KsefCredentialId = Guid.NewGuid();

            try
            {
                var oldCommand = useToken ? viewModel.AuthenticateKsefTokenCommand : viewModel.AuthenticateKsefCertificateCommand;
                Task oldTask = oldCommand.ExecuteAsync(null);
                ApiRequestContext oldContext = Assert.Single(useToken
                    ? authenticationClient.AuthenticateTokenContexts : authenticationClient.AuthenticateCertificateContexts);
                Assert.Equal(tenantA.Id, oldContext.TenantId);
                Assert.Equal("token-A", oldContext.AccessToken);
                Assert.Equal(tokenStore.SessionVersion, oldContext.SessionVersion);
                Assert.Equal(ApiRequestScope.TenantHeader, oldContext.Scope);
                Assert.True(viewModel.IsLoading);

                if (contextChange == "session" || contextChange == "logout")
                    tokenStore.AccessToken = contextChange == "logout" ? null : "token-B";
                else
                    viewModel.SelectedTenant = tenantB;
                await WpfTestDispatcher.DrainAsync();

                if (contextChange == "session" || contextChange == "tenant-return")
                {
                    viewModel.SelectedTenant = tenantA;
                    await WpfTestDispatcher.DrainAsync();
                }

                Assert.True(oldContext.CancellationToken.IsCancellationRequested);
                Assert.False(viewModel.IsLoading);
                viewModel.HasKsefToken = true;
                Guid currentCredentialId = Guid.NewGuid();
                viewModel.KsefCredentialId = currentCredentialId;
                viewModel.HasKsefCertificate = true;
                viewModel.KsefCertificateCredentialId = currentCredentialId;
                viewModel.PreferredKsefCredentialId = currentCredentialId;
                Task? newTask = null;
                if (contextChange != "logout")
                {
                    var newCommand = useToken ? viewModel.AuthenticateKsefCertificateCommand : viewModel.AuthenticateKsefTokenCommand;
                    newTask = newCommand.ExecuteAsync(null);
                    ApiRequestContext newContext = Assert.Single(useToken
                        ? authenticationClient.AuthenticateCertificateContexts : authenticationClient.AuthenticateTokenContexts);
                    Assert.Equal(tenantContext.TenantId, newContext.TenantId);
                    Assert.Equal(tokenStore.AccessToken, newContext.AccessToken);
                    Assert.Equal(tokenStore.SessionVersion, newContext.SessionVersion);
                    Assert.NotEqual(oldContext.Generation, newContext.Generation);
                    Assert.True(viewModel.IsLoading);
                }
                viewModel.Status = "Current context";

                if (outcome == "error")
                    oldResponse.SetException(new InvalidOperationException("Old operation error"));
                else if (outcome == "cancel")
                    oldResponse.SetCanceled(oldContext.CancellationToken);
                else
                    oldResponse.SetResult(true);
                await oldTask;

                Assert.Equal("Current context", viewModel.Status);
                Assert.Equal(contextChange != "logout", viewModel.IsLoading);
                Assert.True(viewModel.HasKsefToken);
                Assert.True(viewModel.HasKsefCertificate);
                Assert.Equal(currentCredentialId, viewModel.KsefCredentialId);
                Assert.Equal(currentCredentialId, viewModel.KsefCertificateCredentialId);
                Assert.Equal(currentCredentialId, viewModel.PreferredKsefCredentialId);

                if (newTask is not null)
                {
                    newResponse.SetResult(true);
                    await newTask;
                    Assert.False(viewModel.IsLoading);
                    Assert.Equal(useToken
                        ? "Uwierzytelnianie certyfikatem KSeF zakończone pomyślnie."
                        : "Uwierzytelnianie tokenem KSeF zakończone pomyślnie.", viewModel.Status);
                }
            }
            finally
            {
                oldResponse.TrySetResult(true);
                newResponse.TrySetResult(true);
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public Task KsefAuthenticationInCurrentContextReportsResultAndClearsLoading(bool useToken, bool fail)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokenStore = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant A", "User"));
            var response = NewCompletionSource<bool>();
            var authenticationClient = new FakeAuthenticationApiClient
            {
                AuthenticateTokenHandler = _ => response.Task,
                AuthenticateCertificateHandler = _ => response.Task
            };
            var viewModel = CreateViewModel(new FakeInvoiceApiClient(), tokenStore, tenantContext, new FakeTenantApiClient(),
                authenticationApiClient: authenticationClient);
            viewModel.IsLoggedIn = true;
            viewModel.HasKsefToken = true;
            viewModel.KsefCredentialId = Guid.NewGuid();

            try
            {
                var command = useToken ? viewModel.AuthenticateKsefTokenCommand : viewModel.AuthenticateKsefCertificateCommand;
                Task task = command.ExecuteAsync(null);
                Assert.True(viewModel.IsLoading);
                if (fail)
                    response.SetException(new InvalidOperationException("API error"));
                else
                    response.SetResult(true);
                await task;

                Assert.False(viewModel.IsLoading);
                string method = useToken ? "tokenem" : "certyfikatem";
                Assert.Equal(fail
                    ? $"Błąd uwierzytelniania {method} KSeF: API error"
                    : $"Uwierzytelnianie {method} KSeF zakończone pomyślnie.", viewModel.Status);
            }
            finally
            {
                response.TrySetResult(true);
                viewModel.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public Task NewTenantRefreshSelectsCreatedTenantOnlyIfCurrentUserHasAccess(bool assignedToCurrentUser, bool hasSelectedTenant)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokens = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            TenantDto oldTenant = new(Guid.NewGuid(), "Old", "User");
            TenantDto newTenant = new(Guid.NewGuid(), "New", "Admin");
            if (hasSelectedTenant) tenantContext.SetTenant(oldTenant);
            var client = new FakeTenantApiClient
            {
                GetMyTenantsContextHandler = _ => Task.FromResult<IReadOnlyList<TenantDto>>(assignedToCurrentUser
                    ? new[] { oldTenant, newTenant } : new[] { oldTenant })
            };
            var navigation = new FakeWindowNavigationService
            {
                ShowNewTenantHandler = () => new TenantCreationDialogResult(newTenant.Id, tokens.SessionVersion)
            };
            var vm = CreateViewModel(new FakeInvoiceApiClient(), tokens, tenantContext, client, navigationService: navigation);
            vm.IsLoggedIn = true;
            if (hasSelectedTenant)
            {
                vm.AvailableTenants.Add(oldTenant);
                vm.SelectedTenant = oldTenant;
            }
            // Reproduce ComboBox clearing its selection when its ItemsSource is reset.
            vm.AvailableTenants.CollectionChanged += (_, args) =>
            {
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) vm.SelectedTenant = null;
            };
            try
            {
                await vm.NewTenantCommand.ExecuteAsync(null);
                ApiRequestContext context = Assert.Single(client.UserContexts);
                Assert.Equal(ApiRequestScope.User, context.Scope);
                Assert.Null(context.TenantId);
                Assert.Equal("token-A", context.AccessToken);
                Assert.Equal(tokens.SessionVersion, context.SessionVersion);
                if (assignedToCurrentUser)
                {
                    Assert.Equal(newTenant, vm.SelectedTenant);
                    Assert.Equal(newTenant.Id, tenantContext.TenantId);
                    Assert.Equal(new[] { oldTenant, newTenant }, vm.AvailableTenants);
                }
                else
                {
                    Assert.Equal(hasSelectedTenant ? oldTenant : null, vm.SelectedTenant);
                    Assert.Equal(hasSelectedTenant ? oldTenant.Id : (Guid?)null, tenantContext.TenantId);
                    Assert.Equal(hasSelectedTenant ? new[] { oldTenant } : Array.Empty<TenantDto>(), vm.AvailableTenants);
                }
                Assert.Equal("token-A", tokens.AccessToken);
            }
            finally
            {
                vm.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Fact]
    public Task NewTenantRefreshFailureReportsCreatedTenantAndPreservesSelection()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokens = new TokenStore { AccessToken = "token-A" };
            TenantDto oldTenant = new(Guid.NewGuid(), "Old", "User");
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(oldTenant);
            var client = new FakeTenantApiClient
            {
                GetMyTenantsContextHandler = _ => Task.FromException<IReadOnlyList<TenantDto>>(new HttpRequestException("raw error"))
            };
            int dialogs = 0;
            var navigation = new FakeWindowNavigationService
            {
                ShowNewTenantHandler = () => { dialogs++; return new TenantCreationDialogResult(Guid.NewGuid(), tokens.SessionVersion); }
            };
            var vm = CreateViewModel(new FakeInvoiceApiClient(), tokens, tenantContext, client, navigationService: navigation);
            vm.IsLoggedIn = true;
            vm.AvailableTenants.Add(oldTenant);
            vm.SelectedTenant = oldTenant;
            try
            {
                await vm.NewTenantCommand.ExecuteAsync(null);
                Assert.Equal("Tenant został utworzony, ale nie udało się odświeżyć listy tenantów.", vm.Status);
                Assert.Equal(oldTenant, vm.SelectedTenant);
                Assert.Equal(oldTenant.Id, tenantContext.TenantId);
                Assert.Equal(oldTenant, Assert.Single(vm.AvailableTenants));
                Assert.Single(client.UserContexts);
                Assert.Equal(1, dialogs);
            }
            finally
            {
                vm.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DelayedNewTenantRefreshCannotUpdateAnotherSession(bool fail)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokens = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            TenantDto newTenantA = new(Guid.NewGuid(), "Created for A", "Admin");
            TenantDto tenantB = new(Guid.NewGuid(), "Current B", "User");
            var response = NewCompletionSource<IReadOnlyList<TenantDto>>();
            var client = new FakeTenantApiClient { GetMyTenantsContextHandler = _ => response.Task, Tenants = new[] { tenantB } };
            var navigation = new FakeWindowNavigationService
            {
                ShowNewTenantHandler = () => new TenantCreationDialogResult(newTenantA.Id, tokens.SessionVersion)
            };
            var vm = CreateViewModel(new FakeInvoiceApiClient(), tokens, tenantContext, client, navigationService: navigation);
            vm.IsLoggedIn = true;
            try
            {
                Task refresh = vm.NewTenantCommand.ExecuteAsync(null);
                ApiRequestContext captured = Assert.Single(client.UserContexts);
                tokens.AccessToken = "token-B";
                await WpfTestDispatcher.DrainAsync();
                vm.Status = "Current session B";
                vm.IsLoading = true;
                if (fail) response.SetException(new HttpRequestException("old request"));
                else response.SetResult(new[] { newTenantA });
                await refresh;
                Assert.True(captured.CancellationToken.IsCancellationRequested);
                Assert.Equal(tenantB, vm.SelectedTenant);
                Assert.Equal(tenantB.Id, tenantContext.TenantId);
                Assert.Equal(tenantB, Assert.Single(vm.AvailableTenants));
                Assert.Equal("Current session B", vm.Status);
                Assert.True(vm.IsLoading);
                Assert.Single(client.UserContexts);
            }
            finally
            {
                vm.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SessionChangeDuringTenantListUpdatePreventsOldSelection(bool changeBeforeSelection)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokens = new TokenStore { AccessToken = "token-A" };
            var tenantContext = new TenantContext();
            TenantDto createdA = new(Guid.NewGuid(), "Created A", "Admin");
            TenantDto tenantB = new(Guid.NewGuid(), "Current B", "User");
            var client = new FakeTenantApiClient
            {
                Tenants = new[] { tenantB },
                GetMyTenantsContextHandler = _ => Task.FromResult<IReadOnlyList<TenantDto>>(new[] { createdA })
            };
            var navigation = new FakeWindowNavigationService
            {
                ShowNewTenantHandler = () => new TenantCreationDialogResult(createdA.Id, tokens.SessionVersion)
            };
            var vm = CreateViewModel(new FakeInvoiceApiClient(), tokens, tenantContext, client, navigationService: navigation);
            vm.IsLoggedIn = true;
            bool changed = false;
            bool selectedOldResult = false;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedTenant) && vm.SelectedTenant?.Id == createdA.Id)
                    selectedOldResult = true;
            };
            vm.AvailableTenants.CollectionChanged += (_, args) =>
            {
                if (!changed && args.Action == (changeBeforeSelection ? System.Collections.Specialized.NotifyCollectionChangedAction.Add : System.Collections.Specialized.NotifyCollectionChangedAction.Reset))
                {
                    changed = true;
                    tokens.AccessToken = "token-B";
                }
            };
            try
            {
                await vm.NewTenantCommand.ExecuteAsync(null);
                await WpfTestDispatcher.DrainAsync();
                Assert.True(changed);
                Assert.False(selectedOldResult);
                Assert.Equal(tenantB, vm.SelectedTenant);
                Assert.Equal(tenantB, Assert.Single(vm.AvailableTenants));
                Assert.Equal(tenantB.Id, tenantContext.TenantId);
            }
            finally
            {
                vm.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CancelledOrOutdatedTenantDialogDoesNotStartRefresh(bool sessionChanged)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokens = new TokenStore { AccessToken = "token-A" };
            long version = tokens.SessionVersion;
            var tenantContext = new TenantContext();
            var client = new FakeTenantApiClient();
            var navigation = new FakeWindowNavigationService
            {
                ShowNewTenantHandler = () =>
                {
                    if (!sessionChanged) return null;
                    tokens.AccessToken = "token-B";
                    return new TenantCreationDialogResult(Guid.NewGuid(), version);
                }
            };
            var vm = CreateViewModel(new FakeInvoiceApiClient(), tokens, tenantContext, client, navigationService: navigation);
            vm.IsLoggedIn = true;
            try
            {
                await vm.NewTenantCommand.ExecuteAsync(null);
                Assert.Empty(client.UserContexts);
            }
            finally
            {
                vm.Cleanup();
                await WpfTestDispatcher.DrainAsync();
            }
        });
    }

    private static KsefCertificateFiles CreateCertificateFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"xksef-certificate-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string certificatePath = Path.Combine(directory, "certificate.pem");
        string privateKeyPath = Path.Combine(directory, "private.key");
        File.WriteAllText(certificatePath, "test-certificate");
        File.WriteAllText(privateKeyPath, "test-private-key");
        return new KsefCertificateFiles { CertificatePath = certificatePath, PrivateKeyPath = privateKeyPath, Password = "test-password" };
    }

    private static void DeleteCertificateFiles(KsefCertificateFiles files)
    {
        string? directory = Path.GetDirectoryName(files.CertificatePath);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static async Task AssertInvoiceContextRejected(ApiRequestContext context)
    {
        var tokenStore = new TokenStore();
        var tenantContext = new TenantContext();
        using var transport = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("[]")));
        using var httpClient = CreatePipeline(tokenStore, tenantContext, transport);
        var client = new InvoiceApiClient(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetInvoicesAsync(context, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), InvoiceDirection.Sales));

        Assert.Equal(0, transport.RequestCount);
    }

    private static void AssertCapturedCredentialsContext(RecordingHandler transport, Guid tenantId)
    {
        Assert.Equal("Bearer token-A", transport.LastAuthorization);
        Assert.Equal(tenantId.ToString("D"), transport.LastTenantId);
        Assert.Equal(ApiRequestScope.TenantHeader, transport.LastRequestContext?.Scope);
        Assert.Equal(tenantId, transport.LastRequestContext?.TenantId);
        Assert.Equal("token-A", transport.LastRequestContext?.AccessToken);
    }

    private static HttpClient CreatePipeline(ITokenStore tokenStore, TenantContext tenantContext, HttpMessageHandler transport)
    {
        var tenantHandler = new TenantHeaderHandler(tenantContext) { InnerHandler = transport };
        var bearerHandler = new BearerTokenHandler(tokenStore) { InnerHandler = tenantHandler };
        return new HttpClient(bearerHandler) { BaseAddress = new Uri("https://xksef.test/") };
    }

    private static ApiRequestContext CreateContext(Guid? tenantId, string? token, long sessionVersion, CancellationToken cancellationToken = default)
    {
        return new ApiRequestContext(1, sessionVersion, tenantId, token, cancellationToken, ApiRequestScope.TenantHeader);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static InvoiceDto CreateInvoice(string ksefNumber)
    {
        return new InvoiceDto { KsefNumber = ksefNumber, InvoiceNumber = ksefNumber };
    }

    private static TaskCompletionSource<T> NewCompletionSource<T>()
    {
        return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static MainViewModel CreateViewModel(FakeInvoiceApiClient invoiceClient, ITokenStore tokenStore, TenantContext tenantContext, FakeTenantApiClient tenantApiClient, FakeKsefCredentialsApiClient? credentialsApiClient = null, FakeKsefTokenWindowService? tokenWindowService = null, FakeAuthenticationApiClient? authenticationApiClient = null, ICertificateSelectionWindowService? certificateSelectionWindowService = null, FakeWindowNavigationService? navigationService = null)
    {
        return new MainViewModel(
            invoiceClient,
            navigationService ?? new FakeWindowNavigationService(),
            tokenStore,
            tenantApiClient,
            tenantContext,
            certificateSelectionWindowService ?? new CertificateSelectionWindowService(new EmptyServiceProvider()),
            authenticationApiClient ?? new FakeAuthenticationApiClient(),
            credentialsApiClient ?? new FakeKsefCredentialsApiClient(),
            tokenWindowService ?? new FakeKsefTokenWindowService());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;
        private int _requestCount;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int RequestCount => Volatile.Read(ref _requestCount);
        public string? LastAuthorization { get; private set; }
        public string? LastTenantId { get; private set; }
        public ApiRequestContext? LastRequestContext { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastTenantId = request.Headers.TryGetValues("X-Tenant-Id", out var values) ? values.Single() : null;
            LastRequestContext = request.TryGetApiRequestContext(out ApiRequestContext context) ? context : null;
            return _responseFactory(request, cancellationToken);
        }
    }

    private sealed class FakeInvoiceApiClient : IInvoiceApiClient
    {
        public List<ApiRequestContext> InvoiceContexts { get; } = [];
        public List<ApiRequestContext> ImportContexts { get; } = [];
        public List<ApiRequestContext> PdfContexts { get; } = [];
        public List<(DateOnly From, DateOnly To, InvoiceDirection Direction, InvoiceDateKind DateKind, string? TimeZoneId)> QueryFilters { get; } = [];
        public Func<ApiRequestContext, DateOnly, DateOnly, InvoiceDirection, InvoiceDateKind, string?, Task<InvoiceQueryResultDto>>? QueryHandler { get; set; }
        public Func<ApiRequestContext, DateOnly, DateOnly, InvoiceDirection, Task<IReadOnlyList<InvoiceDto>>> GetInvoicesHandler { get; set; }
            = (_, _, _, _) => Task.FromResult<IReadOnlyList<InvoiceDto>>([]);
        public Func<ApiRequestContext, DateOnly, DateOnly, InvoiceDirection, Task<InvoicePackageReadResult>> ImportHandler { get; set; }
            = (_, _, _, _) => Task.FromResult(new InvoicePackageReadResult { ReferenceNumber = "ref", DeclaredInvoiceCount = 1, ReadInvoiceCount = 1, ImportedCount = 1, SkippedCount = 0 });
        public Func<ApiRequestContext, string, Task<byte[]>> GetInvoicePdfHandler { get; set; }
            = (_, _) => Task.FromResult(Array.Empty<byte>());

        public async Task<InvoiceQueryResultDto> QueryInvoicesAsync(ApiRequestContext context, DateOnly from, DateOnly to, InvoiceDirection direction, InvoiceDateKind dateKind, string? timeZoneId)
        {
            InvoiceContexts.Add(context);
            QueryFilters.Add((from, to, direction, dateKind, timeZoneId));
            return QueryHandler is not null ? await QueryHandler(context, from, to, direction, dateKind, timeZoneId)
                : new InvoiceQueryResultDto(await GetInvoicesHandler(context, from, to, direction), []);
        }

        public Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(ApiRequestContext context, DateOnly from, DateOnly to, InvoiceDirection direction)
        {
            InvoiceContexts.Add(context);
            return GetInvoicesHandler(context, from, to, direction);
        }

        public Task<byte[]> GetInvoicePdfAsync(ApiRequestContext context, string ksefNumber)
        {
            PdfContexts.Add(context);
            return GetInvoicePdfHandler(context, ksefNumber);
        }

        public Task<InvoicePackageReadResult> ImportFromKsefAsync(ApiRequestContext context, DateOnly from, DateOnly to, InvoiceDirection direction)
        {
            ImportContexts.Add(context);
            return ImportHandler(context, from, to, direction);
        }
    }

    private sealed class FakeTenantApiClient : ITenantApiClient
    {
        public IReadOnlyList<TenantDto> Tenants { get; set; } = [];
        public int GetMyTenantsCallCount { get; private set; }
        public List<ApiRequestContext> UserContexts { get; } = [];
        public Func<ApiRequestContext, Task<IReadOnlyList<TenantDto>>>? GetMyTenantsContextHandler { get; set; }
        public Task<TenantCreationResult> CreateTenantAsync(ApiRequestContext context, string name, string nip, string taxEntityName, string userLogin)
            => Task.FromResult(new TenantCreationResult(HttpStatusCode.Created, Guid.NewGuid(), []));
        public Task<IReadOnlyList<TenantDto>> GetMyTenantsAsync(ApiRequestContext context)
        {
            UserContexts.Add(context);
            return GetMyTenantsContextHandler?.Invoke(context) ?? Task.FromResult(Tenants);
        }

        public Task<IReadOnlyList<TenantDto>> GetMyTenantsAsync(CancellationToken cancellationToken = default)
        {
            GetMyTenantsCallCount++;
            return Task.FromResult(Tenants);
        }

        public Task<Guid?> GetPreferredKsefCredentialIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Guid?>(null);
        }

        public Task SetPreferredKsefCredentialIdAsync(Guid tenantId, Guid? credentialId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeKsefCredentialsApiClient : IKsefCredentialsApiClient
    {
        public List<ApiRequestContext> TokenInfoContexts { get; } = [];
        public List<ApiRequestContext> CertificateInfoContexts { get; } = [];
        public List<ApiRequestContext> SaveTokenContexts { get; } = [];
        public Func<ApiRequestContext, Task<KsefTokenInfo>> GetTokenInfoHandler { get; set; }
            = _ => Task.FromResult(new KsefTokenInfo(false, null));
        public Func<ApiRequestContext, Task<KsefTokenInfo>> GetCertificateInfoHandler { get; set; }
            = _ => Task.FromResult(new KsefTokenInfo(false, null));
        public Func<ApiRequestContext, string, Task<Guid>> SaveTokenHandler { get; set; }
            = (_, _) => Task.FromResult(Guid.NewGuid());

        public Task<KsefTokenInfo> GetCertificateInfoAsync(ApiRequestContext context)
        {
            CertificateInfoContexts.Add(context);
            return GetCertificateInfoHandler(context);
        }

        public Task<KsefTokenInfo> GetTokenInfoAsync(ApiRequestContext context)
        {
            TokenInfoContexts.Add(context);
            return GetTokenInfoHandler(context);
        }

        public Task<Guid> SaveTokenAsync(ApiRequestContext context, string token)
        {
            SaveTokenContexts.Add(context);
            return SaveTokenHandler(context, token);
        }
    }

    private sealed class FakeAuthenticationApiClient : IAuthenticationApiClient
    {
        public Task<xKsef.Wpf.Models.UserCreationResult> CreateUserAsync(ApiRequestContext context, string login, string email, string password)
            => Task.FromResult(new xKsef.Wpf.Models.UserCreationResult(HttpStatusCode.Created, Guid.NewGuid().ToString(), Array.Empty<string>()));

        public List<ApiRequestContext> LoginContexts { get; } = [];
        public List<ApiRequestContext> SaveCertificateContexts { get; } = [];
        public List<ApiRequestContext> AuthenticateTokenContexts { get; } = [];
        public List<ApiRequestContext> AuthenticateCertificateContexts { get; } = [];
        public Func<ApiRequestContext, Task> AuthenticateTokenHandler { get; set; } = _ => Task.CompletedTask;
        public Func<ApiRequestContext, Task> AuthenticateCertificateHandler { get; set; } = _ => Task.CompletedTask;
        public Func<ApiRequestContext, string, string, Task<LoginResponse?>> LoginHandler { get; set; }
            = (_, _, _) => Task.FromResult<LoginResponse?>(null);
        public Func<ApiRequestContext, string, string, string, Task<Guid>> SaveCertificateHandler { get; set; }
            = (_, _, _, _) => Task.FromResult(Guid.NewGuid());

        public Task<LoginResponse?> LoginAsync(ApiRequestContext context, string login, string password)
        {
            LoginContexts.Add(context);
            return LoginHandler(context, login, password);
        }

        public Task AuthenticateKsefTokenAsync(ApiRequestContext context)
        {
            AuthenticateTokenContexts.Add(context);
            return AuthenticateTokenHandler(context);
        }
        public Task<Guid> SaveKsefCertificateAsync(ApiRequestContext context, string certificatePem, string encryptedPrivateKeyPem, string password)
        {
            SaveCertificateContexts.Add(context);
            return SaveCertificateHandler(context, certificatePem, encryptedPrivateKeyPem, password);
        }
        public Task AuthenticateKsefCertificateAsync(ApiRequestContext context)
        {
            AuthenticateCertificateContexts.Add(context);
            return AuthenticateCertificateHandler(context);
        }
    }

    private sealed class FakeWindowNavigationService : IWindowNavigationService
    {
        public bool? ShowLoginWindow() => null;
        public bool? ShowNewUserWindow() => null;
        public Func<TenantCreationDialogResult?> ShowNewTenantHandler { get; set; } = () => null;
        public TenantCreationDialogResult? ShowNewTenantWindow() => ShowNewTenantHandler();
    }

    private sealed class FakeKsefTokenWindowService : IKsefTokenWindowService
    {
        public Func<string?> ShowTokenDialogHandler { get; set; } = () => null;
        public string? ShowTokenDialog() => ShowTokenDialogHandler();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FakeCertificateSelectionWindowService : ICertificateSelectionWindowService
    {
        public Func<KsefCertificateFiles?> SelectHandler { get; set; } = () => null;
        public KsefCertificateFiles? SelectCertificate() => SelectHandler();
    }

    private sealed class ManualTokenStore : ITokenStore
    {
        public ManualTokenStore(string? accessToken, long sessionVersion)
        {
            AccessToken = accessToken;
            SessionVersion = sessionVersion;
        }

        public string? AccessToken { get; set; }
        public long SessionVersion { get; private set; }
        public event EventHandler? TokenChanged;

        public (string? AccessToken, long SessionVersion) GetSessionSnapshot() => (AccessToken, SessionVersion);

        public bool TryClearAccessToken(long expectedSessionVersion)
        {
            if (SessionVersion != expectedSessionVersion || AccessToken is null) return false;
            AccessToken = null;
            SessionVersion++;
            TokenChanged?.Invoke(this, new TokenChangedEventArgs(SessionVersion));
            return true;
        }

        public bool TrySetAccessToken(long expectedSessionVersion, string accessToken)
        {
            if (SessionVersion != expectedSessionVersion) return false;
            if (AccessToken == accessToken) return true;
            AccessToken = accessToken;
            SessionVersion++;
            TokenChanged?.Invoke(this, new TokenChangedEventArgs(SessionVersion));
            return true;
        }

        public void RaiseTokenChanged(long sessionVersion) => TokenChanged?.Invoke(this, new TokenChangedEventArgs(sessionVersion));
    }

    private static class WpfTestDispatcher
    {
        private static readonly Lazy<Dispatcher> SharedDispatcher = new(StartDispatcher);

        public static Task RunAsync(Func<Task> testAction)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SharedDispatcher.Value.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
            {
                try
                {
                    await testAction();
                    completion.SetResult(true);
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }));

            return completion.Task;
        }

        public static Task DrainAsync()
        {
            return SharedDispatcher.Value.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle).Task;
        }

        private static Dispatcher StartDispatcher()
        {
            var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                _ = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                ready.SetResult(dispatcher);
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "WPF test dispatcher"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return ready.Task.GetAwaiter().GetResult();
        }
    }
}