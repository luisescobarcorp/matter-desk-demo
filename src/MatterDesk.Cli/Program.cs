using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// MatterDesk local client.
//
//   matterdesk demo [--operator LES] [--api URL]      walk the REST and MCP surface and show what each response proves
//   matterdesk matters | search <q> | documents <matterNumber> | tools | call <tool> [key=value ...]
//   matterdesk mcp [--operator LES] [--api URL]       run as a stdio MCP server that bridges to the hosted /mcp endpoint
//
// A local executable talking to a remote API over HTTPS is the same shape as a desktop component talking to a
// web tier: it has to authenticate, handle 401/403/404/409 deliberately, retry transient failures, and never
// assume the server is the one it was built against.

var (command, positional, options) = Args.Parse(args);
var api = (options.GetValueOrDefault("api") ?? Environment.GetEnvironmentVariable("MATTERDESK_API") ?? "https://szhzkeau4r.us-east-1.awsapprunner.com").TrimEnd('/');
var op = (options.GetValueOrDefault("operator") ?? Environment.GetEnvironmentVariable("MATTERDESK_OPERATOR") ?? "LES").ToUpperInvariant();
var client = new MatterDeskClient(api, op);

try
{
    return command switch
    {
        "demo" => await Demo.RunAsync(client, api, op),
        "matters" => await Print(client.GetAsync("/api/matters?pageSize=50")),
        "search" => await Print(client.GetAsync($"/api/search?q={Uri.EscapeDataString(positional.ElementAtOrDefault(0) ?? "")}")),
        "documents" => await Documents(client, positional.ElementAtOrDefault(0)),
        "tools" => await Print(client.McpAsync("tools/list")),
        "call" => await Print(client.McpAsync("tools/call", new JsonObject
        {
            ["name"] = positional.ElementAtOrDefault(0) ?? "",
            ["arguments"] = Args.KeyValues(positional.Skip(1)),
        })),
        "mcp" => await StdioBridge.RunAsync(client),
        _ => Usage(),
    };
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Cannot reach {api}: {ex.Message}");
    return 2;
}

static int Usage()
{
    Console.WriteLine("""
        matterdesk — local client for the MatterDesk API

          demo                         walk the REST + MCP surface and explain each response
          matters                      list matters visible to the operator
          search <text>                cross-matter search
          documents <matterNumber>     documents filed to a matter
          tools                        MCP tools/list
          call <tool> [k=v ...]        MCP tools/call, e.g. call search_matters query=Falcon
          mcp                          run as a stdio MCP server bridging to the hosted endpoint

        options: --operator LES|JDU|PAR   --api https://host   (or MATTERDESK_OPERATOR / MATTERDESK_API)
        """);
    return 1;
}

static async Task<int> Print(Task<HttpResponseMessage> task)
{
    using var r = await task;
    var body = await r.Content.ReadAsStringAsync();
    Console.WriteLine($"HTTP {(int)r.StatusCode} {r.ReasonPhrase}");
    Console.WriteLine(Pretty(body));
    return r.IsSuccessStatusCode ? 0 : 1;
}

static async Task<int> Documents(MatterDeskClient c, string? number)
{
    if (string.IsNullOrWhiteSpace(number)) { Console.Error.WriteLine("matter number required, e.g. 10042-0003"); return 1; }
    var id = await c.MatterIdAsync(number);
    if (id is null) { Console.Error.WriteLine($"Matter {number} is not visible to {c.Operator} (or does not exist)."); return 1; }
    return await Print(c.GetAsync($"/api/matters/{id}/documents"));
}

static string Pretty(string json)
{
    try { return JsonSerializer.Serialize(JsonDocument.Parse(json), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }); }
    catch { return json; }
}

// ------------------------------------------------------------------------------------------------------------

sealed class MatterDeskClient
{
    private readonly HttpClient _http;
    public string Operator { get; }
    public string BaseUrl { get; }

