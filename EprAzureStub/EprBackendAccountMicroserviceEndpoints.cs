using Microsoft.AspNetCore.Mvc;

namespace EprAzureStub;

public static class EprBackendAccountMicroserviceEndpoints
{
    public static void MapEprBackendAccountMicroserviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/epr-backend-account-microservice");

        group.MapGet("/admin/health", () => Results.Ok());

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

                if (
                    organisationId == WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid
                    && IsEntityTypeCode(entityTypeCode, EntityTypeCodes.DirectRegistrant)
                )
                {
                    return Results.Ok(CreateSeededDirectProducerPersonEmailsResponse());
                }

                if (
                    organisationId == WasteOrganisationStubIds.SeededComplianceSchemeExternalIdGuid
                    && IsEntityTypeCode(entityTypeCode, EntityTypeCodes.ComplianceScheme)
                )
                {
                    return Results.Ok(CreateSeededComplianceSchemePersonEmailsResponse());
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

    private const string AdminRole = "Admin";
    private const string ApprovedEnrolmentStatus = "Approved";
    private const string DirectorJobTitle = "Director";
    private const string EprPackagingService = "EPR Packaging";
    private const string LimitedCompanyOrganisationType = "Limited Company";

    private static readonly SeededOrganisation SeededComplianceSchemeOrganisation = new(
        WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid,
        "Organisation Name",
        "Trading Name",
        "Compliance Scheme",
        "1",
        "12345678"
    );

    private static readonly SeededOrganisation SeededDirectProducerOrganisation = new(
        WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid,
        "POP QUEST LTD",
        string.Empty,
        "Producer",
        "165282",
        "17121895"
    );

    private static readonly IReadOnlyList<SeededUser> SeededComplianceSchemeUsers =
    [
        new(
            Guid.Parse("579c319d-d552-47a2-bf4c-5a125a3183bc"),
            "First name",
            "Last Name",
            "test+17122025143216@ee.com",
            "07123456789",
            ServiceRoles.ApprovedPerson,
            1,
            SeededComplianceSchemeOrganisation
        ),
        new(
            Guid.Parse("ef2fd2a5-24bf-4b22-89a0-17a0367aee1c"),
            "Francis",
            "Delegated",
            "francis.chelladurai+07042026@equalexperts.com",
            "00441234567892",
            ServiceRoles.DelegatedPerson,
            2,
            SeededComplianceSchemeOrganisation
        ),
        new(
            Guid.Parse("13e26b8a-e2b2-4870-b040-d6bdf5d689fa"),
            "Francis",
            "Basic",
            "francis.chelladurai+260407@equalexperts.com",
            "00441234567893",
            ServiceRoles.BasicUser,
            3,
            SeededComplianceSchemeOrganisation
        ),
    ];

    private static readonly IReadOnlyList<SeededUser> SeededDirectProducerUsers =
    [
        new(
            Guid.Parse("79d0deab-c22d-4c30-8082-508ff8dc1bd7"),
            "Direct",
            "Producer",
            "test+directproducer@ee.com",
            "07123456780",
            ServiceRoles.ApprovedPerson,
            1,
            SeededDirectProducerOrganisation
        ),
        new(
            Guid.Parse("513a78ee-d5bf-4fa4-9d8f-136550ea6072"),
            "SB FirstName",
            "SB LastName",
            "bmmmdmgz@sharklasers.com",
            "00441234567890",
            ServiceRoles.DelegatedPerson,
            2,
            SeededDirectProducerOrganisation
        ),
        new(
            Guid.Parse("d062d4fe-34f8-468e-ada8-d950cc9a3c2a"),
            "Francis",
            "Chelladurai",
            "francis.chelladurai+31032026@equalexperts.com",
            "00441234567891",
            ServiceRoles.BasicUser,
            3,
            SeededDirectProducerOrganisation
        ),
    ];

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

    private static IReadOnlyList<PersonEmailResponseModel> CreateSeededDirectProducerPersonEmailsResponse()
    {
        return CreatePersonEmailsResponse(SeededDirectProducerUsers);
    }

    private static IReadOnlyList<PersonEmailResponseModel> CreateSeededComplianceSchemePersonEmailsResponse()
    {
        return CreatePersonEmailsResponse(SeededComplianceSchemeUsers);
    }

    private static IReadOnlyList<PersonEmailResponseModel> CreatePersonEmailsResponse(
        IReadOnlyList<SeededUser> seededUsers
    )
    {
        return seededUsers
            .Select(user => new PersonEmailResponseModel
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
            })
            .ToList();
    }

    private static UserOrganisationsListModel? CreateUserOrganisationsResponse(Guid userId)
    {
        var seededUser = FindSeededUser(userId);

        return seededUser is null ? null : CreateUserOrganisationsResponse(seededUser);
    }

    private static SeededUser? FindSeededUser(Guid userId)
    {
        foreach (var seededUser in SeededComplianceSchemeUsers)
        {
            if (seededUser.UserId == userId)
            {
                return seededUser;
            }
        }

        foreach (var seededUser in SeededDirectProducerUsers)
        {
            if (seededUser.UserId == userId)
            {
                return seededUser;
            }
        }

        return null;
    }

    private static UserOrganisationsListModel CreateUserOrganisationsResponse(SeededUser seededUser)
    {
        var organisation = CreateOrganisationDetail(seededUser.Organisation);

        return new()
        {
            User = new()
            {
                Id = seededUser.UserId,
                FirstName = seededUser.FirstName,
                LastName = seededUser.LastName,
                Email = seededUser.Email,
                RoleInOrganisation = AdminRole,
                EnrolmentStatus = ApprovedEnrolmentStatus,
                ServiceRole = seededUser.ServiceRole,
                Service = EprPackagingService,
                ServiceRoleId = seededUser.ServiceRoleId,
                Telephone = seededUser.Telephone,
                JobTitle = DirectorJobTitle,
                IsChangeRequestPending = false,
                NumberOfOrganisations = 1,
                Organisations = [organisation],
            },
        };
    }

    private static OrganisationDetailModel CreateOrganisationDetail(SeededOrganisation seededOrganisation)
    {
        return new()
        {
            Id = seededOrganisation.Id,
            Name = seededOrganisation.Name,
            TradingName = seededOrganisation.TradingName,
            OrganisationRole = seededOrganisation.OrganisationRole,
            OrganisationType = LimitedCompanyOrganisationType,
            OrganisationNumber = seededOrganisation.OrganisationNumber,
            CompaniesHouseNumber = seededOrganisation.CompaniesHouseNumber,
            ProducerType = string.Empty,
            NationId = 1,
            Town = string.Empty,
            County = string.Empty,
            Country = string.Empty,
            Postcode = string.Empty,
            PersonRoleInOrganisation = AdminRole,
            IsChangeRequestPending = false,
        };
    }

    private sealed record SeededUser(
        Guid UserId,
        string FirstName,
        string LastName,
        string Email,
        string Telephone,
        string ServiceRole,
        int ServiceRoleId,
        SeededOrganisation Organisation
    );

    private sealed record SeededOrganisation(
        Guid Id,
        string Name,
        string TradingName,
        string OrganisationRole,
        string OrganisationNumber,
        string CompaniesHouseNumber
    );

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
