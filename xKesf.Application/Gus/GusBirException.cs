namespace xKsef.Application.Gus;

public enum GusBirError
{
    Communication,
    Unavailable,
    Timeout,
    Authentication,
    SessionExpired,
    InvalidResponse,
    ServiceFault,
    ServiceRejected
}

public sealed class GusBirException(GusBirError error, string operation, int? serviceCode = null)
    : Exception($"Operacja GUS BIR {operation} nie powiodła się ({error}).")
{
    public GusBirError Error { get; } = error;
    public string Operation { get; } = operation;
    public int? ServiceCode { get; } = serviceCode;
}
