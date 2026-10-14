using Microsoft.AspNetCore.Mvc;

namespace EprAzureStub;

public static class EprBackendAccountMicroserviceEndpoints
{
    public sealed record ExternalIdsRequest(Guid[] ExternalIds);
    public sealed record CompaniesHouseNumbersRequest(string[] CompaniesHouseNumbers);
    public static void MapEprBackendAccountMicroserviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/epr-backend-account-microservice");

        group.MapGet("/admin/health", () => Results.Ok());
        group.MapPost("/api/organisations/organisations-by-externalIds",
            (ExternalIdsRequest request, LoadTestSessionState state) =>
            {
                if (request.ExternalIds is null || request.ExternalIds.Length == 0)
                {
                    return Results.BadRequest();
                }
                var allocations = state.GetAccountAllocations();
                var organisations = request.ExternalIds.Distinct().Select(id =>
                {
                    var allocation = allocations.FirstOrDefault(item => (item.OperatorOrganisationId ?? item.OrganisationId) == id);
                    if (allocation is null && id == WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid)
                    {
                        return new { externalId = id, name = "POP QUEST LTD", referenceNumber = "165282" };
                    }
                    if (allocation is null && (id == WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid || id == WasteOrganisationStubIds.SeededComplianceSchemeExternalIdGuid))
                    {
                        return new { externalId = id, name = "Organisation Name", referenceNumber = "1" };
                    }
                    return allocation is null ? null : new
                    {
                        externalId = id,
                        name = allocation.OrganisationName,
                        referenceNumber = state.ReferenceNumber(allocation)
                    };
                }).Where(item => item is not null).ToArray();
                return Results.Ok(new { organisations, notFoundExternalIds = request.ExternalIds.Except(organisations.Select(item => item!.externalId)) });
            });
        group.MapPost("/api/organisations/organisations-by-companies-house-numbers",
            (CompaniesHouseNumbersRequest request, LoadTestSessionState state) =>
            {
                if (request.CompaniesHouseNumbers is null || !request.CompaniesHouseNumbers.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    return Results.BadRequest();
                }
                var seeded = new[]
                {
                    new { externalId = WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid, name = "POP QUEST LTD", operatorName = (string?)null,
                        referenceNumber = "165282", companiesHouseNumber = "17121895", isComplianceScheme = false },
                    new { externalId = WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid, name = "Organisation Name", operatorName = (string?)"Organisation Name",
                        referenceNumber = "1", companiesHouseNumber = "12345678", isComplianceScheme = true }
                };
                var generated = state.GetAccountAllocations().Select(allocation => new
                {
                    externalId = allocation.OperatorOrganisationId ?? allocation.OrganisationId,
                    name = allocation.OrganisationName,
                    operatorName = allocation.IsComplianceScheme ? allocation.OrganisationName : null,
                    referenceNumber = allocation.ReferenceNumber,
                    companiesHouseNumber = allocation.CompaniesHouseNumber,
                    isComplianceScheme = allocation.IsComplianceScheme
                });
                return Results.Ok(seeded.Concat(generated)
                    .Where(item => request.CompaniesHouseNumbers.Contains(item.companiesHouseNumber)));

            });
        group.MapGet("/api/organisations/organisation-with-persons/{id:guid}", (Guid id, LoadTestSessionState state) =>
        {
            var allocation = state.GetAccountAllocations().FirstOrDefault(item => item.OrganisationId == id || item.OperatorOrganisationId == id);
            var userId = allocation?.UserId ?? (id == WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid
                ? LoadTestSessionState.ComplianceSchemeUserId : id == WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid
                    ? LoadTestSessionState.DirectProducerUserId : Guid.Empty);
            if (userId == Guid.Empty)
            {
                return Results.NotFound();
            }
            var user = FindSeededUser(userId)!;
            return Results.Ok(new
            {
                persons = new[] { new
            {
                userId = user.UserId, firstName = user.FirstName, lastName = user.LastName,
                email = user.Email, serviceRole = "Approved Person"
            } }
            });
        });

