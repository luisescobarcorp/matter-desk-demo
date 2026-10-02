using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MatterDesk.Api.Tests;

/// <summary>
/// The MCP surface must be exactly as blind as the REST surface: an agent acting for LES cannot find,
/// open or list the restricted matter, and the error text does not leak its title.
/// </summary>
public sealed class McpTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public McpTests(ApiFactory f) => _f = f;

    private static object Rpc(string method, object? @params = null, int? id = 1) =>
        id is null ? new { jsonrpc = "2.0", method, @params } : new { jsonrpc = "2.0", id, method, @params };

    private static async Task<JsonElement> Call(HttpClient c, string method, object? @params = null)
    {
        var r = await c.PostAsJsonAsync("/mcp", Rpc(method, @params));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("2025-06-18", r.Headers.GetValues("MCP-Protocol-Version").Single());
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
    }

    private static async Task<JsonElement> Tool(HttpClient c, string name, object args) =>
        await Call(c, "tools/call", new { name, arguments = args });

    [Fact]
    public async Task Unauthenticated_mcp_request_is_401()
    {
        var r = await _f.CreateClient().PostAsJsonAsync("/mcp", Rpc("tools/list"));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Initialize_and_tools_list_describe_three_tools()
    {
        var init = await Call(_f.As("LES"), "initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "0" } });
        Assert.Equal("2025-06-18", init.GetProperty("protocolVersion").GetString());
        Assert.Equal("matterdesk", init.GetProperty("serverInfo").GetProperty("name").GetString());

        var notify = await _f.As("LES").PostAsJsonAsync("/mcp", Rpc("notifications/initialized", id: null));
        Assert.Equal(HttpStatusCode.Accepted, notify.StatusCode);

        var tools = (await Call(_f.As("LES"), "tools/list")).GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Equal(["search_matters", "get_matter", "list_documents"], tools);
    }

    [Fact]
    public async Task Search_tool_is_blind_to_restricted_matter_for_ungranted_operator()
    {
        var les = await Tool(_f.As("LES"), "search_matters", new { query = "Falcon" });
        Assert.False(les.GetProperty("isError").GetBoolean());
        Assert.Equal(0, les.GetProperty("structuredContent").GetProperty("total").GetInt32());
        Assert.DoesNotContain("Falcon", les.GetProperty("content")[0].GetProperty("text").GetString()!.Replace("\"query\": \"Falcon\"", ""));

        var jdu = await Tool(_f.As("JDU"), "search_matters", new { query = "Falcon" });
        Assert.True(jdu.GetProperty("structuredContent").GetProperty("total").GetInt32() >= 1);
    }

    [Fact]
    public async Task Get_matter_and_list_documents_enforce_the_grant()
    {
        var denied = await Tool(_f.As("LES"), "get_matter", new { matterNumber = "10099-0001" });
        Assert.True(denied.GetProperty("isError").GetBoolean());
        Assert.DoesNotContain("Falcon", denied.GetProperty("content")[0].GetProperty("text").GetString());

        var deniedDocs = await Tool(_f.As("LES"), "list_documents", new { matterNumber = "10099-0001" });
        Assert.True(deniedDocs.GetProperty("isError").GetBoolean());

        var granted = await Tool(_f.As("JDU"), "get_matter", new { matterNumber = "10099-0001" });
        Assert.False(granted.GetProperty("isError").GetBoolean());
        Assert.True(granted.GetProperty("structuredContent").GetProperty("isRestricted").GetBoolean());

        var docs = await Tool(_f.As("JDU"), "list_documents", new { matterNumber = "10099-0001" });
        Assert.True(docs.GetProperty("structuredContent").GetProperty("count").GetInt32() >= 1);

        var missing = await Tool(_f.As("JDU"), "get_matter", new { matterNumber = "00000-0000" });
        Assert.True(missing.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task Unknown_method_is_a_json_rpc_error_not_a_crash()
    {
        var r = await _f.As("LES").PostAsJsonAsync("/mcp", Rpc("resources/list"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(-32601, body.GetProperty("error").GetProperty("code").GetInt32());
    }
}
