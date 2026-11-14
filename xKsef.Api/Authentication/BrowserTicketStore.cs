using System.Data.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using xKsef.Infrastructure.Authentication;

namespace xKsef.Api.Authentication;

// Only transport/storage failures become 503. Cancellation and programming errors retain their semantics.
public sealed class BrowserSessionStoreUnavailableException : Exception
{
    public BrowserSessionStoreUnavailableException(Exception inner) : base("Magazyn sesji przeglądarkowych jest chwilowo niedostępny.", inner) { }
}

public sealed class BrowserTicketStore(SqlBrowserTicketStore store) : ITicketStore
{
    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);
    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);
    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);
    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);
    public Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext context, CancellationToken cancellationToken) => StoreAsync(ticket, cancellationToken);
    public Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext context, CancellationToken cancellationToken) => RenewAsync(key, ticket, cancellationToken);
    public Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext context, CancellationToken cancellationToken) => RetrieveAsync(key, cancellationToken);
    public Task RemoveAsync(string key, HttpContext context, CancellationToken cancellationToken) => RemoveAsync(key, cancellationToken);
    public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticket.Principal.FindFirst("AspNet.Identity.SecurityStamp")?.Value)) throw new BrowserSessionIdentityChangedException();
        return Guard(() => store.StoreAsync(ticket, cancellationToken));
    }
    public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken) => Guard(() => store.RetrieveAsync(key, cancellationToken));
    public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken) => Guard(() => store.RenewAsync(key, ticket, cancellationToken));
    public Task RemoveAsync(string key, CancellationToken cancellationToken) => Guard(() => store.RemoveAsync(key, cancellationToken));
    public Task<int> RevokeAllAsync(string userId, CancellationToken cancellationToken) => Guard(() => store.RevokeAllAsync(userId, cancellationToken));
    private static bool IsStorageError(Exception error) => error is DbException or DbUpdateException or IOException or TimeoutException;
    private static async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception error) when (IsStorageError(error)) { throw new BrowserSessionStoreUnavailableException(error); }
    }
    private static async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) when (IsStorageError(error)) { throw new BrowserSessionStoreUnavailableException(error); }
    }
}
