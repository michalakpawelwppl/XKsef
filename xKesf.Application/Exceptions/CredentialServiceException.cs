namespace xKsef.Application.Exceptions;

public enum CredentialServiceFailure
{
    Unavailable,
    InvalidHealthResponse,
    EnvironmentMismatch
}

public sealed class CredentialServiceException : Exception
{
    public CredentialServiceFailure Failure { get; }

    public CredentialServiceException(CredentialServiceFailure failure, string? apiEnvironment = null, string? serviceEnvironment = null)
        : base(failure switch
        {
            CredentialServiceFailure.InvalidHealthResponse => "CredentialService zwrócił nieprawidłową odpowiedź kontroli środowiska KSeF. Operacja została zablokowana.",
            CredentialServiceFailure.EnvironmentMismatch => $"Niezgodne środowiska KSeF: API={SafeEnvironment(apiEnvironment)}, CredentialService={SafeEnvironment(serviceEnvironment)}. Operacja została zablokowana.",
            _ => "CredentialService jest niedostępny lub wystąpił błąd komunikacji. Nie można potwierdzić wykonania operacji."
        })
    {
        Failure = failure;
    }

    private static string SafeEnvironment(string? environment) => environment is "TEST" or "PROD" ? environment : "nieznane";
}