        group.MapGet(
            "/api/organisations/person-emails",
            (
                [FromQuery] Guid organisationId,
                [FromQuery] string? entityTypeCode,
                LoadTestSessionState loadTestSessionState,
                ILoggerFactory loggerFactory
            ) =>
            {
                if (organisationId == Guid.Empty)
                {
                    return Results.BadRequest();
                }

                if (
                    loadTestSessionState.TryGetAllocationForOrganisation(
                        organisationId,
                        out var loadTestAllocation
                    )
                    && loadTestAllocation.UserId == LoadTestSessionState.DirectProducerUserId
                    && IsEntityTypeCode(entityTypeCode, EntityTypeCodes.DirectRegistrant)
                )
                {
                    loggerFactory
                        .CreateLogger(nameof(EprBackendAccountMicroserviceEndpoints))
                        .LogInformation(
                            "Load-test person-email mapping: organisation {OrganisationId} resolves to direct producer user {UserId}, allocation {LoadTestUserIndex}.",
                            organisationId,
                            loadTestAllocation.UserId,
                            loadTestAllocation.UserIndex
                        );

                    return Results.Ok(CreateSeededDirectProducerPersonEmailsResponse());
                }

                if (
                    loadTestSessionState.TryGetAllocationForOrganisation(
                        organisationId,
                        out loadTestAllocation
                    )
                    && loadTestAllocation.UserId == LoadTestSessionState.ComplianceSchemeUserId
                    && organisationId == loadTestAllocation.OrganisationId
                    && IsEntityTypeCode(entityTypeCode, EntityTypeCodes.ComplianceScheme)
                )
                {
                    loggerFactory
                        .CreateLogger(nameof(EprBackendAccountMicroserviceEndpoints))
                        .LogInformation(
                            "Load-test person-email mapping: compliance scheme {OrganisationId} resolves to compliance scheme user {UserId}, allocation {LoadTestUserIndex}.",
                            organisationId,
                            loadTestAllocation.UserId,
                            loadTestAllocation.UserIndex
                        );

                    return Results.Ok(CreateSeededComplianceSchemePersonEmailsResponse());
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
            (
                [FromQuery] Guid userId,
                HttpRequest request,
                LoadTestSessionState loadTestSessionState,
                ILoggerFactory loggerFactory
            ) =>
            {
                var loadTestSessionKey = GetLoadTestSessionKey(request);

                if (loadTestSessionKey is not null && LoadTestSessionState.IsSupportedUser(userId))
                {
                    if (
                        !loadTestSessionState.TryGetAllocationForUser(
                            userId,
                            loadTestSessionKey,
                            out var loadTestAllocation
                        )
                    )
                    {
                        return Results.Conflict(
                            new { message = "Load test session is missing or does not match the user." }
                        );
                    }

                    var loadTestUser = FindSeededUser(userId)!;
                    loggerFactory
                        .CreateLogger(nameof(EprBackendAccountMicroserviceEndpoints))
                        .LogInformation(
                            "Load-test account mapping: session {LoadTestSessionKey}, user {UserId}, allocation {LoadTestUserIndex} maps to organisation {OrganisationId} ({OrganisationName}) and operator {OperatorOrganisationId}.",
                            loadTestSessionKey,
                            userId,
                            loadTestAllocation.UserIndex,
                            loadTestAllocation.OrganisationId,
                            loadTestAllocation.OrganisationName,
                            loadTestAllocation.OperatorOrganisationId
                        );

                    return Results.Ok(
                        CreateUserOrganisationsResponse(loadTestUser, loadTestAllocation, loadTestSessionState)
                    );
                }

                var response = CreateUserOrganisationsResponse(userId);

                return response is null ? Results.NotFound() : Results.Ok(response);
            }
        );

        group.MapGet(
            "/api/compliance-schemes/get-for-operator",
            (
                [FromQuery] Guid organisationId,
                HttpRequest request,
                LoadTestSessionState loadTestSessionState,
                ILoggerFactory loggerFactory
            ) =>
            {
                if (organisationId == Guid.Empty)
                {
                    return Results.BadRequest();
                }

                var loadTestSessionKey = GetLoadTestSessionKey(request);

                if (loadTestSessionKey is not null)
                {
                    if (
                        !loadTestSessionState.TryGetComplianceSchemeForOperator(
                            organisationId,
                            loadTestSessionKey,
                            out var loadTestAllocation
                        )
                    )
                    {
                        return Results.Conflict(
                            new { message = "Load test session is missing or does not match the operator." }
                        );
                    }

                    loggerFactory
                        .CreateLogger(nameof(EprBackendAccountMicroserviceEndpoints))
                        .LogInformation(
                            "Load-test compliance scheme mapping: session {LoadTestSessionKey}, operator organisation {OperatorOrganisationId}, allocation {LoadTestUserIndex} maps to compliance scheme {ComplianceSchemeId} ({ComplianceSchemeName}).",
                            loadTestSessionKey,
                            organisationId,
                            loadTestAllocation.UserIndex,
                            loadTestAllocation.OrganisationId,
                            loadTestAllocation.ComplianceSchemeName
                        );

                    return Results.Ok(CreateLoadTestComplianceSchemeResponse(loadTestAllocation));
                }

                var allocation = loadTestSessionState.GetAccountAllocations()
                    .SingleOrDefault(item => item.OperatorOrganisationId == organisationId);
                if (allocation is not null)
                    return Results.Ok(CreateLoadTestComplianceSchemeResponse(allocation));

                if (organisationId == WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid)
                {
                    return Results.Ok(CreateSeededComplianceSchemeResponse());
                }

                if (organisationId == WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid)
                {
                    return Results.Ok(Array.Empty<ComplianceSchemeResponseModel>());
                }

                return Results.NotFound();
            }
        );
    }

    private static string? GetLoadTestSessionKey(HttpRequest request)
    {
        return request.Headers.TryGetValue(
            LoadTestSessionState.SessionHeaderName,
            out var value
        )
            ? value.ToString()
            : null;
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

    private static IReadOnlyList<ComplianceSchemeResponseModel> CreateSeededComplianceSchemeResponse()
    {
        return
        [
            new()
            {
                RowNumber = 1,
                Id = WasteOrganisationStubIds.SeededComplianceSchemeExternalIdGuid,
                Name = "Compliance Scheme Name",
                CreatedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                NationId = 1,
            },
        ];
    }

    private static IReadOnlyList<ComplianceSchemeResponseModel> CreateLoadTestComplianceSchemeResponse(
        LoadTestOrganisationAllocation allocation
    )
    {
        return
        [
            new()
            {
                RowNumber = 1,
                Id = allocation.OrganisationId,
                Name = allocation.ComplianceSchemeName!,
                CreatedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                NationId = 1,
            },
        ];
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

    private static UserOrganisationsListModel CreateUserOrganisationsResponse(
        SeededUser seededUser,
        LoadTestOrganisationAllocation allocation,
        LoadTestSessionState state
    )
    {
        var organisation = CreateOrganisationDetail(
            seededUser.Organisation with
            {
                Id = allocation.OperatorOrganisationId ?? allocation.OrganisationId,
                Name = allocation.OrganisationName,
                TradingName = allocation.ComplianceSchemeName ?? allocation.OrganisationName,
                OrganisationNumber = state.ReferenceNumber(allocation),
                CompaniesHouseNumber = state.CompaniesHouseNumber(allocation),
            }
        );

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

    private sealed record ComplianceSchemeResponseModel
    {
        public int RowNumber { get; init; }

        public Guid Id { get; init; }

        public required string Name { get; init; }

        public DateTimeOffset CreatedOn { get; init; }

        public int NationId { get; init; }
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
