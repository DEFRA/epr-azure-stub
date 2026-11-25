using System.ComponentModel.DataAnnotations;

namespace EprAzureStub;

public sealed record LoadTestOptions
{
    [Range(1, 1_000_000)]
    public int MaxOrganisationCount { get; init; } = 100_000;

    // PRNs and PERNs generated for each organisation the PRN common backend endpoints serve. Every
    // virtual user of the frontend load test accepts and rejects the shared test accounts' notes, so
    // each account needs enough awaiting acceptance for a whole run.
    [Range(1, 10_000)]
    public int PrnsPerOrganisation { get; init; } = 1_000;

    // PRNs and PERNs generated for each compliance scheme, allocated or fixture, when set; otherwise
    // PrnsPerOrganisation. Lets a run give schemes more notes than direct producers.
    [Range(1, 10_000)]
    public int? PrnsPerComplianceScheme { get; init; }
}
