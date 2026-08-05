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

    // These records mirror epr-local-environment's
    // compose/waste-organisations-seed/payloads/*.json fixtures. The two
    // signed-in test organisations are handled separately below because they
    // have Account Service-specific names and trading names.
    private static readonly IReadOnlyDictionary<Guid, LocalEnvironmentSeededOrganisation>
        LocalEnvironmentSeededOrganisations =
            new Dictionary<Guid, LocalEnvironmentSeededOrganisation>
            {
                [Guid.Parse("07d0a580-ab20-4ee2-bd78-702a793b4d34")] = new(
                    "EcoCircle Holdings",
                    "COMPLIANCE_SCHEME"
                ),
                [Guid.Parse("34341291-b377-4047-ad96-93ddb0a1c469")] = new(
                    "ZESTY GOODS LTD",
                    "LARGE_PRODUCER"
                ),
                [Guid.Parse("42d6a04f-41dc-4e54-8a90-a4b662e5f6ef")] = new(
                    "GADGET CO LTD",
                    "SMALL_PRODUCER"
                ),
                [Guid.Parse("51478eff-46c5-4387-9e95-ad8dd1e6b20e")] = new(
                    "BIG BOX RETAIL LTD",
                    "SMALL_PRODUCER"
                ),
                [Guid.Parse("7f72952a-1aaf-4d04-bd9b-146c04aa207d")] = new(
                    "WastePartners Group",
                    "COMPLIANCE_SCHEME"
                ),
                [Guid.Parse("8947d193-f977-46bd-8beb-52d55c4eca69")] = new(
                    "ReClaim Partners Ltd",
                    "COMPLIANCE_SCHEME"
                ),
                [Guid.Parse("8c910c57-4231-465d-905f-0e20cc083566")] = new(
                    "GreenWaste Operator Ltd",
                    "COMPLIANCE_SCHEME"
                ),
                [Guid.Parse("c5c102bb-11f2-4662-bb77-d724f736f80b")] = new(
                    "CRAFTY THINGS LTD",
                    "LARGE_PRODUCER"
                ),
                [Guid.Parse("ccb3f815-7e75-4dfb-b52a-869d9e7a22c0")] = new(
                    "CleanLoop Operator Ltd",
                    "COMPLIANCE_SCHEME"
                ),
                [Guid.Parse("d0ab1aed-d4fc-4a88-98f0-b8bae048f170")] = new(
                    "PARCEL PROS LTD",
                    "LARGE_PRODUCER"
                ),
            };

    public static void MapWasteOrganisationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/waste-organisations");

        group.MapGet("/health/authorized", () => Results.Ok());

        group.MapGet(
            "/organisations/{id:guid}",
            (Guid id, LoadTestSessionState loadTestSessionState) =>
            {
                if (
                    loadTestSessionState.TryGetAllocationForOrganisation(
                        id,
                        out var allocation
                    )
                    // A compliance allocation also has an operator ID. It is used only by
                    // Account Service's get-for-operator call; the scheme ID is the Waste
                    // Organisations ID used by the frontend and Waste Obligations.
                    && allocation.OrganisationId == id
                )
                {
                    return Results.Ok(CreateOrganisationResponse(allocation));
                }

                var seededOrganisation = CreateSeededOrganisationResponse(id);

                return seededOrganisation is null
                    ? Results.NotFound()
                    : Results.Ok(seededOrganisation);
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

    private static OrganisationResponseModel? CreateSeededOrganisationResponse(Guid id)
    {
        if (LocalEnvironmentSeededOrganisations.TryGetValue(id, out var seededOrganisation))
        {
            return CreateOrganisationResponse(
                id,
                seededOrganisation.Name,
                null,
                string.Empty,
                seededOrganisation.RegistrationType
            );
        }

        return id switch
        {
            var seededDirectProducerId
                when seededDirectProducerId
                    == WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid
                => CreateOrganisationResponse(
                    id,
                    "POP QUEST LTD",
                    null,
                    "17121895",
                    "LARGE_PRODUCER"
                ),
            var seededComplianceSchemeId
                when seededComplianceSchemeId
                    == WasteOrganisationStubIds.SeededComplianceSchemeExternalIdGuid
                => CreateOrganisationResponse(
                    id,
                    "Organisation Name",
                    "Compliance Scheme Name",
                    "12345678",
                    "COMPLIANCE_SCHEME"
                ),
            _ => null,
        };
    }

    private static OrganisationResponseModel CreateOrganisationResponse(
        Guid id,
        string name,
        string? tradingName,
        string companiesHouseNumber,
        string registrationType
    )
    {
        return new()
        {
            Id = id,
            Name = name,
            TradingName = tradingName,
            BusinessCountry = "GB-ENG",
            CompaniesHouseNumber = companiesHouseNumber,
            Address = new()
            {
                AddressLine1 = name,
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

    private sealed record LocalEnvironmentSeededOrganisation(
        string Name,
        string RegistrationType
    );
}
