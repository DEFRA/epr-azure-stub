using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace EprAzureStub.Test;

// The notes state is shared by every test in the class: the read tests use the large producer and the
// status changes the compliance scheme, so no test depends on another's changes.
public class EprPrnCommonBackendPrnEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/epr-prn-common-backend/api/v1/prn";
    private const string OrganisationHeader = "X-EPR-ORGANISATION";
    private const string UserHeader = "X-EPR-USER";

    [Fact]
    public async Task Search_AwaitingAll_ReturnsOnlyAwaitingNotesAndCountsBeforePaging()
    {
        var page = await Search(WasteOrganisationStubIds.LargeProducer, "filterBy=awaiting-all&page=2&pageSize=25");

        Assert.Equal(25, page.Items.Count);
        Assert.Equal(2, page.CurrentPage);
        Assert.True(page.TotalItems > 25);
        Assert.All(page.Items, prn => Assert.Equal("AWAITINGACCEPTANCE", prn.PrnStatus));
        Assert.All(page.Items, prn => Assert.Equal(4, prn.PrnStatusId));
    }

    [Fact]
    public async Task Search_AwaitingMaterialSortedByTonnage_ReturnsThatMaterialLargestFirst()
    {
        var page = await Search(
            WasteOrganisationStubIds.LargeProducer,
            "filterBy=awaiting-plastic&sortBy=tonnage-desc&pageSize=100"
        );

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, prn => Assert.Equal("Plastic", prn.MaterialName));
        Assert.Equal(page.Items.OrderByDescending(prn => prn.TonnageValue).Select(prn => prn.TonnageValue), page.Items.Select(prn => prn.TonnageValue));
    }

    [Fact]
    public async Task Search_AcceptableIntoYear_LeavesOutNotesThatCannotBeAcceptedIntoIt()
    {
        var year = PrnAcceptanceYearRule.ComplianceYear(DateTimeOffset.UtcNow);
        var all = await Search(WasteOrganisationStubIds.LargeProducer, "filterBy=awaiting-all&pageSize=10000");
        var acceptable = await Search(
            WasteOrganisationStubIds.LargeProducer,
            $"filterBy=awaiting-all&pageSize=10000&acceptableIntoYear={year}"
        );

        Assert.True(acceptable.TotalItems < all.TotalItems);
        Assert.All(
            acceptable.Items,
            prn => Assert.True(
                PrnAcceptanceYearRule.IsAcceptableInto(year, int.Parse(prn.ObligationYear), prn.DecemberWaste, DateTimeOffset.UtcNow)
            )
        );
    }

    [Fact]
    public async Task Search_TextMatchesNumberOrIssuer()
    {
        var first = (await Search(WasteOrganisationStubIds.LargeProducer, "pageSize=1")).Items.Single();

        var byNumber = await Search(WasteOrganisationStubIds.LargeProducer, $"search={first.PrnNumber}");
        var byIssuer = await Search(WasteOrganisationStubIds.LargeProducer, "search=northern recycling&pageSize=10000");

        Assert.Contains(byNumber.Items, prn => prn.PrnNumber == first.PrnNumber);
        Assert.NotEmpty(byIssuer.Items);
        Assert.All(byIssuer.Items, prn => Assert.Equal("Northern Recycling Plc", prn.IssuedByOrg));
    }

    [Fact]
    public async Task Search_UnknownOrganisation_ReturnsAnEmptyPage()
    {
        var page = await Search(Guid.NewGuid().ToString(), "filterBy=awaiting-all");

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
    }

    [Fact]
    public async Task Search_WithoutOrganisationHeader_ReturnsUnauthorised()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Base}/search", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(2022)]
    [InlineData(2051)]
    public async Task Search_AcceptableIntoYearOutsideTheBackendRange_ReturnsBadRequest(int year)
    {
        using var client = factory.CreateClient();

        var response = await client.SendAsync(
            Get($"{Base}/search?acceptableIntoYear={year}", WasteOrganisationStubIds.LargeProducer),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("page=0&pageSize=0", 1, 1, 1)]
    [InlineData("page=2147483647&pageSize=100", 2147483647, 100, 0)]
    public async Task Search_PagingEdges_ReturnTheUsedValuesAndNeverThrow(
        string query,
        int expectedPage,
        int expectedPageSize,
        int expectedItems
    )
    {
        var page = await Search(WasteOrganisationStubIds.LargeProducer, query);

        Assert.Equal(expectedPage, page.CurrentPage);
        Assert.Equal(expectedPageSize, page.PageSize);
        Assert.Equal(expectedItems, page.Items.Count);
    }

    [Fact]
    public async Task GetPrn_ReturnsTheNote_OrNotFoundForAnotherId()
    {
        var first = (await Search(WasteOrganisationStubIds.LargeProducer, "pageSize=1")).Items.Single();
        using var client = factory.CreateClient();

        var found = await client.SendAsync(Get($"{Base}/{first.ExternalId}", WasteOrganisationStubIds.LargeProducer), TestContext.Current.CancellationToken);
        var missing = await client.SendAsync(Get($"{Base}/{Guid.NewGuid()}", WasteOrganisationStubIds.LargeProducer), TestContext.Current.CancellationToken);
        var otherOrganisation = await client.SendAsync(Get($"{Base}/{first.ExternalId}", WasteOrganisationStubIds.ComplianceScheme), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var prn = await found.Content.ReadFromJsonAsync<Prn>(TestContext.Current.CancellationToken);
        Assert.Equal(first.PrnNumber, prn!.PrnNumber);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherOrganisation.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_AcceptsAndRejectsAwaitingNotes_AndRefusesASecondChange()
    {
        var awaiting = (await Search(WasteOrganisationStubIds.ComplianceScheme, "filterBy=awaiting-all&pageSize=2")).Items;
        using var client = factory.CreateClient();

        var accepted = await PostStatus(client, [new(awaiting[0].ExternalId, "ACCEPTED", "2026", "AWAITINGACCEPTANCE")]);
        var rejected = await PostStatus(client, [new(awaiting[1].ExternalId, "REJECTED", null, null)]);
        var again = await PostStatus(client, [new(awaiting[0].ExternalId, "REJECTED", null, "AWAITINGACCEPTANCE")]);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var acceptedNote = await (await client.SendAsync(Get($"{Base}/{awaiting[0].ExternalId}", WasteOrganisationStubIds.ComplianceScheme), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<Prn>(TestContext.Current.CancellationToken);
        Assert.Equal("ACCEPTED", acceptedNote!.PrnStatus);
        Assert.Equal("2026", acceptedNote.ObligationYear);
        Assert.NotNull(acceptedNote.StatusUpdatedOn);

        var stillAwaiting = await Search(WasteOrganisationStubIds.ComplianceScheme, "filterBy=awaiting-all&pageSize=10000");
        Assert.DoesNotContain(stillAwaiting.Items, prn => prn.ExternalId == awaiting[0].ExternalId || prn.ExternalId == awaiting[1].ExternalId);
    }

    [Fact]
    public async Task UpdateStatus_ChangesNothing_WhenAnyNoteInTheBatchFails()
    {
        var awaiting = (await Search(WasteOrganisationStubIds.ComplianceScheme, "filterBy=awaiting-all&page=3&pageSize=1")).Items.Single();
        using var client = factory.CreateClient();

        var unknown = await PostStatus(client, [new(awaiting.ExternalId, "ACCEPTED", null, null), new(Guid.NewGuid(), "ACCEPTED", null, null)]);
        var duplicate = await PostStatus(client, [new(awaiting.ExternalId, "ACCEPTED", null, null), new(awaiting.ExternalId, "ACCEPTED", null, null)]);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var note = await (await client.SendAsync(Get($"{Base}/{awaiting.ExternalId}", WasteOrganisationStubIds.ComplianceScheme), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<Prn>(TestContext.Current.CancellationToken);
        Assert.Equal("AWAITINGACCEPTANCE", note!.PrnStatus);
    }

    [Fact]
    public async Task UpdateStatus_RepeatingTheSameStatusWithoutAnExpectedStatus_Succeeds()
    {
        var note = (await Search(WasteOrganisationStubIds.ComplianceScheme, "filterBy=awaiting-all&page=5&pageSize=1")).Items.Single();
        using var client = factory.CreateClient();

        var first = await PostStatus(client, [new(note.ExternalId, "REJECTED", null, null)]);
        var repeat = await PostStatus(client, [new(note.ExternalId, "REJECTED", null, null)]);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_ChangingAnAcceptedNoteWithoutAnExpectedStatus_IsAConflict()
    {
        var note = (await Search(WasteOrganisationStubIds.ComplianceScheme, "filterBy=awaiting-all&page=6&pageSize=1")).Items.Single();
        using var client = factory.CreateClient();

        await PostStatus(client, [new(note.ExternalId, "ACCEPTED", null, null)]);
        var change = await PostStatus(client, [new(note.ExternalId, "REJECTED", null, null)]);

        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
        Assert.Contains("cannot be accepted or rejected", await change.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateStatus_ABatchWithOneConflict_LeavesTheOtherNotesUnchanged()
    {
        var notes = (await Search(WasteOrganisationStubIds.ComplianceScheme, "filterBy=awaiting-all&page=4&pageSize=2")).Items;
        using var client = factory.CreateClient();
        await PostStatus(client, [new(notes[1].ExternalId, "ACCEPTED", null, null)]);

        var batch = await PostStatus(
            client,
            [new(notes[0].ExternalId, "ACCEPTED", null, "AWAITINGACCEPTANCE"), new(notes[1].ExternalId, "ACCEPTED", null, "AWAITINGACCEPTANCE")]
        );

        Assert.Equal(HttpStatusCode.Conflict, batch.StatusCode);
        Assert.Contains("is no longer AWAITINGACCEPTANCE", await batch.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var untouched = await (await client.SendAsync(Get($"{Base}/{notes[0].ExternalId}", WasteOrganisationStubIds.ComplianceScheme), TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<Prn>(TestContext.Current.CancellationToken);
        Assert.Equal("AWAITINGACCEPTANCE", untouched!.PrnStatus);
    }

    [Fact]
    public async Task UpdateStatus_TwoParallelAcceptsOfTheSameNote_OneSucceedsAndOneConflicts()
    {
        var note = (await Search(WasteOrganisationStubIds.ComplianceScheme, "filterBy=awaiting-all&page=7&pageSize=1")).Items.Single();
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();

        var responses = await Task.WhenAll(
            PostStatus(first, [new(note.ExternalId, "ACCEPTED", null, "AWAITINGACCEPTANCE")]),
            PostStatus(second, [new(note.ExternalId, "ACCEPTED", null, "AWAITINGACCEPTANCE")])
        );

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateStatus_WithoutUserHeaderOrWithAnUnknownStatus_ReturnsBadRequest()
    {
        using var client = factory.CreateClient();
        using var noUser = new HttpRequestMessage(HttpMethod.Post, $"{Base}/status")
        {
            Content = JsonContent.Create(new[] { new StatusUpdate(Guid.NewGuid(), "ACCEPTED", null, null) }),
        };
        noUser.Headers.Add(OrganisationHeader, WasteOrganisationStubIds.ComplianceScheme);

        var missingUser = await client.SendAsync(noUser, TestContext.Current.CancellationToken);
        var unknownStatus = await PostStatus(client, [new(Guid.NewGuid(), "APPROVED", null, null)]);

        Assert.Equal(HttpStatusCode.BadRequest, missingUser.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownStatus.StatusCode);
    }

    private async Task<SearchPage> Search(string organisationId, string query)
    {
        using var client = factory.CreateClient();
        var response = await client.SendAsync(Get($"{Base}/search?{query}", organisationId), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SearchPage>(TestContext.Current.CancellationToken))!;
    }

    private static HttpRequestMessage Get(string path, string organisationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(OrganisationHeader, organisationId);

        return request;
    }

    private static async Task<HttpResponseMessage> PostStatus(HttpClient client, StatusUpdate[] updates)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/status") { Content = JsonContent.Create(updates) };
        request.Headers.Add(OrganisationHeader, WasteOrganisationStubIds.ComplianceScheme);
        request.Headers.Add(UserHeader, Guid.NewGuid().ToString());

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record StatusUpdate(Guid PrnId, string Status, string? ObligationYear, string? ExpectedStatus);

    private sealed record SearchPage(IReadOnlyList<Prn> Items, int CurrentPage, int TotalItems, int PageSize);

    private sealed record Prn(
        Guid ExternalId,
        string PrnNumber,
        string PrnStatus,
        int PrnStatusId,
        int TonnageValue,
        string MaterialName,
        string IssuedByOrg,
        bool DecemberWaste,
        string ObligationYear,
        DateTime? StatusUpdatedOn
    );
}
