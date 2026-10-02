using MatterDesk.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MatterDesk.Api.Authorization;

/// <summary>
/// The permission predicate, expressed once and composed into every query.
/// It is evaluated by SQL Server (as an EXISTS probe against MatterAccess), never by filtering rows in memory,
/// so a user outside a restricted matter gets an empty result rather than a row that was hidden too late.
/// </summary>
public static class MatterAccessPolicy
{
    public static IQueryable<Matter> VisibleTo(this IQueryable<Matter> matters, int operatorId) =>
        matters.Where(m => !m.IsRestricted || m.Access.Any(a => a.OperatorId == operatorId));

    public static IQueryable<Document> VisibleTo(this IQueryable<Document> documents, int operatorId) =>
        documents.Where(d => !d.Matter.IsRestricted || d.Matter.Access.Any(a => a.OperatorId == operatorId));

    public static IQueryable<ProfiledEmail> VisibleTo(this IQueryable<ProfiledEmail> emails, int operatorId) =>
        emails.Where(e => !e.Matter.IsRestricted || e.Matter.Access.Any(a => a.OperatorId == operatorId));

    public static IQueryable<Matter> EditableBy(this IQueryable<Matter> matters, int operatorId) =>
        matters.Where(m => m.Access.Any(a => a.OperatorId == operatorId && a.CanEdit)
                           || (!m.IsRestricted && !m.Access.Any()));

    public enum Decision { Allowed, Forbidden, NotFound }

    /// <summary>One round-trip that distinguishes "no such matter" from "exists but not yours".</summary>
    public static async Task<Decision> CanViewAsync(DbSet<Matter> matters, int matterId, int operatorId, CancellationToken ct)
    {
        var probe = await matters.AsNoTracking()
            .Where(m => m.Id == matterId)
            .Select(m => new { m.IsRestricted, HasGrant = m.Access.Any(a => a.OperatorId == operatorId) })
            .FirstOrDefaultAsync(ct);

        if (probe is null) return Decision.NotFound;
        return !probe.IsRestricted || probe.HasGrant ? Decision.Allowed : Decision.Forbidden;
    }

    public static async Task<Decision> CanEditAsync(DbSet<Matter> matters, int matterId, int operatorId, CancellationToken ct)
    {
        var probe = await matters.AsNoTracking()
            .Where(m => m.Id == matterId)
            .Select(m => new
            {
                m.IsRestricted,
                HasGrant = m.Access.Any(a => a.OperatorId == operatorId),
                CanEdit = m.Access.Any(a => a.OperatorId == operatorId && a.CanEdit),
                AnyGrants = m.Access.Any()
            })
            .FirstOrDefaultAsync(ct);

        if (probe is null) return Decision.NotFound;
        if (probe.IsRestricted && !probe.HasGrant) return Decision.Forbidden;
        if (probe.CanEdit || (!probe.AnyGrants && !probe.IsRestricted)) return Decision.Allowed;
        return Decision.Forbidden;
    }
}
