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

        group.MapGet(
            "/api/v1/prn/obligationcalculation/{year:int}",
            (int year, [FromHeader(Name = OrganisationHeader)] Guid? organisationId) =>
            {
                if (year is < StartYear or > EndYear)
                {
                    return Results.BadRequest($"Invalid year provided: {year}.");
                }

                if (organisationId is null || organisationId == Guid.Empty)
                {
                    return Results.BadRequest($"Missing or invalid {OrganisationHeader} header.");
                }

                return organisationId.Value switch
                {
                    var id when id == WasteOrganisationStubIds.LargeProducerGuid => Results.Ok(
                        CreateLargeProducerResponse(id)
                    ),
                    var id when id == WasteOrganisationStubIds.ComplianceSchemeGuid => Results.Ok(
                        CreateComplianceSchemeResponse(id)
                    ),
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
