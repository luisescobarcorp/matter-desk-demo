using MatterDesk.Api.Activities;
using MatterDesk.Api.Auth;
using MatterDesk.Api.Authorization;
using MatterDesk.Api.Contracts;
using MatterDesk.Api.Data;
using MatterDesk.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static MatterDesk.Api.Authorization.MatterAccessPolicy;

namespace MatterDesk.Api.Controllers;

[ApiController]
[Authorize]
public sealed class DocumentsController(MatterDeskDbContext db, ICurrentOperator me, IActivityRecorder activity) : ControllerBase
{
    /// <summary>Document profile plus version history. Documents on forbidden matters return 404, not 403: a document id must not reveal that a restricted file exists.</summary>
    [HttpGet("api/documents/{id:int}")]
    [ProducesResponseType<DocumentDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDetail>> Get(int id, CancellationToken ct)
    {
        var d = await db.Documents.AsNoTracking().VisibleTo(me.Id).Where(x => x.Id == id)
            .Select(x => new DocumentDetail(x.Id, x.MatterId, x.Matter.Number, x.Title, x.DocumentType, x.AuthorCode, x.Keywords, x.Notes, x.CreatedUtc, x.ModifiedUtc, x.Version,
                x.Versions.OrderBy(v => v.VersionNumber).Select(v => new DocumentVersionDto(v.VersionNumber, v.CreatedByCode, v.CreatedUtc, v.Comment)).ToList()))
            .FirstOrDefaultAsync(ct);

        if (d is null) return NotFound();
        Response.Headers.ETag = $"\"{d.Version}\"";
        return Ok(d);
    }

    /// <summary>Profile a new document onto a matter. The profile row and the first version row commit in one transaction.</summary>
    [HttpPost("api/matters/{matterId:int}/documents")]
    [ProducesResponseType<DocumentDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDetail>> Create(int matterId, [FromBody] CreateDocumentRequest req, CancellationToken ct)
    {
        switch (await CanEditAsync(db.Matters, matterId, me.Id, ct))
        {
            case Decision.NotFound: await Denied("document.create", $"matter #{matterId}", 404, ct); return NotFound();
            case Decision.Forbidden: await Denied("document.create", $"matter #{matterId}", 403, ct); return Forbid();
        }

        var now = DateTime.UtcNow;
        var doc = new Document
        {
            MatterId = matterId, Title = req.Title.Trim(), DocumentType = req.DocumentType.Trim(), AuthorCode = me.Code,
            Keywords = req.Keywords?.Trim(), Notes = req.Notes?.Trim(), CreatedUtc = now, ModifiedUtc = now,
        };
        doc.Versions.Add(new DocumentVersion { Document = doc, VersionNumber = 1, StoragePath = req.StoragePath, CreatedByCode = me.Code, CreatedUtc = now });

        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);   // single SaveChanges = single transaction for profile + version

        var created = (await Get(doc.Id, ct)).Result as OkObjectResult;
        var detail = created?.Value as DocumentDetail;
        await activity.RecordAsync("document.create", detail?.MatterNumber, 201, $"document.create on {detail?.MatterNumber} → 201 Created (\"{doc.Title}\", doc {doc.Id})", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = doc.Id }, created?.Value);
    }

    /// <summary>
    /// Update a profile with optimistic concurrency. The client sends the version it last saw in <c>If-Match</c>;
    /// a mismatch returns 409 so two people editing the same profile cannot silently overwrite each other.
    /// </summary>
    [HttpPut("api/documents/{id:int}/profile")]
    [ProducesResponseType<DocumentDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<DocumentDetail>> UpdateProfile(int id, [FromBody] UpdateDocumentProfileRequest req,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ifMatch) || !long.TryParse(ifMatch.Trim().Trim('"'), out var expectedVersion))
            return Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "If-Match header with the document version is required.");

        var doc = await db.Documents.VisibleTo(me.Id).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) { await Denied("document.update", $"doc {id}", 404, ct); return NotFound(); }
        if (await CanEditAsync(db.Matters, doc.MatterId, me.Id, ct) != Decision.Allowed) { await Denied("document.update", $"doc {id}", 403, ct); return Forbid(); }

        if (doc.Version != expectedVersion)
        {
            await activity.RecordAsync("document.update", $"doc {id}", 409, $"document.update on doc {id} → 409 Conflict (saw v{expectedVersion}, current v{doc.Version})", ct: ct);
            return Conflict(new ProblemDetails
            {
                Title = "The document profile was changed by someone else.",
                Detail = $"You last saw version {expectedVersion}; the current version is {doc.Version}. Reload and reapply your change.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        doc.Title = req.Title.Trim();
        doc.DocumentType = req.DocumentType.Trim();
        doc.Keywords = req.Keywords?.Trim();
        doc.Notes = req.Notes?.Trim();

        try
        {
            await db.SaveChangesAsync(ct);   // concurrency token enforced at the database as well, not only above
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(doc).State = EntityState.Detached;   // so the activity insert below does not retry the failed update
            await activity.RecordAsync("document.update", $"doc {id}", 409, $"document.update on doc {id} → 409 Conflict (concurrent write)", ct: ct);
            return Conflict(new ProblemDetails { Title = "The document profile was changed concurrently.", Status = StatusCodes.Status409Conflict });
        }

        await activity.RecordAsync("document.update", $"doc {id}", 200, $"document.update on doc {id} → 200 OK (now v{doc.Version})", ct: ct);
        return await Get(id, ct);
    }

    private Task Denied(string action, string target, int status, CancellationToken ct) =>
        activity.RecordAsync(action, target, status, $"{action} on {target} → {status} {(status == 404 ? "Not Found" : "Forbidden")}", ct: ct);
}
