using MatterDesk.Api.Mail;

namespace MatterDesk.Api.Tests;

/// <summary>
/// In-memory stand-in for Microsoft Graph. Emulates delta semantics: a fresh round pages through all messages,
/// returns a delta link, and a later call with that link returns only messages added since.
/// </summary>
public sealed class FakeMailSource : IMailSource
{
    private readonly List<MailMessage> _inbox = [];
    private readonly Dictionary<string, List<string>> _markers = new();
    public int PageSize { get; set; } = 2;
    public bool FailMarkers { get; set; }
    public IReadOnlyDictionary<string, List<string>> Markers => _markers;

    public MailMessage Add(string id, string subject, string from = "someone@example.com", DateTimeOffset? received = null)
    {
        var m = new MailMessage(id, $"<{id}@example.com>", subject, from, received ?? DateTimeOffset.UtcNow, "preview " + subject, [], false);
        _inbox.Add(m);
        return m;
    }

    public Task<MailDeltaPage> GetInboxChangesAsync(string? cursor, CancellationToken ct)
    {
        // cursor format: "next:<offset>:<end>" during a round, "delta:<watermark>" between rounds
        int start, end;
        if (cursor is null) { start = 0; end = _inbox.Count; }
        else if (cursor.StartsWith("delta:")) { start = int.Parse(cursor[6..]); end = _inbox.Count; }
        else { var parts = cursor.Split(':'); start = int.Parse(parts[1]); end = int.Parse(parts[2]); }

        var slice = _inbox.Skip(start).Take(Math.Min(PageSize, end - start)).ToList();
        var nextStart = start + slice.Count;
        var page = nextStart < end
            ? new MailDeltaPage(slice, $"next:{nextStart}:{end}", null)
            : new MailDeltaPage(slice, null, $"delta:{end}");
        return Task.FromResult(page);
    }

    public Task SetProfiledMarkerAsync(string messageId, string matterNumber, CancellationToken ct)
    {
        if (FailMarkers) throw new HttpRequestException("Graph 503 (simulated)");
        if (!_markers.TryGetValue(messageId, out var list)) _markers[messageId] = list = [];
        var marker = $"Filed: {matterNumber}";
        if (!list.Contains(marker)) list.Add(marker);
        return Task.CompletedTask;
    }
}
