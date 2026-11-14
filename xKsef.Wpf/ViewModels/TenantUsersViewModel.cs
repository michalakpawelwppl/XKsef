using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xKsef.Contracts.Tenants;
using xKsef.Wpf.Http;
using xKsef.Wpf.Services;

namespace xKsef.Wpf.ViewModels;

public sealed partial class TenantUsersViewModel : ObservableObject, IDisposable
{
    private readonly ITenantUsersApiClient _client;
    private readonly ITenantApiClient _tenants;
    private readonly ITokenStore _tokens;
    private readonly TenantContext _tenantContext;
    private readonly TenantDto? _tenant;
    private readonly ApiRequestContext _context;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private bool _disposed;
    private long _operation;
    public event Action? CloseRequested;
    public event Action<string?>? CurrentAccessChanged;
    public ObservableCollection<TenantUserDto> Users { get; } = [];
    public IReadOnlyList<string> Roles { get; } = [TenantRoles.User, TenantRoles.Admin];
    [ObservableProperty] private string _identifier = string.Empty;
    [ObservableProperty] private string _newRole = TenantRoles.User;
    [ObservableProperty] private string _selectedRole = TenantRoles.User;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanModify)), NotifyCanExecuteChangedFor(nameof(SetRoleCommand)), NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private TenantUserDto? _selectedUser;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyPropertyChangedFor(nameof(CanModify)), NotifyCanExecuteChangedFor(nameof(RefreshCommand)), NotifyCanExecuteChangedFor(nameof(AddCommand)), NotifyCanExecuteChangedFor(nameof(SetRoleCommand)), NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private bool _isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyPropertyChangedFor(nameof(CanModify)), NotifyCanExecuteChangedFor(nameof(RefreshCommand)), NotifyCanExecuteChangedFor(nameof(AddCommand)), NotifyCanExecuteChangedFor(nameof(SetRoleCommand)), NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private bool _isValid;
    public bool CanEdit => IsValid && !IsBusy && !_disposed;
    public bool CanModify => CanEdit && SelectedUser is not null;

    public TenantUsersViewModel(ITenantUsersApiClient client, ITenantApiClient tenants, ITokenStore tokens, TenantContext tenantContext)
    {
        _client = client; _tenants = tenants; _tokens = tokens; _tenantContext = tenantContext;
        _tenant = tenantContext.CurrentTenant;
        var session = tokens.GetSessionSnapshot();
        _context = new ApiRequestContext(tenantContext.Version, session.SessionVersion, _tenant?.Id, session.AccessToken, _cancellation.Token, ApiRequestScope.TenantRoute);
        IsValid = _tenant?.Role == TenantRoles.Admin && !string.IsNullOrWhiteSpace(session.AccessToken);
        tokens.TokenChanged += OnContextChanged;
        tenantContext.TenantChanged += OnContextChanged;
        if (!IsCurrent()) Invalidate();
    }
    partial void OnSelectedUserChanged(TenantUserDto? value) => SelectedRole = value?.Role ?? TenantRoles.User;
    private bool IsCurrent() => !_disposed && IsValid && !_context.CancellationToken.IsCancellationRequested && _tokens.SessionVersion == _context.SessionVersion && _tenantContext.Version == _context.Generation;
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RefreshAsync() => RunAsync(() => _client.GetAsync(_context), false);
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(Identifier) || Identifier.Length > 256 || !TenantRoles.IsValid(NewRole)) { ErrorMessage = "Podaj dokładny login lub e-mail oraz rolę Admin albo User."; return Task.CompletedTask; }
        string identifier = Identifier.Trim(), role = NewRole;
        return RunAsync(() => _client.AddAsync(_context, identifier, role), true);
    }
    [RelayCommand(CanExecute = nameof(CanModify))]
    private Task SetRoleAsync() => !CanModify ? Task.CompletedTask : RunAsync(() => _client.SetRoleAsync(_context, SelectedUser!.UserId, SelectedRole), true);
    [RelayCommand(CanExecute = nameof(CanModify))]
    private Task RemoveAsync() => !CanModify ? Task.CompletedTask : RunAsync(() => _client.RemoveAsync(_context, SelectedUser!.UserId), true);

    private async Task RunAsync(Func<Task<TenantUsersResponse>> operation, bool mutation)
    {
        if (!CanEdit || !IsCurrent()) return;
        long id = ++_operation;
        bool saved = false;
        IsBusy = true; ErrorMessage = string.Empty;
        try
        {
            var response = await operation();
            if (!IsCurrent() || id != _operation) return;
            if (response.StatusCode == HttpStatusCode.Forbidden) { await RefreshAccessAsync(); return; }
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created)) { ErrorMessage = Translate(response); return; }
            if (mutation)
            {
                saved = true; Users.Clear(); SelectedUser = null;
                if (response.CurrentUserRole != TenantRoles.Admin) { ApplyOwnAccess(response.CurrentUserRole); return; }
                response = await _client.GetAsync(_context);
                if (!IsCurrent() || id != _operation) return;
                if (response.StatusCode == HttpStatusCode.Forbidden) { await RefreshAccessAsync(); return; }
                if (response.StatusCode != HttpStatusCode.OK) { ErrorMessage = "Zmiana została zapisana, ale nie udało się odświeżyć listy."; return; }
            }
            Users.Clear();
            foreach (var user in response.Users) { if (!IsCurrent() || id != _operation) return; Users.Add(user); }
        }
        catch (OperationCanceledException) when (_context.CancellationToken.IsCancellationRequested) { }
        catch (Exception) { if (IsCurrent() && id == _operation) ErrorMessage = saved ? "Zmiana została zapisana, ale nie udało się odświeżyć listy." : "Nie udało się wykonać operacji. Sprawdź połączenie z API."; }
        finally { if (!_disposed && id == _operation) IsBusy = false; }
    }
    private async Task RefreshAccessAsync()
    {
        Users.Clear(); SelectedUser = null;
        try
        {
            var tenants = await _tenants.GetMyTenantsAsync(_context with { Scope = ApiRequestScope.User, TenantId = null });
            if (!IsCurrent()) return;
            string? role = tenants.FirstOrDefault(tenant => tenant.Id == _context.TenantId)?.Role;
            if (role != TenantRoles.Admin) { ApplyOwnAccess(role); return; }
        }
        finally { if (IsCurrent()) ApplyOwnAccess(null); }
    }
    private void ApplyOwnAccess(string? role)
    {
        if (!IsCurrent()) return;
        CurrentAccessChanged?.Invoke(role);
        if (IsCurrent()) _tenantContext.SetTenant(role is null ? null : _tenant! with { Role = role });
        if (!_disposed) Invalidate();
    }
    private static string Translate(TenantUsersResponse response) => response.Error switch
    {
        "LastAdmin" => "Nie można odebrać dostępu ani zmienić roli ostatniego administratora.",
        "AlreadyMember" => "Użytkownik ma już dostęp do tego tenanta.",
        "AmbiguousUser" => "Login i e-mail wskazują różne konta. Podaj jednoznaczny identyfikator.",
        "UserNotFound" => "Nie znaleziono użytkownika o podanym loginie lub e-mailu.",
        "MemberNotFound" => "Przypisanie użytkownika już nie istnieje. Odśwież listę.",
        "MembershipConflict" => "Dostęp zmienił się podczas operacji. Odśwież listę.",
        _ => response.StatusCode == HttpStatusCode.Unauthorized ? "Sesja wygasła. Zaloguj się ponownie." : "Sprawdź dane i wybraną rolę."
    };
    private void OnContextChanged(object? sender, EventArgs args)
    {
        if (_ui is not null && SynchronizationContext.Current != _ui) _ui.Post(_ => Invalidate(), null);
        else Invalidate();
    }
    private void Invalidate()
    {
        if (_disposed || !IsValid) return;
        ++_operation; IsValid = false; _cancellation.Cancel(); Users.Clear(); SelectedUser = null; IsBusy = false;
        ErrorMessage = "Kontekst lub uprawnienia zmieniły się. Zamknij okno.";
        CloseRequested?.Invoke();
    }
    [RelayCommand] private void Close() => CloseRequested?.Invoke();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_operation; _tokens.TokenChanged -= OnContextChanged; _tenantContext.TenantChanged -= OnContextChanged;
        _cancellation.Cancel(); Users.Clear(); SelectedUser = null; IsBusy = false; IsValid = false; _cancellation.Dispose();
    }
}