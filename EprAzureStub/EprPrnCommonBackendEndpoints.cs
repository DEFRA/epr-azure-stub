using Microsoft.AspNetCore.Mvc;

namespace EprAzureStub;

public static class EprPrnCommonBackendEndpoints
{
    private const string OrganisationHeader = "X-EPR-ORGANISATION";
    private const int StartYear = 2024;
    private const int EndYear = 2029;

    public static void MapEprPrnCommonBackendEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/epr-prn-common-backend");

        group.MapGet("/admin/health", () => Results.Ok());

        group.MapGet(
            "/api/v1/prn/obligationcalculation/{year:int}",
            (
                int year,
                [FromHeader(Name = OrganisationHeader)] Guid? organisationId,
                LoadTestSessionState loadTestSessionState,
                ILoggerFactory loggerFactory
            ) =>
            {
                var phase = 0;
                var simulated = organisationId.HasValue && loadTestSessionState.TryGetPrnPhase(organisationId.Value, year, out phase);
                if (!simulated && ((year is < StartYear or > EndYear)
                    || loadTestSessionState.GetPollingAllocations().Any(allocation => allocation.OrganisationId == organisationId)))
                {
                    return Results.BadRequest($"Invalid year provided: {year}.");
                }

                if (organisationId is null || organisationId == Guid.Empty)
                {
                    return Results.BadRequest($"Missing or invalid {OrganisationHeader} header.");
                }

                if (
                    loadTestSessionState.TryGetAllocationForOrganisation(
                        organisationId.Value,
                        out var loadTestAllocation
                    )
                )
                {
                    loggerFactory
                        .CreateLogger(nameof(EprPrnCommonBackendEndpoints))
                        .LogInformation(
                            "Load-test PRN mapping: organisation {OrganisationId} maps to {OrganisationType} allocation {LoadTestUserIndex} for obligation year {ObligationYear}.",
                            organisationId.Value,
                            loadTestAllocation.IsComplianceScheme
                                ? "compliance scheme"
                                : "direct producer",
                            loadTestAllocation.UserIndex,
                            year
                        );

                    var response = loadTestAllocation.IsComplianceScheme
                        ? CreateComplianceSchemeResponse(organisationId.Value)
                        : CreateLargeProducerResponse(organisationId.Value);
                    if (simulated && phase > 0)
                        response = response with
                        {
                            NumberOfPrnsAwaitingAcceptance = phase % 2 == 1 ? 0 : 1,
                            ObligationData = response.ObligationData.Select(material =>
                            {
                                var obligation = (material.ObligationToMeet ?? 0) + phase * 5;
                                var outstanding = phase % 2 == 1 ? 0 : 10 + phase;
                                return material with
                                {
                                    Tonnage = material.Tonnage + phase * 10,
                                    ObligationToMeet = obligation,
                                    TonnageAwaitingAcceptance = outstanding,
                                    TonnageAccepted = obligation - outstanding,
                                    TonnageOutstanding = outstanding,
                                    Status = outstanding == 0 ? "Met" : "NotMet"
                                };
                            }).ToArray()
                        };
                    return Results.Ok(response);
                }

                return organisationId.Value switch
                {
                    var id when id == WasteOrganisationStubIds.LargeProducerGuid => Results.Ok(
                        CreateLargeProducerResponse(id)
                    ),
                    var id when id == WasteOrganisationStubIds.ComplianceSchemeGuid => Results.Ok(
                        CreateComplianceSchemeResponse(id)
                    ),
                    var id
                        when id == WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid
                            => Results.Ok(CreateLargeProducerResponse(id)),
                    var id
                        when id == WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid
                            => Results.Ok(CreateComplianceSchemeResponse(id)),
                    var id
                        when id == WasteOrganisationStubIds.SeededComplianceSchemeExternalIdGuid
                            => Results.Ok(CreateComplianceSchemeResponse(id)),
                    _ => Results.NotFound(),
                };
            }
        );
    }

    private static ObligationModel CreateLargeProducerResponse(Guid organisationId)
    {
        return new ObligationModel
        {
            NumberOfPrnsAwaitingAcceptance = 1,
            ObligationData =
            [
                new(organisationId, "Plastic", 100, 0.49, 120, 10, 80, 40, "NotMet"),
                new(organisationId, "Wood", 50, 0.35, 40, 0, 45, 0, "Met"),
                new(organisationId, "Glass", 25, 0.75, 30, 5, 20, 10, "NotMet"),
                new(organisationId, "Paper", 75, 0.83, 70, 0, 70, 0, "Met"),
            ],
        };
    }

    private static ObligationModel CreateComplianceSchemeResponse(Guid organisationId)
    {
        return new ObligationModel
        {
            NumberOfPrnsAwaitingAcceptance = 2,
            ObligationData =
            [
                new(organisationId, "Plastic", 160, 0.49, 180, 20, 120, 60, "NotMet"),
                new(organisationId, "Wood", 80, 0.35, 65, 5, 70, 0, "Met"),
                new(organisationId, "Glass", 40, 0.75, 48, 5, 35, 13, "NotMet"),
                new(organisationId, "Paper", 110, 0.83, 105, 0, 105, 0, "Met"),
            ],
        };
    }

    private sealed record ObligationModel
    {
        public int NumberOfPrnsAwaitingAcceptance { get; init; }

        public required IReadOnlyList<ObligationData> ObligationData { get; init; }
    }

    private sealed record ObligationData(
        Guid OrganisationId,
        string MaterialName,
        int Tonnage,
        double MaterialTarget,
        int? ObligationToMeet,
        int TonnageAwaitingAcceptance,
        int TonnageAccepted,
        int? TonnageOutstanding,
        string Status
    );
}