    public MatterDeskClient(string baseUrl, string op)
    {
        BaseUrl = baseUrl; Operator = op;
        _http = new HttpClient { BaseAddress = new Uri(baseUrl + "/"), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("X-Operator-Code", op);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("matterdesk-cli/0.1");
    }

    public MatterDeskClient As(string op) => new(BaseUrl, op);

    public Task<HttpResponseMessage> GetAsync(string path) => Send(() => new HttpRequestMessage(HttpMethod.Get, path.TrimStart('/')));

    public Task<HttpResponseMessage> PostJsonAsync(string path, object body) =>
        Send(() => new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/')) { Content = JsonContent.Create(body) });

    public Task<HttpResponseMessage> PutJsonAsync(string path, object body, string? ifMatch) =>
        Send(() =>
        {
            var m = new HttpRequestMessage(HttpMethod.Put, path.TrimStart('/')) { Content = JsonContent.Create(body) };
            if (ifMatch is not null) m.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            return m;
        });

    public Task<HttpResponseMessage> McpAsync(string method, JsonNode? @params = null, JsonNode? id = null)
    {
        var msg = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (id is not null) msg["id"] = id.DeepClone(); else if (method is not ("notifications/initialized")) msg["id"] = 1;
        if (@params is not null) msg["params"] = @params.DeepClone();
        return Send(() => new HttpRequestMessage(HttpMethod.Post, "mcp")
        {
            Content = new StringContent(msg.ToJsonString(), Encoding.UTF8, "application/json"),
        });
    }

    public async Task<int?> MatterIdAsync(string number)
    {
        using var r = await GetAsync("/api/matters?pageSize=200");
        if (!r.IsSuccessStatusCode) return null;
        var page = await r.Content.ReadFromJsonAsync<JsonObject>();
        return page?["items"]?.AsArray().FirstOrDefault(m => string.Equals(m?["number"]?.GetValue<string>(), number, StringComparison.OrdinalIgnoreCase))?["id"]?.GetValue<int>();
    }

    // Transient failures (503 while the service scales, 429 throttling, socket resets) are retried with backoff.
    // Everything else is returned to the caller: a 403 or 409 is a decision, not a fault.
    private async Task<HttpResponseMessage> Send(Func<HttpRequestMessage> make)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var r = await _http.SendAsync(make());
                if (r.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests or HttpStatusCode.GatewayTimeout && attempt < 4)
                {
                    var wait = r.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt));
                    r.Dispose(); await Task.Delay(wait); continue;
                }
                return r;
            }
            catch (HttpRequestException) when (attempt < 4) { await Task.Delay(TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt))); }
        }
    }
}

// ------------------------------------------------------------------------------------------------------------

