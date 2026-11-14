using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using xKsef.Application.Exceptions;
using xKsef.Infrastructure.Ksef.Configuration;

namespace xKsef.Infrastructure.Credentials;

public sealed class CredentialServiceEnvironmentHandler : DelegatingHandler
{
    private const string EnvironmentHeader = "X-KSeF-Environment";
    private readonly string _environment;
    private readonly TimeSpan _requestTimeout;

    private sealed record HealthResponse(bool Success, string? KsefEnvironment);
    private sealed record ConflictResponse(string? Code, string? KsefEnvironment);

    public CredentialServiceEnvironmentHandler(IOptions<KsefOptions> options) : this(options, TimeSpan.FromSeconds(100)) { }

    internal CredentialServiceEnvironmentHandler(IOptions<KsefOptions> options, TimeSpan requestTimeout)
    {
        _environment = KsefEnvironmentResolver.GetEnvironment(options.Value.BaseUrl);
        _requestTimeout = requestTimeout;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Own the existing 100-second HTTP deadline to distinguish a timeout from caller cancellation.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_requestTimeout);
        CancellationToken operationToken = deadline.Token;

        try
        {
            using var healthRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(request.RequestUri!, "/internal/health"));
            using HttpResponseMessage healthResponse = await base.SendAsync(healthRequest, operationToken);
            if (!healthResponse.IsSuccessStatusCode) throw new CredentialServiceException(CredentialServiceFailure.Unavailable);

            if (!string.Equals(healthResponse.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                throw new CredentialServiceException(CredentialServiceFailure.InvalidHealthResponse);

            HealthResponse? health;
            try
            {
                health = await healthResponse.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken: operationToken);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                throw new CredentialServiceException(CredentialServiceFailure.InvalidHealthResponse);
            }

            if (health is not { Success: true, KsefEnvironment: "TEST" or "PROD" })
                throw new CredentialServiceException(CredentialServiceFailure.InvalidHealthResponse);

            if (health.KsefEnvironment != _environment)
                throw new CredentialServiceException(CredentialServiceFailure.EnvironmentMismatch, _environment, health.KsefEnvironment);

            operationToken.ThrowIfCancellationRequested();
            request.Headers.Remove(EnvironmentHeader);
            request.Headers.Add(EnvironmentHeader, _environment);
            HttpResponseMessage response = await base.SendAsync(request, operationToken);
            try
            {
                // Buffer within the deadline so response transport errors are handled here as well.
                await response.Content.LoadIntoBufferAsync(operationToken);
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    ConflictResponse? conflict = null;
                    try
                    {
                        conflict = await response.Content.ReadFromJsonAsync<ConflictResponse>(cancellationToken: operationToken);
                    }
                    catch (Exception exception) when (exception is JsonException or NotSupportedException) { }

                    if (conflict is { Code: "ksef_environment_mismatch", KsefEnvironment: "TEST" or "PROD" })
                        throw new CredentialServiceException(CredentialServiceFailure.EnvironmentMismatch, _environment, conflict.KsefEnvironment);
                }

                if ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.RequestTimeout)
                    throw new CredentialServiceException(CredentialServiceFailure.Unavailable);

                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CredentialServiceException(CredentialServiceFailure.Unavailable);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new CredentialServiceException(CredentialServiceFailure.Unavailable);
        }
    }
}
