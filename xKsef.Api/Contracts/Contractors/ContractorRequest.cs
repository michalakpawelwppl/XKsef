using System.ComponentModel.DataAnnotations;
using xKsef.Contracts.Contractors;

namespace xKsef.Api.Contracts.Contractors;

public sealed class ContractorRequest
{
    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public string ShortName { get; set; } = string.Empty;

    public string? Nip { get; set; }

    public string? Regon { get; set; }

    public string? KrsNumber { get; set; }

    public string? EuVatNumber { get; set; }

    public string? OtherTaxNumber { get; set; }

    public bool IsEuVatRegistered { get; set; }

    public bool? IsActive { get; set; }

    public List<AddressDto>? Addresses { get; set; }
}
