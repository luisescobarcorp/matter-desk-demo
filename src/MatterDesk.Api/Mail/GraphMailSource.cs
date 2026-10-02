using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace MatterDesk.Api.Mail;

public sealed class GraphOptions
{
    public const string Section = "Graph";

    /// <summary>"Delegated" (user signed in; device-code or auth-code) or "Application" (service sync with client credentials).</summary>
    public string Mode { get; set; } = "Delegated";
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    /// <summary>Required in Application mode: the mailbox being synced. Scope this with an Exchange application access policy.</summary>
    public string? MailboxUserId { get; set; }
    public string[] Scopes { get; set; } = ["Mail.ReadWrite"];
    public string MarkerCategoryPrefix { get; set; } = "Filed:";
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// Microsoft Graph implementation. Uses delta queries on the Inbox so each sync reads only what changed,
/// and marks filed messages with an Outlook category (the "profiled" indicator users rely on).
/// Replaces the EWS-era approach (public-folder watcher, MAPI polling) that Exchange Online stops serving in 2027.
/// </summary>
public sealed class GraphMailSource : IMailSource
{
    private readonly GraphServiceClient _graph;
    private readonly GraphOptions _opt;
    private readonly ILogger<GraphMailSource> _log;

    public GraphMailSource(IOptions<GraphOptions> options, ILogger<GraphMailSource> log)
    {
        _opt = options.Value;
        _log = log;

        TokenCredential credential = _opt.Mode.Equals("Application", StringComparison.OrdinalIgnoreCase)
            // Application permissions: no user present. Admin consent required; restrict reach with an
            // Exchange ApplicationAccessPolicy so the app can only touch the mailboxes it is meant to sync.
            ? new ClientSecretCredential(_opt.TenantId, _opt.ClientId, _opt.ClientSecret)
            // Delegated permissions: acts as the signed-in operator; the token cache keeps the refresh token.
            : new DeviceCodeCredential(new DeviceCodeCredentialOptions
            {
                TenantId = _opt.TenantId,
                ClientId = _opt.ClientId,
                TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "matterdesk" },
                DeviceCodeCallback = (info, _) => { _log.LogWarning("{Message}", info.Message); return Task.CompletedTask; },
            });

        _graph = new GraphServiceClient(credential, _opt.Scopes);
    }

    private string? _mailboxId;

    /// <summary>Delegated mode resolves /me once; application mode uses the configured mailbox. Both then address /users/{id}.</summary>
    private async Task<Microsoft.Graph.Users.Item.UserItemRequestBuilder> MailboxAsync(CancellationToken ct)
    {
        if (_mailboxId is null)
        {
            _mailboxId = _opt.Mode.Equals("Application", StringComparison.OrdinalIgnoreCase)
                ? _opt.MailboxUserId ?? throw new InvalidOperationException("Graph:MailboxUserId is required in Application mode.")
                : (await _graph.Me.GetAsync(cfg => cfg.QueryParameters.Select = ["id"], ct))?.Id
                  ?? throw new InvalidOperationException("Could not resolve the signed-in user's id from Graph.");
        }
        return _graph.Users[_mailboxId];
    }

    public async Task<MailDeltaPage> GetInboxChangesAsync(string? cursor, CancellationToken ct)
    {
        var delta = (await MailboxAsync(ct)).MailFolders["inbox"].Messages.Delta;

        var response = cursor is null
            ? await delta.GetAsDeltaGetResponseAsync(cfg =>
            {
                cfg.QueryParameters.Select = ["id", "internetMessageId", "subject", "from", "receivedDateTime", "bodyPreview", "categories"];
                cfg.QueryParameters.Top = _opt.PageSize;
                // Graph delta on messages: prefer minimal payloads, tolerate large change sets.
                cfg.Headers.Add("Prefer", $"odata.maxpagesize={_opt.PageSize}");
            }, ct)
            : await delta.WithUrl(cursor).GetAsDeltaGetResponseAsync(cancellationToken: ct);

        var messages = (response?.Value ?? []).Select(Map).ToList();
        return new MailDeltaPage(messages, response?.OdataNextLink, response?.OdataDeltaLink);
    }

    public async Task SetProfiledMarkerAsync(string messageId, string matterNumber, CancellationToken ct)
    {
        var marker = $"{_opt.MarkerCategoryPrefix} {matterNumber}";
        try
        {
            var mailbox = await MailboxAsync(ct);
            var current = await mailbox.Messages[messageId].GetAsync(cfg => cfg.QueryParameters.Select = ["id", "categories"], ct);
            var categories = new List<string>(current?.Categories ?? []);
            if (categories.Contains(marker)) return;
            categories.Add(marker);
            await mailbox.Messages[messageId].PatchAsync(new Message { Categories = categories }, cancellationToken: ct);
        }
        catch (ODataError e) when (e.ResponseStatusCode == 404)
        {
            // The message was moved or deleted between delta and patch: the profile stays, the marker is best-effort.
            _log.LogInformation("Message {Id} no longer available for marker; skipping.", messageId);
        }
    }

    private static MailMessage Map(Message m) => new(
        Id: m.Id ?? "",
        InternetMessageId: m.InternetMessageId,
        Subject: m.Subject ?? "(no subject)",
        FromAddress: m.From?.EmailAddress?.Address ?? "",
        ReceivedAt: m.ReceivedDateTime ?? DateTimeOffset.MinValue,
        BodyPreview: m.BodyPreview,
        Categories: m.Categories ?? [],
        IsRemoved: m.AdditionalData?.ContainsKey("@removed") == true);
}
