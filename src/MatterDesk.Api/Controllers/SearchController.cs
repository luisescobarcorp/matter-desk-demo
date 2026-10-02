using MatterDesk.Api.Auth;
using MatterDesk.Api.Contracts;
using MatterDesk.Api.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatterDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/search")]
public sealed class SearchController(SearchService search, ICurrentOperator me) : ControllerBase
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
        if (q.Length < SearchService.MinQueryLength)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["q"] = ["Enter at least two characters."] }));

        return Ok(await search.SearchAsync(q, page, pageSize, me.Id, ct));
    }
}
