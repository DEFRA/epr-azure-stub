namespace EprAzureStub;

public sealed class LoadTestSessionState
{
    public const string SessionHeaderName = "X-EPR-Load-Test-Session";

    private const int MaximumUserCount = 1000;
    public const int MinimumLeaseDurationSeconds = 60;
    public const int MaximumLeaseDurationSeconds = 86_400;
    public static readonly Guid ComplianceSchemeUserId = Guid.Parse(
        "579c319d-d552-47a2-bf4c-5a125a3183bc"
    );
    public static readonly Guid DirectProducerUserId = Guid.Parse(
        "79d0deab-c22d-4c30-8082-508ff8dc1bd7"
    );

    private readonly object _sync = new();
    private LoadTestRunLease? _runLease;
    private LoadTestSession? _session;

    public static bool IsSupportedUser(Guid userId)
    {
        return userId == ComplianceSchemeUserId || userId == DirectProducerUserId;
    }

    public LoadTestRunLeaseAcquireResult AcquireRunLease(
        Guid runId,
        string profile,
        int leaseDurationSeconds
    )
    {
        var now = TimeProvider.System.GetUtcNow();
        var expiresAtUtc = now.AddSeconds(leaseDurationSeconds);

        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(now);

            if (_runLease is null)
            {
                _runLease = new(runId, profile, expiresAtUtc);
                return new(true, false, _runLease.ToResponse());
            }

            if (_runLease.RunId == runId)
            {
                _runLease = _runLease with { ExpiresAtUtc = expiresAtUtc };
                return new(true, true, _runLease.ToResponse());
            }

            return new(false, false, _runLease.ToResponse());
        }
    }

    public bool RenewRunLease(Guid runId, int leaseDurationSeconds)
    {
        var now = TimeProvider.System.GetUtcNow();

        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(now);

            if (_runLease?.RunId != runId)
            {
                return false;
            }

            _runLease = _runLease with { ExpiresAtUtc = now.AddSeconds(leaseDurationSeconds) };
            return true;
        }
    }

    public LoadTestRunLeaseReleaseResult ReleaseRunLease(Guid runId)
    {
        var now = TimeProvider.System.GetUtcNow();

        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(now);

            if (_runLease is null)
            {
                return LoadTestRunLeaseReleaseResult.Released;
            }

            if (_runLease.RunId != runId)
            {
                return LoadTestRunLeaseReleaseResult.HeldByAnotherRun;
            }

            _runLease = null;
            if (_session?.RunId == runId)
            {
                _session = null;
            }

            return LoadTestRunLeaseReleaseResult.Released;
        }
    }

    public LoadTestSessionInitialisationResult Initialise(
        Guid runId,
        int directProducerUserCount,
        int complianceSchemeUserCount
    )
    {
        var totalUserCount = directProducerUserCount + complianceSchemeUserCount;

        if (
            directProducerUserCount < 0
            || complianceSchemeUserCount < 0
            || totalUserCount is < 1 or > MaximumUserCount
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalUserCount),
                $"The total user count must be between 1 and {MaximumUserCount}."
            );
        }

        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(TimeProvider.System.GetUtcNow());

            if (_runLease?.RunId != runId)
            {
                return new(null, false);
            }

            if (_session?.RunId == runId)
            {
                return new(_session.ToResponse(), true);
            }

            var session = new LoadTestSession(
                runId,
                totalUserCount,
                directProducerUserCount,
                complianceSchemeUserCount,
                CreateAllocations(directProducerUserCount, complianceSchemeUserCount)
            );

            _session = session;
            return new(session.ToResponse(), false);
        }
    }

    public bool TryGetAllocationForUser(
        Guid userId,
        string sessionKey,
        out LoadTestOrganisationAllocation allocation
    )
    {
        allocation = default!;

        if (!TryParseSessionKey(sessionKey, out var runId, out var userIndex))
        {
            return false;
        }

        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(TimeProvider.System.GetUtcNow());

            if (_session?.RunId != runId)
            {
                return false;
            }

            allocation = _session.Allocations.SingleOrDefault(candidate =>
                candidate.UserId == userId && candidate.UserIndex == userIndex
            )!;

            return allocation is not null;
        }
    }

    public bool TryGetComplianceSchemeForOperator(
        Guid operatorOrganisationId,
        string sessionKey,
        out LoadTestOrganisationAllocation allocation
    )
    {
        allocation = default!;

        if (!TryParseSessionKey(sessionKey, out var runId, out var userIndex))
        {
            return false;
        }

        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(TimeProvider.System.GetUtcNow());

            if (_session?.RunId != runId)
            {
                return false;
            }

            allocation = _session.Allocations.SingleOrDefault(candidate =>
                candidate.UserId == ComplianceSchemeUserId
                && candidate.UserIndex == userIndex
                && candidate.OperatorOrganisationId == operatorOrganisationId
            )!;

            return allocation is not null;
        }
    }

    public bool TryGetAllocationForOrganisation(
        Guid organisationId,
        out LoadTestOrganisationAllocation allocation
    )
    {
        allocation = default!;

        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(TimeProvider.System.GetUtcNow());

            if (_session is null)
            {
                return false;
            }

            allocation = _session.Allocations.SingleOrDefault(candidate =>
                candidate.OrganisationId == organisationId
            )!;

            return allocation is not null;
        }
    }

    private static bool TryParseSessionKey(string sessionKey, out Guid runId, out int userIndex)
    {
        runId = Guid.Empty;
        userIndex = -1;

        var separatorIndex = sessionKey.LastIndexOf(':');

        if (separatorIndex <= 0 || separatorIndex == sessionKey.Length - 1)
        {
            return false;
        }

        return Guid.TryParse(sessionKey[..separatorIndex], out runId)
            && int.TryParse(sessionKey[(separatorIndex + 1)..], out userIndex)
            && userIndex >= 0;
    }

    private List<LoadTestOrganisationAllocation> CreateAllocations(
        int directProducerUserCount,
        int complianceSchemeUserCount
    )
    {
        var allocations = new List<LoadTestOrganisationAllocation>();

        for (var userIndex = 0; userIndex < directProducerUserCount; userIndex++)
        {
            var displayNumber = userIndex + 1;

            allocations.Add(
                new(
                    DirectProducerUserId,
                    userIndex,
                    Guid.NewGuid(),
                    null,
                    $"POP QUEST LTD {displayNumber}",
                    null
                )
            );
        }

        for (var userIndex = 0; userIndex < complianceSchemeUserCount; userIndex++)
        {
            var displayNumber = userIndex + 1;

            allocations.Add(
                new(
                    ComplianceSchemeUserId,
                    userIndex,
                    Guid.NewGuid(),
                    // The signed-in CSO session owns this seeded operator organisation.
                    // Keep it stable so the frontend does not need a load-test-specific
                    // session override to resolve its organisation number.
                    WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid,
                    $"Organisation Name {displayNumber}",
                    $"Compliance Scheme Name {displayNumber}"
                )
            );
        }

        return allocations;
    }

    private void ExpireRunLeaseIfNeeded(DateTimeOffset now)
    {
        if (_runLease is not null && _runLease.ExpiresAtUtc <= now)
        {
            _runLease = null;
            _session = null;
        }
    }

    private sealed record LoadTestRunLease(Guid RunId, string Profile, DateTimeOffset ExpiresAtUtc)
    {
        public LoadTestRunLeaseResponse ToResponse() => new(RunId, Profile, ExpiresAtUtc);
    }

    private sealed record LoadTestSession(
        Guid RunId,
        int UserCount,
        int DirectProducerUserCount,
        int ComplianceSchemeUserCount,
        IReadOnlyList<LoadTestOrganisationAllocation> Allocations
    )
    {
        public LoadTestSessionResponse ToResponse()
        {
            return new(
                RunId,
                UserCount,
                DirectProducerUserCount,
                ComplianceSchemeUserCount,
                [
                    CreateUserAllocationsResponse(DirectProducerUserId),
                    CreateUserAllocationsResponse(ComplianceSchemeUserId),
                ]
            );
        }

        private LoadTestUserAllocationsResponse CreateUserAllocationsResponse(Guid userId)
        {
            return new(
                userId,
                Allocations
                    .Where(allocation => allocation.UserId == userId)
                    .OrderBy(allocation => allocation.UserIndex)
                    .ToList()
            );
        }
    }
}

