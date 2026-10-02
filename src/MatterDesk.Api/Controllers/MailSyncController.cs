using MatterDesk.Api.Auth;
using MatterDesk.Api.Contracts;
using MatterDesk.Api.Data;
using MatterDesk.Api.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static MatterDesk.Api.Authorization.MatterAccessPolicy;

namespace MatterDesk.Api.Controllers;

[ApiController]
[Authorize]
public sealed class MailSyncController(EmailProfilingService profiling, MatterDeskDbContext db, ICurrentOperator me) : ControllerBase
{
    /// <summary>Sync the operator's Inbox via Graph delta and file messages tagged with any visible matter number.</summary>
    [HttpPost("api/mail/sync")]
    [ProducesResponseType<MailSyncResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MailSyncResult>> SyncAll(CancellationToken ct) =>
        Ok(await profiling.SyncInboxAsync(onlyMatterId: null, ct));

    /// <summary>Same sync, but only file messages for one matter (the "profile email to this matter" button).</summary>
    [HttpPost("api/matters/{id:int}/emails/sync")]
    [ProducesResponseType<MailSyncResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MailSyncResult>> SyncMatter(int id, CancellationToken ct)
    {
        switch (await CanEditAsync(db.Matters, id, me.Id, ct))
        {
            case Decision.NotFound: return NotFound();
            case Decision.Forbidden: return Forbid();
        }
        return Ok(await profiling.SyncInboxAsync(id, ct));
    }
}
