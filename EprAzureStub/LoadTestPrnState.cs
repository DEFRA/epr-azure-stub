using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace EprAzureStub;

/// <summary>
/// Generated PRNs and PERNs for every organisation the stub knows: the load-test allocations and the
/// static fixtures. Each organisation's notes are generated on first use from its ID, so they are the
/// same on every request, and their statuses change through <see cref="UpdateStatuses"/>. Everything
/// is cleared when a new run lease starts, so each run starts with the same notes awaiting acceptance.
/// </summary>
public sealed class LoadTestPrnState(
    LoadTestSessionState loadTestSessionState,
    TimeProvider timeProvider,
    IOptions<LoadTestOptions> options
)
{
    public const string AwaitingAcceptance = "AWAITINGACCEPTANCE";
    public const string Accepted = "ACCEPTED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";

    // Material names as the PRN common backend stores them (PrnConstants.Materials).
    public static readonly IReadOnlyList<string> Materials =
    [
        "Aluminium",
        "Glass Other",
        "Glass Re-melt",
        "Paper/board",
        "Plastic",
        "Steel",
        "Wood",
    ];

    private static readonly IReadOnlyDictionary<string, int> StatusIds = new Dictionary<string, int>
    {
        [Accepted] = 1,
        [Rejected] = 2,
        [Cancelled] = 3,
        [AwaitingAcceptance] = 4,
    };

    private static readonly string[] Issuers =
    [
        "Green Reprocessing Ltd",
        "Northern Recycling Plc",
        "Severn Materials Recovery",
        "Highland Glass Works",
        "Coastal Export Services",
        "Midlands Metal Recyclers",
    ];

    private static readonly string[] Agencies =
    [
        "Environment Agency",
        "Natural Resources Wales",
        "Scottish Environment Protection Agency",
        "Northern Ireland Environment Agency",
    ];

    private static readonly HashSet<Guid> FixtureOrganisationIds =
    [
        WasteOrganisationStubIds.LargeProducerGuid,
        WasteOrganisationStubIds.ComplianceSchemeGuid,
        WasteOrganisationStubIds.SeededDirectProducerOrganisationGuid,
        WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid,
        WasteOrganisationStubIds.SeededComplianceSchemeExternalIdGuid,
    ];

    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, List<StubPrn>> _prnsByOrganisation = [];
    private Guid? _runId;

    public PrnResponse? GetPrn(Guid organisationId, Guid prnId)
    {
        lock (_sync)
        {
            return GetOrCreate(organisationId)?.SingleOrDefault(prn => prn.ExternalId == prnId)?.ToResponse();
        }
    }

    /// <summary>
    /// The PRN common backend's search: text in the number or issuer, a status or awaiting-material filter,
    /// the years a note can be accepted into, then the sort (ties by number) and the page. The total is
    /// counted before paging. Null when the stub doesn't know the organisation.
    /// </summary>
    public PrnSearchResponse? Search(Guid organisationId, PrnSearchQuery query)
    {
        lock (_sync)
        {
            var prns = GetOrCreate(organisationId);

            if (prns is null)
            {
                return null;
            }

            var now = timeProvider.GetUtcNow();
            IEnumerable<StubPrn> matches = prns;

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                matches = matches.Where(prn =>
                    prn.PrnNumber.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                    || prn.IssuedByOrg.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                );
            }

            matches = matches.Where(Filter(query.FilterBy));

            if (query.AcceptableIntoYear is { } year)
            {
                matches = matches.Where(prn =>
                    int.TryParse(prn.ObligationYear, CultureInfo.InvariantCulture, out var obligationYear)
                    && PrnAcceptanceYearRule.IsAcceptableInto(year, obligationYear, prn.DecemberWaste, now)
                );
            }

            var matched = matches.ToList();
            var (descending, key) = Sort(query.SortBy);
            var ordered = descending
                ? matched.OrderByDescending(key).ThenBy(prn => prn.PrnNumber, StringComparer.Ordinal)
                : matched.OrderBy(key).ThenBy(prn => prn.PrnNumber, StringComparer.Ordinal);
            // The endpoint passes a page and page size of at least 1; a page past the end is empty.
            var skip = ((long)query.Page - 1) * query.PageSize;
            var items = skip >= matched.Count
                ? []
                : ordered.Skip((int)skip).Take(query.PageSize).Select(prn => prn.ToResponse()).ToList();

            return new PrnSearchResponse(
                items,
                query.Page,
                matched.Count,
                query.PageSize,
                query.Search,
                query.FilterBy,
                query.SortBy
            );
        }
    }

    private static Func<StubPrn, bool> Filter(string? filterBy) =>
        filterBy switch
        {
            "accepted-all" => prn => prn.Status == Accepted,
            "rejected-all" => prn => prn.Status == Rejected,
            "cancelled-all" => prn => prn.Status == Cancelled,
            "awaiting-all" => prn => prn.Status == AwaitingAcceptance,
            "awaiting-aluminium" => Awaiting("Aluminium"),
            "awaiting-glassother" => Awaiting("Glass Other"),
            "awaiting-glassremelt" => Awaiting("Glass Re-melt"),
            "awaiting-paperfiber" => Awaiting("Paper/board"),
            "awaiting-plastic" => Awaiting("Plastic"),
            "awaiting-steel" => Awaiting("Steel"),
            "awaiting-wood" => Awaiting("Wood"),
            _ => _ => true,
        };

    private static Func<StubPrn, bool> Awaiting(string material) =>
        prn => prn.Status == AwaitingAcceptance && prn.MaterialName == material;

    private static (bool Descending, Func<StubPrn, IComparable> Key) Sort(string? sortBy) =>
        sortBy switch
        {
            "date-issued-asc" => (false, prn => prn.IssueDate),
            "tonnage-desc" => (true, prn => prn.TonnageValue),
            "tonnage-asc" => (false, prn => prn.TonnageValue),
            "issued-by-desc" => (true, prn => prn.IssuedByOrg),
            "issued-by-asc" => (false, prn => prn.IssuedByOrg),
            "december-waste-desc" => (true, prn => prn.DecemberWaste),
            "material-desc" => (true, prn => prn.MaterialName),
            "material-asc" => (false, prn => prn.MaterialName),
            _ => (true, prn => prn.IssueDate),
        };

    /// <summary>
    /// Applies the whole batch or none of it, as the PRN common backend does: a PRN named twice or one
    /// that can no longer make the change is a conflict; a PRN the organisation doesn't have is not found.
    /// </summary>
    public PrnStatusUpdateResult UpdateStatuses(Guid organisationId, IReadOnlyList<PrnStatusUpdateRequest> updates)
    {
        if (updates.Select(update => update.PrnId).Distinct().Count() != updates.Count)
        {
            return PrnStatusUpdateResult.Conflict("A batch cannot name the same PRN more than once. No PRNs were updated.");
        }

        lock (_sync)
        {
            var prns = GetOrCreate(organisationId);

            if (prns is null || prns.Count == 0)
            {
                return PrnStatusUpdateResult.NotFound($"No record present for organisation {organisationId}");
            }

            var missing = updates.Select(update => update.PrnId).Except(prns.Select(prn => prn.ExternalId)).ToList();

            if (missing.Count > 0)
            {
                return PrnStatusUpdateResult.NotFound($"{string.Join(",", missing)} Prns doesn't exists in system");
            }

            foreach (var update in updates)
            {
                var prn = prns.Single(candidate => candidate.ExternalId == update.PrnId);

                // The backend's two rules and messages: with an expected status the note must still be in it;
                // without one it must be awaiting acceptance or already in the requested status.
                if (update.ExpectedStatus is { } expected && prn.Status != expected)
                {
                    return PrnStatusUpdateResult.Conflict(
                        $"{update.PrnId} is no longer {expected} and cannot be updated. Please refresh and try again"
                    );
                }

                if (update.ExpectedStatus is null && prn.Status != AwaitingAcceptance && prn.Status != update.Status)
                {
                    return PrnStatusUpdateResult.Conflict(
                        $"{update.PrnId} cannot be accepted or rejected please refresh and try again"
                    );
                }
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;

            foreach (var update in updates)
            {
                var prn = prns.Single(candidate => candidate.ExternalId == update.PrnId);
                prn.Status = update.Status;
                prn.StatusUpdatedOn = now;
                prn.LastUpdatedDate = now;

                if (!string.IsNullOrEmpty(update.ObligationYear))
                {
                    prn.ObligationYear = update.ObligationYear;
                }
            }

            return PrnStatusUpdateResult.Updated;
        }
    }

    public static bool IsKnownStatus(string status) => StatusIds.ContainsKey(status);

    private List<StubPrn>? GetOrCreate(Guid organisationId)
    {
        // A new run starts from fresh notes. A lease that ends or expires mid-run (a missed heartbeat) keeps
        // them, so notes already accepted or rejected don't come back as awaiting while users are running.
        if (loadTestSessionState.GetActiveRunId() is { } activeRunId && activeRunId != _runId)
        {
            _prnsByOrganisation.Clear();
            _runId = activeRunId;
        }

        if (_prnsByOrganisation.TryGetValue(organisationId, out var existing))
        {
            return existing;
        }

        string recipientName;

        if (loadTestSessionState.TryGetAllocationForOrganisation(organisationId, out var allocation))
        {
            recipientName = allocation.ComplianceSchemeName ?? allocation.OrganisationName;
        }
        else if (FixtureOrganisationIds.Contains(organisationId))
        {
            recipientName = "Stub organisation";
        }
        else
        {
            return null;
        }

        var prns = Generate(organisationId, recipientName, options.Value.PrnsPerOrganisation, timeProvider.GetUtcNow());
        _prnsByOrganisation[organisationId] = prns;

        return prns;
    }

    /// <summary>
    /// Every material, PRNs and PERNs, mostly awaiting acceptance with some accepted, rejected and
    /// cancelled, a few from the previous compliance year and some December waste. In the December
    /// window some December waste is this year's, so it can be accepted into this year or the next.
    /// </summary>
    internal static List<StubPrn> Generate(Guid organisationId, string recipientName, int count, DateTimeOffset now)
    {
        var complianceYear = PrnAcceptanceYearRule.ComplianceYear(now);
        var inDecemberWindow = now >= new DateTimeOffset(complianceYear, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var seed = BitConverter.ToInt32(SHA256.HashData(organisationId.ToByteArray()), 0) & int.MaxValue;
        var prns = new List<StubPrn>(count);

        for (var index = 0; index < count; index++)
        {
            var isExport = index % 3 == 2;
            var decemberWaste = index % 8 == 5;
            var obligationYear = decemberWaste
                ? (inDecemberWindow && index % 16 == 5 ? complianceYear : complianceYear - 1)
                : (index % 12 == 11 ? complianceYear - 1 : complianceYear);
            var status = (index % 10) switch
            {
                7 => Accepted,
                8 => Rejected,
                9 => Cancelled,
                _ => AwaitingAcceptance,
            };
            var issueDate = decemberWaste
                ? NotAfter(new DateTime(obligationYear, 12, 1 + (index % 28), 10, 0, 0, DateTimeKind.Utc), now)
                : IssueDateWithinYear(obligationYear, index, now);
            var created = issueDate.AddHours(1);
            // 7919 is prime and shares no factor with 900 000, so up to 900 000 notes get different numbers.
            // Long arithmetic: the seed can be as large as int.MaxValue.
            var number = (((long)seed + (long)index * 7919) % 900_000 + 100_000).ToString(CultureInfo.InvariantCulture);
            var yearSuffix = (obligationYear % 100).ToString("00", CultureInfo.InvariantCulture);

            prns.Add(
                new StubPrn
                {
                    ExternalId = DeterministicGuid($"{organisationId:D}:{index}"),
                    PrnNumber = $"{(isExport ? "EX" : "ER")}{yearSuffix}{number}",
                    OrganisationId = organisationId,
                    OrganisationName = recipientName,
                    MaterialName = Materials[index % Materials.Count],
                    TonnageValue = 5 + ((seed / 7 + index * 37) % 495),
                    IsExport = isExport,
                    DecemberWaste = decemberWaste,
                    ObligationYear = obligationYear.ToString(CultureInfo.InvariantCulture),
                    AccreditationYear = obligationYear.ToString(CultureInfo.InvariantCulture),
                    IssueDate = issueDate,
                    CreatedOn = created,
                    LastUpdatedDate = created,
                    StatusUpdatedOn = status == AwaitingAcceptance ? null : created,
                    Status = status,
                    IssuedByOrg = Issuers[index % Issuers.Length],
                    ReprocessorExporterAgency = Agencies[index % Agencies.Length],
                    AccreditationNumber = $"{(isExport ? "EX" : "ER")}-ACC-{((long)seed + index) % 100_000:00000}",
                    ProcessToBeUsed = $"R{3 + index % 3}",
                    ReprocessingSite = isExport ? null : $"Unit {1 + index % 40}, Industrial Estate",
                    PrnSignatory = "Stub Signatory",
                    PrnSignatoryPosition = "Director",
                    IssuerNotes = index % 4 == 0 ? "Generated by the Azure stub for load testing." : null,
                }
            );
        }

        return prns;
    }

    private static DateTime IssueDateWithinYear(int obligationYear, int index, DateTimeOffset now) =>
        NotAfter(new DateTime(obligationYear, 2, 1, 9, 0, 0, DateTimeKind.Utc).AddDays(index * 3 % 300), now);

    // A note is never issued in the future: anything later than an hour ago is moved back to it.
    private static DateTime NotAfter(DateTime issueDate, DateTimeOffset now)
    {
        var latest = now.UtcDateTime.AddHours(-1);

        return issueDate > latest ? latest : issueDate;
    }

    private static Guid DeterministicGuid(string value) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    internal sealed class StubPrn
    {
        public required Guid ExternalId { get; init; }
        public required string PrnNumber { get; init; }
        public required Guid OrganisationId { get; init; }
        public required string OrganisationName { get; init; }
        public required string MaterialName { get; init; }
        public required int TonnageValue { get; init; }
        public required bool IsExport { get; init; }
        public required bool DecemberWaste { get; init; }
        public required string ObligationYear { get; set; }
        public required string AccreditationYear { get; init; }
        public required DateTime IssueDate { get; init; }
        public required DateTime CreatedOn { get; init; }
        public required DateTime LastUpdatedDate { get; set; }
        public DateTime? StatusUpdatedOn { get; set; }
        public required string Status { get; set; }
        public required string IssuedByOrg { get; init; }
        public required string ReprocessorExporterAgency { get; init; }
        public required string AccreditationNumber { get; init; }
        public required string ProcessToBeUsed { get; init; }
        public string? ReprocessingSite { get; init; }
        public string? PrnSignatory { get; init; }
        public string? PrnSignatoryPosition { get; init; }
        public string? IssuerNotes { get; init; }

        public PrnResponse ToResponse() =>
            new(
                ExternalId,
                PrnNumber,
                OrganisationId,
                OrganisationName,
                ReprocessorExporterAgency,
                StatusIds[Status],
                Status,
                TonnageValue,
                MaterialName,
                IssuerNotes,
                PrnSignatory,
                PrnSignatoryPosition,
                IssueDate,
                ProcessToBeUsed,
                DecemberWaste,
                IssuedByOrg,
                AccreditationNumber,
                ReprocessingSite,
                AccreditationYear,
                ObligationYear,
                CreatedOn,
                LastUpdatedDate,
                StatusUpdatedOn,
                IsExport
            );
    }
}

/// <summary>The PRN common backend's PrnDto, as Waste Obligations reads it.</summary>
public sealed record PrnResponse(
    Guid ExternalId,
    string PrnNumber,
    Guid OrganisationId,
    string OrganisationName,
    string ReprocessorExporterAgency,
    int PrnStatusId,
    string PrnStatus,
    int TonnageValue,
    string MaterialName,
    string? IssuerNotes,
    string? PrnSignatory,
    string? PrnSignatoryPosition,
    DateTime IssueDate,
    string ProcessToBeUsed,
    bool DecemberWaste,
    string IssuedByOrg,
    string AccreditationNumber,
    string? ReprocessingSite,
    string AccreditationYear,
    string ObligationYear,
    DateTime CreatedOn,
    DateTime LastUpdatedDate,
    DateTime? StatusUpdatedOn,
    bool IsExport
);

public sealed record PrnSearchQuery(
    int Page,
    int PageSize,
    string? Search,
    string? FilterBy,
    string? SortBy,
    int? AcceptableIntoYear
);

/// <summary>The PRN common backend's PaginatedResponseDto of PrnDto.</summary>
public sealed record PrnSearchResponse(
    IReadOnlyList<PrnResponse> Items,
    int CurrentPage,
    int TotalItems,
    int PageSize,
    string? SearchTerm,
    string? FilterBy,
    string? SortBy
);

public sealed record PrnStatusUpdateRequest(Guid PrnId, string Status, string? ObligationYear, string? ExpectedStatus);

public sealed record PrnStatusUpdateResult(int StatusCode, string? Detail)
{
    public static readonly PrnStatusUpdateResult Updated = new(StatusCodes.Status200OK, null);

    public static PrnStatusUpdateResult NotFound(string detail) => new(StatusCodes.Status404NotFound, detail);

    public static PrnStatusUpdateResult Conflict(string detail) => new(StatusCodes.Status409Conflict, detail);
}
