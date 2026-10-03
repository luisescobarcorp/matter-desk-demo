using MatterDesk.Api.Auth;
using MatterDesk.Api.Data;
using MatterDesk.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MatterDesk.Api.Activities;

public static class Channels
{
    public const string Web = "web", Cli = "cli", Mcp = "mcp";
    public const string ClientHeader = "X-Client";
    public const string CliClient = "matterdesk-cli";
}

public static class Outcomes
{
    public const string Ok = "ok", Denied = "denied", Conflict = "conflict", Error = "error";

    public static string FromStatus(int status) => status switch
    {
        < 300 => Ok,
        401 or 403 or 404 => Denied,
        409 or 412 or 428 => Conflict,
        _ => Error,
    };
}

/// <summary>
/// Appends one <see cref="ActivityEvent"/> per governed action. Cheap by design: a single INSERT on the request's
/// own DbContext, no reads, and a failure to record never fails the request it describes.
/// </summary>
public interface IActivityRecorder
{
    Task RecordAsync(string action, string? target, int httpStatus, string summary, string? outcome = null, CancellationToken ct = default);
}

public sealed class ActivityRecorder(MatterDeskDbContext db, ICurrentOperator me, IHttpContextAccessor http, ILogger<ActivityRecorder> log) : IActivityRecorder
{
    public async Task RecordAsync(string action, string? target, int httpStatus, string summary, string? outcome = null, CancellationToken ct = default)
    {
        var ev = new ActivityEvent
        {
            OccurredUtc = DateTime.UtcNow,
            OperatorCode = me.Code,
            Channel = ChannelOf(http.HttpContext),
            Action = action,
            Target = Clip(target, 300),
            Outcome = outcome ?? Outcomes.FromStatus(httpStatus),
            HttpStatus = httpStatus,
            Summary = Clip(summary, 500)!,
        };
        try
        {
            db.ActivityEvents.Add(ev);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            db.Entry(ev).State = EntityState.Detached;
            log.LogWarning(ex, "Activity event {Action} was not recorded.", action);
        }
    }

    public static string ChannelOf(HttpContext? ctx)
    {
        if (ctx is null) return Channels.Web;
        if (ctx.Request.Path.StartsWithSegments("/mcp")) return Channels.Mcp;
        return string.Equals(ctx.Request.Headers[Channels.ClientHeader], Channels.CliClient, StringComparison.OrdinalIgnoreCase) ? Channels.Cli : Channels.Web;
    }

    private static string? Clip(string? s, int max) => s is null || s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>
/// The schema is created with <c>EnsureCreated</c>, which does nothing on a database that already exists.
/// Adding a table to an existing demo database therefore needs this one idempotent statement per provider.
/// (A real codebase would carry an EF migration instead; see "What I would do differently" in the README.)
/// </summary>
public static class ActivitySchema
{
    public static async Task EnsureAsync(MatterDeskDbContext db, CancellationToken ct = default)
    {
        if (db.Database.IsSqlite())
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "ActivityEvents" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ActivityEvents" PRIMARY KEY AUTOINCREMENT,
                    "OccurredUtc" TEXT NOT NULL, "OperatorCode" TEXT NOT NULL, "Channel" TEXT NOT NULL, "Action" TEXT NOT NULL,
                    "Target" TEXT NULL, "Outcome" TEXT NOT NULL, "HttpStatus" INTEGER NOT NULL, "Summary" TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS "IX_ActivityEvents_OccurredUtc" ON "ActivityEvents" ("OccurredUtc");
                """, ct);
        else if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[ActivityEvents]', N'U') IS NULL BEGIN
                    CREATE TABLE [ActivityEvents] (
                        [Id] bigint NOT NULL IDENTITY CONSTRAINT [PK_ActivityEvents] PRIMARY KEY,
                        [OccurredUtc] datetime2 NOT NULL, [OperatorCode] nvarchar(8) NOT NULL, [Channel] nvarchar(8) NOT NULL,
                        [Action] nvarchar(64) NOT NULL, [Target] nvarchar(300) NULL, [Outcome] nvarchar(16) NOT NULL,
                        [HttpStatus] int NOT NULL, [Summary] nvarchar(500) NOT NULL);
                    CREATE INDEX [IX_ActivityEvents_OccurredUtc] ON [ActivityEvents] ([OccurredUtc]);
                END
                """, ct);
    }
}
