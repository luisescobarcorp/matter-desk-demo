using System.Text.Json;
using System.Text.Json.Nodes;
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
public sealed class McpController(MatterTools tools, ICurrentOperator me) : ControllerBase
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
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
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
        var r = await tools.CallAsync(nameEl.GetString()!, args, ct);

        var result = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = r.Text }),
            ["isError"] = r.IsError,
        };
        if (r.Structured is not null) result["structuredContent"] = JsonSerializer.SerializeToNode(r.Structured, Json.Indented);
        return Result(id, result);
    }

    private static JsonObject Result(JsonNode id, JsonNode result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
