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
            (LoadTestSessionInitialisationRequest request, LoadTestSessionState loadTestSessionState) =>
            {
                if (request.RunId == Guid.Empty)
                {
                    return Results.BadRequest(new { message = "runId must be a non-empty GUID." });
                }

                try
                {
                    return Results.Ok(
                        loadTestSessionState.Initialise(
                            request.RunId,
                            request.DirectProducerUserCount,
                            request.ComplianceSchemeUserCount
                        )
                    );
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
