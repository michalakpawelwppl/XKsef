using xKesf.Application.Contractors;

namespace xKsef.Application.Contractors;

public sealed class UpdateContractorCommand
{
    public string FullName { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public string? Nip { get; init; }
    public string? Regon { get; init; }
    public string? KrsNumber { get; init; }
    public string? EuVatNumber { get; init; }
    public string? OtherTaxNumber { get; init; }
    public bool IsEuVatRegistered { get; init; }
    public IReadOnlyList<CreateContractorAddress> Addresses { get; init; } = [];
    public bool IsActive { get; set; }
}
