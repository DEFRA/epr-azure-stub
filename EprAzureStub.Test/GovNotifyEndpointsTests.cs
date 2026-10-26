using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EprAzureStub.Test;

public class GovNotifyEndpointsTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly object Email = new
    {
        email_address = "producer@example.com",
        template_id = "5f64e3bd-d454-4a45-a9c6-9409bf940d7a",
        reference = "journey-reference",
        personalisation = new { obligationYear = 2026 }
    };

    private static async Task Acquire(HttpClient client, Guid runId)
    {
        var response = await client.PostAsJsonAsync("/admin/load-test-runs",
            new LoadTestRunLeaseRequest(runId, "unsubmitted-journey", 60), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task EmailAcceptance_PreservesSdkEvidenceAndCapturesRepeatedRequests()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var token = TestContext.Current.CancellationToken;
        var runId = Guid.NewGuid();
        await Acquire(client, runId);
        var session = await client.PostAsJsonAsync("/admin/load-test-sessions",
            new LoadTestSessionInitialisationRequest(runId, 1, 1), token);
        Assert.Equal(runId, (await session.Content.ReadFromJsonAsync<LoadTestSessionResponse>(token))!.RunId);
        var health = await client.GetFromJsonAsync<JsonElement>("/govuk-notify/v2/templates?type=email", token);
        Assert.Equal(JsonValueKind.Array, health.GetProperty("templates").ValueKind);
        var initial = await client.GetFromJsonAsync<NotifyCapture>("/admin/notify/notifications", token);
        var template = "5f64e3bd-d454-4a45-a9c6-9409bf940d7a";
        var request = new { email_address = "producer@example.com", template_id = template,
            reference = "journey-reference", personalisation = new { obligationYear = 2026, user = "Producer" } };

        for (var i = 0; i < 2; i++)
        {
            var response = await client.PostAsJsonAsync("/govuk-notify/v2/notifications/email", request, token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var acceptance = await response.Content.ReadFromJsonAsync<JsonElement>(token);
            Assert.NotEqual(Guid.Empty, acceptance.GetProperty("id").GetGuid());
            Assert.Equal("journey-reference", acceptance.GetProperty("reference").GetString());
            Assert.Equal(template, acceptance.GetProperty("template").GetProperty("id").GetString());
            Assert.Equal(1, acceptance.GetProperty("template").GetProperty("version").GetInt32());
        }

        var capture = (await client.GetFromJsonAsync<NotifyCapture>($"/admin/notify/notifications?after={initial!.Cursor}", token))!;
        Assert.Equal(initial.InstanceId, capture.InstanceId);
        Assert.False(capture.Truncated);
        Assert.Equal(2, capture.Notifications.Count);
        Assert.All(capture.Notifications, item =>
        {
            Assert.Equal(runId, item.RunId);
            Assert.Equal(item.AcceptedAtUtc.AddHours(24), item.ExpiresAtUtc);
        });
        Assert.Equal("producer@example.com", capture.Notifications[0].Request.GetProperty("email_address").GetString());
        Assert.Equal(2026, capture.Notifications[0].Request.GetProperty("personalisation").GetProperty("obligationYear").GetInt32());
        Assert.NotEqual(capture.Notifications[0].Id, capture.Notifications[1].Id);
        Assert.Empty((await client.GetFromJsonAsync<NotifyCapture>($"/admin/notify/notifications?after={capture.Cursor}", token))!.Notifications);
    }

    [Fact]
    public async Task InvalidRequests_AreRejectedWithoutCapture()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/govuk-notify/v2/notifications/email", new { }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/notify/notifications?after=-1", token)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<NotifyCapture>("/admin/notify/notifications", token))!.Notifications);
    }

    [Fact]
    public void CaptureOverflow_IsReportedInsteadOfSilentlyLosingEvidence()
    {
        var store = new NotifyCaptureStore(TimeProvider.System, Options.Create(new NotifyCaptureOptions()));
        var body = JsonSerializer.SerializeToElement(new { reference = "repeat" });
        for (var i = 0; i <= NotifyCaptureStore.Capacity; i++) store.Add(body, Guid.NewGuid(), Guid.NewGuid());
        var capture = store.Read(0);
        Assert.True(capture.Truncated);
        Assert.Equal(NotifyCaptureStore.Capacity, capture.Notifications.Count);
        Assert.Equal(2, capture.Notifications[0].Sequence);
        Assert.False(store.Read(1).Truncated);
    }

    [Fact]
    public async Task ReleasedRunsRemainInspectable_AndAcceptanceOutsideRunDoesNotCapture()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var token = TestContext.Current.CancellationToken;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await Acquire(client, first);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/govuk-notify/v2/notifications/email", Email, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/admin/load-test-runs/{first}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/govuk-notify/v2/notifications/email", Email, token)).StatusCode);
        await Acquire(client, second);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/govuk-notify/v2/notifications/email", Email, token)).StatusCode);

        var oldRun = (await client.GetFromJsonAsync<NotifyCapture>($"/admin/notify/notifications?runId={first}", token))!;
        var newRun = (await client.GetFromJsonAsync<NotifyCapture>($"/admin/notify/notifications?runId={second}", token))!;
        Assert.Equal(first, Assert.Single(oldRun.Notifications).RunId);
        Assert.Equal(second, Assert.Single(newRun.Notifications).RunId);
        Assert.Equal(2, (await client.GetFromJsonAsync<NotifyCapture>("/admin/notify/notifications", token))!.Notifications.Count);
    }

    [Fact]
    public async Task RenewalExtendsCapture_AndLeaseExpiryPreservesRetainedRequests()
    {
        var clock = new Clock();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            }));
        using var client = factory.CreateClient();
        var token = TestContext.Current.CancellationToken;
        var runId = Guid.NewGuid();
        await Acquire(client, runId);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/govuk-notify/v2/notifications/email", Email, token)).StatusCode);
        clock.Now = clock.Now.AddSeconds(30);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/admin/load-test-runs/{runId}",
            new LoadTestRunLeaseRenewalRequest(60), token)).StatusCode);
        clock.Now = clock.Now.AddSeconds(30);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/govuk-notify/v2/notifications/email", Email, token)).StatusCode);
        clock.Now = clock.Now.AddSeconds(30);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/govuk-notify/v2/notifications/email", Email, token)).StatusCode);
        var capture = (await client.GetFromJsonAsync<NotifyCapture>($"/admin/notify/notifications?runId={runId}", token))!;
        Assert.Equal(2, capture.Notifications.Count);
        Assert.All(capture.Notifications, item => Assert.Equal(runId, item.RunId));
    }

    [Fact]
    public void RequestsExpireIndividually_AndExpiryMarksAnIncompleteObservationWindow()
    {
        var clock = new Clock();
        var store = new NotifyCaptureStore(clock, Options.Create(new NotifyCaptureOptions { RetentionSeconds = 60 }));
        var body = JsonSerializer.SerializeToElement(Email);
        var firstRun = Guid.NewGuid();
        var first = store.Add(body, firstRun, Guid.NewGuid());
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 12, 1, 0, TimeSpan.Zero), first.ExpiresAtUtc);
        clock.Now = clock.Now.AddSeconds(30);
        var second = store.Add(body, Guid.NewGuid(), Guid.NewGuid());
        clock.Now = clock.Now.AddSeconds(30);

        var capture = store.Read(0);
        Assert.True(capture.Truncated);
        Assert.Equal(second.Id, Assert.Single(capture.Notifications).Id);
        Assert.Empty(store.Read(0, firstRun).Notifications);
        clock.Now = clock.Now.AddSeconds(30);
        Assert.Empty(store.Read(0).Notifications);
    }
}
