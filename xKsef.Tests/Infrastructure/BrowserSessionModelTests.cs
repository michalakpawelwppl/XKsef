using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using xKsef.Infrastructure.Authentication;
using xKsef.Infrastructure.Migrations;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Infrastructure;

public sealed class BrowserSessionModelTests
{
    [Fact]
    public void AdditiveMigrationAndModelContainOnlyTheNewSessionTable()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=offline.invalid;Database=ModelOnly;Integrated Security=True").Options);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(BrowserSession))!;
        Assert.True(entity.FindProperty(nameof(BrowserSession.Version))!.IsConcurrencyToken);
        Assert.Equal(3, entity.GetCheckConstraints().Count()); Assert.Equal(3, entity.GetIndexes().Count());
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(entity.GetForeignKeys()).DeleteBehavior);
        var migration = new AddBrowserSessions();
        Assert.Equal(4, migration.UpOperations.Count);
        var table = Assert.IsType<CreateTableOperation>(migration.UpOperations[0]); Assert.Equal("BrowserSessions", table.Name);
        Assert.Equal(9, table.Columns.Count); Assert.Equal("RevokedAt", Assert.Single(table.Columns, column => column.IsNullable).Name);
        Assert.All(migration.UpOperations.Skip(1), operation => Assert.Equal("BrowserSessions", Assert.IsType<CreateIndexOperation>(operation).Table));
        Assert.Equal("BrowserSessions", Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("00:00:00", "08:00:00")]
    [InlineData("-00:01:00", "08:00:00")]
    [InlineData("02:00:00", "01:00:00")]
    public void InvalidLifetimeConfigurationFailsBeforeAnyStoreAccess(string idle, string absolute)
    {
        var services = new ServiceCollection(); services.AddBrowserSessionStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["BrowserSessions:IdleTimeout"] = idle, ["BrowserSessions:AbsoluteLifetime"] = absolute }).Build());
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BrowserSessionOptions>>().Value);
    }

    [Fact]
    public void DefaultAndCustomLifetimesAreBoundWithoutEnablingCookieAuthentication()
    {
        var defaults = new BrowserSessionOptions(); Assert.Equal(TimeSpan.FromMinutes(30), defaults.IdleTimeout); Assert.Equal(TimeSpan.FromHours(8), defaults.AbsoluteLifetime);
        var services = new ServiceCollection(); services.AddBrowserSessionStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["BrowserSessions:IdleTimeout"] = "00:45:00", ["BrowserSessions:AbsoluteLifetime"] = "10:00:00" }).Build());
        using var provider = services.BuildServiceProvider(); var configured = provider.GetRequiredService<IOptions<BrowserSessionOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(45), configured.IdleTimeout); Assert.Equal(TimeSpan.FromHours(10), configured.AbsoluteLifetime);
        Assert.Null(provider.GetService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>());
    }

    [Theory]
    [InlineData("Bearer", "owner", true)]
    [InlineData("BrowserCookie", "", true)]
    [InlineData("BrowserCookie", "owner", false)]
    public async Task InvalidIdentityCannotCreateSession(string scheme, string userId, bool authenticated)
    {
        var store = new SqlBrowserTicketStore(new UnavailableScopes(), new EphemeralDataProtectionProvider(), Options.Create(new BrowserSessionOptions()), TimeProvider.System);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, authenticated ? scheme : null)), scheme);
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreAsync(ticket));
    }

    [Fact]
    public async Task UnknownMalformedOrCancelledKeysDoNotProduceIdentity()
    {
        var store = new SqlBrowserTicketStore(new UnavailableScopes(), new EphemeralDataProtectionProvider(), Options.Create(new BrowserSessionOptions()), TimeProvider.System);
        foreach (string key in new[] { "", "invalid", new string('!', 43) }) Assert.Null(await store.RetrieveAsync(key));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RetrieveAsync(new string('A', 43), cancel.Token));
        await Assert.ThrowsAsync<IOException>(() => store.RetrieveAsync(new string('A', 43)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.DeleteExpiredAsync(0));
    }
    private sealed class UnavailableScopes : IServiceScopeFactory
    { public IServiceScope CreateScope() => throw new IOException("Store is unavailable."); }
}