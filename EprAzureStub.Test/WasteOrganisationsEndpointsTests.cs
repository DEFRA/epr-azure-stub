using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace EprAzureStub.Test;

public class WasteOrganisationsEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string LoadTestSessionsEndpoint =
        "/admin/load-test-sessions";

    private const string OrganisationsEndpoint = "/waste-organisations/organisations";

    [Fact]
    public async Task GetAuthorisedHealth_ReturnsOk()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/waste-organisations/health/authorized",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganisation_ReturnsAllocatedDirectProducerOrganisation()
    {
        using var client = factory.CreateClient();
        var allocation = await InitialiseLoadTestSession(
            client,
            LoadTestSessionState.DirectProducerUserId,
            2,
            1
        );

        var response = await client.GetAsync(
            $"{OrganisationsEndpoint}/{allocation.OrganisationId}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organisation = await response.Content.ReadFromJsonAsync<OrganisationResponseModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(organisation);
        Assert.Equal(allocation.OrganisationId, organisation.Id);
        Assert.Equal("POP QUEST LTD 2", organisation.Name);
        Assert.Null(organisation.TradingName);
        Assert.Equal("GB-ENG", organisation.BusinessCountry);
        Assert.Equal("17121895", organisation.CompaniesHouseNumber);
        Assert.Equal("POP QUEST LTD 2", organisation.Address.AddressLine1);
        Assert.Equal("UK", organisation.Address.Country);
        Assert.Equal(6, organisation.Registrations.Count);
        Assert.All(organisation.Registrations, registration => Assert.Equal("REGISTERED", registration.Status));
        Assert.All(
            organisation.Registrations,
            registration => Assert.Equal("LARGE_PRODUCER", registration.Type)
        );
        Assert.Contains(organisation.Registrations, registration => registration.RegistrationYear == 2026);
    }

    [Fact]
    public async Task GetOrganisation_ReturnsAllocatedComplianceSchemeOrganisation()
    {
        using var client = factory.CreateClient();
        var allocation = await InitialiseLoadTestSession(
            client,
            LoadTestSessionState.ComplianceSchemeUserId,
            2,
            1
        );

        var response = await client.GetAsync(
            $"{OrganisationsEndpoint}/{allocation.OrganisationId}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organisation = await response.Content.ReadFromJsonAsync<OrganisationResponseModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(organisation);
        Assert.Equal(allocation.OrganisationId, organisation.Id);
        Assert.Equal("Organisation Name 2", organisation.Name);
        Assert.Equal("Compliance Scheme Name 2", organisation.TradingName);
        Assert.Equal("12345678", organisation.CompaniesHouseNumber);
        Assert.All(
            organisation.Registrations,
            registration => Assert.Equal("COMPLIANCE_SCHEME", registration.Type)
        );
    }

    [Fact]
    public async Task GetOrganisation_ReturnsNotFound_ForUnknownOrOperatorOrganisationId()
    {
        using var client = factory.CreateClient();
        var allocation = await InitialiseLoadTestSession(
            client,
            LoadTestSessionState.ComplianceSchemeUserId,
            1,
            0
        );
        Assert.NotNull(allocation.OperatorOrganisationId);

        var unknownResponse = await client.GetAsync(
            $"{OrganisationsEndpoint}/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken
        );
        var operatorResponse = await client.GetAsync(
            $"{OrganisationsEndpoint}/{allocation.OperatorOrganisationId}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, operatorResponse.StatusCode);
    }

    private static async Task<LoadTestOrganisationAllocation> InitialiseLoadTestSession(
        HttpClient client,
        Guid userId,
        int userCount,
        int userIndex
    )
    {
        var response = await client.PostAsJsonAsync(
            LoadTestSessionsEndpoint,
            new LoadTestSessionInitialisationRequest(Guid.NewGuid(), userCount, userCount),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<LoadTestSessionResponse>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(session);

        var user = Assert.Single(session.Users, candidate => candidate.UserId == userId);

        return Assert.Single(user.Allocations, candidate => candidate.UserIndex == userIndex);
    }

    private sealed record OrganisationResponseModel
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? TradingName { get; init; }

        public string BusinessCountry { get; init; } = string.Empty;

        public string CompaniesHouseNumber { get; init; } = string.Empty;

        public AddressResponseModel Address { get; init; } = new();

        public IReadOnlyList<RegistrationResponseModel> Registrations { get; init; } = [];
    }

    private sealed record AddressResponseModel
    {
        public string AddressLine1 { get; init; } = string.Empty;

        public string Country { get; init; } = string.Empty;
    }

    private sealed record RegistrationResponseModel
    {
        public string Status { get; init; } = string.Empty;

        public string Type { get; init; } = string.Empty;

        public int RegistrationYear { get; init; }
    }
}
