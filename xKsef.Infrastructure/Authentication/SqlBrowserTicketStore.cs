using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Infrastructure.Authentication;

/// <summary>Server-only cookie tickets. Database failures propagate; no local or stateless fallback exists.</summary>
public sealed class SqlBrowserTicketStore : ITicketStore
{
    private const string StampClaim = "AspNet.Identity.SecurityStamp";
    private const string AbsoluteProperty = ".xksef.absolute-expiry";
    private const string IdleProperty = ".xksef.idle-expiry";
    private const string RenewedProperty = ".xksef.renewed-at";
    private readonly IServiceScopeFactory _scopes;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;
    private readonly BrowserSessionOptions _options;

    public SqlBrowserTicketStore(IServiceScopeFactory scopes, IDataProtectionProvider protection, IOptions<BrowserSessionOptions> options, TimeProvider clock)
    {
        _scopes = scopes; _protector = protection.CreateProtector("xKsef.BrowserSessions.v1"); _clock = clock;
        _options = options.Value;
        if (!_options.IsValid()) throw new OptionsValidationException(BrowserSessionOptions.SectionName, typeof(BrowserSessionOptions), ["Invalid browser session lifetime configuration."]);
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);
    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);
    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);
    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);
    public Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext context, CancellationToken cancellationToken) => StoreAsync(ticket, cancellationToken);
    public Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext context, CancellationToken cancellationToken) => RenewAsync(key, ticket, cancellationToken);
    public Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext context, CancellationToken cancellationToken) => RetrieveAsync(key, cancellationToken);
    public Task RemoveAsync(string key, HttpContext context, CancellationToken cancellationToken) => RemoveAsync(key, cancellationToken);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        string owner = Owner(ticket);
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await Users(db).SingleOrDefaultAsync(user => user.Id == owner, cancellationToken)
            ?? throw new InvalidOperationException("Cannot create a session for an unavailable user.");
        if (string.IsNullOrWhiteSpace(user.SecurityStamp)) throw new InvalidOperationException("User security state is unavailable.");
        string? verifiedStamp = ticket.Principal.FindFirstValue(StampClaim);
        if (verifiedStamp is not null && verifiedStamp != user.SecurityStamp) throw new BrowserSessionIdentityChangedException();
        string key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = _clock.GetUtcNow();
        var session = new BrowserSession
        {
            IdHash = Hash(key)!, UserId = user.Id, ProtectedTicket = [], CreatedAt = now, RenewedAt = now,
            IdleExpiresAt = now.Add(_options.IdleTimeout), AbsoluteExpiresAt = now.Add(_options.AbsoluteLifetime), Version = Guid.NewGuid()
        };
        session.ProtectedTicket = Protect(session, CanonicalTicket(session, user));
        db.BrowserSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return key;
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? hash = Hash(key);
        if (hash is null) return null;
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await Active(db, _clock.GetUtcNow()).SingleOrDefaultAsync(session => session.IdHash == hash, cancellationToken);
        if (session is null) return null;
        var ticket = Unprotect(session);
        if (ticket is null) return null;
        var user = await Users(db).SingleOrDefaultAsync(user => user.Id == session.UserId, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.SecurityStamp) || ticket.Principal.FindFirstValue(StampClaim) != (user.SecurityStamp ?? string.Empty)) return null;
        var now = _clock.GetUtcNow();
        return now < session.IdleExpiresAt && now < session.AbsoluteExpiresAt ? ticket : null;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        string owner = Owner(ticket);
        string? hash = Hash(key);
        if (hash is null) return;
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await Active(db, _clock.GetUtcNow()).SingleOrDefaultAsync(session => session.IdHash == hash && session.UserId == owner, cancellationToken);
        if (session is null) return;
        var previous = Unprotect(session);
        if (previous is null) return;
        var user = await Users(db).SingleOrDefaultAsync(user => user.Id == owner, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.SecurityStamp) || previous.Principal.FindFirstValue(StampClaim) != (user.SecurityStamp ?? string.Empty)) return;
        var now = _clock.GetUtcNow();
        var idle = now.Add(_options.IdleTimeout);
        if (idle > session.AbsoluteExpiresAt) idle = session.AbsoluteExpiresAt;
        if (now >= session.IdleExpiresAt || now >= session.AbsoluteExpiresAt || now < session.RenewedAt) return;
        Guid previousVersion = session.Version;
        session.RenewedAt = now; session.IdleExpiresAt = idle;
        byte[] payload = Protect(session, CanonicalTicket(session, user));
        Guid nextVersion = Guid.NewGuid();
        // No INSERT/upsert: revocation, cleanup and competing renewals can only make this UPDATE a no-op.
        await Active(db, now).Where(row => row.IdHash == hash && row.UserId == owner && row.Version == previousVersion
            && db.Users.Any(current => current.Id == owner && current.SecurityStamp == user.SecurityStamp))
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProtectedTicket, payload)
                .SetProperty(row => row.RenewedAt, now).SetProperty(row => row.IdleExpiresAt, idle)
                .SetProperty(row => row.Version, nextVersion), cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? hash = Hash(key);
        if (hash is null) return;
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = _clock.GetUtcNow(); var version = Guid.NewGuid();
        await db.BrowserSessions.Where(row => row.IdHash == hash && row.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, now).SetProperty(row => row.Version, version), cancellationToken);
    }

    public async Task<int> RevokeAllAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = _clock.GetUtcNow(); var version = Guid.NewGuid();
        return await db.BrowserSessions.Where(row => row.UserId == userId && row.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, now).SetProperty(row => row.Version, version), cancellationToken);
    }

    public async Task<int> DeleteExpiredAsync(int batchSize = 1000, CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var now = _clock.GetUtcNow();
        return await db.BrowserSessions.Where(row => row.RevokedAt != null || row.IdleExpiresAt <= now || row.AbsoluteExpiresAt <= now)
            .OrderBy(row => row.IdleExpiresAt).ThenBy(row => row.IdHash).Take(batchSize).ExecuteDeleteAsync(cancellationToken);
    }

    private static IQueryable<ApplicationUser> Users(AppDbContext db) => db.Users.AsNoTracking()
        .Select(user => new ApplicationUser { Id = user.Id, UserName = user.UserName, SecurityStamp = user.SecurityStamp });

    private static IQueryable<BrowserSession> Active(AppDbContext db, DateTimeOffset now) => db.BrowserSessions.AsNoTracking()
        .Where(row => row.RevokedAt == null && row.IdleExpiresAt > now && row.AbsoluteExpiresAt > now);

    private static string Owner(AuthenticationTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var owners = ticket.Principal.FindAll(ClaimTypes.NameIdentifier).ToArray();
        if (ticket.AuthenticationScheme != BrowserSessionOptions.AuthenticationScheme || ticket.Principal.Identity?.IsAuthenticated != true
            || owners.Length != 1 || string.IsNullOrWhiteSpace(owners[0].Value) || owners[0].Value.Length > 450)
            throw new ArgumentException("A single authenticated browser session owner is required.", nameof(ticket));
        return owners[0].Value;
    }

    private static AuthenticationTicket CanonicalTicket(BrowserSession session, ApplicationUser user)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
            new Claim(StampClaim, user.SecurityStamp ?? string.Empty) }, BrowserSessionOptions.AuthenticationScheme);
        var properties = new AuthenticationProperties { IssuedUtc = session.RenewedAt, ExpiresUtc = session.IdleExpiresAt, IsPersistent = false, AllowRefresh = true };
        properties.Items[AbsoluteProperty] = session.AbsoluteExpiresAt.ToString("O", CultureInfo.InvariantCulture);
        properties.Items[IdleProperty] = session.IdleExpiresAt.ToString("O", CultureInfo.InvariantCulture);
        properties.Items[RenewedProperty] = session.RenewedAt.ToString("O", CultureInfo.InvariantCulture);
        return new(new ClaimsPrincipal(identity), properties, BrowserSessionOptions.AuthenticationScheme);
    }

    private byte[] Protect(BrowserSession session, AuthenticationTicket ticket)
    {
        byte[] data = TicketSerializer.Default.Serialize(ticket);
        try { return _protector.CreateProtector(session.IdHash, session.UserId).Protect(data); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    private AuthenticationTicket? Unprotect(BrowserSession session)
    {
        try
        {
            byte[] data = _protector.CreateProtector(session.IdHash, session.UserId).Unprotect(session.ProtectedTicket);
            AuthenticationTicket? ticket;
            try { ticket = TicketSerializer.Default.Deserialize(data); }
            finally { CryptographicOperations.ZeroMemory(data); }
            if (ticket is null) return null;
            ticket.Properties.Items.TryGetValue(AbsoluteProperty, out string? absolute);
            ticket.Properties.Items.TryGetValue(IdleProperty, out string? idle);
            ticket.Properties.Items.TryGetValue(RenewedProperty, out string? renewed);
            if (Owner(ticket) != session.UserId || renewed != session.RenewedAt.ToString("O", CultureInfo.InvariantCulture) || idle != session.IdleExpiresAt.ToString("O", CultureInfo.InvariantCulture)
                || absolute != session.AbsoluteExpiresAt.ToString("O", CultureInfo.InvariantCulture)) return null;
            return ticket;
        }
        catch (Exception error) when (error is CryptographicException or InvalidDataException or EndOfStreamException or ArgumentException or FormatException) { return null; }
    }

    private static string? Hash(string? key) => key is { Length: 43 } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        ? Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(key))) : null;
}