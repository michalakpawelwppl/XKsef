using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using xKsef.Contracts.Authentication;
using xKsef.Contracts.Tenants;
using xKsef.Wpf.Authentication;
using xKsef.Wpf.Behaviors;
using xKsef.Wpf.Http;
using xKsef.Wpf.Models;
using xKsef.Wpf.Services;
using xKsef.Wpf.ViewModels;
using xKsef.Wpf.Views;

namespace xKsef.Tests.Wpf;

public sealed class NewUserTests
{
    [Theory]
    [InlineData("", "new@example.com", "password", "password")]
    [InlineData("new", "", "password", "password")]
    [InlineData("new", "new@example.com", "", "")]
    [InlineData("new", "new@example.com", "password", "different")]
    [InlineData("new", "new@example.com", "password", "")]
    public async Task InvalidFormDoesNotSendRequest(string login, string email, string password, string confirmation)
    {
        var client = new FakeAuthenticationClient();
        using var vm = new NewUserViewModel(client, new TokenStore { AccessToken = "token-A" })
        {
            Login = login, Email = email, Password = password, ConfirmPassword = confirmation
        };
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(client.Contexts);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.IsSaving);
    }

    [Fact]
    public async Task NoSessionAllowsRegistrationWithoutRequiringTenant()
    {
        var client = new FakeAuthenticationClient();
        using var vm = ValidForm(client, new TokenStore());
        Assert.True(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        var context = Assert.Single(client.Contexts);
        Assert.Equal(ApiRequestScope.Anonymous, context.Scope);
        Assert.Null(context.AccessToken);
        Assert.Null(context.TenantId);
    }

    [Fact]
    public async Task SuccessClosesFormWithoutChangingSessionOrTenant()
    {
        var tokenStore = new TokenStore { AccessToken = "token-A" };
        long sessionVersion = tokenStore.SessionVersion;
        var tenantContext = new TenantContext();
        TenantDto tenant = new(Guid.NewGuid(), "Tenant A", "Admin");
        tenantContext.SetTenant(tenant);
        var client = new FakeAuthenticationClient();
        using var vm = ValidForm(client, tokenStore);
        bool? closed = null;
        vm.CloseRequested += result => { closed = result; vm.Dispose(); };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(closed);
        ApiRequestContext context = Assert.Single(client.Contexts);
        Assert.Equal(ApiRequestScope.Anonymous, context.Scope);
        Assert.Null(context.AccessToken);
        Assert.Equal(sessionVersion, context.SessionVersion);
        Assert.Null(context.TenantId);
        Assert.Equal("token-A", tokenStore.AccessToken);
        Assert.Equal(sessionVersion, tokenStore.SessionVersion);
        Assert.Same(tenant, tenantContext.CurrentTenant);
        Assert.Empty(vm.Password);
        Assert.Empty(vm.ConfirmPassword);
        Assert.False(vm.IsSaving);
    }

    [Theory]
    [InlineData("DuplicateUserName", "loginie")]
    [InlineData("DuplicateEmail", "e-mail")]
    [InlineData("DuplicateAccount", "Login lub")]
    [InlineData("InvalidUserName", "login")]
    [InlineData("InvalidEmail", "e-mail")]
    [InlineData("PasswordTooShort", "krótkie")]
    [InlineData("PasswordRequiresDigit", "cyfrę")]
    [InlineData("PasswordRequiresLower", "małą")]
    [InlineData("PasswordRequiresUpper", "wielką")]
    [InlineData("PasswordRequiresNonAlphanumeric", "specjalny")]
    [InlineData("PasswordRequiresUniqueChars", "różnych")]
    [InlineData("PasswordRequired", "hasło")]
    public async Task IdentityErrorsAreReadableAndFormRemainsOpen(string code, string expectedText)
    {
        var client = new FakeAuthenticationClient
        {
            Handler = _ => Task.FromResult(new UserCreationResult(HttpStatusCode.BadRequest, null, new[] { code }))
        };
        using var vm = ValidForm(client, new TokenStore { AccessToken = "token-A" });
        bool closed = false;
        vm.CloseRequested += _ => closed = true;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains(expectedText, vm.ErrorMessage);
        Assert.False(closed);
        Assert.False(vm.IsSaving);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "niedostępna")]
    [InlineData(HttpStatusCode.TooManyRequests, "Zbyt wiele")]
    [InlineData(HttpStatusCode.Forbidden, "niedostępna")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Nie udało")]
    public async Task HttpErrorsAreReportedWithoutRetry(HttpStatusCode status, string message)
    {
        var client = new FakeAuthenticationClient { Handler = _ => Task.FromResult(new UserCreationResult(status, null, [])) };
        using var vm = ValidForm(client, new TokenStore { AccessToken = "token-A" });
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains(message, vm.ErrorMessage);
        Assert.Single(client.Contexts);
        Assert.False(vm.IsSaving);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExceptionsDoNotExposeSensitiveMessagesAndClearLoading(bool networkError)
    {
        var client = new FakeAuthenticationClient
        {
            Handler = _ => networkError
                ? Task.FromException<UserCreationResult>(new HttpRequestException("secret-password"))
                : Task.FromException<UserCreationResult>(new InvalidOperationException("secret-password"))
        };
        using var vm = ValidForm(client, new TokenStore { AccessToken = "token-A" });
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.DoesNotContain("secret-password", vm.ErrorMessage);
        Assert.False(vm.IsSaving);
        Assert.Single(client.Contexts);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("error")]
    [InlineData("cancel")]
    [InlineData("unauthorized")]
    public async Task LateResponseAfterSessionChangeCannotAffectNewForm(string outcome)
    {
        var tokenStore = new TokenStore { AccessToken = "token-A" };
        var oldResponse = Completion();
        var newResponse = Completion();
        var client = new FakeAuthenticationClient
        {
            Handler = context => context.SessionVersion == 1 ? oldResponse.Task : newResponse.Task
        };
        using var oldVm = ValidForm(client, tokenStore);
        bool oldClosed = false;
        oldVm.CloseRequested += _ => oldClosed = true;
        Task oldSave = oldVm.SaveCommand.ExecuteAsync(null);

        tokenStore.AccessToken = "token-B";
        using var newVm = ValidForm(client, tokenStore);
        Task newSave = newVm.SaveCommand.ExecuteAsync(null);
        Assert.True(newVm.IsSaving);
        if (outcome == "error") oldResponse.SetException(new HttpRequestException("old error"));
        else if (outcome == "cancel") oldResponse.SetCanceled();
        else oldResponse.SetResult(new UserCreationResult(outcome == "unauthorized" ? HttpStatusCode.Unauthorized : HttpStatusCode.Created, "old-id", []));
        await oldSave;
        await Task.Yield();

        Assert.False(oldClosed);
        Assert.False(oldVm.IsSessionValid);
        Assert.Empty(oldVm.Password);
        Assert.Empty(oldVm.ConfirmPassword);
        Assert.False(oldVm.SaveCommand.CanExecute(null));
        Assert.True(newVm.IsSaving);
        Assert.Empty(newVm.ErrorMessage);
        Assert.Equal("token-B", tokenStore.AccessToken);
        Assert.Equal(2, client.Contexts.Count);

        newResponse.SetResult(new UserCreationResult(HttpStatusCode.Forbidden, null, []));
        await newSave;
        Assert.False(newVm.IsSaving);
    }

    [Fact]
    public async Task SessionChangeBeforeSavePreventsSendingUnderAnotherUser()
    {
        var tokenStore = new TokenStore { AccessToken = "token-A" };
        var client = new FakeAuthenticationClient();
        using var vm = ValidForm(client, tokenStore);
        tokenStore.AccessToken = "token-B";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(client.Contexts);
    }

    [Fact]
    public async Task ChangingTenantDoesNotInvalidateUserScopedOperation()
    {
        var response = Completion();
        var client = new FakeAuthenticationClient { Handler = _ => response.Task };
        var tokenStore = new TokenStore { AccessToken = "token-A" };
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant A", "User"));
        using var vm = ValidForm(client, tokenStore);
        bool closed = false;
        vm.CloseRequested += _ => closed = true;
        Task save = vm.SaveCommand.ExecuteAsync(null);
        var tenantB = new TenantDto(Guid.NewGuid(), "Tenant B", "User");
        tenantContext.SetTenant(tenantB);
        response.SetResult(new UserCreationResult(HttpStatusCode.Created, "new-user-id", []));
        await save;
        Assert.True(closed);
        Assert.Null(Assert.Single(client.Contexts).TenantId);
        Assert.Same(tenantB, tenantContext.CurrentTenant);
        Assert.Equal("token-A", tokenStore.AccessToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingOrCancellingFormClearsPasswordsAndIgnoresPendingResponse(bool cancel)
    {
        var response = Completion();
        var client = new FakeAuthenticationClient { Handler = _ => response.Task };
        var tokenStore = new TokenStore { AccessToken = "token-A" };
        using var vm = ValidForm(client, tokenStore);
        bool? result = null;
        vm.CloseRequested += value => { result = value; vm.Dispose(); };
        Task save = vm.SaveCommand.ExecuteAsync(null);
        if (cancel) vm.CancelCommand.Execute(null);
        else vm.Dispose();
        Assert.Empty(vm.Password);
        Assert.Empty(vm.ConfirmPassword);
        Assert.True(Assert.Single(client.Contexts).CancellationToken.IsCancellationRequested);
        response.SetResult(new UserCreationResult(HttpStatusCode.Created, "new-user", []));
        await save;
        Assert.Equal(cancel ? false : (bool?)null, result);
        Assert.False(vm.IsSaving);
        Assert.False(vm.SaveCommand.CanExecute(null));
        tokenStore.AccessToken = "token-B";
        Assert.Empty(vm.ErrorMessage);
    }

    [Fact]
    public async Task ClientUsesCapturedJwtWithoutTenantHeader()
    {
        var tokenStore = new TokenStore { AccessToken = "token-A" };
        ApiRequestContext context = Context(tokenStore);
        tokenStore.AccessToken = "token-B";
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(new TenantDto(Guid.NewGuid(), "Tenant B", "Admin"));
        using var transport = new Transport(async (request, cancellation) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/users", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer token-A", request.Headers.Authorization!.ToString());
            Assert.False(request.Headers.Contains("X-Tenant-Id"));
            Assert.True(request.TryGetApiRequestContext(out ApiRequestContext captured));
            Assert.Equal(context, captured);
            using var json = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            Assert.Equal(3, json.RootElement.EnumerateObject().Count());
            Assert.Equal("new", json.RootElement.GetProperty("login").GetString());
            Assert.Equal("new@example.com", json.RootElement.GetProperty("email").GetString());
            Assert.Equal("password", json.RootElement.GetProperty("password").GetString());
            return Json(HttpStatusCode.Created, "{\"userId\":\"string-identity-id\"}");
        });
        using var http = Pipeline(tokenStore, tenantContext, transport);
        var result = await new AuthenticationApiClient(http).CreateUserAsync(context, "new", "new@example.com", "password");
        Assert.Equal("string-identity-id", result.UserId);
        Assert.Equal("token-B", tokenStore.AccessToken);
        Assert.Single(transport.Calls);
    }

    [Fact]
    public async Task ClientRejectsInvalidContextBeforeTransport()
    {
        var tokenStore = new TokenStore { AccessToken = "token-A" };
        using var transport = new Transport((_, _) => Task.FromResult(Json(HttpStatusCode.Created, "{}")));
        using var http = Pipeline(tokenStore, new TenantContext(), transport);
        var client = new AuthenticationApiClient(http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateUserAsync(Context(tokenStore) with { AccessToken = null }, "", "", ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateUserAsync(Context(tokenStore) with { Scope = ApiRequestScope.TenantHeader }, "", "", ""));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateUserAsync(Context(tokenStore) with { CancellationToken = cancellation.Token }, "", "", ""));
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public async Task ClientReadsIdentityCodesAndSanitizesFieldErrors()
    {
        var tokenStore = new TokenStore { AccessToken = "token-A" };
        using var transport = new Transport((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest,
            "{\"errors\":{\"Identity\":[\"DuplicateEmail\",\"PasswordRequiresDigit\"],\"Login\":[\"secret-password\"]}}")));
        using var http = Pipeline(tokenStore, new TenantContext(), transport);
        var result = await new AuthenticationApiClient(http).CreateUserAsync(Context(tokenStore), "new", "email", "password");
        Assert.Equal(new[] { "DuplicateEmail", "PasswordRequiresDigit", "InvalidUserName" }, result.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedUnauthorizedResponseOnlyClearsOriginalSession(bool changeSession)
    {
        var tokenStore = new TokenStore { AccessToken = "token-A" };
        ApiRequestContext context = Context(tokenStore);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transport = new Transport((_, _) => response.Task);
        using var http = Pipeline(tokenStore, new TenantContext(), transport);
        Task<UserCreationResult> request = new AuthenticationApiClient(http).CreateUserAsync(context, "new", "email", "password");
        if (changeSession) tokenStore.AccessToken = "token-B";
        response.SetResult(Json(HttpStatusCode.Unauthorized, "{}"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await request).StatusCode);
        Assert.Equal(changeSession ? "token-B" : null, tokenStore.AccessToken);
        Assert.Single(transport.Calls);
    }

    [Fact]
    public async Task MalformedSuccessDoesNotReportUserCreated()
    {
        var tokenStore = new TokenStore { AccessToken = "token-A" };
        using var transport = new Transport((_, _) => Task.FromResult(Json(HttpStatusCode.Created, "{}")));
        using var http = Pipeline(tokenStore, new TenantContext(), transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AuthenticationApiClient(http).CreateUserAsync(Context(tokenStore), "new", "email", "password"));
        Assert.Single(transport.Calls);
    }

    [Fact]
    public async Task UnknownValidationTextIsNeverDisplayed()
    {
        var client = new FakeAuthenticationClient
        {
            Handler = _ => Task.FromResult(new UserCreationResult(HttpStatusCode.BadRequest, null, new[] { "secret-password" }))
        };
        using var vm = ValidForm(client, new TokenStore { AccessToken = "token-A" });
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.DoesNotContain("secret-password", vm.ErrorMessage);
    }

    [Fact]
    public Task PasswordBoxesAndViewModelAreClearedOnCleanup()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var vm = ValidForm(new FakeAuthenticationClient(), new TokenStore { AccessToken = "token-A" });
                var window = new NewUserWindow(vm);
                var password = (PasswordBox)window.FindName("PasswordInput");
                var confirmation = (PasswordBox)window.FindName("ConfirmPasswordInput");
                password.DataContext = vm;
                confirmation.DataContext = vm;
                BindingOperations.GetBindingExpression(password, PasswordBoxHelper.BoundPasswordProperty)!.UpdateTarget();
                BindingOperations.GetBindingExpression(confirmation, PasswordBoxHelper.BoundPasswordProperty)!.UpdateTarget();
                DrainDispatcher();
                password.Password = "first-secret";
                confirmation.Password = "second-secret";
                DrainDispatcher();
                Assert.Equal("first-secret", vm.Password);
                Assert.Equal("second-secret", vm.ConfirmPassword);
                vm.Dispose();
                BindingOperations.GetBindingExpression(password, PasswordBoxHelper.BoundPasswordProperty)!.UpdateTarget();
                BindingOperations.GetBindingExpression(confirmation, PasswordBoxHelper.BoundPasswordProperty)!.UpdateTarget();
                DrainDispatcher();
                Assert.Empty(password.Password);
                Assert.Empty(confirmation.Password);
                Assert.Empty(vm.Password);
                Assert.Empty(vm.ConfirmPassword);
                window.Close();
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [Fact]
    public async Task RegistrationClientAlwaysOmitsJwtAndTenantEvenWhenBothAreSelected()
    {
        var tokens = new TokenStore { AccessToken = "existing-token" };
        var tenants = new TenantContext();
        tenants.SetTenant(new TenantDto(Guid.NewGuid(), "Selected", "Admin"));
        var context = new ApiRequestContext(1, tokens.SessionVersion, null, null, CancellationToken.None, ApiRequestScope.Anonymous);
        using var transport = new Transport(async (request, cancellation) =>
        {
            Assert.Equal("/api/auth/register", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("X-Tenant-Id"));
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            Assert.Equal(3, body.RootElement.EnumerateObject().Count());
            Assert.Equal(" exact-password ", body.RootElement.GetProperty("password").GetString());
            return Json(HttpStatusCode.Created, "{\"userId\":\"new-id\"}");
        });
        using var http = Pipeline(tokens, tenants, transport);
        var result = await new AuthenticationApiClient(http).RegisterAsync(context, "new", "new@example.com", " exact-password ");
        Assert.Equal("new-id", result.UserId);
        Assert.Equal("existing-token", tokens.AccessToken);
        Assert.Single(transport.Calls);
    }

    [Fact]
    public async Task RegistrationClientRejectsCredentialBearingContextsAndHonorsCancellation()
    {
        var tokens = new TokenStore { AccessToken = "token" };
        using var transport = new Transport((_, _) => Task.FromResult(Json(HttpStatusCode.Created, "{}")));
        using var http = Pipeline(tokens, new TenantContext(), transport);
        var client = new AuthenticationApiClient(http);
        var anonymous = new ApiRequestContext(1, tokens.SessionVersion, null, null, CancellationToken.None, ApiRequestScope.Anonymous);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RegisterAsync(Context(tokens), "new", "email", "password"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RegisterAsync(anonymous with { AccessToken = "token" }, "new", "email", "password"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RegisterAsync(anonymous with { TenantId = Guid.NewGuid() }, "new", "email", "password"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RegisterAsync(anonymous with { CancellationToken = cancellation.Token }, "new", "email", "password"));
        Assert.Empty(transport.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task RegistrationErrorsNeverClearAnExistingSessionOrRetry(HttpStatusCode status)
    {
        var tokens = new TokenStore { AccessToken = "token" };
        using var transport = new Transport((_, _) => Task.FromResult(Json(status, "{}")));
        using var http = Pipeline(tokens, new TenantContext(), transport);
        var context = new ApiRequestContext(1, tokens.SessionVersion, null, null, CancellationToken.None, ApiRequestScope.Anonymous);
        var result = await new AuthenticationApiClient(http).RegisterAsync(context, "new", "email", "password");
        Assert.Equal(status, result.StatusCode);
        Assert.Equal("token", tokens.AccessToken);
        Assert.Single(transport.Calls);
    }

    [Fact]
    public void AccountSelectorOffersBothActionsWithoutLogin()
    {
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Wpf", "MainWindow.xaml"));
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var selector = Assert.Single(document.Descendants(), element => (string?)element.Attribute(x + "Name") == "AccountActionSelector");
        Assert.Equal("ComboBox", selector.Name.LocalName);
        Assert.Equal(new[] { "Zaloguj", "Utwórz nowego użytkownika" }, selector.Elements().Where(element => element.Name.LocalName == "ComboBoxItem").Select(element => (string?)element.Attribute("Content")));
        Assert.Contains("SelectedAccountAction", (string?)selector.Attribute("SelectedIndex"));
        Assert.Null(selector.Attribute("IsEnabled"));
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == "Zaloguj");
    }
    [Fact]
    public async Task AnonymousLoggedInAnonymousRoundTripCannotCommitTheOldRegistration()
    {
        var tokens = new TokenStore();
        var pending = Completion();
        var client = new FakeAuthenticationClient { Handler = _ => pending.Task };
        using var vm = ValidForm(client, tokens);
        bool closed = false;
        vm.CloseRequested += _ => closed = true;
        Task save = vm.SaveCommand.ExecuteAsync(null);
        tokens.AccessToken = "intermediate-session";
        tokens.AccessToken = null;
        pending.SetResult(new UserCreationResult(HttpStatusCode.Created, "old-id", []));
        await save;
        Assert.False(closed);
        Assert.False(vm.IsSessionValid);
        Assert.Empty(vm.Password);
        Assert.Empty(vm.ConfirmPassword);
        Assert.Null(tokens.AccessToken);
        Assert.True(Assert.Single(client.Contexts).CancellationToken.IsCancellationRequested);
    }
    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static NewUserViewModel ValidForm(IAuthenticationApiClient client, ITokenStore tokenStore) => new(client, tokenStore)
    {
        Login = "new", Email = "new@example.com", Password = "password", ConfirmPassword = "password"
    };

    private static TaskCompletionSource<UserCreationResult> Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ApiRequestContext Context(ITokenStore tokens) => new(1, tokens.SessionVersion, null, tokens.AccessToken, CancellationToken.None, ApiRequestScope.User);
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpClient Pipeline(ITokenStore tokens, TenantContext tenants, HttpMessageHandler transport) => new(new BearerTokenHandler(tokens)
    {
        InnerHandler = new TenantHeaderHandler(tenants) { InnerHandler = transport }
    }) { BaseAddress = new Uri("https://xksef.test/") };

    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            return handler(request, cancellationToken);
        }
    }

    private sealed class FakeAuthenticationClient : IAuthenticationApiClient
    {
        public List<ApiRequestContext> Contexts { get; } = [];
        public Func<ApiRequestContext, Task<UserCreationResult>> Handler { get; set; }
            = _ => Task.FromResult(new UserCreationResult(HttpStatusCode.Created, "new-id", []));
        public Task<UserCreationResult> CreateUserAsync(ApiRequestContext context, string login, string email, string password) => throw new NotSupportedException();
        public Task<UserCreationResult> RegisterAsync(ApiRequestContext context, string login, string email, string password)
        {
            Contexts.Add(context);
            return Handler(context);
        }
        public Task<LoginResponse?> LoginAsync(ApiRequestContext context, string login, string password) => throw new NotSupportedException();
        public Task AuthenticateKsefTokenAsync(ApiRequestContext context, Guid credentialId) => throw new NotSupportedException();
        public Task AuthenticateKsefCertificateAsync(ApiRequestContext context, Guid credentialId) => throw new NotSupportedException();
        public Task<Guid> SaveKsefCertificateAsync(ApiRequestContext context, string certificatePem, string encryptedPrivateKeyPem, string password) => throw new NotSupportedException();
    }
}
