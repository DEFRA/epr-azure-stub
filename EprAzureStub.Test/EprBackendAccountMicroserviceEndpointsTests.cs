using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace EprAzureStub.Test;

public class EprBackendAccountMicroserviceEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string PersonEmailsEndpoint =
        "/epr-backend-account-microservice/api/organisations/person-emails";

    private const string UserOrganisationsEndpoint =
        "/epr-backend-account-microservice/api/users/user-organisations";

    [Fact]
    public async Task GetPersonEmails_ReturnsLargeProducerResponse()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{PersonEmailsEndpoint}?organisationId={WasteOrganisationStubIds.LargeProducer}&entityTypeCode={EntityTypeCodes.DirectRegistrant}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<PersonEmailResponseModel>>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        var person = Assert.Single(body);
        Assert.Equal("Large", person.FirstName);
        Assert.Equal("Producer", person.LastName);
        Assert.Equal("large.producer@example.com", person.Email);
    }

    [Fact]
    public async Task GetPersonEmails_ReturnsComplianceSchemeResponse()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{PersonEmailsEndpoint}?organisationId={WasteOrganisationStubIds.ComplianceScheme}&entityTypeCode={EntityTypeCodes.ComplianceScheme}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<PersonEmailResponseModel>>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        var person = Assert.Single(body);
        Assert.Equal("Compliance", person.FirstName);
        Assert.Equal("Scheme", person.LastName);
        Assert.Equal("compliance.scheme@example.com", person.Email);
    }

    [Fact]
    public async Task GetPersonEmails_ReturnsBadRequest_WhenOrganisationIdIsEmpty()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{PersonEmailsEndpoint}?organisationId={Guid.Empty}&entityTypeCode={EntityTypeCodes.DirectRegistrant}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPersonEmails_ReturnsNoContent_WhenOrganisationIdIsUnknown()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{PersonEmailsEndpoint}?organisationId={Guid.NewGuid()}&entityTypeCode={EntityTypeCodes.DirectRegistrant}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(WasteOrganisationStubIds.LargeProducer, EntityTypeCodes.ComplianceScheme)]
    [InlineData(WasteOrganisationStubIds.ComplianceScheme, EntityTypeCodes.DirectRegistrant)]
    public async Task GetPersonEmails_ReturnsNoContent_WhenOrganisationAndEntityTypeAreMismatched(
        string organisationId,
        string entityTypeCode
    )
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{PersonEmailsEndpoint}?organisationId={organisationId}&entityTypeCode={entityTypeCode}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("entityTypeCode=Producer")]
    public async Task GetPersonEmails_ReturnsNoContent_WhenEntityTypeCodeIsMissingOrUnsupported(
        string queryString
    )
    {
        using var client = factory.CreateClient();
        var separator = string.IsNullOrEmpty(queryString) ? string.Empty : "&";

        var response = await client.GetAsync(
            $"{PersonEmailsEndpoint}?organisationId={WasteOrganisationStubIds.LargeProducer}{separator}{queryString}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetPersonEmails_MatchesEntityTypeCodeCaseInsensitively()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{PersonEmailsEndpoint}?organisationId={WasteOrganisationStubIds.LargeProducer}&entityTypeCode=dr",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetPersonEmails_ReturnsSeededDirectProducerResponse()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{PersonEmailsEndpoint}?organisationId={WasteOrganisationStubIds.SeededDirectProducerOrganisation}&entityTypeCode={EntityTypeCodes.DirectRegistrant}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<PersonEmailResponseModel>>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal(3, body.Count);
        Assert.Contains(
            body,
            person =>
                person.FirstName == "Direct"
                && person.LastName == "Producer"
                && person.Email == "test+directproducer@ee.com"
        );
        Assert.Contains(
            body,
            person =>
                person.FirstName == "SB FirstName"
                && person.LastName == "SB LastName"
                && person.Email == "bmmmdmgz@sharklasers.com"
        );
        Assert.Contains(
            body,
            person =>
                person.FirstName == "Francis"
                && person.LastName == "Chelladurai"
                && person.Email == "francis.chelladurai+31032026@equalexperts.com"
        );
    }

    [Fact]
    public async Task GetPersonEmails_ReturnsSeededComplianceSchemeResponse()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{PersonEmailsEndpoint}?organisationId={WasteOrganisationStubIds.SeededComplianceSchemeExternalId}&entityTypeCode={EntityTypeCodes.ComplianceScheme}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<List<PersonEmailResponseModel>>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal(3, body.Count);
        Assert.Contains(
            body,
            person =>
                person.FirstName == "First name"
                && person.LastName == "Last Name"
                && person.Email == "test+17122025143216@ee.com"
        );
        Assert.Contains(
            body,
            person =>
                person.FirstName == "Francis"
                && person.LastName == "Delegated"
                && person.Email == "francis.chelladurai+07042026@equalexperts.com"
        );
        Assert.Contains(
            body,
            person =>
                person.FirstName == "Francis"
                && person.LastName == "Basic"
                && person.Email == "francis.chelladurai+260407@equalexperts.com"
        );
    }

    [Theory]
    [InlineData(
        WasteOrganisationStubIds.SeededDirectProducerOrganisation,
        EntityTypeCodes.ComplianceScheme
    )]
    [InlineData(
        WasteOrganisationStubIds.SeededComplianceSchemeExternalId,
        EntityTypeCodes.DirectRegistrant
    )]
    [InlineData(
        WasteOrganisationStubIds.SeededComplianceSchemeOrganisation,
        EntityTypeCodes.ComplianceScheme
    )]
    public async Task GetPersonEmails_ReturnsNoContent_WhenSeededOrganisationAndEntityTypeAreMismatched(
        string organisationId,
        string entityTypeCode
    )
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{PersonEmailsEndpoint}?organisationId={organisationId}&entityTypeCode={entityTypeCode}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetPersonEmails_MatchesSeededEntityTypeCodeCaseInsensitively()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{PersonEmailsEndpoint}?organisationId={WasteOrganisationStubIds.SeededComplianceSchemeExternalId}&entityTypeCode=cs",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetUserOrganisations_ReturnsApprovedComplianceSchemeUserResponse()
    {
        using var client = factory.CreateClient();
        var userId = Guid.Parse("579c319d-d552-47a2-bf4c-5a125a3183bc");

        var response = await client.GetAsync(
            $"{UserOrganisationsEndpoint}?userId={userId}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UserOrganisationsListModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal(userId, body.User.Id);
        Assert.Equal("First name", body.User.FirstName);
        Assert.Equal("Last Name", body.User.LastName);
        Assert.Equal("test+17122025143216@ee.com", body.User.Email);
        Assert.Equal("Approved Person", body.User.ServiceRole);
        Assert.Equal("EPR Packaging", body.User.Service);
        Assert.Equal(1, body.User.ServiceRoleId);
        Assert.Equal(1, body.User.NumberOfOrganisations);
        var organisation = Assert.Single(body.User.Organisations);
        Assert.Equal(WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid, organisation.Id);
        Assert.Equal("Organisation Name", organisation.Name);
        Assert.Equal("Trading Name", organisation.TradingName);
        Assert.Equal("Compliance Scheme", organisation.OrganisationRole);
        Assert.Equal("1", organisation.OrganisationNumber);
        Assert.Equal("12345678", organisation.CompaniesHouseNumber);
    }

    [Fact]
    public async Task GetUserOrganisations_ReturnsApprovedDirectProducerUserResponse()
    {
        using var client = factory.CreateClient();
        var userId = Guid.Parse("79d0deab-c22d-4c30-8082-508ff8dc1bd7");

        var response = await client.GetAsync(
            $"{UserOrganisationsEndpoint}?userId={userId}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UserOrganisationsListModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal(userId, body.User.Id);
        Assert.Equal("Direct", body.User.FirstName);
        Assert.Equal("Producer", body.User.LastName);
        Assert.Equal("test+directproducer@ee.com", body.User.Email);
        Assert.Equal("Approved Person", body.User.ServiceRole);
        Assert.Equal("EPR Packaging", body.User.Service);
        Assert.Equal(1, body.User.ServiceRoleId);
        var organisation = Assert.Single(body.User.Organisations);
        Assert.Equal(WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid, organisation.Id);
        Assert.Equal("POP QUEST LTD", organisation.Name);
        Assert.Equal("Producer", organisation.OrganisationRole);
        Assert.Equal("165282", organisation.OrganisationNumber);
        Assert.Equal("17121895", organisation.CompaniesHouseNumber);
    }

    [Fact]
    public async Task GetUserOrganisations_ReturnsDelegatedDirectProducerUserResponse()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{UserOrganisationsEndpoint}?userId=513a78ee-d5bf-4fa4-9d8f-136550ea6072",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UserOrganisationsListModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal("SB FirstName", body.User.FirstName);
        Assert.Equal("SB LastName", body.User.LastName);
        Assert.Equal("bmmmdmgz@sharklasers.com", body.User.Email);
        Assert.Equal("Delegated Person", body.User.ServiceRole);
        Assert.Equal("EPR Packaging", body.User.Service);
        Assert.Equal(2, body.User.ServiceRoleId);
        Assert.Contains(
            body.User.Organisations,
            organisation =>
                organisation.Id == WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid
                && organisation.OrganisationRole == "Producer"
        );
    }

    [Fact]
    public async Task GetUserOrganisations_ReturnsBasicComplianceSchemeUserResponse()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{UserOrganisationsEndpoint}?userId=13e26b8a-e2b2-4870-b040-d6bdf5d689fa",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UserOrganisationsListModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal("Francis", body.User.FirstName);
        Assert.Equal("Basic", body.User.LastName);
        Assert.Equal("francis.chelladurai+260407@equalexperts.com", body.User.Email);
        Assert.Equal("Basic User", body.User.ServiceRole);
        Assert.Equal("EPR Packaging", body.User.Service);
        Assert.Equal(3, body.User.ServiceRoleId);
        Assert.Contains(
            body.User.Organisations,
            organisation =>
                organisation.Id == WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid
                && organisation.OrganisationRole == "Compliance Scheme"
        );
    }

    [Theory]
    [InlineData(
        "d062d4fe-34f8-468e-ada8-d950cc9a3c2a",
        "Francis",
        "Chelladurai",
        "francis.chelladurai+31032026@equalexperts.com",
        "Basic User"
    )]
    [InlineData(
        "ef2fd2a5-24bf-4b22-89a0-17a0367aee1c",
        "Francis",
        "Delegated",
        "francis.chelladurai+07042026@equalexperts.com",
        "Delegated Person"
    )]
    public async Task GetUserOrganisations_ReturnsRemainingSeededUserResponses(
        string userId,
        string firstName,
        string lastName,
        string email,
        string serviceRole
    )
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{UserOrganisationsEndpoint}?userId={userId}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<UserOrganisationsListModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal(firstName, body.User.FirstName);
        Assert.Equal(lastName, body.User.LastName);
        Assert.Equal(email, body.User.Email);
        Assert.Equal(serviceRole, body.User.ServiceRole);
        Assert.Equal("EPR Packaging", body.User.Service);
    }

    [Fact]
    public async Task GetUserOrganisations_ReturnsNotFound_WhenUserIdIsUnknown()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{UserOrganisationsEndpoint}?userId={Guid.NewGuid()}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUserOrganisations_ReturnsNotFound_WhenUserIdIsEmpty()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{UserOrganisationsEndpoint}?userId={Guid.Empty}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record PersonEmailResponseModel
    {
        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;
    }

    private sealed record UserOrganisationsListModel
    {
        public UserDetailsModel User { get; init; } = new();
    }

    private sealed record UserDetailsModel
    {
        public Guid Id { get; init; }

        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string ServiceRole { get; init; } = string.Empty;

        public string Service { get; init; } = string.Empty;

        public int ServiceRoleId { get; init; }

        public int NumberOfOrganisations { get; init; }

        public IReadOnlyList<OrganisationDetailModel> Organisations { get; init; } = [];
    }

    private sealed record OrganisationDetailModel
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string TradingName { get; init; } = string.Empty;

        public string OrganisationRole { get; init; } = string.Empty;

        public string OrganisationNumber { get; init; } = string.Empty;

        public string CompaniesHouseNumber { get; init; } = string.Empty;
    }
}
