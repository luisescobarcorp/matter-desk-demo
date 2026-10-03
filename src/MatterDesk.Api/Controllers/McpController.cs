using System.Text.Json;
using System.Text.Json.Nodes;
using MatterDesk.Api.Activities;
using MatterDesk.Api.Auth;
using MatterDesk.Api.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatterDesk.Api.Controllers;

/// <summary>
/// A minimal Model Context Protocol server over the Streamable HTTP transport (JSON responses, no SSE stream).
/// Each request is authenticated like any other API call; tool calls run under the resolved operator, so the
/// permission predicate — not the model, not a prompt — decides what an agent can see.
/// </summary>
[ApiController]
[Authorize]
[Route("mcp")]
[Route("mcp/{operatorCode:alpha:length(2,8)}")]   // header-less variant for remote connectors (demo only; see DevHeaderAuthenticationHandler)
public sealed class McpController(MatterTools tools, ICurrentOperator me, IActivityRecorder activity) : ControllerBase
{
    public const string ProtocolVersion = "2025-06-18";
    private const int ParseError = -32700, InvalidRequest = -32600, MethodNotFound = -32601, InvalidParams = -32602;

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Post([FromBody] JsonElement message, CancellationToken ct)
    {
        Response.Headers["MCP-Protocol-Version"] = ProtocolVersion;

        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("method", out var methodEl) || methodEl.ValueKind != JsonValueKind.String)
            return Ok(Error(null, InvalidRequest, "Expected a JSON-RPC 2.0 request object with a `method`."));

        var method = methodEl.GetString()!;
        JsonNode? id = message.TryGetProperty("id", out var idEl) && idEl.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? JsonNode.Parse(idEl.GetRawText()) : null;
        JsonElement? @params = message.TryGetProperty("params", out var p) ? p : null;

        // Notifications carry no id and get no body.
        if (id is null) return Accepted();

        var result = method switch
        {
            "initialize" => Result(id, new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject { ["listChanged"] = false },
                    ["prompts"] = new JsonObject { ["listChanged"] = false },
                },
                ["serverInfo"] = new JsonObject { ["name"] = "matterdesk", ["version"] = "0.1.0" },
                ["instructions"] = $"MatterDesk tools act as operator {me.Code}. Restricted matters the operator has not been granted are invisible; do not try to infer their existence.",
            }),
            "ping" => Result(id, new JsonObject()),
            "tools/list" => Result(id, new JsonObject
            {
                ["tools"] = new JsonArray(MatterTools.Descriptors.Select(t => (JsonNode)new JsonObject
                {
                    ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = t.InputSchema.DeepClone(),
                }).ToArray()),
            }),
            "tools/call" => await CallAsync(id, @params, ct),
            "prompts/list" => Result(id, new JsonObject
            {
                ["prompts"] = new JsonArray(MatterPrompts.Descriptors.Select(p => (JsonNode)MatterPrompts.ToJson(p)).ToArray()),
            }),
            "prompts/get" => await GetPromptAsync(id, @params, ct),
            _ => Error(id, MethodNotFound, $"Method '{method}' is not supported by this server."),
        };
        return Ok(result);
    }

    /// <summary>No server-initiated stream is offered; clients that open one get a clear answer instead of a hang.</summary>
    [HttpGet]
    public IActionResult Get() => StatusCode(StatusCodes.Status405MethodNotAllowed);

    /// <summary>Sessions are stateless here, so ending one is a no-op.</summary>
    [HttpDelete]
    public IActionResult Delete() => NoContent();

    private async Task<JsonObject> CallAsync(JsonNode id, JsonElement? @params, CancellationToken ct)
    {
        if (@params is not { ValueKind: JsonValueKind.Object } p || !p.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return Error(id, InvalidParams, "`params.name` is required.");

        JsonElement? args = p.TryGetProperty("arguments", out var a) ? a : null;
        var name = nameEl.GetString()!;
        var r = await tools.CallAsync(name, args, ct);
        await activity.RecordAsync(name, TargetOf(args), 200, Describe(name, args, r), OutcomeOf(r), ct);

        var result = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = r.Text }),
            ["isError"] = r.IsError,
        };
        if (r.Structured is not null) result["structuredContent"] = JsonSerializer.SerializeToNode(r.Structured, Json.Indented);
        return Result(id, result);
    }

    private async Task<JsonObject> GetPromptAsync(JsonNode id, JsonElement? @params, CancellationToken ct)
    {
        if (@params is not { ValueKind: JsonValueKind.Object } p || !p.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return Error(id, InvalidParams, "`params.name` is required.");

        var name = nameEl.GetString()!;
        JsonElement? args = p.TryGetProperty("arguments", out var a) ? a : null;

        if (MatterPrompts.MissingRequiredArgument(name, args) is { } missing)
            return Error(id, InvalidParams, $"Prompt '{name}' requires argument `{missing}`.");

        var prompt = MatterPrompts.Get(name, args, me.Code);
        if (prompt is null) return Error(id, InvalidParams, $"Unknown prompt '{name}'.");

        var target = MatterPrompts.Arg(args, "query") is { Length: > 0 } q ? q : MatterPrompts.Arg(args, "matterNumber") is { Length: > 0 } m ? m : null;
        await activity.RecordAsync($"prompt.{name}", target, 200, $"prompt.{name}{(target is null ? "" : $" \"{target}\"")} → prompt delivered", ct: ct);
        return Result(id, prompt);
    }

    // ----- activity summaries -------------------------------------------------------------------------------

    private static string? TargetOf(JsonElement? args) =>
        MatterPrompts.Arg(args, "query") is { Length: > 0 } q ? q : MatterPrompts.Arg(args, "matterNumber") is { Length: > 0 } m ? m : null;

    private static string OutcomeOf(ToolResult r) =>
        !r.IsError ? Outcomes.Ok
        : r.Text.Contains("restricted", StringComparison.OrdinalIgnoreCase) || r.Text.StartsWith("No matter", StringComparison.Ordinal) ? Outcomes.Denied
        : Outcomes.Error;

    private static string Describe(string name, JsonElement? args, ToolResult r)
    {
        var target = TargetOf(args);
        var head = target is null ? name : $"{name} \"{target}\"";
        if (r.IsError) return $"{head} → {r.Text.TrimEnd('.')}";

        var data = r.Structured is null ? null : JsonSerializer.SerializeToNode(r.Structured, Json.Indented);
        string Prop(string key) => data?[key]?.ToJsonString().Trim('"') ?? "?";
        string Plural(string key, string noun) => $"{Prop(key)} {noun}{(Prop(key) == "1" ? "" : "s")}";

        return name switch
        {
            "search_matters" => $"{head} → {Plural("total", "result")}",
            "list_documents" => $"{head} → {Plural("count", "document")}",
            "get_matter" => $"{head} → matter returned ({Prop("documentCount")} docs, {(Prop("canEdit") == "true" ? "can edit" : "read only")})",
            _ => $"{head} → ok",
        };
    }

    private static JsonObject Result(JsonNode id, JsonNode result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
