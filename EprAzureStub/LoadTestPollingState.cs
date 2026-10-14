namespace EprAzureStub;

public sealed partial class LoadTestSessionState
{
    private PollingCatalogue? _polling;

    public LoadTestPollingResponse? InitialisePolling(LoadTestPollingRequest request)
    {
        var years = request.RegistrationYears?.Distinct().Order().ToArray() ?? [];
        var count = (long)request.DirectProducerUserCount + request.ComplianceSchemeUserCount;
        var growthInterval = request.OrganisationGrowthIntervalSeconds ?? request.ChangeIntervalSeconds ?? 1800;
        var obligationInterval = request.ObligationChangeIntervalSeconds ?? request.ChangeIntervalSeconds ?? 1800;
        if (request.RunId == Guid.Empty || request.DirectProducerUserCount < 0
            || request.ComplianceSchemeUserCount < 0 || count < 1 || count > MaximumOrganisationCount
            || years.Length == 0 || years.Any(year => year is < 2023 or > 2050)
            || growthInterval is < 1 or > 86400 || obligationInterval is < 1 or > 86400
            || request.MaximumAdditionalOrganisations < 0
            || count + request.MaximumAdditionalOrganisations > MaximumOrganisationCount)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        request = request with { RegistrationYears = years, ChangeIntervalSeconds = null,
            OrganisationGrowthIntervalSeconds = growthInterval, ObligationChangeIntervalSeconds = obligationInterval };
        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(timeProvider.GetUtcNow());
            if (_runLease?.RunId != request.RunId)
            {
                return null;
            }
            if (_polling is not null)
            {
                var previous = _polling.Request;
                if (previous.DirectProducerUserCount != request.DirectProducerUserCount
                    || previous.ComplianceSchemeUserCount != request.ComplianceSchemeUserCount
                    || previous.OrganisationGrowthIntervalSeconds != request.OrganisationGrowthIntervalSeconds
                    || previous.ObligationChangeIntervalSeconds != request.ObligationChangeIntervalSeconds
                    || previous.MaximumAdditionalOrganisations != request.MaximumAdditionalOrganisations
                    || !previous.RegistrationYears.SequenceEqual(years))
                {
                    return null;
                }
                return GetPollingStatus(request.RunId);
            }
            if (_session is not null && (_session.DirectProducerUserCount != request.DirectProducerUserCount
                || _session.ComplianceSchemeUserCount != request.ComplianceSchemeUserCount))
            {
                return null;
            }
            var session = Initialise(request.RunId, request.DirectProducerUserCount, request.ComplianceSchemeUserCount).Response!;
            _polling = new(request, timeProvider.GetUtcNow(), session.Users.SelectMany(user => user.Allocations).ToList());
            return GetPollingStatus(request.RunId);
        }
    }

    public LoadTestPollingResponse? GetPollingStatus(Guid runId)
    {
        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(timeProvider.GetUtcNow());
            if (_polling?.Request.RunId != runId)
            {
                return null;
            }
            GrowCatalogue();
            return new(runId, _polling.StartedAtUtc, _polling.Request.RegistrationYears,
                _polling.Request.DirectProducerUserCount + _polling.Request.ComplianceSchemeUserCount,
                _polling.Allocations.ToArray());
        }
    }

    public IReadOnlyList<LoadTestOrganisationAllocation> GetPollingAllocations()
    {
        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(timeProvider.GetUtcNow());
            GrowCatalogue();
            return _polling?.Allocations.ToArray() ?? [];
        }
    }

    public IReadOnlyList<int> GetRegistrationYears()
    {
        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(timeProvider.GetUtcNow());
            return _polling?.Request.RegistrationYears.ToArray() ?? Enumerable.Range(2025, 6).ToArray();
        }
    }

    public bool TryGetPrnPhase(Guid organisationId, int year, out int phase)
    {
        lock (_sync)
        {
            phase = 0;
            if (!TryGetAllocationForOrganisation(organisationId, out _) || _polling is null
                || !_polling.Request.RegistrationYears.Contains(year))
            {
                return false;
            }
            var key = (organisationId, year);
            var now = timeProvider.GetUtcNow();
            if (!_polling.FirstPrnReads.TryGetValue(key, out var firstRead))
                _polling.FirstPrnReads[key] = firstRead = now;
            phase = (int)((now - firstRead).TotalSeconds / _polling.Request.ObligationChangeIntervalSeconds!.Value);
            return true;
        }
    }

    public string ReferenceNumber(LoadTestOrganisationAllocation allocation) => allocation.ReferenceNumber;
    public string CompaniesHouseNumber(LoadTestOrganisationAllocation allocation) => allocation.CompaniesHouseNumber;

    public IReadOnlyList<LoadTestOrganisationAllocation> GetAccountAllocations()
    {
        lock (_sync)
        {
            ExpireRunLeaseIfNeeded(timeProvider.GetUtcNow());
            GrowCatalogue();
            return (_polling?.Allocations ?? _session?.Allocations)?.ToArray() ?? [];
        }
    }

    private void GrowCatalogue()
    {
        if (_polling is null)
        {
            return;
        }
        var request = _polling.Request;
        var initialCount = request.DirectProducerUserCount + request.ComplianceSchemeUserCount;
        var growth = Math.Min(request.MaximumAdditionalOrganisations,
            (int)((timeProvider.GetUtcNow() - _polling.StartedAtUtc).TotalSeconds / request.OrganisationGrowthIntervalSeconds!.Value));
        while (_polling.Allocations.Count < initialCount + growth)
        {
            var additionalIndex = _polling.Allocations.Count - initialCount;
            var scheme = request.DirectProducerUserCount == 0
                || (request.ComplianceSchemeUserCount > 0 && additionalIndex % 2 == 1);
            var index = scheme ? _polling.NextComplianceSchemeIndex++ : _polling.NextDirectProducerIndex++;
            _polling.Allocations.Add(CreateAllocation(scheme, index));
        }
    }

    private sealed record PollingCatalogue(LoadTestPollingRequest Request, DateTimeOffset StartedAtUtc,
        List<LoadTestOrganisationAllocation> Allocations)
    {
        public Dictionary<(Guid OrganisationId, int Year), DateTimeOffset> FirstPrnReads { get; } = [];
        public int NextDirectProducerIndex { get; set; } = Request.DirectProducerUserCount;
        public int NextComplianceSchemeIndex { get; set; } = Request.ComplianceSchemeUserCount;
    }
}

public sealed record LoadTestPollingRequest(Guid RunId, int DirectProducerUserCount,
    int ComplianceSchemeUserCount, int[] RegistrationYears, int? ChangeIntervalSeconds = null,
    int MaximumAdditionalOrganisations = 288, int? OrganisationGrowthIntervalSeconds = null,
    int? ObligationChangeIntervalSeconds = null);

public sealed record LoadTestPollingResponse(Guid RunId, DateTimeOffset StartedAtUtc,
    IReadOnlyList<int> RegistrationYears, int InitialOrganisationCount,
    IReadOnlyList<LoadTestOrganisationAllocation> Allocations);
