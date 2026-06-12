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

        group.MapGet(
            "/api/users/user-organisations",
            ([FromQuery] Guid userId) =>
            {
                var response = CreateUserOrganisationsResponse(userId);

                return response is null ? Results.NotFound() : Results.Ok(response);
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

    private static UserOrganisationsListModel? CreateUserOrganisationsResponse(Guid userId)
    {
        return userId.ToString().ToLowerInvariant() switch
        {
            "579c319d-d552-47a2-bf4c-5a125a3183bc" => CreateSeededComplianceSchemeUserResponse(
                userId,
                "First name",
                "Last Name",
                "test+17122025143216@ee.com",
                ServiceRoles.ApprovedPerson,
                1
            ),
            "79d0deab-c22d-4c30-8082-508ff8dc1bd7" => CreateSeededDirectProducerUserResponse(
                userId,
                "Direct",
                "Producer",
                "test+directproducer@ee.com",
                ServiceRoles.ApprovedPerson,
                1
            ),
            "513a78ee-d5bf-4fa4-9d8f-136550ea6072" => CreateSeededDirectProducerUserResponse(
                userId,
                "SB FirstName",
                "SB LastName",
                "bmmmdmgz@sharklasers.com",
                ServiceRoles.DelegatedPerson,
                2
            ),
            "d062d4fe-34f8-468e-ada8-d950cc9a3c2a" => CreateSeededDirectProducerUserResponse(
                userId,
                "Francis",
                "Chelladurai",
                "francis.chelladurai+31032026@equalexperts.com",
                ServiceRoles.BasicUser,
                3
            ),
            "ef2fd2a5-24bf-4b22-89a0-17a0367aee1c" => CreateSeededComplianceSchemeUserResponse(
                userId,
                "Francis",
                "Delegated",
                "francis.chelladurai+07042026@equalexperts.com",
                ServiceRoles.DelegatedPerson,
                2
            ),
            "13e26b8a-e2b2-4870-b040-d6bdf5d689fa" => CreateSeededComplianceSchemeUserResponse(
                userId,
                "Francis",
                "Basic",
                "francis.chelladurai+260407@equalexperts.com",
                ServiceRoles.BasicUser,
                3
            ),
            _ => null,
        };
    }

    private static UserOrganisationsListModel CreateSeededComplianceSchemeUserResponse(
        Guid userId,
        string firstName,
        string lastName,
        string email,
        string serviceRole,
        int serviceRoleId
    )
    {
        return CreateUserOrganisationsResponse(
            userId,
            firstName,
            lastName,
            email,
            serviceRole,
            serviceRoleId,
            CreateSeededComplianceSchemeOrganisation()
        );
    }

    private static UserOrganisationsListModel CreateSeededDirectProducerUserResponse(
        Guid userId,
        string firstName,
        string lastName,
        string email,
        string serviceRole,
        int serviceRoleId
    )
    {
        return CreateUserOrganisationsResponse(
            userId,
            firstName,
            lastName,
            email,
            serviceRole,
            serviceRoleId,
            CreateSeededDirectProducerOrganisation()
        );
    }

    private static UserOrganisationsListModel CreateUserOrganisationsResponse(
        Guid userId,
        string firstName,
        string lastName,
        string email,
        string serviceRole,
        int serviceRoleId,
        OrganisationDetailModel organisation
    )
    {
        return new()
        {
            User = new()
            {
                Id = userId,
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                RoleInOrganisation = "Admin",
                EnrolmentStatus = "Approved",
                ServiceRole = serviceRole,
                Service = "EPR Packaging",
                ServiceRoleId = serviceRoleId,
                Telephone = organisation.OrganisationRole == "Producer"
                    ? GetDirectProducerTelephone(serviceRole)
                    : GetComplianceSchemeTelephone(serviceRole),
                JobTitle = "Director",
                IsChangeRequestPending = false,
                NumberOfOrganisations = 1,
                Organisations = [organisation],
            },
        };
    }

    private static string GetDirectProducerTelephone(string serviceRole)
    {
        return serviceRole switch
        {
            ServiceRoles.ApprovedPerson => "07123456780",
            ServiceRoles.DelegatedPerson => "00441234567890",
            ServiceRoles.BasicUser => "00441234567891",
            _ => string.Empty,
        };
    }

    private static string GetComplianceSchemeTelephone(string serviceRole)
    {
        return serviceRole switch
        {
            ServiceRoles.ApprovedPerson => "07123456789",
            ServiceRoles.DelegatedPerson => "00441234567892",
            ServiceRoles.BasicUser => "00441234567893",
            _ => string.Empty,
        };
    }

    private static OrganisationDetailModel CreateSeededComplianceSchemeOrganisation()
    {
        return new()
        {
            Id = WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid,
            Name = "Organisation Name",
            TradingName = "Trading Name",
            OrganisationRole = "Compliance Scheme",
            OrganisationType = "Limited Company",
            OrganisationNumber = "1",
            CompaniesHouseNumber = "12345678",
            ProducerType = string.Empty,
            NationId = 1,
            Town = string.Empty,
            County = string.Empty,
            Country = string.Empty,
            Postcode = string.Empty,
            PersonRoleInOrganisation = "Admin",
            IsChangeRequestPending = false,
        };
    }

    private static OrganisationDetailModel CreateSeededDirectProducerOrganisation()
    {
        return new()
        {
            Id = WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid,
            Name = "POP QUEST LTD",
            TradingName = string.Empty,
            OrganisationRole = "Producer",
            OrganisationType = "Limited Company",
            OrganisationNumber = "165282",
            CompaniesHouseNumber = "17121895",
            ProducerType = string.Empty,
            NationId = 1,
            Town = string.Empty,
            County = string.Empty,
            Country = string.Empty,
            Postcode = string.Empty,
            PersonRoleInOrganisation = "Admin",
            IsChangeRequestPending = false,
        };
    }

    private sealed record PersonEmailResponseModel
    {
        public required string FirstName { get; init; }

        public required string LastName { get; init; }

        public required string Email { get; init; }
    }

    private sealed record UserOrganisationsListModel
    {
        public required UserDetailsModel User { get; init; }
    }

    private sealed record UserDetailsModel
    {
        public Guid Id { get; init; }

        public required string FirstName { get; init; }

        public required string LastName { get; init; }

        public required string Email { get; init; }

        public required string RoleInOrganisation { get; init; }

        public required string EnrolmentStatus { get; init; }

        public required string ServiceRole { get; init; }

        public required string Service { get; init; }

        public int ServiceRoleId { get; init; }

        public required string Telephone { get; init; }

        public required string JobTitle { get; init; }

        public bool IsChangeRequestPending { get; init; }

        public int NumberOfOrganisations { get; init; }

        public required IReadOnlyList<OrganisationDetailModel> Organisations { get; init; }
    }

    private sealed record OrganisationDetailModel
    {
        public Guid Id { get; init; }

        public required string Name { get; init; }

        public required string TradingName { get; init; }

        public required string OrganisationRole { get; init; }

        public required string OrganisationType { get; init; }

        public required string OrganisationNumber { get; init; }

        public required string CompaniesHouseNumber { get; init; }

        public required string ProducerType { get; init; }

        public int NationId { get; init; }

        public required string Town { get; init; }

        public required string County { get; init; }

        public required string Country { get; init; }

        public required string Postcode { get; init; }

        public required string PersonRoleInOrganisation { get; init; }

        public bool IsChangeRequestPending { get; init; }
    }

    private static class ServiceRoles
    {
        public const string ApprovedPerson = "Approved Person";
        public const string DelegatedPerson = "Delegated Person";
        public const string BasicUser = "Basic User";
    }
}
