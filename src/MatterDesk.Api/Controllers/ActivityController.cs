using MatterDesk.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatterDesk.Api.Controllers;

public sealed record ActivityEventDto(long Id, DateTime OccurredUtc, string OperatorCode, string Channel, string Action, string? Target, string Outcome, int HttpStatus, string Summary);

/// <summary>
/// The activity feed behind the Activity panel. Any authenticated operator sees the whole feed: it is an audit
/// trail of *who did what through which surface*, and in this demo the point is to watch the browser, the local
/// executable and an AI client land in the same log. Rows carry no matter titles, only numbers, ids and query text.
/// </summary>
[ApiController]
[Authorize]
[Route("api/activity")]
public sealed class ActivityController(MatterDeskDbContext db) : ControllerBase
{
    /// <summary>Newest events first. Pass <c>after</c> (the highest id already seen) to poll for only what is new.</summary>
    [HttpGet]
    [ProducesResponseType<List<ActivityEventDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ActivityEventDto>>> List([FromQuery] long? after, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        var q = db.ActivityEvents.AsNoTracking();
        if (after is > 0) q = q.Where(e => e.Id > after);

        var items = await q.OrderByDescending(e => e.Id).Take(take)
            .Select(e => new ActivityEventDto(e.Id, e.OccurredUtc, e.OperatorCode, e.Channel, e.Action, e.Target, e.Outcome, e.HttpStatus, e.Summary))
            .ToListAsync(ct);
        return Ok(items);
    }
}
