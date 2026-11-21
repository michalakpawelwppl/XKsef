public sealed class ContractorsData
{
    public Guid Id { get; set; }
    public Guid ContractorId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string? Nip { get; set; }
    public string? Regon { get; set; }
    public string? KrsNumber { get; set; }
    public string? EuVatNumber { get; set; }
    public string? OtherTaxNumber { get; set; }
    public bool IsEuVatRegistered { get; set; }
}
