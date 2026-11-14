using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using xKsef.Infrastructure.Authentication;

namespace xKsef.Api.Authentication;

public sealed class BrowserSignInService(IOptionsMonitor<CookieAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder)
{
    public async Task SignInAsync(HttpContext context, ClaimsPrincipal principal)
    {
        await context.SignOutAsync(BrowserSessionOptions.AuthenticationScheme);
        // CookieAuthenticationHandler retains its session key after SignOut and would call Renew, not Store.
        // Use a fresh standard handler without an incoming cookie only for issuing the new login.
        // Authentication and CSRF have already used the original request; restore its headers in all cases.
        var cookies = context.Request.Headers.Cookie;
        try
        {
            context.Request.Headers.Remove("Cookie");
            var handler = new CookieAuthenticationHandler(options, logger, encoder);
            await handler.InitializeAsync(new AuthenticationScheme(BrowserSessionOptions.AuthenticationScheme, null, typeof(CookieAuthenticationHandler)), context);
            await handler.SignInAsync(principal, new AuthenticationProperties { IsPersistent = false });
        }
        finally { context.Request.Headers.Cookie = cookies; }
    }
}
