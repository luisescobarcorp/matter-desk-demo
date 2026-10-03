using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MatterDesk.Api.Tests;

/// <summary>
/// Every governed action lands in the activity feed with its channel, and the MCP prompts surface as slash
/// commands that only tell the model which tools to call.
/// </summary>
public sealed class ActivityAndPromptTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public ActivityAndPromptTests(ApiFactory f) => _f = f;

    private static object Rpc(string method, object? @params = null) => new { jsonrpc = "2.0", id = 1, method, @params };

    private static async Task<JsonElement> Mcp(HttpClient c, string method, object? @params = null)
    {
        var r = await c.PostAsJsonAsync("/mcp", Rpc(method, @params));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Mcp_tool_calls_are_recorded_with_channel_mcp_and_a_readable_summary()
    {
        var jdu = _f.As("JDU");
        await Mcp(jdu, "tools/call", new { name = "search_matters", arguments = new { query = "Falcon" } });

        var les = _f.As("LES");
        await Mcp(les, "tools/call", new { name = "get_matter", arguments = new { matterNumber = "10099-0001" } });   // restricted → denied

        var feed = await les.GetFromJsonAsync<JsonElement>("/api/activity?take=10");
        var rows = feed.EnumerateArray().ToList();

        var search = rows.First(r => r.GetProperty("action").GetString() == "search_matters" && r.GetProperty("operatorCode").GetString() == "JDU");
        Assert.Equal("mcp", search.GetProperty("channel").GetString());
        Assert.Equal("ok", search.GetProperty("outcome").GetString());
        Assert.Equal("Falcon", search.GetProperty("target").GetString());
        Assert.Matches(@"^search_matters ""Falcon"" → \d+ results?$", search.GetProperty("summary").GetString());

        var denied = rows.First(r => r.GetProperty("action").GetString() == "get_matter" && r.GetProperty("operatorCode").GetString() == "LES");
        Assert.Equal("denied", denied.GetProperty("outcome").GetString());
        Assert.DoesNotContain("Falcon", denied.GetProperty("summary").GetString());   // the restricted title never leaks into the log

        // Newest first, and `after` returns only what is newer than the cursor.
        Assert.True(rows[0].GetProperty("id").GetInt64() > rows[^1].GetProperty("id").GetInt64());
        var newer = await les.GetFromJsonAsync<JsonElement>($"/api/activity?after={rows[0].GetProperty("id").GetInt64()}");
        Assert.Empty(newer.EnumerateArray());
    }

    [Fact]
    public async Task Rest_calls_are_recorded_with_channel_web_or_cli_from_the_client_header()
    {
        var web = _f.As("LES");
        await web.GetAsync("/api/search?q=Acme");

        var cli = _f.As("LES");
        cli.DefaultRequestHeaders.Add("X-Client", "matterdesk-cli");
        var matters = await cli.GetFromJsonAsync<JsonElement>("/api/matters?pageSize=50");
        var acmeId = matters.GetProperty("items").EnumerateArray().First(m => m.GetProperty("number").GetString() == "10042-0003").GetProperty("id").GetInt32();
        var created = await cli.PostAsJsonAsync($"/api/matters/{acmeId}/documents", new { title = "Activity test", documentType = "Note", storagePath = "t/a.txt" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var rows = (await web.GetFromJsonAsync<JsonElement>("/api/activity?take=10")).EnumerateArray().ToList();
        var search = rows.First(r => r.GetProperty("action").GetString() == "search" && r.GetProperty("target").GetString() == "Acme");
        Assert.Equal("web", search.GetProperty("channel").GetString());

        var create = rows.First(r => r.GetProperty("action").GetString() == "document.create");
        Assert.Equal("cli", create.GetProperty("channel").GetString());
        Assert.Equal(201, create.GetProperty("httpStatus").GetInt32());
        Assert.StartsWith("document.create on 10042-0003 → 201 Created", create.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Initialize_advertises_prompts_and_prompts_list_returns_three()
    {
        var init = await Mcp(_f.As("LES"), "initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "0" } });
        Assert.True(init.GetProperty("result").GetProperty("capabilities").TryGetProperty("prompts", out _));

        var list = await Mcp(_f.As("LES"), "prompts/list");
        var prompts = list.GetProperty("result").GetProperty("prompts").EnumerateArray().ToList();
        Assert.Equal(["demo", "find_matter", "file_check"], prompts.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal("query", prompts[1].GetProperty("arguments")[0].GetProperty("name").GetString());
        Assert.True(prompts[2].GetProperty("arguments")[0].GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task Prompts_get_demo_returns_a_user_message_and_is_recorded()
    {
        var jdu = _f.As("JDU");
        var get = await Mcp(jdu, "prompts/get", new { name = "demo", arguments = new { } });
        var messages = get.GetProperty("result").GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        var text = messages[0].GetProperty("content").GetProperty("text").GetString()!;
        Assert.Contains("search_matters", text);
        Assert.Contains("Falcon", text);
        Assert.Contains("JDU", text);
        Assert.Contains("Activity panel", text);

        var missing = await Mcp(jdu, "prompts/get", new { name = "find_matter" });
        Assert.Equal(-32602, missing.GetProperty("error").GetProperty("code").GetInt32());

        var unknown = await Mcp(jdu, "prompts/get", new { name = "nope" });
        Assert.Equal(-32602, unknown.GetProperty("error").GetProperty("code").GetInt32());

        var rows = (await jdu.GetFromJsonAsync<JsonElement>("/api/activity?take=10")).EnumerateArray().ToList();
        var row = rows.First(r => r.GetProperty("action").GetString() == "prompt.demo");
        Assert.Equal("mcp", row.GetProperty("channel").GetString());
        Assert.Equal("JDU", row.GetProperty("operatorCode").GetString());
    }
}
