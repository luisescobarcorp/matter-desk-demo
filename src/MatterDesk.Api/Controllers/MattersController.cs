using MatterDesk.Api.Auth;
using MatterDesk.Api.Authorization;
using MatterDesk.Api.Contracts;
using MatterDesk.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static MatterDesk.Api.Authorization.MatterAccessPolicy;

namespace MatterDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/matters")]
public sealed class MattersController(MatterDeskDbContext db, ICurrentOperator me) : ControllerBase
{
    /// <summary>Matters the calling operator may see. Pagination is mandatory on list endpoints.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<MatterSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MatterSummary>>> List(
        [FromQuery] string? areaOfLaw, [FromQuery] string? clientNumber,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);

        var q = db.Matters.AsNoTracking().VisibleTo(me.Id);
        if (!string.IsNullOrWhiteSpace(areaOfLaw)) q = q.Where(m => m.AreaOfLaw == areaOfLaw);
        if (!string.IsNullOrWhiteSpace(clientNumber)) q = q.Where(m => m.Client.Number == clientNumber);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(m => m.OpenedUtc).ThenBy(m => m.Number)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new MatterSummary(m.Id, m.Number, m.Title, m.AreaOfLaw, m.Client.Name, m.IsRestricted, m.OpenedUtc, m.Documents.Count, m.Emails.Count))
            .ToListAsync(ct);

        return Ok(new PagedResult<MatterSummary>(items, page, pageSize, total));
    }

    /// <summary>403 when the matter exists but is restricted and the operator has no grant; 404 when it does not exist.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<MatterDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MatterDetail>> Get(int id, CancellationToken ct)
    {
        switch (await CanViewAsync(db.Matters, id, me.Id, ct))
        {
            case Decision.NotFound: return NotFound();
            case Decision.Forbidden: return Forbid();
        }

        var canEdit = await CanEditAsync(db.Matters, id, me.Id, ct) == Decision.Allowed;
        var m = await db.Matters.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new MatterDetail(x.Id, x.Number, x.Title, x.AreaOfLaw, x.Client.Number, x.Client.Name, x.IsRestricted, x.OpenedUtc, canEdit))
            .SingleAsync(ct);
        return Ok(m);
    }

    [HttpGet("{id:int}/documents")]
    [ProducesResponseType<PagedResult<DocumentSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<DocumentSummary>>> Documents(
        int id, [FromQuery] string? documentType, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        switch (await CanViewAsync(db.Matters, id, me.Id, ct))
        {
            case Decision.NotFound: return NotFound();
            case Decision.Forbidden: return Forbid();
        }
        (page, pageSize) = Paging.Clamp(page, pageSize);

        var q = db.Documents.AsNoTracking().Where(d => d.MatterId == id);
        if (!string.IsNullOrWhiteSpace(documentType)) q = q.Where(d => d.DocumentType == documentType);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(d => d.ModifiedUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new DocumentSummary(d.Id, d.MatterId, d.Matter.Number, d.Title, d.DocumentType, d.AuthorCode, d.Keywords, d.ModifiedUtc, d.Versions.Count, d.Version))
            .ToListAsync(ct);
        return Ok(new PagedResult<DocumentSummary>(items, page, pageSize, total));
    }

    [HttpGet("{id:int}/emails")]
    [ProducesResponseType<PagedResult<EmailSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<EmailSummary>>> Emails(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        switch (await CanViewAsync(db.Matters, id, me.Id, ct))
        {
            case Decision.NotFound: return NotFound();
            case Decision.Forbidden: return Forbid();
        }
        (page, pageSize) = Paging.Clamp(page, pageSize);

        var q = db.ProfiledEmails.AsNoTracking().Where(e => e.MatterId == id);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(e => e.ReceivedUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new EmailSummary(e.Id, e.MatterId, e.Subject, e.FromAddress, e.ReceivedUtc, e.BodyPreview, e.ProfiledByCode, e.MarkerSet))
            .ToListAsync(ct);
        return Ok(new PagedResult<EmailSummary>(items, page, pageSize, total));
    }
}

internal static class Paging
{
    public static (int page, int pageSize) Clamp(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 200));
}
