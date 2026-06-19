using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc.Testing;

namespace EprAzureStub.Test;

public class OAuthEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Endpoint = "/oauth2/v2.0/token";

    [Fact]
    public async Task PostToken_ReturnsAccessToken()
    {
        using var client = factory.CreateClient();
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = "ignored",
                ["client_secret"] = "ignored",
                ["grant_type"] = "client_credentials",
            }
        );

        var response = await client.PostAsync(
            Endpoint,
            content,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(body);
        Assert.Equal("access_token", body.AccessToken);
        Assert.Equal(60, body.ExpiresIn);
    }

    [Fact]
    public async Task PostToken_ReturnsAccessToken_WhenRequestHasNoBody()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            Endpoint,
            null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
