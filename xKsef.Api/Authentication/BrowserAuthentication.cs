using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using xKsef.Infrastructure.Authentication;

namespace xKsef.Api.Authentication;

public static class BrowserAuthentication
{
    public const string Selector = "ApiAuthentication";
    public const string BrowserPolicy = "BrowserSession";
    public const string CookieName = "__Host-xksef-session";
    public const string CsrfHeader = "X-CSRF-TOKEN";

    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var key = Convert.FromBase64String(configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Brak Jwt:SigningKey."));
        if (key.Length < 32) throw new InvalidOperationException("Klucz JWT musi mieć co najmniej 256 bitów.");
        string issuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Brak Jwt:Issuer.");
        string audience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Brak Jwt:Audience.");
        services.AddBrowserSessionStore(configuration);
        services.AddPersistentBrowserDataProtection(configuration, environment);
        services.AddSingleton<BrowserTicketStore>();
        services.AddScoped<BrowserSignInService>();
        services.AddAuthentication(Selector)
            .AddPolicyScheme(Selector, Selector, options => options.ForwardDefaultSelector = context =>
                context.Request.Headers.ContainsKey("Authorization") ? JwtBearerDefaults.AuthenticationScheme : BrowserSessionOptions.AuthenticationScheme)
            .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = issuer, ValidateAudience = true, ValidAudience = audience,
                ValidateLifetime = true, RequireExpirationTime = true, ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key), RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromSeconds(30)
            })
            .AddCookie(BrowserSessionOptions.AuthenticationScheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax; options.Cookie.Path = "/"; options.Cookie.Domain = null;
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
                options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            });
        services.AddOptions<CookieAuthenticationOptions>(BrowserSessionOptions.AuthenticationScheme)
            .Configure<BrowserTicketStore, IOptions<BrowserSessionOptions>, TimeProvider>((options, store, lifetime, clock) =>
            {
                options.SessionStore = store; options.ExpireTimeSpan = lifetime.Value.IdleTimeout; options.TimeProvider = clock;
            });
        services.AddAuthorization(options => options.AddPolicy(BrowserPolicy, policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Identity?.AuthenticationType == BrowserSessionOptions.AuthenticationScheme)));
        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeader;
            options.Cookie.Name = "__Host-xksef-csrf"; options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/"; options.Cookie.Domain = null;
        });
        services.AddScoped<BrowserCsrfFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<BrowserCsrfFilter>());
        services.AddOptions<BrowserSessionCleanupOptions>().Bind(configuration.GetSection("BrowserSessions:Cleanup"))
            .Validate(options => options.Interval > TimeSpan.Zero && options.BatchSize is >= 1 and <= 10000 && options.MaxBatches is >= 1 and <= 100,
                "Invalid browser session cleanup configuration.").ValidateOnStart();
        services.AddHostedService<BrowserSessionCleanupService>();
        return services;
    }

    private static void AddPersistentBrowserDataProtection(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        string? configuredPath = configuration["DataProtection:KeyRingPath"];
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (configuredPath is null && string.IsNullOrEmpty(local)) throw new InvalidOperationException("Skonfiguruj trwały DataProtection:KeyRingPath poza katalogiem aplikacji.");
        string path = configuredPath ?? Path.Combine(local, "xKsef", "Api", "DataProtectionKeys");
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("DataProtection:KeyRingPath musi być ścieżką bezwzględną.");
        path = Path.GetFullPath(path);
        var roots = new List<string> { environment.ContentRootPath, AppContext.BaseDirectory };
        for (var parent = new DirectoryInfo(environment.ContentRootPath); parent is not null; parent = parent.Parent)
            if (Directory.Exists(Path.Combine(parent.FullName, ".git")) || File.Exists(Path.Combine(parent.FullName, ".git"))) { roots.Add(parent.FullName); break; }
        if (roots.Any(root => IsWithin(path, root))) throw new InvalidOperationException("Key ring nie może znajdować się w repozytorium ani katalogu aplikacji/publikacji.");
        string applicationName = configuration["DataProtection:ApplicationName"] ?? "xKsef.Api";
        if (string.IsNullOrWhiteSpace(applicationName)) throw new InvalidOperationException("DataProtection:ApplicationName nie może być puste.");
        var protection = services.AddDataProtection().SetApplicationName(applicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(path));
        string mode = configuration["DataProtection:ProtectionMode"] ?? "DpapiCurrentUser";
        if (mode == "Certificate")
            protection.ProtectKeysWithCertificate(configuration["DataProtection:CertificateThumbprint"] ?? throw new InvalidOperationException("Brak certyfikatu ochrony key ring."));
        else if (mode == "DpapiCurrentUser" && OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        else throw new InvalidOperationException("Wymagana ochrona key ring: DpapiCurrentUser (Windows) albo Certificate.");
    }

    private static bool IsWithin(string path, string root)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(path, full, comparison) || path.StartsWith(full + Path.DirectorySeparatorChar, comparison);
    }
}
