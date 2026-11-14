using System.Net;
using System.Text;
using System.Text.Json;
using xKsef.Contracts.Tenants;
using xKsef.Wpf.Http;
using xKsef.Wpf.Models;
using xKsef.Wpf.Services;
using xKsef.Wpf.ViewModels;

namespace xKsef.Tests.Wpf;

public sealed class NewTenantTests
{
    [Fact]
    public async Task UserScopedCreationDoesNotRequireADecodedLogin()
    {
        var client = new FakeTenantClient();
        using var vm = Form(client, new TokenStore { AccessToken = "opaque-token" });
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(ApiRequestScope.User, Assert.Single(client.Contexts).Scope);
    }

    [Fact]
    public async Task MissingJwtDisablesCreation()
    {
        var client = new FakeTenantClient();
        using var vm = Form(client, new TokenStore());
        Assert.False(vm.CanEdit);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(client.Contexts);
    }
    [Theory]
    [InlineData("Name", "")]
    [InlineData("TaxEntityName", " ")]
    [InlineData("Nip", "")]
    [InlineData("Nip", "123456789")]
    [InlineData("Nip", "12345678901")]
    [InlineData("Nip", "123456789x")]
    [InlineData("Nip", "１２３４５６７８９０")]
    [InlineData("Nip", "1234567890\n")]
    [InlineData("Name", "too-long")]
    [InlineData("TaxEntityName", "too-long")]
    public async Task InvalidFormDoesNotSendRequest(string field, string value)
    {
        var client = new FakeTenantClient();
        using var vm = Form(client, new TokenStore { AccessToken = "token-A" });
        value = value == "too-long" ? new string('x', 201) : value;
        switch (field)
        {
            case "Name": vm.Name = value; break;
            case "Nip": vm.Nip = value; break;
            case "TaxEntityName": vm.TaxEntityName = value; break;
        }
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(client.Contexts);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.IsSaving);
    }

    [Fact]
    public async Task CreationWithoutSelectedTenantCapturesSessionAndClosesWithoutRetry()
    {
        var client = new FakeTenantClient();
        var tokens = new TokenStore { AccessToken = "token-A" };
        long version = tokens.SessionVersion;
        using var vm = Form(client, tokens);
        bool? closed = null;
        vm.CloseRequested += result => closed = result;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(closed);
        Assert.NotNull(vm.CreatedTenant);
        Assert.Equal(client.TenantId, vm.CreatedTenant.TenantId);
        Assert.Equal(version, vm.CreatedTenant.SessionVersion);
        ApiRequestContext context = Assert.Single(client.Contexts);
        Assert.Equal(ApiRequestScope.User, context.Scope);
        Assert.Equal("token-A", context.AccessToken);
        Assert.Null(context.TenantId);
        Assert.Equal(version, tokens.SessionVersion);
        Assert.Equal("token-A", tokens.AccessToken);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Single(client.Contexts);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Sprawdź")]
    [InlineData(HttpStatusCode.Unauthorized, "Zaloguj")]
    [InlineData(HttpStatusCode.Forbidden, "dostępu")]
    [InlineData(HttpStatusCode.Conflict, "Konflikt")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Nie udało")]
    public async Task HttpErrorsAreReadableAndDoNotRetry(HttpStatusCode status, string text)
    {
        var client = new FakeTenantClient { Handler = _ => Task.FromResult(new TenantCreationResult(status, null, [])) };
        using var vm = Form(client, new TokenStore { AccessToken = "token-A" });
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains(text, vm.ErrorMessage);
        Assert.False(vm.IsSaving);
        Assert.Null(vm.CreatedTenant);
        Assert.Single(client.Contexts);
    }

    [Theory]
    [InlineData("InvalidName", "nazwę tenanta")]
    [InlineData("InvalidTaxEntityName", "podmiotu")]
    [InlineData("InvalidNip", "10 cyfr")]
    [InlineData("untrusted-server-text", "Sprawdź")]
    public async Task ValidationErrorsAreTranslated(string code, string expected)
    {
        var client = new FakeTenantClient { Handler = _ => Task.FromResult(new TenantCreationResult(HttpStatusCode.BadRequest, null, new[] { code })) };
        using var vm = Form(client, new TokenStore { AccessToken = "token-A" });
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains(expected, vm.ErrorMessage);
        Assert.DoesNotContain("untrusted-server-text", vm.ErrorMessage);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("error")]
    [InlineData("cancel")]
    [InlineData("unauthorized")]
    public async Task LateResponseCannotAffectNewSessionOrForm(string outcome)
    {
        var oldResponse = Completion();
        var newResponse = Completion();
        var client = new FakeTenantClient { Handler = context => context.AccessToken == "token-A" ? oldResponse.Task : newResponse.Task };
        var tokens = new TokenStore { AccessToken = "token-A" };
        using var oldVm = Form(client, tokens);
        bool closed = false;
        oldVm.CloseRequested += _ => closed = true;
        Task oldSave = oldVm.SaveCommand.ExecuteAsync(null);
        tokens.AccessToken = "token-B";
        using var newVm = Form(client, tokens);
        Task newSave = newVm.SaveCommand.ExecuteAsync(null);
        if (outcome == "error") oldResponse.SetException(new HttpRequestException("old response"));
        else if (outcome == "cancel") oldResponse.SetCanceled();
        else oldResponse.SetResult(new TenantCreationResult(outcome == "unauthorized" ? HttpStatusCode.Unauthorized : HttpStatusCode.Created, client.TenantId, []));
        await oldSave;
        await Task.Yield();
        Assert.False(closed);
        Assert.Null(oldVm.CreatedTenant);
        Assert.False(oldVm.IsSessionValid);
        Assert.True(client.Contexts[0].CancellationToken.IsCancellationRequested);
        Assert.True(newVm.IsSaving);
        Assert.Empty(newVm.ErrorMessage);
        Assert.Equal("token-B", tokens.AccessToken);
        Assert.Equal(2, client.Contexts.Count);
        newResponse.SetResult(new TenantCreationResult(HttpStatusCode.Forbidden, null, []));
        await newSave;
        Assert.False(newVm.IsSaving);
    }

    [Fact]
    public async Task ClosingFormCancelsRequestAndIgnoresLateSuccess()
    {
        var response = Completion();
        var client = new FakeTenantClient { Handler = _ => response.Task };
        using var vm = Form(client, new TokenStore { AccessToken = "token-A" });
        bool? closed = null;
        vm.CloseRequested += result => { closed = result; vm.Dispose(); };
        Task save = vm.SaveCommand.ExecuteAsync(null);
        vm.CancelCommand.Execute(null);
        response.SetResult(new TenantCreationResult(HttpStatusCode.Created, client.TenantId, []));
        await save;
        Assert.False(closed);
        Assert.Null(vm.CreatedTenant);
        Assert.True(Assert.Single(client.Contexts).CancellationToken.IsCancellationRequested);
        Assert.False(vm.IsSaving);
    }

    [Fact]
    public async Task NoSessionPreventsSending()
    {
        var client = new FakeTenantClient();
        using var vm = Form(client, new TokenStore());
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(client.Contexts);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task TransportExceptionIsReportedWithoutRawDetailsOrRetry()
    {
        var client = new FakeTenantClient { Handler = _ => Task.FromException<TenantCreationResult>(new HttpRequestException("raw-server-error")) };
        using var vm = Form(client, new TokenStore { AccessToken = "token-A" });
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.DoesNotContain("raw-server-error", vm.ErrorMessage);
        Assert.False(vm.IsSaving);
        Assert.Single(client.Contexts);
    }

    [Fact]
    public async Task PostAndRefreshUseCapturedJwtAndUserScope()
    {
        var tokens = new TokenStore { AccessToken = "token-A" };
        ApiRequestContext context = Context(tokens);
        tokens.AccessToken = "token-B";
        var tenants = new TenantContext();
        tenants.SetTenant(new TenantDto(Guid.NewGuid(), "Selected", "User"));
        Guid tenantId = Guid.NewGuid();
        using var transport = new Transport(async (request, cancellation) =>
        {
            Assert.Equal("/api/tenants", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer token-A", request.Headers.Authorization!.ToString());
            Assert.False(request.Headers.Contains("X-Tenant-Id"));
            Assert.True(request.TryGetApiRequestContext(out ApiRequestContext captured));
            Assert.Equal(context, captured);
            if (request.Method == HttpMethod.Post)
            {
                using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
                Assert.Equal(3, body.RootElement.EnumerateObject().Count());
                Assert.Equal("New tenant", body.RootElement.GetProperty("name").GetString());
                Assert.Equal("1234567890", body.RootElement.GetProperty("nip").GetString());
                Assert.Equal("Tax entity", body.RootElement.GetProperty("taxEntityName").GetString());
                Assert.False(body.RootElement.TryGetProperty("userLogin", out _));
                return Json(HttpStatusCode.Created, JsonSerializer.Serialize(new { tenantId }));
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new[] { new TenantDto(tenantId, "New tenant", "Admin") }));
        });
        using var http = Pipeline(tokens, tenants, transport);
        var client = new TenantApiClient(http);
        Assert.Equal(tenantId, (await client.CreateTenantAsync(context, "New tenant", "1234567890", "Tax entity")).TenantId);
        Assert.Equal(tenantId, Assert.Single(await client.GetMyTenantsAsync(context)).Id);
        Assert.Equal("token-B", tokens.AccessToken);
        Assert.Equal(2, transport.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task ClientReturnsExpectedErrorStatus(HttpStatusCode status)
    {
        var tokens = new TokenStore { AccessToken = "token-A" };
        using var transport = new Transport((_, _) => Task.FromResult(Json(status, "{}")));
        using var http = Pipeline(tokens, new TenantContext(), transport);
        var result = await new TenantApiClient(http).CreateTenantAsync(Context(tokens), "name", "1234567890", "tax");
        Assert.Equal(status, result.StatusCode);
        Assert.Null(result.TenantId);
        Assert.Equal(1, transport.Count);
    }

    [Fact]
    public async Task ClientParsesValidationWithoutDisplayingRawServerText()
    {
        var tokens = new TokenStore { AccessToken = "token-A" };
        using var transport = new Transport((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest,
            "{\"errors\":{\"UserLogin\":[\"UserNotFound\"],\"Nip\":[\"raw-server-text\"]}}")));
        using var http = Pipeline(tokens, new TenantContext(), transport);
        var result = await new TenantApiClient(http).CreateTenantAsync(Context(tokens), "name", "1234567890", "tax");
        Assert.Equal(new[] { "InvalidData", "InvalidNip" }, result.Errors);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"tenantId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public async Task InvalidSuccessDoesNotReturnCreatedTenant(string body)
    {
        var tokens = new TokenStore { AccessToken = "token-A" };
        using var transport = new Transport((_, _) => Task.FromResult(Json(HttpStatusCode.Created, body)));
        using var http = Pipeline(tokens, new TenantContext(), transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TenantApiClient(http).CreateTenantAsync(Context(tokens), "name", "1234567890", "tax"));
        Assert.Equal(1, transport.Count);
    }

    [Fact]
    public async Task InvalidOrCancelledContextIsRejectedBeforeTransport()
    {
        var tokens = new TokenStore { AccessToken = "token-A" };
        using var transport = new Transport((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "[]")));
        using var http = Pipeline(tokens, new TenantContext(), transport);
        var client = new TenantApiClient(http);
        foreach (ApiRequestContext context in new[] { Context(tokens) with { AccessToken = null }, Context(tokens) with { Scope = ApiRequestScope.TenantHeader } })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateTenantAsync(context, "name", "1234567890", "tax"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetMyTenantsAsync(context));
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ApiRequestContext cancelled = Context(tokens) with { CancellationToken = cancellation.Token };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateTenantAsync(cancelled, "name", "1234567890", "tax"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetMyTenantsAsync(cancelled));
        Assert.Equal(0, transport.Count);
    }

    private static NewTenantViewModel Form(ITenantApiClient client, ITokenStore tokens) => new(client, tokens)
    {
        Name = "New tenant", Nip = "1234567890", TaxEntityName = "Tax entity"
    };
    private static TaskCompletionSource<TenantCreationResult> Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ApiRequestContext Context(ITokenStore tokens) => new(1, tokens.SessionVersion, null, tokens.AccessToken, CancellationToken.None, ApiRequestScope.User);
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpClient Pipeline(ITokenStore tokens, TenantContext tenants, HttpMessageHandler transport) => new(new BearerTokenHandler(tokens)
    {
        InnerHandler = new TenantHeaderHandler(tenants) { InnerHandler = transport }
    }) { BaseAddress = new Uri("https://xksef.test/") };

    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return handler(request, cancellationToken);
        }
    }

    private sealed class FakeTenantClient : ITenantApiClient
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public List<ApiRequestContext> Contexts { get; } = [];
        public Func<ApiRequestContext, Task<TenantCreationResult>>? Handler { get; set; }
        public Task<TenantCreationResult> CreateTenantAsync(ApiRequestContext context, string name, string nip, string taxEntityName)
        {
            Contexts.Add(context);
            return Handler?.Invoke(context) ?? Task.FromResult(new TenantCreationResult(HttpStatusCode.Created, TenantId, []));
        }
        public Task<IReadOnlyList<TenantDto>> GetMyTenantsAsync(ApiRequestContext context) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantDto>> GetMyTenantsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid?> GetPreferredKsefCredentialIdAsync(ApiRequestContext context) => throw new NotSupportedException();
        public Task SetPreferredKsefCredentialIdAsync(ApiRequestContext context, Guid? credentialId) => throw new NotSupportedException();
    }
}
