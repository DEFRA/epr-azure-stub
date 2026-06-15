using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace EprAzureStub.Test;

public class EprPrnCommonBackendEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Endpoint = "/epr-prn-common-backend/api/v1/prn/obligationcalculation/2025";
    private const string OrganisationHeader = "X-EPR-ORGANISATION";

    [Fact]
    public async Task GetAdminHealth_ReturnsOk()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/epr-prn-common-backend/admin/health",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetObligationCalculation_ReturnsLargeProducerResponse()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Add(OrganisationHeader, WasteOrganisationStubIds.LargeProducer);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ObligationModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal(1, body.NumberOfPrnsAwaitingAcceptance);
        Assert.All(
            body.ObligationData,
            data => Assert.Equal(WasteOrganisationStubIds.LargeProducerGuid, data.OrganisationId)
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
        request.Headers.Add(OrganisationHeader, WasteOrganisationStubIds.ComplianceScheme);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ObligationModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal(2, body.NumberOfPrnsAwaitingAcceptance);
        Assert.All(
            body.ObligationData,
            data => Assert.Equal(WasteOrganisationStubIds.ComplianceSchemeGuid, data.OrganisationId)
        );
        Assert.Contains(
            body.ObligationData,
            data => data.MaterialName == "Glass" && data.TonnageOutstanding == 13
        );
    }

    [Theory]
    [MemberData(nameof(AllWasteOrganisationStubIds))]
    public async Task GetObligationCalculation_ReturnsOk_ForEveryWasteOrganisationStubId(
        string organisationId
    )
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Add(OrganisationHeader, organisationId);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ObligationModel>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.NotEmpty(body.ObligationData);
        Assert.All(
            body.ObligationData,
            data => Assert.Equal(Guid.Parse(organisationId), data.OrganisationId)
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
        request.Headers.Add(OrganisationHeader, WasteOrganisationStubIds.LargeProducer);

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

    public static IEnumerable<object[]> AllWasteOrganisationStubIds()
    {
        return typeof(WasteOrganisationStubIds)
            .GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => new[] { field.GetRawConstantValue()! });
    }
}
