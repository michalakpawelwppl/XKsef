using System.Net;
using WpfTestDispatcher = xKsef.Tests.Wpf.InvoiceContextIsolationTests.WpfTestDispatcher;
using System.Net.Http.Json;
using System.ComponentModel.DataAnnotations;
using xKsef.Contracts.Tenants;
using xKsef.Wpf.Http;
using xKsef.Wpf.Services;
using xKsef.Wpf.ViewModels;

namespace xKsef.Tests.Wpf;

[Collection(WpfTestCollection.Name)]
public sealed class TenantUsersTests
{
    [Theory]
    [InlineData("User")]
    [InlineData("Admin")]
    [InlineData("GlobalAdmin")]
    [InlineData("Admin\n")]
    [InlineData("")]
    public void OnlyTheTwoTenantRolesPassValidation(string role)
    {
        var request = new ChangeTenantUserRoleRequest { Role = role };
        Assert.Equal(role is "Admin" or "User", Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Admin")]
    public async Task OnlyAdminCanLoadTheTenantScopedList(string role)
    {
        var tokens = new TokenStore { AccessToken = "token" };
        var context = new TenantContext(); context.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant", role));
        var client = new MembersClient();
        using var vm = new TenantUsersViewModel(client, new TenantClient(), tokens, context);
        Assert.Equal(role == "Admin", vm.RefreshCommand.CanExecute(null));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(role == "Admin" ? 1 : 0, client.Calls.Count);
        if (role == "Admin") { Assert.Equal(ApiRequestScope.TenantRoute, client.Calls[0].Scope); Assert.Equal(context.TenantId, client.Calls[0].TenantId); }
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("session")]
    [InlineData("closed")]
    public Task LateResponsesCannotRestoreUsersOrReleaseAnotherWindowsLoading(string change)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokens = new TokenStore { AccessToken = "token" }; var context = new TenantContext();
            var a = new TenantDto(Guid.NewGuid(), "A", "Admin"); context.SetTenant(a);
            var pending = new TaskCompletionSource<TenantUsersResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new MembersClient { Load = _ => pending.Task };
            using var old = new TenantUsersViewModel(client, new TenantClient(), tokens, context);
            var task = old.RefreshCommand.ExecuteAsync(null);
            if (change == "tenant") { context.SetTenant(new TenantDto(Guid.NewGuid(), "B", "Admin")); context.SetTenant(a); }
            else if (change == "session") { tokens.AccessToken = "other"; tokens.AccessToken = "token"; }
            else old.Dispose();
            var newerPending = new TaskCompletionSource<TenantUsersResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var newerClient = new MembersClient { Load = _ => newerPending.Task };
            using var newer = new TenantUsersViewModel(newerClient, new TenantClient(), tokens, context);
            var newerTask = newer.RefreshCommand.ExecuteAsync(null);
            pending.SetResult(new(HttpStatusCode.OK, [new("old", "old", "old@example.com", "Admin")]));
            await task;
            Assert.Empty(old.Users); Assert.False(old.IsValid); Assert.True(newer.IsBusy); Assert.Empty(newer.Users);
            newerPending.SetResult(new(HttpStatusCode.OK, [])); await newerTask;
            Assert.False(newer.IsBusy);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SuccessfulSelfChangeImmediatelyInvalidatesTheWindowAndUpdatesContext(bool remove)
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokens = new TokenStore { AccessToken = "token" }; var context = new TenantContext(); context.SetTenant(new TenantDto(Guid.NewGuid(), "A", "Admin"));
            var client = new MembersClient { Mutate = _ => Task.FromResult(new TenantUsersResponse(HttpStatusCode.OK, [], remove ? null : "User")) };
            using var vm = new TenantUsersViewModel(client, new TenantClient(), tokens, context);
            vm.SelectedUser = new("self", "self", "self@example.com", "Admin"); vm.SelectedRole = "User";
            if (remove) await vm.RemoveCommand.ExecuteAsync(null); else await vm.SetRoleCommand.ExecuteAsync(null);
            Assert.False(vm.IsValid); Assert.False(vm.IsBusy); Assert.Empty(vm.Users);
            Assert.Equal(remove ? null : "User", context.CurrentTenant?.Role); Assert.Equal("token", tokens.AccessToken);
            Assert.Single(client.Calls);
        });
    }

    [Fact]
    public async Task LastAdminConflictIsReadableAndDoesNotRetry()
    {
        var tokens = new TokenStore { AccessToken = "token" }; var context = new TenantContext(); context.SetTenant(new TenantDto(Guid.NewGuid(), "A", "Admin"));
        var client = new MembersClient { Mutate = _ => Task.FromResult(new TenantUsersResponse(HttpStatusCode.Conflict, [], Error: "LastAdmin")) };
        using var vm = new TenantUsersViewModel(client, new TenantClient(), tokens, context);
        vm.SelectedUser = new("self", "self", null, "Admin");
        await vm.RemoveCommand.ExecuteAsync(null);
        Assert.Contains("ostatniego", vm.ErrorMessage); Assert.Single(client.Calls); Assert.True(vm.IsValid); Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SuccessfulWriteAndFailedRefreshNeverRepeatTheWrite()
    {
        var tokens = new TokenStore { AccessToken = "token" }; var context = new TenantContext(); context.SetTenant(new TenantDto(Guid.NewGuid(), "A", "Admin"));
        var client = new MembersClient { Load = _ => Task.FromException<TenantUsersResponse>(new HttpRequestException("SQL-secret")) };
        using var vm = new TenantUsersViewModel(client, new TenantClient(), tokens, context);
        vm.Identifier = "exact@example.com";
        await vm.AddCommand.ExecuteAsync(null);
        Assert.Contains("zapisana", vm.ErrorMessage); Assert.DoesNotContain("SQL-secret", vm.ErrorMessage); Assert.Equal(1, client.Writes); Assert.False(vm.IsBusy);
    }

    [Fact]
    public Task ForbiddenClearsSensitiveListAndRefreshesCurrentAccess()
    {
        return WpfTestDispatcher.RunAsync(async () =>
        {
            var tokens = new TokenStore { AccessToken = "token" }; var context = new TenantContext(); var tenant = new TenantDto(Guid.NewGuid(), "A", "Admin"); context.SetTenant(tenant);
            var client = new MembersClient { Load = _ => Task.FromResult(new TenantUsersResponse(HttpStatusCode.Forbidden, [])) };
            var tenants = new TenantClient { Tenants = [tenant with { Role = "User" }] };
            using var vm = new TenantUsersViewModel(client, tenants, tokens, context);
            vm.Users.Add(new("old", "old", "old@example.com", "Admin"));
            await vm.RefreshCommand.ExecuteAsync(null);
            Assert.Empty(vm.Users); Assert.False(vm.IsValid); Assert.Equal("User", context.CurrentTenant?.Role);
            Assert.Equal(ApiRequestScope.User, Assert.Single(tenants.Calls).Scope); Assert.Null(tenants.Calls[0].TenantId);
        });
    }

    [Fact]
    public async Task ClientUsesCapturedTenantRouteAndTokenWithoutTenantHeader()
    {
        var tokens = new TokenStore { AccessToken = "other-token" }; var context = new TenantContext(); context.SetTenant(new TenantDto(Guid.NewGuid(), "Other", "User"));
        var id = Guid.NewGuid(); var snapshot = new ApiRequestContext(7, 3, id, "captured-token", CancellationToken.None, ApiRequestScope.TenantRoute);
        using var transport = new Transport(request =>
        {
            Assert.StartsWith($"/api/tenants/{id:D}/users", request.RequestUri!.AbsolutePath); Assert.Equal("Bearer captured-token", request.Headers.Authorization!.ToString()); Assert.False(request.Headers.Contains("X-Tenant-Id"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new TenantAccessChangeDto("Admin")) };
        });
        using var http = new HttpClient(new BearerTokenHandler(tokens) { InnerHandler = new TenantHeaderHandler(context) { InnerHandler = transport } }) { BaseAddress = new Uri("https://test/") };
        var client = new TenantUsersApiClient(http);
        await client.SetRoleAsync(snapshot, "identity-id", "User");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync(snapshot with { Scope = ApiRequestScope.User }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync(snapshot with { AccessToken = null }));
        Assert.Equal(1, transport.Calls);
    }

    private sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return Task.FromResult(handler(request)); }
    }
    private sealed class MembersClient : ITenantUsersApiClient
    {
        public List<ApiRequestContext> Calls { get; } = []; public int Writes { get; private set; }
        public Func<ApiRequestContext, Task<TenantUsersResponse>> Load { get; set; } = _ => Task.FromResult(new TenantUsersResponse(HttpStatusCode.OK, []));
        public Func<ApiRequestContext, Task<TenantUsersResponse>> Mutate { get; set; } = _ => Task.FromResult(new TenantUsersResponse(HttpStatusCode.OK, [], "Admin"));
        public Task<TenantUsersResponse> GetAsync(ApiRequestContext context) { Calls.Add(context); return Load(context); }
        private Task<TenantUsersResponse> Write(ApiRequestContext context) { Calls.Add(context); Writes++; return Mutate(context); }
        public Task<TenantUsersResponse> AddAsync(ApiRequestContext context, string identifier, string role) => Write(context);
        public Task<TenantUsersResponse> SetRoleAsync(ApiRequestContext context, string userId, string role) => Write(context);
        public Task<TenantUsersResponse> RemoveAsync(ApiRequestContext context, string userId) => Write(context);
    }
    private sealed class TenantClient : ITenantApiClient
    {
        public IReadOnlyList<TenantDto> Tenants { get; set; } = []; public List<ApiRequestContext> Calls { get; } = [];
        public Task<IReadOnlyList<TenantDto>> GetMyTenantsAsync(ApiRequestContext context) { Calls.Add(context); return Task.FromResult(Tenants); }
        public Task<IReadOnlyList<TenantDto>> GetMyTenantsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<xKsef.Wpf.Models.TenantCreationResult> CreateTenantAsync(ApiRequestContext context, string name, string nip, string taxEntityName) => throw new NotSupportedException();
        public Task<Guid?> GetPreferredKsefCredentialIdAsync(ApiRequestContext context) => throw new NotSupportedException();
        public Task SetPreferredKsefCredentialIdAsync(ApiRequestContext context, Guid? credentialId) => throw new NotSupportedException();
    }
}