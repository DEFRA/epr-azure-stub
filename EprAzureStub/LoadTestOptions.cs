using System.ComponentModel.DataAnnotations;

namespace EprAzureStub;

public sealed record LoadTestOptions
{
    [Range(1, 1_000_000)]
    public int MaxOrganisationCount { get; init; } = 100_000;
}
