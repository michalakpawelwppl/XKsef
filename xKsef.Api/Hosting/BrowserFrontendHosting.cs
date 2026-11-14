using Microsoft.Extensions.FileProviders;

namespace xKsef.Api.Hosting;

public static class BrowserFrontendHosting
{
    public static WebApplication UseBrowserFrontend(this WebApplication app)
    {
        string? configuredPath = app.Configuration["BrowserFrontend:Path"];
        if (string.IsNullOrWhiteSpace(configuredPath)) return app;
        if (!Path.IsPathFullyQualified(configuredPath)) throw new InvalidOperationException("BrowserFrontend:Path must be an absolute path to the built frontend directory.");
        string root = Path.GetFullPath(configuredPath);
        string index = Path.Combine(root, "index.html");
        if (!Directory.Exists(root) || !File.Exists(index)) throw new InvalidOperationException("BrowserFrontend:Path must contain the built frontend index.html.");
        var files = new PhysicalFileProvider(root);
        app.Lifetime.ApplicationStopped.Register(files.Dispose);
        // Static frontend files must never shadow API endpoints or their authorization.
        app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase), branch =>
            branch.UseStaticFiles(new StaticFileOptions { FileProvider = files }));
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                || (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.SendFileAsync(index, context.RequestAborted);
        });
        return app;
    }
}
