namespace EprAzureStub;

public static class WasteOrganisationsEndpoints
{
    private static readonly DateTimeOffset RegistrationTimestamp = new(
        2026,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero
    );

    public static void MapWasteOrganisationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/waste-organisations");

        group.MapGet("/health/authorized", () => Results.Ok());

        group.MapGet(
            "/organisations/{id:guid}",
            (Guid id, LoadTestSessionState loadTestSessionState) =>
            {
                if (
                    !loadTestSessionState.TryGetAllocationForOrganisation(
                        id,
                        out var allocation
                    )
                    // A compliance allocation also has an operator ID. It is used only by
                    // Account Service's get-for-operator call; the scheme ID is the Waste
                    // Organisations ID used by the frontend and Waste Obligations.
                    || allocation.OrganisationId != id
                )
                {
                    return Results.NotFound();
                }

                return Results.Ok(CreateOrganisationResponse(allocation));
            }
        );
    }

    private static OrganisationResponseModel CreateOrganisationResponse(
        LoadTestOrganisationAllocation allocation
    )
    {
        var registrationType = allocation.IsComplianceScheme
            ? "COMPLIANCE_SCHEME"
            : "LARGE_PRODUCER";

        return new()
        {
            Id = allocation.OrganisationId,
            Name = allocation.OrganisationName,
            TradingName = allocation.IsComplianceScheme
                ? allocation.ComplianceSchemeName
                : null,
            BusinessCountry = "GB-ENG",
            CompaniesHouseNumber = allocation.IsComplianceScheme ? "12345678" : "17121895",
            Address = new()
            {
                AddressLine1 = allocation.OrganisationName,
                AddressLine2 = "123 Street",
                Town = "Town",
                County = "County",
                Postcode = "UK1",
                Country = "UK",
            },
            Registrations = CreateRegistrations(registrationType),
        };
    }

    private static IReadOnlyList<RegistrationResponseModel> CreateRegistrations(string registrationType)
    {
        return Enumerable
            .Range(2025, 6)
            .Select(
                registrationYear =>
                    new RegistrationResponseModel
                    {
                        Status = "REGISTERED",
                        Type = registrationType,
                        RegistrationYear = registrationYear,
                        Created = RegistrationTimestamp,
                        Updated = RegistrationTimestamp,
                    }
            )
            .ToArray();
    }

    private sealed record OrganisationResponseModel
    {
        public Guid Id { get; init; }

        public required string Name { get; init; }

        public string? TradingName { get; init; }

        public required string BusinessCountry { get; init; }

        public required string CompaniesHouseNumber { get; init; }

        public required AddressResponseModel Address { get; init; }

        public required IReadOnlyList<RegistrationResponseModel> Registrations { get; init; }
    }

    private sealed record AddressResponseModel
    {
        public required string AddressLine1 { get; init; }

        public required string AddressLine2 { get; init; }

        public required string Town { get; init; }

        public required string County { get; init; }

        public required string Postcode { get; init; }

        public required string Country { get; init; }
    }

    private sealed record RegistrationResponseModel
    {
        public required string Status { get; init; }

        public required string Type { get; init; }

        public int RegistrationYear { get; init; }

        public DateTimeOffset Created { get; init; }

        public DateTimeOffset Updated { get; init; }
    }
}
