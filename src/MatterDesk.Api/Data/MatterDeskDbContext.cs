using MatterDesk.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MatterDesk.Api.Data;

public sealed class MatterDeskDbContext(DbContextOptions<MatterDeskDbContext> options) : DbContext(options)
{
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Matter> Matters => Set<Matter>();
    public DbSet<MatterAccess> MatterAccess => Set<MatterAccess>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<ProfiledEmail> ProfiledEmails => Set<ProfiledEmail>();
    public DbSet<MailSyncState> MailSyncStates => Set<MailSyncState>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ActivityEvent>(e =>
        {
            e.HasIndex(x => x.OccurredUtc);
            e.Property(x => x.OperatorCode).HasMaxLength(8);
            e.Property(x => x.Channel).HasMaxLength(8);
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.Target).HasMaxLength(300);
            e.Property(x => x.Outcome).HasMaxLength(16);
            e.Property(x => x.Summary).HasMaxLength(500);
        });

        b.Entity<Operator>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(8);
            e.Property(x => x.DisplayName).HasMaxLength(100);
        });

        b.Entity<Client>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Number).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        b.Entity<Matter>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.ClientId);
            e.Property(x => x.Number).HasMaxLength(24);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.AreaOfLaw).HasMaxLength(60);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.ResponsibleCode).HasMaxLength(8);
        });

        b.Entity<MatterAccess>(e =>
        {
            e.HasKey(x => new { x.MatterId, x.OperatorId });
            // Supports the EXISTS probe used by the permission predicate: (OperatorId, MatterId).
            e.HasIndex(x => new { x.OperatorId, x.MatterId });
        });

        b.Entity<Document>(e =>
        {
            e.HasIndex(x => new { x.MatterId, x.DocumentType });
            e.HasIndex(x => x.ModifiedUtc);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.DocumentType).HasMaxLength(40);
            e.Property(x => x.AuthorCode).HasMaxLength(8);
            e.Property(x => x.Keywords).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        b.Entity<DocumentVersion>(e =>
        {
            e.HasIndex(x => new { x.DocumentId, x.VersionNumber }).IsUnique();
            e.Property(x => x.StoragePath).HasMaxLength(1000);
        });

        b.Entity<ProfiledEmail>(e =>
        {
            // One Graph message can be filed to many matters, but only once per matter.
            e.HasIndex(x => new { x.MatterId, x.ExternalMessageId }).IsUnique();
            e.HasIndex(x => x.InternetMessageId);
            e.Property(x => x.ExternalMessageId).HasMaxLength(512);
            e.Property(x => x.InternetMessageId).HasMaxLength(512);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.FromAddress).HasMaxLength(320);
        });

        b.Entity<MailSyncState>(e =>
        {
            e.HasIndex(x => new { x.OperatorId, x.Folder }).IsUnique();
            e.Property(x => x.Folder).HasMaxLength(64);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // Bump the concurrency token on every modified document so a stale client's PUT fails with 409.
        foreach (var entry in ChangeTracker.Entries<Document>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedUtc = DateTime.UtcNow;
                entry.Entity.Version = entry.OriginalValues.GetValue<long>(nameof(Document.Version)) + 1;
            }
            else if (entry.State == EntityState.Added && entry.Entity.Version == 0)
            {
                entry.Entity.Version = 1;
            }
        }
        return base.SaveChangesAsync(ct);
    }
}
