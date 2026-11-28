using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using xKsef.Application.Authentication;
using xKsef.Application.Contractors;
using xKsef.Application.Credentials;
using xKsef.Application.Gus;
using xKsef.Application.Invoices;
using xKsef.Application.Invoices.Pdf;
using xKsef.Application.Tenants;
using xKsef.Infrastructure.Authentication;
using xKsef.Infrastructure.Credentials;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Gus;
using xKsef.Infrastructure.Invoices;
using xKsef.Infrastructure.Invoices.Pdf;
using xKsef.Infrastructure.Ksef;
using xKsef.Infrastructure.Ksef.Authentication;
using xKsef.Infrastructure.Ksef.Configuration;
using xKsef.Infrastructure.Ksef.Invoices;
using xKsef.Infrastructure.Persistence;
using xKsef.Infrastructure.Persistence.Repositories;
using xKsef.Infrastructure.Tenants;

namespace xKsef.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKsefInfrastructure(this IServiceCollection services,IConfiguration configuration)
    {
        string ksefBaseUrl = KsefEnvironmentResolver.ResolveBaseUrl(configuration);
        services.AddScoped<KsefAuthenticationService>();
        services.AddSingleton<KsefTokenStore>();
        services.AddScoped<KsefTokenProvider>();
        services.Configure<KsefOptions>(configuration.GetSection(KsefOptions.SectionName));
        services.PostConfigure<KsefOptions>(options => options.BaseUrl = ksefBaseUrl);
        services.AddScoped<KsefAuthenticationXmlValidator>();
        services.AddScoped<KsefInvoiceService>(provider =>
        {
            var httpClientFactory =
                provider.GetRequiredService<IHttpClientFactory>();

            var downloadClient = httpClientFactory.CreateClient("KsefPackageDownload");

            return ActivatorUtilities.CreateInstance<KsefInvoiceService>(provider,downloadClient);
        });
        services.AddSingleton<KsefRateLimitState>();
        services.AddSingleton<ICertificateFetcher, KsefCertificateFetcher>();
        services.AddScoped<IInvoicePdfGenerator, NodeInvoicePdfGenerator>();

        services.AddHttpClient("KsefPackageDownload", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddScoped<IInvoiceService>(
    provider => provider.GetRequiredService<KsefInvoiceService>());

        services.AddKSeFClient(options =>
        {
            options.BaseUrl = ksefBaseUrl;
        });

        services.AddCryptographyClient();
        if (configuration.GetValue("Ksef:WarmupOnStartup", true))
            services.AddHostedService<KsefCryptographyWarmupService>();
        services.AddMemoryCache();
        services.AddSingleton<PendingKsefAuthenticationStore>();
        services.AddScoped<ICredentialServiceClient, CredentialServiceClient>();
        services.AddTransient<CredentialServiceEnvironmentHandler>();

        return services;
    }

    public static IServiceCollection AddDatabaseInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Brak konfiguracji połączenia z bazą danych.");
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IGusQuotaStore, SqlGusQuotaStore>();
        services.AddScoped<IGusRateLimiter, GusRateLimiter>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;

            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;

            options.User.RequireUniqueEmail = true;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<IUserCreationService, UserCreationService>();
        services.AddScoped<DevelopmentUserSeeder>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IContractorRepository, ContractorRepository>();
        services.AddScoped<IContractorService, ContractorService>();
        services.AddSingleton<xKsef.Domain.SalesInvoices.Calculation.SalesInvoiceCalculator>();
        services.AddSingleton<xKsef.Application.SalesInvoices.SalesInvoiceIssuanceValidator>();
        services.AddScoped<xKsef.Application.SalesInvoices.ISalesInvoiceRepository, SalesInvoiceRepository>();
        services.AddScoped<xKsef.Application.SalesInvoices.ISalesInvoiceService, xKsef.Application.SalesInvoices.SalesInvoiceService>();
        services.AddSingleton<xKsef.Application.SalesInvoices.Snapshots.ISalesInvoiceApprovalSnapshotWriter, xKsef.Application.SalesInvoices.Snapshots.SalesInvoiceApprovalSnapshotWriter>();
        services.AddSingleton<xKsef.Application.SalesInvoices.Snapshots.ISalesInvoiceApprovalSnapshotReader, xKsef.Application.SalesInvoices.Snapshots.SalesInvoiceApprovalSnapshotReader>();
        services.AddScoped<IInvoiceQueryService, InvoiceQueryService>();
        services.AddScoped<InvoiceImportService>();
        services.AddScoped<IInvoiceImportTransaction, InvoiceImportTransaction>();
        services.AddScoped<IInvoicePackageImportService, InvoicePackageImportService>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<ITenantUsersService, TenantUsersService>();
        services.AddScoped<ITenantAccessService, TenantAccessService>();
        services.AddScoped<ITenantPermissionService, TenantPermissionService>();
        services.AddScoped<xKsef.Application.InvoiceNumbering.IInvoiceNumberSeriesRepository, InvoiceNumberSeriesRepository>();
        services.AddScoped<xKsef.Application.InvoiceNumbering.IInvoiceNumberSeriesService, xKsef.Application.InvoiceNumbering.InvoiceNumberSeriesService>();
        services.AddScoped<xKsef.Application.InvoiceNumbering.IInvoiceNumberingRepository, InvoiceNumberingRepository>();
        services.AddScoped<xKsef.Application.InvoiceNumbering.IInvoiceNumberingService, xKsef.Application.InvoiceNumbering.InvoiceNumberingService>();
        services.TryAddScoped<xKsef.Application.InvoiceNumbering.IInvoiceIssuanceOutcomeSource, xKsef.Application.InvoiceNumbering.UnavailableInvoiceIssuanceOutcomeSource>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IInvoiceMetadataParser, KsefInvoiceMetadataParser>();

        return services;
    }
}