static class Demo
{
    public static async Task<int> RunAsync(MatterDeskClient c, string api, string op)
    {
        Console.OutputEncoding = Encoding.UTF8;
        H($"MatterDesk local client → {api}  (operator {op})");
        Console.WriteLine("Each step is a real HTTP call to the API. The point is not the data; it is what each status code proves.\n");

        // 1. Health, no auth
        using (var r = await c.GetAsync("/healthz"))
            Step("GET /healthz (anonymous)", r, "liveness endpoint is reachable without credentials");

        // 2. No credentials → 401
        using (var r = await c.As("").GetAsync("/api/matters"))
            Step("GET /api/matters with no operator", r, "no identity → 401, not an empty list");

        // 3. Unknown operator → 403
        using (var r = await c.As("NOPE").GetAsync("/api/matters"))
            Step("GET /api/matters as NOPE", r, "authenticated but not a known operator → 403");

        // 4. Visible matters per operator
        var visible = new Dictionary<string, JsonArray>();
        foreach (var who in new[] { "LES", "JDU", "PAR" })
        {
            using var r = await c.As(who).GetAsync("/api/matters?pageSize=50");
            var page = await r.Content.ReadFromJsonAsync<JsonObject>();
            var items = page?["items"]?.AsArray() ?? [];
            visible[who] = items;
            Step($"GET /api/matters as {who}", r, $"{items.Count} matters: {string.Join(", ", items.Select(m => m?["number"]?.GetValue<string>()))}");
        }
        var restricted = visible["JDU"].FirstOrDefault(m => m?["isRestricted"]?.GetValue<bool>() == true);
        var restrictedId = restricted?["id"]?.GetValue<int>() ?? 0;
        var restrictedNo = restricted?["number"]?.GetValue<string>() ?? "?";
        Note($"The restricted matter {restrictedNo} appears for JDU only. The predicate runs in SQL (EXISTS against MatterAccess); the row never left the database for LES or PAR.");

        // 5. 403 vs 404 on the restricted matter
        using (var r = await c.As("LES").GetAsync($"/api/matters/{restrictedId}"))
            Step($"GET /api/matters/{restrictedId} as LES", r, "exists but not granted → 403 (matter numbers are on every bill; an explicit denial is acceptable)");
        using (var r = await c.As("JDU").GetAsync($"/api/matters/{restrictedId}/documents"))
        {
            var docs = await r.Content.ReadFromJsonAsync<JsonObject>();
            var firstDoc = docs?["items"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<int>() ?? 0;
            Step($"GET /api/matters/{restrictedId}/documents as JDU", r, $"{docs?["total"]} documents");
            using var d = await c.As("LES").GetAsync($"/api/documents/{firstDoc}");
            Step($"GET /api/documents/{firstDoc} as LES", d, "a document id must not reveal that a restricted file exists → 404, not 403");
        }

        // 6. Search never leaks
        using (var r = await c.As("LES").GetAsync("/api/search?q=Falcon"))
            Step("GET /api/search?q=Falcon as LES", r, $"total {(await r.Content.ReadFromJsonAsync<JsonObject>())?["total"]} — the restricted title never reaches the client");
        using (var r = await c.As("JDU").GetAsync("/api/search?q=Falcon"))
            Step("GET /api/search?q=Falcon as JDU", r, $"total {(await r.Content.ReadFromJsonAsync<JsonObject>())?["total"]} — same query, same SQL, different operator");

        // 7. Validation is problem+json
        using (var r = await c.As("LES").GetAsync("/api/search?q=a"))
            Step("GET /api/search?q=a", r, $"{r.Content.Headers.ContentType?.MediaType} — every error, including validation, is RFC 7807");

        // 8. Concurrency: create a profile, update with a stale version, retry
        var acmeId = visible["LES"].FirstOrDefault(m => m?["number"]?.GetValue<string>() == "10042-0003")?["id"]?.GetValue<int>() ?? 0;
        var title = $"CLI demo note {DateTime.UtcNow:yyyyMMdd-HHmmss}";
        using (var r = await c.As("LES").PostJsonAsync($"/api/matters/{acmeId}/documents", new { title, documentType = "Note", keywords = "cli demo", storagePath = $"demo/{title}.txt" }))
        {
            Step($"POST /api/matters/{acmeId}/documents as LES", r, $"201 with Location {r.Headers.Location} and ETag {r.Headers.ETag}");
            var created = await r.Content.ReadFromJsonAsync<JsonObject>();
            var docId = created?["id"]?.GetValue<int>() ?? 0;
            var etag = r.Headers.ETag?.Tag ?? "\"1\"";
            var body = new { title, documentType = "Note", keywords = "cli demo, edited", notes = "edited by the CLI" };

            using (var p0 = await c.As("LES").PutJsonAsync($"/api/documents/{docId}/profile", body, null))
                Step($"PUT /api/documents/{docId}/profile without If-Match", p0, "428 Precondition Required — the client must say which version it edited");
            using (var p1 = await c.As("LES").PutJsonAsync($"/api/documents/{docId}/profile", body, etag))
                Step($"PUT /api/documents/{docId}/profile If-Match: {etag}", p1, $"200, new ETag {p1.Headers.ETag}");
            using (var p2 = await c.As("LES").PutJsonAsync($"/api/documents/{docId}/profile", body, etag))
                Step($"PUT /api/documents/{docId}/profile If-Match: {etag} again", p2, "409 Conflict — a second writer with the old version cannot silently overwrite the first");
            using (var p3 = await c.As("PAR").PutJsonAsync($"/api/documents/{docId}/profile", body, "\"2\""))
                Step($"PUT /api/documents/{docId}/profile as PAR", p3, "PAR can view Acme but has no edit grant → 403");
        }

        // 9. MCP
        using (var r = await c.As("LES").McpAsync("tools/list"))
        {
            var tools = (await r.Content.ReadFromJsonAsync<JsonObject>())?["result"]?["tools"]?.AsArray().Select(t => t?["name"]?.GetValue<string>());
            Step("POST /mcp tools/list as LES", r, $"tools: {string.Join(", ", tools ?? [])}");
        }
        foreach (var who in new[] { "LES", "JDU" })
        {
            using var r = await c.As(who).McpAsync("tools/call", new JsonObject { ["name"] = "search_matters", ["arguments"] = new JsonObject { ["query"] = "Falcon" } });
            var total = (await r.Content.ReadFromJsonAsync<JsonObject>())?["result"]?["structuredContent"]?["total"];
            Step($"POST /mcp tools/call search_matters(Falcon) as {who}", r, $"total {total} — an AI agent is exactly as blind as the operator it acts for");
        }

        Console.WriteLine();
        H("Done.");
        Console.WriteLine($"Open {api}/#about for the background, {api}/swagger for the API, or run `matterdesk mcp` to use this executable as an MCP connector.");
        return 0;
    }

    static void H(string s) { Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine(s); Console.ResetColor(); }
    static void Note(string s) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine($"      {s}\n"); Console.ResetColor(); }
    static void Step(string what, HttpResponseMessage r, string meaning)
    {
        var code = (int)r.StatusCode;
        Console.ForegroundColor = code < 300 ? ConsoleColor.Green : code < 500 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.Write($"  {code,3} ");
        Console.ResetColor();
        Console.Write(what.PadRight(58));
        Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine($" {meaning}"); Console.ResetColor();
    }
}

