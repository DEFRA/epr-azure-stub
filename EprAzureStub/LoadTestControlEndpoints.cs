namespace EprAzureStub;

/// <summary>
/// Provides stub-owned controls used to initialise deterministic load-test data.
/// These routes do not replicate an upstream service API.
/// </summary>
public static class LoadTestControlEndpoints
{
    public static void MapLoadTestControlEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin");

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
                    var response = loadTestSessionState.Initialise(
                        request.RunId,
                        request.DirectProducerUserCount,
                        request.ComplianceSchemeUserCount
                    );
                    loggerFactory
                        .CreateLogger(nameof(LoadTestControlEndpoints))
                        .LogInformation(
                            "Initialised load-test organisation mappings for run {LoadTestRunId}: {DirectProducerUserCount} direct producer and {ComplianceSchemeUserCount} compliance scheme users.",
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
}
