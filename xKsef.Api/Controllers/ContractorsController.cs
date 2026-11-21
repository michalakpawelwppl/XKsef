using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKesf.Application.Contractors;
using xKsef.Api.Authorization;
using xKsef.Api.Contracts.Contractors;
using xKsef.Application.Contractors;
using xKsef.Application.Gus;
using xKsef.Contracts.Contractors;
using xKsef.Domain.Contractors;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(TenantAuthorizationFilter))]
[Route("api/[controller]")]
public sealed class ContractorsController : ControllerBase
{
    private readonly IContractorService _contractorService;

    public ContractorsController(IContractorService contractorService)
    {
        _contractorService = contractorService;
    }

    [HttpGet]
    public async Task<IActionResult> GetContractors(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();

        var contractors = await _contractorService.GetContractorsAsync(tenantId, cancellationToken);
        return Ok(contractors.Select(MapToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetContractor(Guid id, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();

        var contractor = await _contractorService.GetByIdAsync(tenantId, id, cancellationToken);
        return contractor is null ? NotFound() : Ok(MapToDto(contractor));
    }

    [HttpGet("gus")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetGusContractors([FromServices] IGusBirClient gusClient, [FromQuery] string? nip, [FromQuery] string? regon, [FromQuery] string? krs)
    {
        if (HttpContext.Items["TenantId"] is not Guid)
            return Forbid();

        var criteria = new[] { "nip", "regon", "krs" }.Where(Request.Query.ContainsKey).ToArray();
        if (criteria.Length != 1 || Request.Query[criteria[0]].Count != 1)
            return InvalidGusQuery("Podaj dokładnie jeden identyfikator: nip, regon albo krs.");

        var criterion = criteria[0];
        string identifier;
        try
        {
            identifier = (criterion switch
            {
                "nip" => ContractorIdentifierValidator.NormalizeAndValidateNip(nip),
                "regon" => ContractorIdentifierValidator.NormalizeAndValidateRegon(regon),
                _ => ContractorIdentifierValidator.NormalizeAndValidateKrsNumber(krs)
            }) ?? throw new ArgumentException("Identyfikator wyszukiwania nie może być pusty.");
        }
        catch (ArgumentException exception)
        {
            return InvalidGusQuery(exception.Message);
        }

        var entities = await (criterion switch
        {
            "nip" => gusClient.SearchByNipAsync(identifier, HttpContext.RequestAborted),
            "regon" => gusClient.SearchByRegonAsync(identifier, HttpContext.RequestAborted),
            _ => gusClient.SearchByKrsAsync(identifier, HttpContext.RequestAborted)
        });
        return Ok(entities.Select(entity => new GusContractorDto
        {
            Nip = entity.Nip,
            Regon = entity.Regon,
            Krs = entity.KrsNumber,
            FullName = entity.FullName,
            Address = entity.Address
        }).ToArray());
    }

    private BadRequestObjectResult InvalidGusQuery(string detail) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Nieprawidłowe parametry wyszukiwania GUS",
        Detail = detail
    });

    [HttpPost]
    public async Task<IActionResult> CreateContractor([FromBody] ContractorRequest request, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();

        try
        {
            var contractorId = await _contractorService.CreateAsync(tenantId, MapRequest(request), cancellationToken);
            return CreatedAtAction(nameof(GetContractor), new { id = contractorId }, new { id = contractorId });
        }
        catch (ArgumentException ex)
        {
            return InvalidContractor(ex);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateContractor(Guid id, [FromBody] ContractorRequest request, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();
        if (request.IsActive is null)
            return BadRequest("Pole IsActive jest wymagane podczas aktualizacji kontrahenta.");
        if (request.Addresses is null)
            return BadRequest("Pole Addresses jest wymagane podczas aktualizacji kontrahenta.");

        try
        {
            var mapped = MapRequest(request);
            await _contractorService.UpdateAsync(tenantId, id, new UpdateContractorCommand
            {
                FullName = mapped.FullName,
                ShortName = mapped.ShortName,
                Nip = mapped.Nip,
                Regon = mapped.Regon,
                KrsNumber = mapped.KrsNumber,
                EuVatNumber = mapped.EuVatNumber,
                OtherTaxNumber = mapped.OtherTaxNumber,
                IsEuVatRegistered = mapped.IsEuVatRegistered,
                Addresses = mapped.Addresses,
                IsActive = request.IsActive.Value
            }, cancellationToken);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return InvalidContractor(ex);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private BadRequestObjectResult InvalidContractor(ArgumentException exception)
    {
        return BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Nieprawidłowe dane kontrahenta",
            Detail = exception.Message
        });
    }

    private static CreateContractorCommand MapRequest(ContractorRequest request)
    {
        return new CreateContractorCommand
        {
            FullName = request.FullName,
            ShortName = request.ShortName,
            Nip = request.Nip,
            Regon = request.Regon,
            KrsNumber = request.KrsNumber,
            EuVatNumber = request.EuVatNumber,
            OtherTaxNumber = request.OtherTaxNumber,
            IsEuVatRegistered = request.IsEuVatRegistered,
            Addresses = request.Addresses?.Select(address =>
            {
                if (address is null)
                    throw new ArgumentException("Kolekcja adresów nie może zawierać null.");
                return new CreateContractorAddress
                {
                    AddressType = (AddressType)address.AddressType,
                    CountryCode = address.CountryCode,
                    Street = address.Street,
                    BuildingNumber = address.BuildingNumber,
                    ApartmentNumber = address.ApartmentNumber,
                    PostalCode = address.PostalCode,
                    City = address.City
                };
            }).ToList() ?? []
        };
    }

    private static ContractorDto MapToDto(ContractorAggregate aggregate)
    {
        return new ContractorDto
        {
            Id = aggregate.Contractor.Id,
            IsActive = aggregate.Contractor.IsActive,
            CreatedAt = aggregate.Contractor.CreatedAt,
            FullName = aggregate.ContractorData.FullName,
            ShortName = aggregate.ContractorData.ShortName,
            Nip = aggregate.ContractorData.Nip,
            Regon = aggregate.ContractorData.Regon,
            KrsNumber = aggregate.ContractorData.KrsNumber,
            EuVatNumber = aggregate.ContractorData.EuVatNumber,
            OtherTaxNumber = aggregate.ContractorData.OtherTaxNumber,
            IsEuVatRegistered = aggregate.ContractorData.IsEuVatRegistered,
            Addresses = aggregate.Addresses.Select(item => new AddressDto
            {
                AddressType = (int)item.Address.AddressType,
                CountryCode = item.AddressData.CountryCode,
                Street = item.AddressData.Street,
                BuildingNumber = item.AddressData.BuildingNumber,
                ApartmentNumber = item.AddressData.ApartmentNumber,
                PostalCode = item.AddressData.PostalCode,
                City = item.AddressData.City
            }).ToList()
        };
    }
}
