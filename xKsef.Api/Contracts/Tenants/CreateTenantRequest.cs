using System.ComponentModel.DataAnnotations;

namespace xKsef.Api.Contracts.Tenants;

public sealed class CreateTenantRequest
{
    [Required]
    [StringLength(200)]
    public required string Name { get; init; }

    [Required]
    [RegularExpression("^[0-9]{10}$")]
    public required string Nip { get; init; }

    [Required]
    [StringLength(200)]
    public required string TaxEntityName { get; init; }

    [Required]
    [StringLength(256)]
    public required string UserLogin { get; init; }
}
