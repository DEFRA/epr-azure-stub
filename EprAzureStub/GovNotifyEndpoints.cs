using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace EprAzureStub;

public static class GovNotifyEndpoints
{
    public static void MapGovNotifyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/govuk-notify");
        group.MapGet("/v2/templates", () => Results.Ok(new { templates = Array.Empty<object>() }));
        group.MapPost("/v2/notifications/email", (JsonElement body, NotifyCaptureStore store, LoadTestSessionState sessions) =>
        {
            if (body.ValueKind != JsonValueKind.Object
                || !ReadString(body, "email_address", out var email) || !email.Contains('@')
                || !ReadString(body, "template_id", out var template) || !Guid.TryParse(template, out _)
                || !ReadString(body, "reference", out _)
                || !body.TryGetProperty("personalisation", out var values) || values.ValueKind != JsonValueKind.Object)
            {
                return Results.BadRequest(new { errors = new[] { new { error = "BadRequestError", message = "Email, template, reference and personalisation are required." } } });
            }

            var id = Guid.NewGuid();
            if (sessions.GetActiveRunId() is { } runId)
            {
                store.Add(body, runId, id);
            }

            return Results.Json(new
            {
                id,
                reference = body.GetProperty("reference").GetString(),
                template = new { id = template, version = 1 },
                content = new { subject = "Captured by EPR Azure stub", body = "Template personalisation captured; no email sent." }
            }, statusCode: StatusCodes.Status201Created);
        });

        app.MapGet("/admin/notify/notifications", (long? after, Guid? runId, NotifyCaptureStore store) =>
            after is < 0 ? Results.BadRequest() : Results.Ok(store.Read(after ?? 0, runId)));
    }

    private static bool ReadString(JsonElement body, string name, out string value)
    {
        value = body.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? "" : "";
        return !string.IsNullOrWhiteSpace(value);
    }
}

// A bounded process-local observation window. References are deliberately not deduplicated:
// repeated HTTP requests must remain visible when diagnosing delivery retries.
public sealed class NotifyCaptureStore(TimeProvider clock, IOptions<NotifyCaptureOptions> options)
{
    public const int Capacity = 10_000;
    private readonly object _sync = new();
    private readonly Queue<CapturedNotification> _notifications = new();
    private long _sequence;
    private readonly Guid _instanceId = Guid.NewGuid();

    public CapturedNotification Add(JsonElement request, Guid runId, Guid id)
    {
        lock (_sync)
        {
            var now = clock.GetUtcNow();
            RemoveExpired(now);
            var item = new CapturedNotification(++_sequence, id, runId, now,
                now.AddSeconds(options.Value.RetentionSeconds), request.Clone());
            _notifications.Enqueue(item);
            if (_notifications.Count > Capacity) _notifications.Dequeue();
            return item;
        }
    }

    public NotifyCapture Read(long after, Guid? runId = null)
    {
        lock (_sync)
        {
            RemoveExpired(clock.GetUtcNow());
            var oldest = _notifications.TryPeek(out var item) ? item.Sequence : _sequence + 1;
            return new(_instanceId, _sequence, after < oldest - 1,
                _notifications.Where(item => item.Sequence > after && (runId is null || item.RunId == runId)).ToArray());
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        while (_notifications.TryPeek(out var item) && item.ExpiresAtUtc <= now)
        {
            _notifications.Dequeue();
        }
    }
}

public sealed class NotifyCaptureOptions
{
    [Range(60, 604_800)]
    public int RetentionSeconds { get; init; } = 86_400;
}

public sealed record CapturedNotification(long Sequence, Guid Id, Guid RunId,
    DateTimeOffset AcceptedAtUtc, DateTimeOffset ExpiresAtUtc, JsonElement Request);
public sealed record NotifyCapture(Guid InstanceId, long Cursor, bool Truncated, IReadOnlyList<CapturedNotification> Notifications);
