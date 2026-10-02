using System.Net.Http.Json;
using MatterDesk.Api.Data;
using MatterDesk.Api.Mail;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MatterDesk.Api.Tests;

/// <summary>
/// Boots the real pipeline (auth, middleware, controllers, EF) against a private SQLite in-memory database
/// and a fake Graph mail source. Each factory instance gets its own database so tests do not interfere.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    public FakeMailSource Mail { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MatterDeskDbContext>>();
            services.RemoveAll<MatterDeskDbContext>();
            _conn.Open();
            services.AddDbContext<MatterDeskDbContext>(o => o.UseSqlite(_conn));

            services.RemoveAll<IMailSource>();
            services.AddSingleton<IMailSource>(Mail);

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MatterDeskDbContext>();
            db.Database.EnsureCreated();
            Seed.ApplyAsync(db).GetAwaiter().GetResult();
        });
    }

    /// <summary>An HttpClient authenticated as the given operator code via the dev header scheme.</summary>
    public HttpClient As(string operatorCode)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Add("X-Operator-Code", operatorCode);
        return c;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _conn.Dispose();
    }
}

public static class HttpExtensions
{
    public static async Task<T> ReadAs<T>(this HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<T>())!;
}
