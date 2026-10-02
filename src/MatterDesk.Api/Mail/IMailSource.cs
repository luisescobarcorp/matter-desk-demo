namespace MatterDesk.Api.Mail;

public sealed record MailMessage(
    string Id,
    string? InternetMessageId,
    string Subject,
    string FromAddress,
    DateTimeOffset ReceivedAt,
    string? BodyPreview,
    IReadOnlyList<string> Categories,
    bool IsRemoved);

/// <summary>A page of changes since the last cursor. <see cref="NextLink"/> continues the current round; <see cref="DeltaLink"/> is stored for the next sync.</summary>
public sealed record MailDeltaPage(IReadOnlyList<MailMessage> Messages, string? NextLink, string? DeltaLink);

/// <summary>
/// The only seam between the domain and Microsoft Graph. Production binds <see cref="GraphMailSource"/>;
/// tests bind an in-memory fake so the permission and idempotency logic is exercised without a tenant.
/// </summary>
public interface IMailSource
{
    /// <param name="cursor">A previous delta link (resume) or next link (continue), or null for a fresh delta round.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<MailDeltaPage> GetInboxChangesAsync(string? cursor, CancellationToken ct);

    /// <summary>Applies the "filed to matter" marker users see in Outlook so they do not profile the same message twice.</summary>
    Task SetProfiledMarkerAsync(string messageId, string matterNumber, CancellationToken ct);
}
