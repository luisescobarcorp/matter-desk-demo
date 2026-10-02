using System.Text.RegularExpressions;
using MatterDesk.Api.Auth;
using MatterDesk.Api.Contracts;
using MatterDesk.Api.Data;
using MatterDesk.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MatterDesk.Api.Mail;

/// <summary>
/// Pulls Inbox changes through the delta cursor and files messages whose subject carries a matter number
/// (e.g. "[10042-0003]") onto that matter — but only matters the operator is allowed to see.
/// Idempotent: a message already profiled to a matter is skipped, and markers are set once.
/// </summary>
public sealed partial class EmailProfilingService(MatterDeskDbContext db, IMailSource mail, ICurrentOperator me, ILogger<EmailProfilingService> log)
{
    private const string Folder = "inbox";
    private const int MaxPagesPerSync = 20;   // bounded work per request; the delta link resumes the rest next time

    [GeneratedRegex(@"\b(\d{5}-\d{4})\b")]
    private static partial Regex MatterNumberPattern();

    public async Task<MailSyncResult> SyncInboxAsync(int? onlyMatterId, CancellationToken ct)
    {
        var state = await db.MailSyncStates.FirstOrDefaultAsync(s => s.OperatorId == me.Id && s.Folder == Folder, ct)
                    ?? db.MailSyncStates.Add(new MailSyncState { OperatorId = me.Id, Folder = Folder }).Entity;

        var resumed = state.DeltaLink is not null;
        var cursor = state.DeltaLink;
        string? newDeltaLink = null;
        int scanned = 0, profiled = 0, already = 0, markers = 0;

        // Matters this operator may file into, keyed by number. The predicate is applied in SQL here too.
        var allowed = await db.Matters.AsNoTracking()
            .Where(m => !m.IsRestricted || m.Access.Any(a => a.OperatorId == me.Id))
            .Where(m => onlyMatterId == null || m.Id == onlyMatterId)
            .Select(m => new { m.Id, m.Number })
            .ToDictionaryAsync(m => m.Number, m => m.Id, ct);

        for (var pageNo = 0; pageNo < MaxPagesPerSync; pageNo++)
        {
            var page = await mail.GetInboxChangesAsync(cursor, ct);

            foreach (var msg in page.Messages.Where(m => !m.IsRemoved))
            {
                scanned++;
                foreach (Match hit in MatterNumberPattern().Matches(msg.Subject))
                {
                    if (!allowed.TryGetValue(hit.Groups[1].Value, out var matterId)) continue;

                    var exists = await db.ProfiledEmails.AnyAsync(e => e.MatterId == matterId && e.ExternalMessageId == msg.Id, ct);
                    if (exists) { already++; continue; }

                    var row = new ProfiledEmail
                    {
                        MatterId = matterId, ExternalMessageId = msg.Id, InternetMessageId = msg.InternetMessageId,
                        Subject = msg.Subject, FromAddress = msg.FromAddress, ReceivedUtc = msg.ReceivedAt.UtcDateTime,
                        BodyPreview = msg.BodyPreview, ProfiledByCode = me.Code, ProfiledUtc = DateTime.UtcNow, MarkerSet = false,
                    };
                    db.ProfiledEmails.Add(row);
                    await db.SaveChangesAsync(ct);          // the profile row is durable before the marker is attempted
                    profiled++;

                    try
                    {
                        await mail.SetProfiledMarkerAsync(msg.Id, hit.Groups[1].Value, ct);
                        row.MarkerSet = true;
                        await db.SaveChangesAsync(ct);
                        markers++;
                    }
                    catch (Exception ex)
                    {
                        // Marker failure must not roll back the profile; a later sync or repair job can retry it.
                        log.LogWarning(ex, "Profiled message {Id} to matter {Matter} but could not set the Outlook marker.", msg.Id, matterId);
                    }
                }
            }

            if (page.NextLink is not null) { cursor = page.NextLink; continue; }
            newDeltaLink = page.DeltaLink;
            break;
        }

        if (newDeltaLink is not null)
        {
            state.DeltaLink = newDeltaLink;
            state.LastSyncUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new MailSyncResult(scanned, profiled, already, markers, resumed,
            newDeltaLink is null ? null : newDeltaLink[..Math.Min(40, newDeltaLink.Length)] + "…");
    }
}
