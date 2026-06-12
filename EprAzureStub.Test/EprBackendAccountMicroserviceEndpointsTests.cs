using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace EprAzureStub.Test;

public class EprBackendAccountMicroserviceEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Endpoint =
        "/epr-backend-account-microservice/api/organisations/person-emails";

    [Fact]
    public async Task GetPersonEmails_ReturnsLargeProducerResponse()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{Endpoint}?organisationId={WasteOrganisationStubIds.LargeProducer}&entityTypeCode=DR",
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
            $"{Endpoint}?organisationId={WasteOrganisationStubIds.ComplianceScheme}&entityTypeCode=CS",
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
            $"{Endpoint}?organisationId={Guid.Empty}&entityTypeCode=DR",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPersonEmails_ReturnsNoContent_WhenOrganisationIdIsUnknown()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{Endpoint}?organisationId={Guid.NewGuid()}&entityTypeCode=DR",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(WasteOrganisationStubIds.LargeProducer, "CS")]
    [InlineData(WasteOrganisationStubIds.ComplianceScheme, "DR")]
    public async Task GetPersonEmails_ReturnsNoContent_WhenOrganisationAndEntityTypeAreMismatched(
        string organisationId,
        string entityTypeCode
    )
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{Endpoint}?organisationId={organisationId}&entityTypeCode={entityTypeCode}",
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
            $"{Endpoint}?organisationId={WasteOrganisationStubIds.LargeProducer}{separator}{queryString}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetPersonEmails_MatchesEntityTypeCodeCaseInsensitively()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"{Endpoint}?organisationId={WasteOrganisationStubIds.LargeProducer}&entityTypeCode=dr",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record PersonEmailResponseModel
    {
        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;
    }
}
