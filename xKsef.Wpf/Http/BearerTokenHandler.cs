using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using xKsef.Wpf.Http;
using xKsef.Wpf.Services;

public class BearerTokenHandler : DelegatingHandler
{
    private readonly ITokenStore _tokenStore;
    

    public BearerTokenHandler(ITokenStore tokenStore)
    {
        _tokenStore = tokenStore;
     
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        bool hasRequestContext = request.TryGetApiRequestContext(out ApiRequestContext requestContext);
        string? token;

        if (hasRequestContext)
        {
            switch (requestContext.Scope)
            {
                case ApiRequestScope.Anonymous:
                    request.Headers.Authorization = null;
                    token = null;
                    break;
                case ApiRequestScope.User:
                case ApiRequestScope.TenantHeader:
                case ApiRequestScope.TenantRoute:
                    if (string.IsNullOrWhiteSpace(requestContext.AccessToken))
                        throw new InvalidOperationException("The request context requires an access token.");
                    if (requestContext.Scope is ApiRequestScope.TenantHeader or ApiRequestScope.TenantRoute && requestContext.TenantId is null)
                        throw new InvalidOperationException("The request context requires a tenant ID.");

                    token = requestContext.AccessToken;
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    break;
                default:
                    throw new InvalidOperationException("The request context has an unsupported scope.");
            }
        }
        else
        {
            token = _tokenStore.AccessToken;
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrWhiteSpace(token))
        {
            if (hasRequestContext)
                _tokenStore.TryClearAccessToken(requestContext.SessionVersion);
            else if (_tokenStore.AccessToken == token)
                _tokenStore.AccessToken = null;
        }

        return response;
    }
}