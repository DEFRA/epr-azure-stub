using Microsoft.AspNetCore.Mvc;

namespace EprAzureStub;

public static class EprBackendAccountMicroserviceEndpoints
{
    public static void MapEprBackendAccountMicroserviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/epr-backend-account-microservice");

        group.MapGet(
            "/api/organisations/person-emails",
            ([FromQuery] Guid organisationId, [FromQuery] string? entityTypeCode) =>
            {
                if (organisationId == Guid.Empty)
                {
                    return Results.BadRequest();
                }

                if (
                    organisationId == WasteOrganisationStubIds.LargeProducerGuid
                    && IsEntityTypeCode(entityTypeCode, EntityTypeCodes.DirectRegistrant)
                )
                {
                    return Results.Ok(CreateLargeProducerPersonEmailsResponse());
                }

                if (
                    organisationId == WasteOrganisationStubIds.ComplianceSchemeGuid
                    && IsEntityTypeCode(entityTypeCode, EntityTypeCodes.ComplianceScheme)
                )
                {
                    return Results.Ok(CreateComplianceSchemePersonEmailsResponse());
                }

                return Results.NoContent();
            }
        );
    }

    private static bool IsEntityTypeCode(string? actual, string expected)
    {
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<PersonEmailResponseModel> CreateLargeProducerPersonEmailsResponse()
    {
        return
        [
            new()
            {
                FirstName = "Large",
                LastName = "Producer",
                Email = "large.producer@example.com",
            },
        ];
    }

    private static IReadOnlyList<PersonEmailResponseModel> CreateComplianceSchemePersonEmailsResponse()
    {
        return
        [
            new()
            {
                FirstName = "Compliance",
                LastName = "Scheme",
                Email = "compliance.scheme@example.com",
            },
        ];
    }

    private sealed record PersonEmailResponseModel
    {
        public required string FirstName { get; init; }

        public required string LastName { get; init; }

        public required string Email { get; init; }
    }
}
