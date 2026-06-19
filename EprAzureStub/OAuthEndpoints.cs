using System.Text.Json.Serialization;

namespace EprAzureStub;

public static class OAuthEndpoints
{
    public static void MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/oauth2/v2.0/token",
            () => Results.Ok(new OAuthTokenResponse("access_token", 60))
        );
    }

    private sealed record OAuthTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn
    );
}
