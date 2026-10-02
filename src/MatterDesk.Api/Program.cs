using MatterDesk.Api.Auth;
using MatterDesk.Api.Data;
using MatterDesk.Api.Mail;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
var env = builder.Environment;
var allowDevHeaderAuth = env.IsDevelopment() || env.IsEnvironment("Testing");

// ---------- Data ------------------------------------------------------------------------------------
// SQL Server in any real environment; SQLite when no connection string is configured (local demo, CI, tests).
var sqlServer = cfg.GetConnectionString("SqlServer");
builder.Services.AddDbContext<MatterDeskDbContext>(o =>
{
    if (!string.IsNullOrWhiteSpace(sqlServer))
        o.UseSqlServer(sqlServer, s => s.EnableRetryOnFailure());
    else
        o.UseSqlite(cfg.GetConnectionString("Sqlite") ?? "Data Source=matterdesk.db");
});

// ---------- Authentication ---------------------------------------------------------------------------
// "Smart" policy scheme: a Bearer token (OIDC / Entra ID) wins; otherwise, in dev/test only, the X-Operator-Code header.
builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = Schemes.Smart;
        o.DefaultChallengeScheme = Schemes.Smart;
    })
    .AddPolicyScheme(Schemes.Smart, "Bearer or dev header", o =>
    {
        o.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || !allowDevHeaderAuth
                ? Schemes.Bearer
                : Schemes.DevHeader;
    })
    .AddJwtBearer(Schemes.Bearer, o =>
    {
        // Entra ID: Authority = https://login.microsoftonline.com/{tenant}/v2.0, Audience = api://{client-id}
        o.Authority = cfg["Auth:Authority"];
        o.Audience = cfg["Auth:Audience"];
        o.TokenValidationParameters.NameClaimType = "preferred_username";
        o.RequireHttpsMetadata = !env.IsDevelopment();
    })
    .AddScheme<DevHeaderOptions, DevHeaderAuthenticationHandler>(Schemes.DevHeader, _ => { });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentOperator, HttpCurrentOperator>();

// ---------- Mail (Microsoft Graph) -------------------------------------------------------------------
builder.Services.Configure<GraphOptions>(cfg.GetSection(GraphOptions.Section));
if (!string.IsNullOrWhiteSpace(cfg[$"{GraphOptions.Section}:ClientId"]))
    builder.Services.AddSingleton<IMailSource, GraphMailSource>();
else
    builder.Services.AddSingleton<IMailSource, NoMailboxConfigured>();   // keeps the API runnable without a tenant
builder.Services.AddScoped<EmailProfilingService>();

// ---------- Web ------------------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddProblemDetails();                       // RFC 7807 shape for every error, including unhandled ones
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
{
    // Make the validation contract explicit: always application/problem+json with the standard "errors" map.
    o.InvalidModelStateResponseFactory = ctx =>
    {
        var pd = new Microsoft.AspNetCore.Mvc.ValidationProblemDetails(ctx.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Instance = ctx.HttpContext.Request.Path,
        };
        pd.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(pd) { ContentTypes = { "application/problem+json" } };
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MatterDesk API",
        Version = "v1",
        Description = "Matter-centric document and email profiling API: permission predicate in the query, optimistic concurrency on profiles, Graph delta sync for Inbox filing.",
    });
    o.AddSecurityDefinition("DevHeader", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Operator-Code",
        Description = "Dev/test only. Operator code, e.g. LES, JDU or PAR.",
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "DevHeader" } }] = []
    });
    var xml = Path.Combine(AppContext.BaseDirectory, "MatterDesk.Api.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(cfg.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
     .AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("ETag")));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<CurrentOperatorMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow })).AllowAnonymous();

// Schema + seed. In production this would be `dotnet ef database update` in the release pipeline, not at startup.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MatterDeskDbContext>();
    await db.Database.EnsureCreatedAsync();
    await Seed.ApplyAsync(db);
}

app.Run();

/// <summary>Bound when no Graph client id is configured so the rest of the API still runs.</summary>
file sealed class NoMailboxConfigured : IMailSource
{
    public Task<MailDeltaPage> GetInboxChangesAsync(string? cursor, CancellationToken ct) =>
        throw new InvalidOperationException("Microsoft Graph is not configured. Set Graph:TenantId and Graph:ClientId (see README).");
    public Task SetProfiledMarkerAsync(string messageId, string matterNumber, CancellationToken ct) => Task.CompletedTask;
}

public partial class Program { }   // exposes the entry point to WebApplicationFactory in tests
