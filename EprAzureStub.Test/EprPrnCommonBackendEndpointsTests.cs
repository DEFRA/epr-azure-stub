using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace EprAzureStub.Test;

public class EprPrnCommonBackendEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Endpoint = "/epr-prn-common-backend/api/v1/prn/obligationcalculation/2025";
    private const string OrganisationHeader = "X-EPR-ORGANISATION";
    private const string LargeProducerId = "9d3c4d0f-8e5a-4b91-9f7a-2e8d6a1c5f42";
    private const string ComplianceSchemeId = "c71b2e84-3f9d-47aa-a8c6-5b4ef0139d8e";

    [Fact]
    public async Task GetObligationCalculation_ReturnsLargeProducerResponse()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Add(OrganisationHeader, LargeProducerId);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ObligationModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal(1, body.NumberOfPrnsAwaitingAcceptance);
        Assert.All(
            body.ObligationData,
            data => Assert.Equal(Guid.Parse(LargeProducerId), data.OrganisationId)
        );
        Assert.Contains(
            body.ObligationData,
            data => data.MaterialName == "Plastic" && data.Status == "NotMet"
        );
    }

    [Fact]
    public async Task GetObligationCalculation_ReturnsComplianceSchemeResponse()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Add(OrganisationHeader, ComplianceSchemeId);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ObligationModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal(2, body.NumberOfPrnsAwaitingAcceptance);
        Assert.All(
            body.ObligationData,
            data => Assert.Equal(Guid.Parse(ComplianceSchemeId), data.OrganisationId)
        );
        Assert.Contains(
            body.ObligationData,
            data => data.MaterialName == "Glass" && data.TonnageOutstanding == 13
        );
    }

    [Theory]
    [InlineData(2023)]
    [InlineData(2030)]
    public async Task GetObligationCalculation_ReturnsBadRequest_WhenYearIsOutOfRange(int year)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/epr-prn-common-backend/api/v1/prn/obligationcalculation/{year}"
        );
        request.Headers.Add(OrganisationHeader, LargeProducerId);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = await response.Content.ReadFromJsonAsync<string>(
            TestContext.Current.CancellationToken
        );
        Assert.Equal($"Invalid year provided: {year}.", message);
    }

    [Fact]
    public async Task GetObligationCalculation_ReturnsBadRequest_WhenOrganisationHeaderIsMissing()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Endpoint, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetObligationCalculation_ReturnsNotFound_WhenOrganisationIsUnknown()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Add(OrganisationHeader, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record ObligationModel
    {
        public int NumberOfPrnsAwaitingAcceptance { get; init; }

        public IReadOnlyList<ObligationData> ObligationData { get; init; } = [];
    }

    private sealed record ObligationData
    {
        public Guid OrganisationId { get; init; }

        public string MaterialName { get; init; } = string.Empty;

        public int? TonnageOutstanding { get; init; }

        public string Status { get; init; } = string.Empty;
    }
}
