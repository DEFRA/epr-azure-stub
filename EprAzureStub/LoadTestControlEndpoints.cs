namespace EprAzureStub;

/// <summary>
/// Provides stub-owned controls used to coordinate and initialise load-test data.
/// These routes do not replicate an upstream service API.
/// </summary>
public static class LoadTestControlEndpoints
{
    private static readonly HashSet<string> SupportedProfiles =
    [
        "all",
        "k6",
        "lighthouse",
        "browser-load",
    ];

    public static void MapLoadTestControlEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin");

        group.MapPost(
            "/load-test-runs",
            (
                LoadTestRunLeaseRequest request,
                LoadTestSessionState loadTestSessionState,
                ILoggerFactory loggerFactory
            ) =>
            {
                if (request.RunId == Guid.Empty)
                {
                    return Results.BadRequest(new { message = "runId must be a non-empty GUID." });
                }

                if (!SupportedProfiles.Contains(request.Profile))
                {
                    return Results.BadRequest(
                        new { message = "profile must be one of: all, k6, lighthouse, browser-load." }
                    );
                }

                if (!IsValidLeaseDuration(request.LeaseDurationSeconds))
                {
                    return Results.BadRequest(new { message = LeaseDurationValidationMessage });
                }

                var result = loadTestSessionState.AcquireRunLease(
                    request.RunId,
                    request.Profile,
                    request.LeaseDurationSeconds
                );

                if (!result.Acquired)
                {
                    return Results.Conflict(
                        new
                        {
                            message = "A load-test run is already active.",
                            activeRunId = result.ActiveLease.RunId,
                            activeProfile = result.ActiveLease.Profile,
                            activeRunExpiresAtUtc = result.ActiveLease.ExpiresAtUtc,
                        }
                    );
                }

                loggerFactory
                    .CreateLogger(nameof(LoadTestControlEndpoints))
                    .LogInformation(
                        "{LoadTestRunLeaseAction} load-test run {LoadTestRunId} for profile {LoadTestProfile}; lease expires at {LoadTestRunLeaseExpiry}.",
                        result.Renewed ? "Renewed" : "Acquired",
                        result.ActiveLease.RunId,
                        result.ActiveLease.Profile,
                        result.ActiveLease.ExpiresAtUtc
                    );

                return result.Renewed
                    ? Results.Ok(result.ActiveLease)
                    : Results.Created(
                        $"/admin/load-test-runs/{result.ActiveLease.RunId}",
                        result.ActiveLease
                    );
            }
        );

        group.MapPut(
            "/load-test-runs/{runId:guid}",
            (
                Guid runId,
                LoadTestRunLeaseRenewalRequest request,
                LoadTestSessionState loadTestSessionState
            ) =>
            {
                if (!IsValidLeaseDuration(request.LeaseDurationSeconds))
                {
                    return Results.BadRequest(new { message = LeaseDurationValidationMessage });
                }

                return loadTestSessionState.RenewRunLease(runId, request.LeaseDurationSeconds)
                    ? Results.NoContent()
                    : Results.Conflict(
                        new { message = "The load-test run lease is no longer active." }
                    );
            }
        );

        group.MapDelete(
            "/load-test-runs/{runId:guid}",
            (Guid runId, LoadTestSessionState loadTestSessionState, ILoggerFactory loggerFactory) =>
            {
                var result = loadTestSessionState.ReleaseRunLease(runId);

                if (result == LoadTestRunLeaseReleaseResult.HeldByAnotherRun)
                {
                    return Results.Conflict(
                        new { message = "A different load-test run holds the active lease." }
                    );
                }

                loggerFactory
                    .CreateLogger(nameof(LoadTestControlEndpoints))
                    .LogInformation("Released load-test run lease {LoadTestRunId}.", runId);

                return Results.NoContent();
            }
        );

        group.MapPost(
            "/load-test-sessions",
            (
                LoadTestSessionInitialisationRequest request,
                LoadTestSessionState loadTestSessionState,
                ILoggerFactory loggerFactory
            ) =>
            {
                if (request.RunId == Guid.Empty)
                {
                    return Results.BadRequest(new { message = "runId must be a non-empty GUID." });
                }

                try
                {
                    var result = loadTestSessionState.Initialise(
                        request.RunId,
                        request.DirectProducerUserCount,
                        request.ComplianceSchemeUserCount
                    );

                    if (result.Response is null)
                    {
                        return Results.Conflict(
                            new
                            {
                                message = "An active load-test run lease with the same runId is required before initialising allocations."
                            }
                        );
                    }

                    var response = result.Response;
                    loggerFactory
                        .CreateLogger(nameof(LoadTestControlEndpoints))
                        .LogInformation(
                            "{LoadTestSessionAction} load-test organisation mappings for run {LoadTestRunId}: {DirectProducerUserCount} direct producer and {ComplianceSchemeUserCount} compliance scheme users.",
                            result.Existing ? "Reused" : "Initialised",
                            response.RunId,
                            response.DirectProducerUserCount,
                            response.ComplianceSchemeUserCount
                        );

                    return Results.Ok(response);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return Results.BadRequest(
                        new { message = "The total user count must be between 1 and 1000." }
                    );
                }
            }
        );
    }

    private const string LeaseDurationValidationMessage =
        "leaseDurationSeconds must be between 60 and 86400.";

    private static bool IsValidLeaseDuration(int leaseDurationSeconds)
    {
        return leaseDurationSeconds is >= LoadTestSessionState.MinimumLeaseDurationSeconds
            and <= LoadTestSessionState.MaximumLeaseDurationSeconds;
    }
}

public sealed record LoadTestRunLeaseRenewalRequest(int LeaseDurationSeconds);
