using Microsoft.EntityFrameworkCore;

namespace MatterDesk.Api.Data;

/// <summary>
/// <c>EnsureCreated</c> builds the full model on an empty database but never alters an existing one, so the two
/// columns added to <c>Matters</c> after the first release (<c>Status</c>, <c>ResponsibleCode</c>) are patched in
/// here with one idempotent statement per provider — the same approach as <c>ActivitySchema</c>.
/// </summary>
public static class MatterSchema
{
    public static async Task EnsureAsync(MatterDeskDbContext db, CancellationToken ct = default)
    {
        if (db.Database.IsSqlite())
        {
            var columns = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Matters')").ToListAsync(ct);
            if (!columns.Contains("Status"))
                await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Matters" ADD COLUMN "Status" TEXT NOT NULL DEFAULT 'Open'""", ct);
            if (!columns.Contains("ResponsibleCode"))
                await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Matters" ADD COLUMN "ResponsibleCode" TEXT NULL""", ct);
        }
        else if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'Matters', N'Status') IS NULL
                    ALTER TABLE [Matters] ADD [Status] nvarchar(16) NOT NULL CONSTRAINT [DF_Matters_Status] DEFAULT N'Open';
                IF COL_LENGTH(N'Matters', N'ResponsibleCode') IS NULL
                    ALTER TABLE [Matters] ADD [ResponsibleCode] nvarchar(8) NULL;
                """, ct);
        }
    }
}