// ------------------------------------------------------------------------------------------------------------

/// <summary>
/// Stdio MCP server: reads newline-delimited JSON-RPC from stdin (what Claude Desktop, Cursor and VS Code speak),
/// forwards each request to the hosted HTTP endpoint as the configured operator, and writes the response to stdout.
/// Notifications are forwarded and produce no output. Nothing is interpreted locally, so the hosted server's
/// authorization is the only authorization.
/// </summary>
static class StdioBridge
{
    public static async Task<int> RunAsync(MatterDeskClient c)
    {
        using var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin, new UTF8Encoding(false));
        using var stdout = Console.OpenStandardOutput();
        await using var writer = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };
        await Console.Error.WriteLineAsync($"matterdesk mcp bridge → {c.BaseUrl}/mcp as {c.Operator}");

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject msg;
            try { msg = JsonNode.Parse(line)!.AsObject(); }
            catch (Exception)
            {
                await writer.WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = null, ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "Parse error" } }.ToJsonString());
                continue;
            }

            var method = msg["method"]?.GetValue<string>() ?? "";
            var id = msg["id"];
            using var r = await c.McpAsync(method, msg["params"], id);
            if (id is null) continue;   // notification: nothing to return

            var body = await r.Content.ReadAsStringAsync();
            if (!r.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
            {
                var err = new JsonObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(),
                    ["error"] = new JsonObject { ["code"] = -32000, ["message"] = $"Hosted endpoint returned HTTP {(int)r.StatusCode}", ["data"] = body },
                };
                await writer.WriteLineAsync(err.ToJsonString());
                continue;
            }
            await writer.WriteLineAsync(JsonNode.Parse(body)!.ToJsonString());   // one line per message
        }
        return 0;
    }
}

// ------------------------------------------------------------------------------------------------------------

static class Args
{
    public static (string Command, List<string> Positional, Dictionary<string, string> Options) Parse(string[] args)
    {
        var positional = new List<string>(); var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var key = args[i][2..];
                var eq = key.IndexOf('=');
                if (eq > 0) options[key[..eq]] = key[(eq + 1)..];
                else if (i + 1 < args.Length) options[key] = args[++i];
                else options[key] = "true";
            }
            else positional.Add(args[i]);
        }
        var command = positional.Count > 0 ? positional[0].ToLowerInvariant() : "demo";
        return (command, positional.Skip(1).ToList(), options);
    }

    public static JsonObject KeyValues(IEnumerable<string> pairs)
    {
        var o = new JsonObject();
        foreach (var p in pairs)
        {
            var eq = p.IndexOf('=');
            if (eq <= 0) continue;
            var (k, v) = (p[..eq], p[(eq + 1)..]);
            o[k] = int.TryParse(v, out var n) ? n : v;
        }
        return o;
    }
}
