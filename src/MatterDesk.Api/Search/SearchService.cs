using MatterDesk.Api.Authorization;
using MatterDesk.Api.Contracts;
using MatterDesk.Api.Controllers;
using MatterDesk.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MatterDesk.Api.Search;

/// <summary>
/// Cross-matter search over matters, document profiles and profiled emails, shared by the REST endpoint
/// and the MCP tool so an agent can never see more than the operator it is acting for.
/// </summary>
public sealed class SearchService(MatterDeskDbContext db)
{
    public const int MinQueryLength = 2;

    public async Task<PagedResult<SearchHit>> SearchAsync(string q, int page, int pageSize, int operatorId, CancellationToken ct)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);

        // On SQL Server the document/email text search would use the full-text index (CONTAINS/FREETEXT);
        // LIKE is used here so the same query plan shape runs on SQLite in tests. The leading wildcard is
        // acceptable on these columns only because the full-text path replaces it in production.
        var pattern = $"%{q}%";

        // Project to a shared anonymous shape so EF Core translates the UNION ALL server-side
        // (a record constructor would force client evaluation and break the set operation).
        var matters = db.Matters.AsNoTracking().VisibleTo(operatorId)
            .Where(m => EF.Functions.Like(m.Title, pattern) || EF.Functions.Like(m.Number, pattern) || EF.Functions.Like(m.Client.Name, pattern))
            .Select(m => new { Kind = "matter", m.Id, MatterId = m.Id, MatterNumber = m.Number, m.Title, Snippet = (string?)m.Client.Name, WhenUtc = m.OpenedUtc });

        var documents = db.Documents.AsNoTracking().VisibleTo(operatorId)
            .Where(d => EF.Functions.Like(d.Title, pattern) || EF.Functions.Like(d.Keywords!, pattern))
            .Select(d => new { Kind = "document", d.Id, d.MatterId, MatterNumber = d.Matter.Number, d.Title, Snippet = d.Keywords, WhenUtc = d.ModifiedUtc });

        var emails = db.ProfiledEmails.AsNoTracking().VisibleTo(operatorId)
            .Where(e => EF.Functions.Like(e.Subject, pattern) || EF.Functions.Like(e.BodyPreview!, pattern))
            .Select(e => new { Kind = "email", e.Id, e.MatterId, MatterNumber = e.Matter.Number, Title = e.Subject, Snippet = e.BodyPreview, WhenUtc = e.ReceivedUtc });

        var union = matters.Concat(documents).Concat(emails);
        var total = await union.CountAsync(ct);
        var rows = await union.OrderByDescending(h => h.WhenUtc).ThenBy(h => h.Kind).ThenBy(h => h.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(h => new SearchHit(h.Kind, h.Id, h.MatterId, h.MatterNumber, h.Title, h.Snippet, h.WhenUtc)).ToList();

        return new PagedResult<SearchHit>(items, page, pageSize, total);
    }
}
