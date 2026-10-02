using MatterDesk.Api.Auth;
using MatterDesk.Api.Authorization;
using MatterDesk.Api.Contracts;
using MatterDesk.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatterDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/search")]
public sealed class SearchController(MatterDeskDbContext db, ICurrentOperator me) : ControllerBase
{
    /// <summary>
    /// Cross-matter search over matters, document profiles and profiled emails.
    /// The access predicate is part of each query, so a restricted matter the operator cannot see
    /// contributes zero rows — its title never leaves the database.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<SearchHit>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<SearchHit>>> Search([FromQuery] string q, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        q = (q ?? "").Trim();
        if (q.Length < 2) return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["q"] = ["Enter at least two characters."] }));
        (page, pageSize) = Paging.Clamp(page, pageSize);

        // On SQL Server the document/email text search would use the full-text index (CONTAINS/FREETEXT);
        // LIKE is used here so the same query plan shape runs on SQLite in tests. The leading wildcard is
        // acceptable on these columns only because the full-text path replaces it in production.
        var pattern = $"%{q}%";

        // Project to a shared anonymous shape so EF Core translates the UNION ALL server-side
        // (a record constructor would force client evaluation and break the set operation).
        var matters = db.Matters.AsNoTracking().VisibleTo(me.Id)
            .Where(m => EF.Functions.Like(m.Title, pattern) || EF.Functions.Like(m.Number, pattern) || EF.Functions.Like(m.Client.Name, pattern))
            .Select(m => new { Kind = "matter", m.Id, MatterId = m.Id, MatterNumber = m.Number, m.Title, Snippet = (string?)m.Client.Name, WhenUtc = m.OpenedUtc });

        var documents = db.Documents.AsNoTracking().VisibleTo(me.Id)
            .Where(d => EF.Functions.Like(d.Title, pattern) || EF.Functions.Like(d.Keywords!, pattern))
            .Select(d => new { Kind = "document", d.Id, d.MatterId, MatterNumber = d.Matter.Number, d.Title, Snippet = d.Keywords, WhenUtc = d.ModifiedUtc });

        var emails = db.ProfiledEmails.AsNoTracking().VisibleTo(me.Id)
            .Where(e => EF.Functions.Like(e.Subject, pattern) || EF.Functions.Like(e.BodyPreview!, pattern))
            .Select(e => new { Kind = "email", e.Id, e.MatterId, MatterNumber = e.Matter.Number, Title = e.Subject, Snippet = e.BodyPreview, WhenUtc = e.ReceivedUtc });

        var union = matters.Concat(documents).Concat(emails);
        var total = await union.CountAsync(ct);
        var rows = await union.OrderByDescending(h => h.WhenUtc).ThenBy(h => h.Kind).ThenBy(h => h.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(h => new SearchHit(h.Kind, h.Id, h.MatterId, h.MatterNumber, h.Title, h.Snippet, h.WhenUtc)).ToList();

        return Ok(new PagedResult<SearchHit>(items, page, pageSize, total));
    }
}
