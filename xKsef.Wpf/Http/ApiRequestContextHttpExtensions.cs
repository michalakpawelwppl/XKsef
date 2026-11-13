using System.Net.Http;

namespace xKsef.Wpf.Http;

public static class ApiRequestContextHttpExtensions
{
    public static readonly HttpRequestOptionsKey<ApiRequestContext> OptionsKey = new("xKsef.ApiRequestContext");

    public static void SetApiRequestContext(this HttpRequestMessage request, ApiRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(OptionsKey, context);
    }

    public static bool TryGetApiRequestContext(this HttpRequestMessage request, out ApiRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Options.TryGetValue(OptionsKey, out context);
    }
}