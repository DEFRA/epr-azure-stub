using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EprAzureStub.Test;

public class LoadTestPollingEndpointsTests
{
    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }

    private static WebApplicationFactory<Program> Factory(MutableTimeProvider clock) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));

    private static async Task<LoadTestPollingResponse> Initialise(HttpClient client, Guid runId, int growth = 2)
    {
        var lease = await client.PostAsJsonAsync("/admin/load-test-runs", new LoadTestRunLeaseRequest(runId, "unsubmitted-journey", 3600), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, lease.StatusCode);
        var response = await client.PostAsJsonAsync("/admin/load-test-polling", new LoadTestPollingRequest(runId, 125, 375, [2025, 2026, 2030], 300, growth), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoadTestPollingResponse>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<JsonElement> Read(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Catalogue_JoinsDiscoveryDetailsAccountAndPrn_WithUniqueOperatorMetadata()
    {
        var clock = new MutableTimeProvider();
        using var factory = Factory(clock);
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        var initial = await Initialise(client, runId);
        Assert.Equal(500, initial.Allocations.Count);
        Assert.Equal(125, initial.Allocations.Count(item => !item.IsComplianceScheme));
        Assert.Equal(375, initial.Allocations.Count(item => item.IsComplianceScheme));
        Assert.Equal(500, initial.Allocations.Select(item => item.OrganisationId).Distinct().Count());
        var collection = await Read(client, "/waste-organisations/organisations");
        Assert.Equal(500, collection.GetProperty("organisations").GetArrayLength());
        var discovered = collection.GetProperty("organisations").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToHashSet();
        Assert.All(initial.Allocations, allocation => Assert.Contains(allocation.OrganisationId, discovered));

        foreach (var allocation in new[] { initial.Allocations.First(), initial.Allocations.Last() })
        {
            var detail = await Read(client, $"/waste-organisations/organisations/{allocation.OrganisationId}");
            Assert.Equal(allocation.OrganisationName, detail.GetProperty("name").GetString());
            var recipients = await Read(client, $"/epr-backend-account-microservice/api/organisations/person-emails?organisationId={allocation.OrganisationId}&entityTypeCode={(allocation.IsComplianceScheme ? "CS" : "DR")}");
            Assert.Contains(recipients.EnumerateArray(), recipient => recipient.GetProperty("email").GetString()
                == (allocation.IsComplianceScheme ? "test+17122025143216@ee.com" : "test+directproducer@ee.com"));
            Assert.Equal(new[] { 2025, 2026, 2030 }, detail.GetProperty("registrations").EnumerateArray().Select(item => item.GetProperty("registrationYear").GetInt32()));
            var accountResponse = await client.PostAsJsonAsync("/epr-backend-account-microservice/api/organisations/organisations-by-externalIds", new { externalIds = new[] { allocation.OperatorOrganisationId ?? allocation.OrganisationId } }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);
            var account = await accountResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal(allocation.OperatorOrganisationId ?? allocation.OrganisationId, account.GetProperty("organisations")[0].GetProperty("externalId").GetGuid());
            using var prnRequest = new HttpRequestMessage(HttpMethod.Get, "/epr-prn-common-backend/api/v1/prn/obligationcalculation/2030");
            prnRequest.Headers.Add("X-EPR-ORGANISATION", allocation.OrganisationId.ToString());
            var prn = await client.SendAsync(prnRequest, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, prn.StatusCode);
            var obligations = await prn.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.All(obligations.GetProperty("obligationData").EnumerateArray(), material => Assert.Equal(allocation.OrganisationId, material.GetProperty("organisationId").GetGuid()));
        }
        Assert.Equal(500, initial.Allocations.Select(item => item.CompaniesHouseNumber).Distinct().Count());
        Assert.Equal(500, initial.Allocations.Select(item => item.ReferenceNumber).Distinct().Count());
        Assert.Equal(375, initial.Allocations.Where(item => item.IsComplianceScheme).Select(item => item.OperatorOrganisationId).Distinct().Count());
        Assert.All(initial.Allocations, item => Assert.Matches("^[0-9]{8}$", item.CompaniesHouseNumber));
        var schemes = await client.PostAsJsonAsync("/epr-backend-account-microservice/api/organisations/organisations-by-companies-house-numbers",
            new { companiesHouseNumbers = initial.Allocations.Select(item => item.CompaniesHouseNumber).ToArray() }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, schemes.StatusCode);
        var schemeRows = await schemes.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(500, schemeRows.GetArrayLength());
        foreach (var allocation in initial.Allocations)
        {
            var row = Assert.Single(schemeRows.EnumerateArray(), item => item.GetProperty("companiesHouseNumber").GetString() == allocation.CompaniesHouseNumber);
            Assert.Equal(allocation.OperatorOrganisationId ?? allocation.OrganisationId, row.GetProperty("externalId").GetGuid());
            Assert.Equal(allocation.ReferenceNumber, row.GetProperty("referenceNumber").GetString());
            Assert.Equal(allocation.OrganisationName, row.GetProperty("name").GetString());
            Assert.Equal(allocation.IsComplianceScheme, row.GetProperty("isComplianceScheme").GetBoolean());
            if (allocation.IsComplianceScheme)
            {
                var linked = await Read(client, $"/epr-backend-account-microservice/api/compliance-schemes/get-for-operator?organisationId={allocation.OperatorOrganisationId}");
                Assert.Equal(allocation.OrganisationId, Assert.Single(linked.EnumerateArray()).GetProperty("id").GetGuid());
            }
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/waste-organisations/organisations/{WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid}", TestContext.Current.CancellationToken)).StatusCode);
        foreach (var id in new[] { initial.Allocations.First().OrganisationId, initial.Allocations.Last().OperatorOrganisationId!.Value })
        {
            var persons = await Read(client, $"/epr-backend-account-microservice/api/organisations/organisation-with-persons/{id}");
            Assert.Equal("Approved Person", persons.GetProperty("persons")[0].GetProperty("serviceRole").GetString());
            Assert.False(string.IsNullOrEmpty(persons.GetProperty("persons")[0].GetProperty("email").GetString()));
        }
    }

    [Fact]
    public async Task SessionOnlyAccountLookups_UseSharedMetadata_AndNewRunsDoNotReuseNumbers()
    {
        var clock = new MutableTimeProvider();
        using var factory = Factory(clock);
        using var client = factory.CreateClient();
        async Task<LoadTestOrganisationAllocation[]> Allocate(Guid runId)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/admin/load-test-runs",
                new LoadTestRunLeaseRequest(runId, "unsubmitted-journey", 3600), TestContext.Current.CancellationToken)).StatusCode);
            var response = await client.PostAsJsonAsync("/admin/load-test-sessions",
                new LoadTestSessionInitialisationRequest(runId, 1, 2), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<LoadTestSessionResponse>(TestContext.Current.CancellationToken))!
                .Users.SelectMany(user => user.Allocations).ToArray();
        }
        var firstRun = Guid.NewGuid();
        var first = await Allocate(firstRun);
        var seededResponse = await client.PostAsJsonAsync("/epr-backend-account-microservice/api/organisations/organisations-by-companies-house-numbers",
            new { companiesHouseNumbers = new[] { "17121895", "12345678" } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, seededResponse.StatusCode);
        var seededRows = (await seededResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).EnumerateArray().ToArray();
        Assert.Equal(2, seededRows.Length);
        var seededProducer = Assert.Single(seededRows, row => row.GetProperty("companiesHouseNumber").GetString() == "17121895");
        Assert.Equal(WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid, seededProducer.GetProperty("externalId").GetGuid());
        Assert.Equal("165282", seededProducer.GetProperty("referenceNumber").GetString());
        var seededScheme = Assert.Single(seededRows, row => row.GetProperty("companiesHouseNumber").GetString() == "12345678");
        Assert.Equal(WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid, seededScheme.GetProperty("externalId").GetGuid());
        Assert.Equal("1", seededScheme.GetProperty("referenceNumber").GetString());
        var repeated = await client.PostAsJsonAsync("/admin/load-test-sessions",
            new LoadTestSessionInitialisationRequest(firstRun, 1, 2), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(first, (await repeated.Content.ReadFromJsonAsync<LoadTestSessionResponse>(TestContext.Current.CancellationToken))!
            .Users.SelectMany(user => user.Allocations));
        foreach (var allocation in first)
        {
            var recipients = await Read(client, $"/epr-backend-account-microservice/api/organisations/person-emails?organisationId={allocation.OrganisationId}&entityTypeCode={(allocation.IsComplianceScheme ? "CS" : "DR")}");
            Assert.Contains(recipients.EnumerateArray(), recipient => recipient.GetProperty("email").GetString()
                == (allocation.IsComplianceScheme ? "test+17122025143216@ee.com" : "test+directproducer@ee.com"));
            if (allocation.IsComplianceScheme)
                Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync(
                    $"/epr-backend-account-microservice/api/organisations/person-emails?organisationId={allocation.OperatorOrganisationId}&entityTypeCode=CS",
                    TestContext.Current.CancellationToken)).StatusCode);
            var bulk = await client.PostAsJsonAsync("/epr-backend-account-microservice/api/organisations/organisations-by-companies-house-numbers",
                new { companiesHouseNumbers = new[] { allocation.CompaniesHouseNumber } }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, bulk.StatusCode);
            var row = Assert.Single((await bulk.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).EnumerateArray());
            Assert.Equal(allocation.OperatorOrganisationId ?? allocation.OrganisationId, row.GetProperty("externalId").GetGuid());
            Assert.Equal(allocation.ReferenceNumber, row.GetProperty("referenceNumber").GetString());
            var external = await client.PostAsJsonAsync("/epr-backend-account-microservice/api/organisations/organisations-by-externalIds",
                new { externalIds = new[] { allocation.OperatorOrganisationId ?? allocation.OrganisationId } }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, external.StatusCode);
            var externalRow = Assert.Single((await external.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
                .GetProperty("organisations").EnumerateArray());
            Assert.Equal(allocation.ReferenceNumber, externalRow.GetProperty("referenceNumber").GetString());
            if (allocation.IsComplianceScheme)
            {
                var scheme = await Read(client, $"/epr-backend-account-microservice/api/compliance-schemes/get-for-operator?organisationId={allocation.OperatorOrganisationId}");
                Assert.Equal(allocation.OrganisationId, Assert.Single(scheme.EnumerateArray()).GetProperty("id").GetGuid());
                using var mismatched = new HttpRequestMessage(HttpMethod.Get,
                    $"/epr-backend-account-microservice/api/compliance-schemes/get-for-operator?organisationId={allocation.OperatorOrganisationId}");
                mismatched.Headers.Add(LoadTestSessionState.SessionHeaderName, $"{firstRun}:{(allocation.UserIndex == 0 ? 1 : 0)}");
                Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(mismatched, TestContext.Current.CancellationToken)).StatusCode);
            }
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/admin/load-test-runs/{firstRun}", TestContext.Current.CancellationToken)).StatusCode);
        var second = await Allocate(Guid.NewGuid());
        using var expired = new HttpRequestMessage(HttpMethod.Get,
            $"/epr-backend-account-microservice/api/compliance-schemes/get-for-operator?organisationId={first.First(item => item.IsComplianceScheme).OperatorOrganisationId}");
        expired.Headers.Add(LoadTestSessionState.SessionHeaderName, $"{firstRun}:0");
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(expired, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Empty(first.Select(item => item.CompaniesHouseNumber).Intersect(second.Select(item => item.CompaniesHouseNumber)));
        Assert.Empty(first.Select(item => item.ReferenceNumber).Intersect(second.Select(item => item.ReferenceNumber)));
        var oldScheme = first.First(item => item.IsComplianceScheme);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/epr-backend-account-microservice/api/compliance-schemes/get-for-operator?organisationId={oldScheme.OperatorOrganisationId}",
            TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SessionsAndPolling_ShareAllocationsInEitherInitialisationOrder_AfterGrowth(bool sessionsFirst)
    {
        var clock = new MutableTimeProvider();
        using var factory = Factory(clock);
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        var lease = await client.PostAsJsonAsync("/admin/load-test-runs",
            new LoadTestRunLeaseRequest(runId, "unsubmitted-journey", 3600), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, lease.StatusCode);

        async Task<LoadTestSessionResponse> Sessions()
        {
            var response = await client.PostAsJsonAsync("/admin/load-test-sessions",
                new LoadTestSessionInitialisationRequest(runId, 2, 2), TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<LoadTestSessionResponse>(TestContext.Current.CancellationToken))!;
        }

        async Task<LoadTestPollingResponse> Polling()
        {
            var response = await client.PostAsJsonAsync("/admin/load-test-polling",
                new LoadTestPollingRequest(runId, 2, 2, [2026], 300, 2), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<LoadTestPollingResponse>(TestContext.Current.CancellationToken))!;
        }

        async Task AssertAccountAllocation(LoadTestOrganisationAllocation allocation)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"/epr-backend-account-microservice/api/users/user-organisations?userId={allocation.UserId}");
            request.Headers.Add(LoadTestSessionState.SessionHeaderName, $"{runId}:{allocation.UserIndex}");
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal(allocation.UserId, body.GetProperty("user").GetProperty("id").GetGuid());
            var organisation = Assert.Single(body.GetProperty("user").GetProperty("organisations").EnumerateArray());
            Assert.Equal(allocation.OperatorOrganisationId ?? allocation.OrganisationId, organisation.GetProperty("id").GetGuid());
            Assert.Equal(allocation.OrganisationName, organisation.GetProperty("name").GetString());
            Assert.Equal(allocation.CompaniesHouseNumber, organisation.GetProperty("companiesHouseNumber").GetString());
            Assert.Equal(allocation.ReferenceNumber, organisation.GetProperty("organisationNumber").GetString());

            if (allocation.IsComplianceScheme)
            {
                using var schemeRequest = new HttpRequestMessage(HttpMethod.Get,
                    $"/epr-backend-account-microservice/api/compliance-schemes/get-for-operator?organisationId={allocation.OperatorOrganisationId}");
                schemeRequest.Headers.Add(LoadTestSessionState.SessionHeaderName, $"{runId}:{allocation.UserIndex}");
                var schemeResponse = await client.SendAsync(schemeRequest, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.OK, schemeResponse.StatusCode);
                var schemes = await schemeResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
                var scheme = Assert.Single(schemes.EnumerateArray());
                Assert.Equal(allocation.OrganisationId, scheme.GetProperty("id").GetGuid());
                Assert.Equal(allocation.ComplianceSchemeName, scheme.GetProperty("name").GetString());
                using var mismatched = new HttpRequestMessage(HttpMethod.Get,
                    $"/epr-backend-account-microservice/api/compliance-schemes/get-for-operator?organisationId={allocation.OperatorOrganisationId}");
                mismatched.Headers.Add(LoadTestSessionState.SessionHeaderName, $"{runId}:{(allocation.UserIndex == 0 ? 1 : 0)}");
                Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(mismatched, TestContext.Current.CancellationToken)).StatusCode);
            }

            var detail = await Read(client, $"/waste-organisations/organisations/{allocation.OrganisationId}");
            var registration = Assert.Single(detail.GetProperty("registrations").EnumerateArray());
            Assert.Equal(allocation.IsComplianceScheme ? "COMPLIANCE_SCHEME" : "LARGE_PRODUCER",
                registration.GetProperty("type").GetString());
        }

        LoadTestSessionResponse session;
        LoadTestPollingResponse polling;
        if (sessionsFirst)
        {
            session = await Sessions();
            polling = await Polling();
        }
        else
        {
            polling = await Polling();
            session = await Sessions();
        }

        var original = session.Users.SelectMany(user => user.Allocations).ToArray();
        Assert.Equal(4, original.Length);
        Assert.Equal(2, original.Count(allocation => !allocation.IsComplianceScheme));
        Assert.Equal(2, original.Count(allocation => allocation.IsComplianceScheme));
        Assert.Equal(original, polling.Allocations);
        foreach (var allocation in original)
            await AssertAccountAllocation(allocation);

        clock.Advance(600);
        var grown = await Polling();
        Assert.Equal(6, grown.Allocations.Count);
        Assert.Equal(6, grown.Allocations.Select(item => item.CompaniesHouseNumber).Distinct().Count());
        Assert.Equal(6, grown.Allocations.Select(item => item.ReferenceNumber).Distinct().Count());
        Assert.Equal(original, grown.Allocations.Take(4));
        var repeatedSession = await Sessions();
        Assert.Equal(4, repeatedSession.UserCount);
        Assert.Equal(original, repeatedSession.Users.SelectMany(user => user.Allocations));
        var catalogue = await Read(client, "/waste-organisations/organisations");
        var ids = catalogue.GetProperty("organisations").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToHashSet();
        foreach (var allocation in grown.Allocations)
        {
            Assert.Contains(allocation.OrganisationId, ids);
            await AssertAccountAllocation(allocation);
        }
    }

    [Fact]
    public async Task Growth_UsesElapsedTimeAndCap_RenewalAndRepeatedInitialisationKeepIdsAndStart()
    {
        var clock = new MutableTimeProvider();
        using var factory = Factory(clock);
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        var initial = await Initialise(client, runId);
        clock.Advance(299);
        Assert.Equal(500, (await Read(client, $"/admin/load-test-polling/{runId}")).GetProperty("allocations").GetArrayLength());
        clock.Advance(1);
        var at300 = await Read(client, $"/admin/load-test-polling/{runId}");
        Assert.Equal(501, at300.GetProperty("allocations").GetArrayLength());
        var grownId = at300.GetProperty("allocations")[500].GetProperty("organisationId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/waste-organisations/organisations/{grownId}", TestContext.Current.CancellationToken)).StatusCode);
        using var userRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/epr-backend-account-microservice/api/users/user-organisations?userId={LoadTestSessionState.DirectProducerUserId}");
        userRequest.Headers.Add(LoadTestSessionState.SessionHeaderName, $"{runId}:125");
        var userResponse = await client.SendAsync(userRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, userResponse.StatusCode);
        var user = await userResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(grownId, user.GetProperty("user").GetProperty("organisations")[0].GetProperty("id").GetGuid());
        Assert.Equal(at300.GetProperty("allocations")[500].GetProperty("referenceNumber").GetString(), user.GetProperty("user").GetProperty("organisations")[0].GetProperty("organisationNumber").GetString());
        var renewal = await client.PutAsJsonAsync($"/admin/load-test-runs/{runId}", new { leaseDurationSeconds = 3600 }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, renewal.StatusCode);
        var repeated = await client.PostAsJsonAsync("/admin/load-test-polling", new LoadTestPollingRequest(runId, 125, 375, [2030, 2026, 2025, 2026], 300, 2), TestContext.Current.CancellationToken);
        var reused = (await repeated.Content.ReadFromJsonAsync<LoadTestPollingResponse>(TestContext.Current.CancellationToken))!;
        Assert.Equal(initial.StartedAtUtc, reused.StartedAtUtc);
        Assert.Equal(initial.Allocations.Select(item => item.OrganisationId), reused.Allocations.Take(500).Select(item => item.OrganisationId));
        clock.Advance(300);
        Assert.Equal(502, (await Read(client, $"/admin/load-test-polling/{runId}")).GetProperty("allocations").GetArrayLength());
        clock.Advance(1200);
        Assert.Equal(502, (await Read(client, $"/admin/load-test-polling/{runId}")).GetProperty("allocations").GetArrayLength());
        var session = await client.PostAsJsonAsync("/admin/load-test-sessions", new LoadTestSessionInitialisationRequest(runId, 125, 375), TestContext.Current.CancellationToken);
        Assert.Equal(500, (await session.Content.ReadFromJsonAsync<LoadTestSessionResponse>(TestContext.Current.CancellationToken))!.UserCount);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/admin/load-test-polling", new LoadTestPollingRequest(runId, 125, 375, [2026], 300, 2), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/admin/load-test-runs/{Guid.NewGuid()}", TestContext.Current.CancellationToken)).StatusCode);
        await client.DeleteAsync($"/admin/load-test-runs/{runId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/waste-organisations/organisations/{grownId}", TestContext.Current.CancellationToken)).StatusCode);
        var baseline = (await Read(client, "/waste-organisations/organisations")).GetProperty("organisations");
        Assert.Equal(2, baseline.GetArrayLength());
        Assert.DoesNotContain(baseline.EnumerateArray(), item => item.GetProperty("id").GetGuid() == grownId);
    }

    [Fact]
    public async Task PrnClocks_AreIndependentPerOrganisationAndYear_ExpiryClearsCatalogue()
    {
        var clock = new MutableTimeProvider();
        using var factory = Factory(clock);
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        var catalogue = await Initialise(client, runId);
        async Task<JsonElement> Prn(Guid id, int year)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/epr-prn-common-backend/api/v1/prn/obligationcalculation/{year}");
            request.Headers.Add("X-EPR-ORGANISATION", id.ToString());
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        }
        var id = catalogue.Allocations[0].OrganisationId;
        var before = await Prn(id, 2026);
        clock.Advance(299);
        Assert.Equal(before.ToString(), (await Prn(id, 2026)).ToString());
        await Prn(id, 2030);
        await Prn(catalogue.Allocations[1].OrganisationId, 2026);
        clock.Advance(1);
        var changed = await Prn(id, 2026);
        Assert.Equal(0, changed.GetProperty("numberOfPrnsAwaitingAcceptance").GetInt32());
        Assert.All(changed.GetProperty("obligationData").EnumerateArray(), material =>
        {
            Assert.Equal("Met", material.GetProperty("status").GetString());
            Assert.Equal(material.GetProperty("obligationToMeet").GetInt32(), material.GetProperty("tonnageAccepted").GetInt32());
            Assert.Equal(0, material.GetProperty("tonnageOutstanding").GetInt32());
        });
        Assert.Equal(before.ToString(), (await Prn(id, 2030)).ToString());
        Assert.Equal(1, (await Prn(catalogue.Allocations[1].OrganisationId, 2026)).GetProperty("numberOfPrnsAwaitingAcceptance").GetInt32());
        clock.Advance(300);
        var phase2 = await Prn(id, 2026);
        Assert.NotEqual(before.ToString(), phase2.ToString());
        Assert.Equal("NotMet", phase2.GetProperty("obligationData")[0].GetProperty("status").GetString());
        clock.Advance(300);
        Assert.NotEqual(changed.ToString(), (await Prn(id, 2026)).ToString());
        clock.Advance(2700);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/load-test-polling/{runId}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/waste-organisations/organisations/{id}", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task SeparateIntervals_Support750PlusGrowthPast1000_AndSkippedPrnPeriodsChangeValues()
    {
        var clock = new MutableTimeProvider();
        using var factory = Factory(clock);
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        await client.PostAsJsonAsync("/admin/load-test-runs", new LoadTestRunLeaseRequest(runId, "unsubmitted", 86400), TestContext.Current.CancellationToken);
        var request = new LoadTestPollingRequest(runId, 250, 500, [2026], MaximumAdditionalOrganisations: 288,
            OrganisationGrowthIntervalSeconds: 1, ObligationChangeIntervalSeconds: 10);
        var response = await client.PostAsJsonAsync("/admin/load-test-polling", request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catalogue = (await response.Content.ReadFromJsonAsync<LoadTestPollingResponse>(TestContext.Current.CancellationToken))!;
        client.DefaultRequestHeaders.Add("X-EPR-ORGANISATION", catalogue.Allocations[0].OrganisationId.ToString());
        var before = await Read(client, "/epr-prn-common-backend/api/v1/prn/obligationcalculation/2026");
        clock.Advance(9);
        Assert.Equal(before.ToString(), (await Read(client, "/epr-prn-common-backend/api/v1/prn/obligationcalculation/2026")).ToString());
        Assert.Equal(759, (await Read(client, $"/admin/load-test-polling/{runId}")).GetProperty("allocations").GetArrayLength());
        clock.Advance(11);
        var phase2 = await Read(client, "/epr-prn-common-backend/api/v1/prn/obligationcalculation/2026");
        Assert.NotEqual(before.ToString(), phase2.ToString());
        Assert.Equal("NotMet", phase2.GetProperty("obligationData")[0].GetProperty("status").GetString());
        clock.Advance(268);
        Assert.Equal(1038, (await Read(client, $"/admin/load-test-polling/{runId}")).GetProperty("allocations").GetArrayLength());
    }

    [Fact]
    public async Task ConfiguredBound_RejectsOversizedAndOverflowingCounts_BeforeAllocating()
    {
        var clock = new MutableTimeProvider();
        using var factory = Factory(clock).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<Microsoft.Extensions.Options.IOptions<LoadTestOptions>>(
                Microsoft.Extensions.Options.Options.Create(new LoadTestOptions { MaxOrganisationCount = 750 }))));
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        await client.PostAsJsonAsync("/admin/load-test-runs", new LoadTestRunLeaseRequest(runId, "unsubmitted", 3600), TestContext.Current.CancellationToken);
        foreach (var counts in new[] { (751, 0), (int.MaxValue, int.MaxValue), (int.MaxValue, -1) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/load-test-sessions",
                new LoadTestSessionInitialisationRequest(runId, counts.Item1, counts.Item2), TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/load-test-polling",
                new LoadTestPollingRequest(runId, counts.Item1, counts.Item2, [2026], MaximumAdditionalOrganisations: 0), TestContext.Current.CancellationToken)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/load-test-polling",
            new LoadTestPollingRequest(runId, 250, 500, [2026], MaximumAdditionalOrganisations: 1), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/admin/load-test-polling",
            new LoadTestPollingRequest(runId, 250, 500, [2026], MaximumAdditionalOrganisations: 0), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("organisations-by-externalIds", "{}")]
    [InlineData("organisations-by-externalIds", "{\"externalIds\":[]}")]
    [InlineData("organisations-by-companies-house-numbers", "{}")]
    [InlineData("organisations-by-companies-house-numbers", "{\"companiesHouseNumbers\":[\"\"]}")]
    public async Task BulkLookup_InvalidBody_ReturnsBadRequest(string route, string body)
    {
        using var factory = Factory(new MutableTimeProvider());
        using var client = factory.CreateClient();
        var response = await client.PostAsync($"/epr-backend-account-microservice/api/organisations/{route}", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
