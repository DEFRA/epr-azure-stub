using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EprAzureStub.Test;

// The acceptance-year rule (cases ported from the PRN common backend's AcceptanceYearRuleTests), the notes generator,
// and the notes on a fixed clock and across run leases.
public class LoadTestPrnStateTests
{
    private const string Base = "/epr-prn-common-backend/api/v1/prn";
    private const string OrganisationHeader = "X-EPR-ORGANISATION";

    private static readonly DateTimeOffset DecemberChoiceWindow = new(2026, 12, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BeforeChoiceWindow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset JanuaryChoiceWindow = new(2027, 1, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AfterChoiceWindow = new(2027, 2, 15, 12, 0, 0, TimeSpan.Zero);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }

    [Theory]
    [InlineData(2026, new[] { 2026 })]
    [InlineData(2027, new int[0])]
    [InlineData(2025, new int[0])]
    public void StandardNote_IsAcceptableOnlyIntoTheCurrentComplianceYear(int obligationYear, int[] expected)
    {
        Assert.Equal(expected, PrnAcceptanceYearRule.AvailableYears(obligationYear, false, DecemberChoiceWindow));
    }

    [Fact]
    public void DecemberWaste_FollowsTheChoiceWindow()
    {
        Assert.Equal([2026, 2027], PrnAcceptanceYearRule.AvailableYears(2026, true, DecemberChoiceWindow));
        Assert.Equal([2026, 2027], PrnAcceptanceYearRule.AvailableYears(2026, true, JanuaryChoiceWindow));
        Assert.Equal([2026], PrnAcceptanceYearRule.AvailableYears(2026, true, BeforeChoiceWindow));
        Assert.Equal([2027], PrnAcceptanceYearRule.AvailableYears(2026, true, AfterChoiceWindow));
        Assert.Empty(PrnAcceptanceYearRule.AvailableYears(2024, true, AfterChoiceWindow));
    }

    [Fact]
    public void December2025Waste_NeverOffersAChoice()
    {
        Assert.Equal([2026], PrnAcceptanceYearRule.AvailableYears(2025, true, BeforeChoiceWindow));
        Assert.Equal([2026], PrnAcceptanceYearRule.AvailableYears(2025, true, DecemberChoiceWindow));
    }

    [Fact]
    public void NoteFromAFutureYearOrAnImpossibleYear_IsNeverAcceptable()
    {
        Assert.Empty(PrnAcceptanceYearRule.AvailableYears(2028, false, DecemberChoiceWindow));
        Assert.Empty(PrnAcceptanceYearRule.AvailableYears(2028, true, DecemberChoiceWindow));
        Assert.Empty(PrnAcceptanceYearRule.AvailableYears(0, true, DecemberChoiceWindow));
        Assert.Empty(PrnAcceptanceYearRule.AvailableYears(10000, true, DecemberChoiceWindow));
    }

    [Fact]
    public void ComplianceYear_RunsFebruaryToJanuaryInUkTime()
    {
        Assert.Equal(2026, PrnAcceptanceYearRule.ComplianceYear(JanuaryChoiceWindow));
        Assert.Equal(2027, PrnAcceptanceYearRule.ComplianceYear(AfterChoiceWindow));
        Assert.Equal(2026, PrnAcceptanceYearRule.ComplianceYear(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal(2027, PrnAcceptanceYearRule.ComplianceYear(new DateTimeOffset(2027, 1, 31, 20, 0, 0, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void Generate_GivesEveryOrganisationUniqueWellFormedNumbersAndIds()
    {
        var random = new Random(720);

        for (var organisation = 0; organisation < 2_000; organisation++)
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            var prns = LoadTestPrnState.Generate(new Guid(bytes), "Organisation", 1_000, DecemberChoiceWindow);

            Assert.All(prns, prn => Assert.Matches(new Regex(@"^E[RX]\d{8}$"), prn.PrnNumber));
            Assert.Equal(prns.Count, prns.Select(prn => prn.PrnNumber).Distinct().Count());
            Assert.Equal(prns.Count, prns.Select(prn => prn.ExternalId).Distinct().Count());
        }
    }

    [Fact]
    public async Task Search_InDecember_OffersThisYearsDecemberWasteForNextYear()
    {
        using var factory = Factory(new MutableTimeProvider(DecemberChoiceWindow));
        using var client = factory.CreateClient();

        var nextYear = await Search(client, "filterBy=awaiting-all&pageSize=10000&acceptableIntoYear=2027");

        Assert.NotEmpty(nextYear.Items);
        Assert.All(nextYear.Items, prn => Assert.True(prn.DecemberWaste));
        Assert.All(nextYear.Items, prn => Assert.Equal("2026", prn.ObligationYear));
    }

    [Theory]
    [InlineData("prns")]
    [InlineData("prns-lighthouse")]
    [InlineData("prns-browser-load")]
    public async Task RunLease_AcceptsThePrnPerformanceProfiles(string profile)
    {
        using var factory = Factory(new MutableTimeProvider(DecemberChoiceWindow));
        using var client = factory.CreateClient();

        var lease = await client.PostAsJsonAsync(
            "/admin/load-test-runs",
            new LoadTestRunLeaseRequest(Guid.NewGuid(), profile, 60),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.Created, lease.StatusCode);
    }

    [Fact]
    public async Task FixtureNotes_StayAsTheyAreWhenTheLeaseExpires_AndStartAgainWithANewRun()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        using var factory = Factory(clock);
        using var client = factory.CreateClient();
        await StartRun(client, Guid.NewGuid());
        var note = (await Search(client, "filterBy=awaiting-all&pageSize=1")).Items.Single();
        using var accept = new HttpRequestMessage(HttpMethod.Post, $"{Base}/status")
        {
            Content = JsonContent.Create(new[] { new { prnId = note.ExternalId, status = "ACCEPTED" } }),
        };
        accept.Headers.Add(OrganisationHeader, WasteOrganisationStubIds.SeededDirectProducerOrganisation);
        accept.Headers.Add("X-EPR-USER", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(accept, TestContext.Current.CancellationToken)).StatusCode);

        clock.Advance(120);
        Assert.Equal("ACCEPTED", (await Read(client, note.ExternalId)).PrnStatus);

        await StartRun(client, Guid.NewGuid());
        Assert.Equal("AWAITINGACCEPTANCE", (await Read(client, note.ExternalId)).PrnStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllocatedNotes_EndWithTheLease(bool expire)
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        using var factory = Factory(clock);
        using var client = factory.CreateClient();
        var token = TestContext.Current.CancellationToken;
        var runId = Guid.NewGuid();
        await StartRun(client, runId);
        var session = await client.PostAsJsonAsync(
            "/admin/load-test-sessions",
            new LoadTestSessionInitialisationRequest(runId, 1, 0),
            token
        );
        var organisationId = (await session.Content.ReadFromJsonAsync<LoadTestSessionResponse>(token))!
            .Users.SelectMany(user => user.Allocations)
            .Single()
            .OrganisationId;
        var note = (await Search(client, "filterBy=awaiting-all&pageSize=1", organisationId)).Items.Single();

        if (expire)
        {
            clock.Advance(120);
        }
        else
        {
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.DeleteAsync($"/admin/load-test-runs/{runId}", token)).StatusCode
            );
        }

        Assert.Equal(0, (await Search(client, "filterBy=awaiting-all", organisationId)).TotalItems);
        using var read = new HttpRequestMessage(HttpMethod.Get, $"{Base}/{note.ExternalId}");
        read.Headers.Add(OrganisationHeader, organisationId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(read, token)).StatusCode);
        using var accept = new HttpRequestMessage(HttpMethod.Post, $"{Base}/status")
        {
            Content = JsonContent.Create(new[] { new { prnId = note.ExternalId, status = "ACCEPTED" } }),
        };
        accept.Headers.Add(OrganisationHeader, organisationId.ToString());
        accept.Headers.Add("X-EPR-USER", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(accept, token)).StatusCode);
    }

    [Fact]
    public void Generate_GivesEveryBackendMaterialNotesAwaitingAcceptance()
    {
        var prns = LoadTestPrnState.Generate(Guid.NewGuid(), "Organisation", 1000, BeforeChoiceWindow);

        Assert.Equal(10, LoadTestPrnState.Materials.Count);
        Assert.Contains("Fibre", LoadTestPrnState.Materials);
        Assert.All(
            LoadTestPrnState.Materials,
            material => Assert.Contains(
                prns,
                prn => prn.MaterialName == material && prn.Status == LoadTestPrnState.AwaitingAcceptance
            )
        );
    }

    private static WebApplicationFactory<Program> Factory(TimeProvider clock) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock);
        }));

    private static async Task StartRun(HttpClient client, Guid runId)
    {
        var lease = await client.PostAsJsonAsync(
            "/admin/load-test-runs",
            new LoadTestRunLeaseRequest(runId, "prns", 60),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Created, lease.StatusCode);
    }

    private static async Task<SearchPage> Search(HttpClient client, string query, Guid? organisationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/search?{query}");
        request.Headers.Add(
            OrganisationHeader,
            organisationId?.ToString() ?? WasteOrganisationStubIds.SeededDirectProducerOrganisation
        );
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SearchPage>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<Prn> Read(HttpClient client, Guid prnId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/{prnId}");
        request.Headers.Add(OrganisationHeader, WasteOrganisationStubIds.SeededDirectProducerOrganisation);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Prn>(TestContext.Current.CancellationToken))!;
    }

    private sealed record SearchPage(IReadOnlyList<Prn> Items, int TotalItems);

    private sealed record Prn(Guid ExternalId, string PrnNumber, string PrnStatus, bool DecemberWaste, string ObligationYear);
}