public sealed record LoadTestRunLeaseAcquireResult(
    bool Acquired,
    bool Renewed,
    LoadTestRunLeaseResponse ActiveLease
);

public enum LoadTestRunLeaseReleaseResult
{
    Released,
    HeldByAnotherRun,
}

public sealed record LoadTestSessionInitialisationResult(
    LoadTestSessionResponse? Response,
    bool Existing
);

public sealed record LoadTestRunLeaseRequest(Guid RunId, string Profile, int LeaseDurationSeconds);

public sealed record LoadTestRunLeaseResponse(
    Guid RunId,
    string Profile,
    DateTimeOffset ExpiresAtUtc
);

public sealed record LoadTestSessionInitialisationRequest(
    Guid RunId,
    int DirectProducerUserCount,
    int ComplianceSchemeUserCount
);

public sealed record LoadTestSessionResponse(
    Guid RunId,
    int UserCount,
    int DirectProducerUserCount,
    int ComplianceSchemeUserCount,
    IReadOnlyList<LoadTestUserAllocationsResponse> Users
);

public sealed record LoadTestUserAllocationsResponse(
    Guid UserId,
    IReadOnlyList<LoadTestOrganisationAllocation> Allocations
);

public sealed record LoadTestOrganisationAllocation(
    Guid UserId,
    int UserIndex,
    Guid OrganisationId,
    Guid? OperatorOrganisationId,
    string OrganisationName,
    string? ComplianceSchemeName
)
{
    public bool IsComplianceScheme => OperatorOrganisationId.HasValue;
}
