using System.Text.Json;
using System.Text.Json.Nodes;
using MatterDesk.Api.Auth;
using MatterDesk.Api.Authorization;
using MatterDesk.Api.Data;
using MatterDesk.Api.Search;
using Microsoft.EntityFrameworkCore;
using static MatterDesk.Api.Authorization.MatterAccessPolicy;

namespace MatterDesk.Api.Mcp;

/// <summary>A tool as advertised by <c>tools/list</c>.</summary>
public sealed record ToolDescriptor(string Name, string Description, JsonObject InputSchema);

/// <summary>Result of <c>tools/call</c>: text for the model, structured data for the client, and an error flag.</summary>
public sealed record ToolResult(string Text, object? Structured, bool IsError = false)
{
    public static ToolResult Error(string message) => new(message, null, true);
    public static ToolResult Ok(object structured) =>
        new(JsonSerializer.Serialize(structured, Json.Indented), structured);
}

internal static class Json
{
    public static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

/// <summary>
/// The tools MatterDesk exposes to an AI client over MCP. Every tool runs as the authenticated operator and
/// composes the same <see cref="MatterAccessPolicy"/> predicate as the REST API, so an agent acting for
/// operator LES cannot discover a restricted matter that LES cannot open in the product.
/// </summary>
public sealed class MatterTools(MatterDeskDbContext db, SearchService search, ICurrentOperator me)
{
    private const int MaxLimit = 50;

    public static IReadOnlyList<ToolDescriptor> Descriptors { get; } =
    [
        new("search_matters",
            "Search matters, documents and email visible to the operator; newest hits first.",
            Schema(("query", "string", "Text to match against matter numbers, titles, client names, document titles/keywords, and email subjects. At least two characters.", true),
                   ("limit", "integer", "Maximum number of hits to return.", false))),
        new("get_matter",
            "Get one matter's profile by matter number, including whether the operator may edit it.",
            Schema(("matterNumber", "string", "Matter number in CCCCC-MMMM form.", true))),
        new("list_documents",
            "List the documents filed to a matter, newest first; errors if the operator has no access.",
            Schema(("matterNumber", "string", "Matter number in CCCCC-MMMM form.", true),
                   ("limit", "integer", "Maximum number of documents to return (default 25, max 50).", false))),
    ];

    public async Task<ToolResult> CallAsync(string name, JsonElement? args, CancellationToken ct) => name switch
    {
        "search_matters" => await SearchAsync(Str(args, "query"), Int(args, "limit", 10), ct),
        "get_matter" => await GetMatterAsync(Str(args, "matterNumber"), ct),
        "list_documents" => await ListDocumentsAsync(Str(args, "matterNumber"), Int(args, "limit", 25), ct),
        _ => ToolResult.Error($"Unknown tool '{name}'."),
    };

    private async Task<ToolResult> SearchAsync(string? query, int limit, CancellationToken ct)
    {
        query = (query ?? "").Trim();
        if (query.Length < SearchService.MinQueryLength) return ToolResult.Error("`query` must be at least two characters.");

        var page = await search.SearchAsync(query, 1, Math.Clamp(limit, 1, MaxLimit), me.Id, ct);
        return ToolResult.Ok(new
        {
            @operator = me.Code,
            query,
            total = page.Total,
            hits = page.Items.Select(h => new { h.Kind, h.Id, h.MatterNumber, h.Title, h.Snippet, h.WhenUtc }),
        });
    }

    private async Task<ToolResult> GetMatterAsync(string? number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(number)) return ToolResult.Error("`matterNumber` is required.");

        var id = await db.Matters.AsNoTracking().Where(m => m.Number == number).Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);
        if (id is null) return ToolResult.Error($"No matter {number}.");
        if (await CanViewAsync(db.Matters, id.Value, me.Id, ct) != Decision.Allowed)
            return ToolResult.Error($"Matter {number} is restricted and operator {me.Code} has not been granted access.");

        var canEdit = await CanEditAsync(db.Matters, id.Value, me.Id, ct) == Decision.Allowed;
        var m = await db.Matters.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                x.Number, x.Title, x.AreaOfLaw, Client = new { x.Client.Number, x.Client.Name },
                x.IsRestricted, x.Status, x.ResponsibleCode, x.OpenedUtc, CanEdit = canEdit, DocumentCount = x.Documents.Count, EmailCount = x.Emails.Count,
            })
            .SingleAsync(ct);
        return ToolResult.Ok(m);
    }

    private async Task<ToolResult> ListDocumentsAsync(string? number, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(number)) return ToolResult.Error("`matterNumber` is required.");

        var id = await db.Matters.AsNoTracking().Where(m => m.Number == number).Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);
        if (id is null) return ToolResult.Error($"No matter {number}.");
        if (await CanViewAsync(db.Matters, id.Value, me.Id, ct) != Decision.Allowed)
            return ToolResult.Error($"Matter {number} is restricted and operator {me.Code} has not been granted access.");

        var docs = await db.Documents.AsNoTracking().Where(d => d.MatterId == id)
            .OrderByDescending(d => d.ModifiedUtc).Take(Math.Clamp(limit, 1, MaxLimit))
            .Select(d => new { d.Id, d.Title, d.DocumentType, d.AuthorCode, d.Keywords, d.ModifiedUtc, VersionCount = d.Versions.Count })
            .ToListAsync(ct);
        return ToolResult.Ok(new { matterNumber = number, count = docs.Count, documents = docs });
    }

    private static string? Str(JsonElement? args, string name) =>
        args is { ValueKind: JsonValueKind.Object } a && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement? args, string name, int fallback) =>
        args is { ValueKind: JsonValueKind.Object } a && a.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;

    private static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] props)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, type, description, isRequired) in props)
        {
            properties[name] = new JsonObject { ["type"] = type, ["description"] = description };
            if (isRequired) required.Add(name);
        }
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false };
    }
}
