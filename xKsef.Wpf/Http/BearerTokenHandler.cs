using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

public class BearerTokenHandler : DelegatingHandler
{
    private readonly ITokenStore _tokenStore;

    public BearerTokenHandler(ITokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken)
    {
        var token = _tokenStore.AccessToken;

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);


        if (response.StatusCode == HttpStatusCode.Unauthorized &&
            !string.IsNullOrWhiteSpace(token) &&
            _tokenStore.AccessToken == token)
        {
            _tokenStore.AccessToken = null;
        }

        return response;
    }
}