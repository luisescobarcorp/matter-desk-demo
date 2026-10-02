using System.Security.Claims;
using System.Text.Encodings.Web;
using MatterDesk.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MatterDesk.Api.Auth;

public static class Schemes
{
    public const string Smart = "Smart";          // policy scheme: picks Bearer or DevHeader per request
    public const string Bearer = "Bearer";        // Entra ID / any OIDC issuer (production)
    public const string DevHeader = "DevHeader";  // X-Operator-Code header (local dev and tests only)
    public const string OperatorCodeClaim = "operator_code";
}

/// <summary>Resolves the calling operator once per request from whichever scheme authenticated it.</summary>
public interface ICurrentOperator
{
    int Id { get; }
    string Code { get; }
}

public sealed class CurrentOperator : ICurrentOperator
{
    public int Id { get; init; }
    public required string Code { get; init; }
}

public sealed class CurrentOperatorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, MatterDeskDbContext db)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            // Bearer tokens carry the firm's operator code as a claim (app-role / optional claim in Entra).
            // Falling back to preferred_username lets a plain OIDC token map by email.
            var code = ctx.User.FindFirstValue(Schemes.OperatorCodeClaim);
            var email = ctx.User.FindFirstValue("preferred_username") ?? ctx.User.FindFirstValue(ClaimTypes.Email);

            var op = await db.Operators.AsNoTracking()
                .Where(o => (code != null && o.Code == code) || (email != null && o.Email == email))
                .Select(o => new { o.Id, o.Code })
                .FirstOrDefaultAsync(ctx.RequestAborted);

            if (op is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { title = "Authenticated identity is not a known operator.", status = 403 });
                return;
            }
            ctx.Items[nameof(ICurrentOperator)] = new CurrentOperator { Id = op.Id, Code = op.Code };
        }
        await next(ctx);
    }
}

/// <summary>Scoped accessor so controllers and services can inject the resolved operator.</summary>
public sealed class HttpCurrentOperator(IHttpContextAccessor accessor) : ICurrentOperator
{
    private CurrentOperator Resolved =>
        accessor.HttpContext?.Items[nameof(ICurrentOperator)] as CurrentOperator
        ?? throw new InvalidOperationException("No authenticated operator on this request.");

    public int Id => Resolved.Id;
    public string Code => Resolved.Code;
}

public sealed class DevHeaderOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-Operator-Code";
}

/// <summary>
/// Development-only scheme: trusts an operator code supplied in a header.
/// Never registered outside Development/Testing environments (see Program.cs).
/// </summary>
public sealed class DevHeaderAuthenticationHandler(
    IOptionsMonitor<DevHeaderOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<DevHeaderOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var raw) || string.IsNullOrWhiteSpace(raw))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity([new Claim(Schemes.OperatorCodeClaim, raw.ToString().Trim().ToUpperInvariant())], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
