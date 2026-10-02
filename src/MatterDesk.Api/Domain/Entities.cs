namespace MatterDesk.Api.Domain;

/// <summary>A firm user. Mirrors the "operator code" concept: one identity shared by desktop and browser clients.</summary>
public sealed class Operator
{
    public int Id { get; set; }
    public required string Code { get; set; }          // e.g. "LES", "JDU"
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public ICollection<MatterAccess> MatterAccess { get; set; } = new List<MatterAccess>();
}

public sealed class Client
{
    public int Id { get; set; }
    public required string Number { get; set; }        // e.g. "10042"
    public required string Name { get; set; }
    public ICollection<Matter> Matters { get; set; } = new List<Matter>();
}

/// <summary>The hub. Everything else hangs off a matter.</summary>
public sealed class Matter
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public required string Number { get; set; }        // e.g. "10042-0003"
    public required string Title { get; set; }
    public required string AreaOfLaw { get; set; }
    public bool IsRestricted { get; set; }             // restricted matters require an explicit MatterAccess row
    public DateTime OpenedUtc { get; set; }
    public ICollection<Document> Documents { get; set; } = new List<Document>();
    public ICollection<ProfiledEmail> Emails { get; set; } = new List<ProfiledEmail>();
    public ICollection<MatterAccess> Access { get; set; } = new List<MatterAccess>();
}

/// <summary>Explicit grant. For restricted matters, no row means no access — including through search.</summary>
public sealed class MatterAccess
{
    public int MatterId { get; set; }
    public Matter Matter { get; set; } = null!;
    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;
    public bool CanEdit { get; set; }
}

/// <summary>A document profile: metadata about a file, not the bytes. Versions hang off it.</summary>
public sealed class Document
{
    public int Id { get; set; }
    public int MatterId { get; set; }
    public Matter Matter { get; set; } = null!;
    public required string Title { get; set; }
    public required string DocumentType { get; set; }  // Pleading, Correspondence, Discovery, ...
    public required string AuthorCode { get; set; }
    public string? Keywords { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    /// <summary>
    /// Optimistic concurrency token. On SQL Server this would normally be a <c>rowversion</c> column
    /// (<c>[Timestamp] byte[]</c>); a counter is used here so the same model runs on SQLite in tests.
    /// </summary>
    public long Version { get; set; }

    public ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
}

public sealed class DocumentVersion
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public int VersionNumber { get; set; }
    public required string StoragePath { get; set; }
    public required string CreatedByCode { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string? Comment { get; set; }
}

/// <summary>An email filed to a matter. The Graph message id is the idempotency key.</summary>
public sealed class ProfiledEmail
{
    public int Id { get; set; }
    public int MatterId { get; set; }
    public Matter Matter { get; set; } = null!;
    public required string ExternalMessageId { get; set; }   // Graph message id
    public string? InternetMessageId { get; set; }           // RFC 5322 Message-ID, stable across mailboxes
    public required string Subject { get; set; }
    public required string FromAddress { get; set; }
    public DateTime ReceivedUtc { get; set; }
    public string? BodyPreview { get; set; }
    public required string ProfiledByCode { get; set; }
    public DateTime ProfiledUtc { get; set; }
    public bool MarkerSet { get; set; }                       // Outlook category applied so users don't file twice
}

/// <summary>Per-operator, per-folder Graph delta token so sync never re-reads the whole mailbox.</summary>
public sealed class MailSyncState
{
    public int Id { get; set; }
    public int OperatorId { get; set; }
    public required string Folder { get; set; }
    public string? DeltaLink { get; set; }
    public DateTime? LastSyncUtc { get; set; }
}
