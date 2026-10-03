using System.Text.Json;
using System.Text.Json.Nodes;

namespace MatterDesk.Api.Mcp;

/// <summary>A prompt as advertised by <c>prompts/list</c>: a named, parameterised instruction the client offers as a slash command.</summary>
public sealed record PromptDescriptor(string Name, string Title, string Description, IReadOnlyList<PromptArgument> Arguments);

public sealed record PromptArgument(string Name, string Description, bool Required);

/// <summary>
/// The prompts MatterDesk exposes over MCP. They surface as slash commands in Claude Desktop and Cursor and
/// simply tell the model which MatterDesk tools to call; the tools, not the prompt text, enforce who sees what.
/// </summary>
public static class MatterPrompts
{
    public static IReadOnlyList<PromptDescriptor> Descriptors { get; } =
    [
        new("demo", "MatterDesk demo",
            "Walk the MatterDesk tools end to end: search for Falcon, list documents, open the matter, and report what was visible.",
            []),
        new("find_matter", "Find a matter",
            "Search matters, documents and email for a query and present the hits as a table.",
            [new("query", "Text to search for (matter number, title, client, document keywords).", true)]),
        new("file_check", "File check",
            "Review one matter: profile, documents filed, anything privileged, and a short summary.",
            [new("matterNumber", "Matter number in CCCCC-MMMM form, e.g. 10042-0003.", true)]),
    ];

    /// <summary>Returns the <c>prompts/get</c> result, or <c>null</c> when the prompt name is unknown.</summary>
    public static JsonObject? Get(string name, JsonElement? args, string operatorCode)
    {
        var text = name switch
        {
            "demo" => $"""
                You are connected to MatterDesk, a legal matter-management system, acting as operator {operatorCode}. Run this walkthrough using only the MatterDesk tools:

                1. Call `search_matters` with query "Falcon".
                2. Take the first hit's matter number and call `list_documents` for it.
                3. Call `get_matter` for that same matter number.
                4. Summarise what you found: the matter, its client, how many documents are filed and their titles, and whether you may edit it.

                If a step returns an error saying the matter is restricted or does not exist, report that verbatim rather than guessing.
                Finish by stating explicitly which operator you acted as ({operatorCode}) and that each tool call you made was recorded in the MatterDesk Activity panel, where the person watching can see it alongside actions taken in the browser and from the local executable.
                """,
            "find_matter" => $"""
                Acting as MatterDesk operator {operatorCode}, call `search_matters` with query "{Arg(args, "query")}".
                Present the hits as a table with columns: kind, matter number, title, client (if present in the title or snippet). Group document and email hits under their matter number.
                If the hits belong to exactly one matter, also call `list_documents` for that matter number and list its documents under the table.
                If there are no hits, say so plainly; do not speculate about matters that might exist but are not visible to {operatorCode}.
                """,
            "file_check" => $"""
                Acting as MatterDesk operator {operatorCode}, review matter {Arg(args, "matterNumber")}:

                1. Call `get_matter` for it and note the client, area of law, open date and whether {operatorCode} may edit it.
                2. Call `list_documents` for it.
                3. Flag any document whose title or keywords mark it as privileged (for example "PRIVILEGED" or "privileged").
                4. Summarise in a short paragraph: what the matter is, how complete the file looks, and anything that needs attention.

                If the matter is restricted and {operatorCode} has no grant, the tools will say so; report that and stop.
                """,
            _ => null,
        };
        if (text is null) return null;

        var d = Descriptors.First(p => p.Name == name);
        return new JsonObject
        {
            ["description"] = d.Description,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonObject { ["type"] = "text", ["text"] = text },
            }),
        };
    }

    public static string? MissingRequiredArgument(string name, JsonElement? args) =>
        Descriptors.FirstOrDefault(p => p.Name == name)?.Arguments
            .FirstOrDefault(a => a.Required && string.IsNullOrWhiteSpace(Arg(args, a.Name)))?.Name;

    public static string Arg(JsonElement? args, string name) =>
        args is { ValueKind: JsonValueKind.Object } a && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    public static JsonObject ToJson(PromptDescriptor p) => new()
    {
        ["name"] = p.Name,
        ["title"] = p.Title,
        ["description"] = p.Description,
        ["arguments"] = new JsonArray(p.Arguments.Select(a => (JsonNode)new JsonObject
        {
            ["name"] = a.Name, ["description"] = a.Description, ["required"] = a.Required,
        }).ToArray()),
    };
}
