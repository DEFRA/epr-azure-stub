namespace EprAzureStub;

public sealed partial class LoadTestSessionState
{
    private PollingCatalogue? _polling;

    public LoadTestPollingResponse? InitialisePolling(LoadTestPollingRequest request)
    {
        var years = request.RegistrationYears?.Distinct().Order().ToArray() ?? [];
        var count = (long)request.DirectProducerUserCount + request.ComplianceSchemeUserCount;
        if (request.RunId == Guid.Empty || request.DirectProducerUserCount < 0
            || request.ComplianceSchemeUserCount < 0 || count is < 1 or > 1000
            || years.Length == 0 || years.Any(year => year is < 2023 or > 2050)
            || request.ChangeIntervalSeconds is < 1 or > 86400
            || request.MaximumAdditionalOrganisations < 0
            || count + request.MaximumAdditionalOrganisations > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        request = request with { RegistrationYears = years };
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
                    || previous.ChangeIntervalSeconds != request.ChangeIntervalSeconds
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

    public IReadOnlyList<int> GetRegistrationYears() => GetPollingStatus(_runLease?.RunId ?? Guid.Empty)?.RegistrationYears
        ?? Enumerable.Range(2025, 6).ToArray();

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
            phase = (int)((now - firstRead).TotalSeconds / _polling.Request.ChangeIntervalSeconds);
            return true;
        }
    }

    public string ReferenceNumber(LoadTestOrganisationAllocation allocation) => allocation.IsComplianceScheme
        ? "1" : _polling is null ? "165282" : (200000 + allocation.UserIndex).ToString();

    public string CompaniesHouseNumber(LoadTestOrganisationAllocation allocation) => allocation.IsComplianceScheme
        ? "12345678" : _polling is null ? "17121895" : (20000000 + allocation.UserIndex).ToString();

    private void GrowCatalogue()
    {
        if (_polling is null)
        {
            return;
        }
        var request = _polling.Request;
        var initialCount = request.DirectProducerUserCount + request.ComplianceSchemeUserCount;
        var growth = Math.Min(request.MaximumAdditionalOrganisations,
            (int)((timeProvider.GetUtcNow() - _polling.StartedAtUtc).TotalSeconds / request.ChangeIntervalSeconds));
        while (_polling.Allocations.Count < initialCount + growth)
        {
            var additionalIndex = _polling.Allocations.Count - initialCount;
            var scheme = request.DirectProducerUserCount == 0
                || (request.ComplianceSchemeUserCount > 0 && additionalIndex % 2 == 1);
            var index = _polling.Allocations.Count(allocation => allocation.IsComplianceScheme == scheme);
            _polling.Allocations.Add(new(scheme ? ComplianceSchemeUserId : DirectProducerUserId, index,
                Guid.NewGuid(), scheme ? WasteOrganisationStubIds.SeededComplianceSchemeOrganisationGuid : null,
                scheme ? $"Organisation Name {index + 1}" : $"POP QUEST LTD {index + 1}",
                scheme ? $"Compliance Scheme Name {index + 1}" : null));
        }
    }

    private sealed record PollingCatalogue(LoadTestPollingRequest Request, DateTimeOffset StartedAtUtc,
        List<LoadTestOrganisationAllocation> Allocations)
    {
        public Dictionary<(Guid OrganisationId, int Year), DateTimeOffset> FirstPrnReads { get; } = [];
    }
}

public sealed record LoadTestPollingRequest(Guid RunId, int DirectProducerUserCount,
    int ComplianceSchemeUserCount, int[] RegistrationYears, int ChangeIntervalSeconds = 300,
    int MaximumAdditionalOrganisations = 288);

public sealed record LoadTestPollingResponse(Guid RunId, DateTimeOffset StartedAtUtc,
    IReadOnlyList<int> RegistrationYears, int InitialOrganisationCount,
    IReadOnlyList<LoadTestOrganisationAllocation> Allocations);
