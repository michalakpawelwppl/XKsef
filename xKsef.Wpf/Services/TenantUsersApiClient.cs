using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using xKsef.Contracts.Tenants;
using xKsef.Wpf.Http;

namespace xKsef.Wpf.Services;

public sealed record TenantUsersResponse(HttpStatusCode StatusCode, IReadOnlyList<TenantUserDto> Users, string? CurrentUserRole = null, string? Error = null);

public interface ITenantUsersApiClient
{
    Task<TenantUsersResponse> GetAsync(ApiRequestContext context);
    Task<TenantUsersResponse> AddAsync(ApiRequestContext context, string identifier, string role);
    Task<TenantUsersResponse> SetRoleAsync(ApiRequestContext context, string userId, string role);
    Task<TenantUsersResponse> RemoveAsync(ApiRequestContext context, string userId);
}

public sealed class TenantUsersApiClient(HttpClient http) : ITenantUsersApiClient
{
    private sealed record ErrorResponse(string? Code);
    public Task<TenantUsersResponse> GetAsync(ApiRequestContext context) => SendAsync(context, HttpMethod.Get, "", null);
    public Task<TenantUsersResponse> AddAsync(ApiRequestContext context, string identifier, string role) => SendAsync(context, HttpMethod.Post, "", new AddTenantUserRequest { Identifier = identifier, Role = role });
    public Task<TenantUsersResponse> SetRoleAsync(ApiRequestContext context, string userId, string role) => SendAsync(context, HttpMethod.Put, "/" + Uri.EscapeDataString(userId) + "/role", new ChangeTenantUserRoleRequest { Role = role });
    public Task<TenantUsersResponse> RemoveAsync(ApiRequestContext context, string userId) => SendAsync(context, HttpMethod.Delete, "/" + Uri.EscapeDataString(userId), null);

    private async Task<TenantUsersResponse> SendAsync(ApiRequestContext context, HttpMethod method, string suffix, object? body)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Scope != ApiRequestScope.TenantRoute || context.TenantId is not Guid id || id == Guid.Empty || string.IsNullOrWhiteSpace(context.AccessToken))
            throw new InvalidOperationException("Tenant user management requires a captured tenant route context.");
        using var request = new HttpRequestMessage(method, $"api/tenants/{id:D}/users{suffix}");
        request.SetApiRequestContext(context);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, context.CancellationToken);
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
        {
            if (method == HttpMethod.Get)
                return new(response.StatusCode, await response.Content.ReadFromJsonAsync<List<TenantUserDto>>(cancellationToken: context.CancellationToken) ?? throw new InvalidOperationException("Invalid member list."));
            var result = await response.Content.ReadFromJsonAsync<TenantAccessChangeDto>(cancellationToken: context.CancellationToken) ?? throw new InvalidOperationException("Invalid access response.");
            if (result.CurrentUserRole is not null && !TenantRoles.IsValid(result.CurrentUserRole)) throw new InvalidOperationException("Invalid access role.");
            return new(response.StatusCode, [], result.CurrentUserRole);
        }
        string? error = null;
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken: context.CancellationToken))?.Code;
        return new(response.StatusCode, [], Error: error);
    }
}