using System.ComponentModel.DataAnnotations;

namespace MatterDesk.Api.Contracts;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public bool HasMore => Page * PageSize < Total;
}

public sealed record MatterSummary(int Id, string Number, string Title, string AreaOfLaw, string ClientName, bool IsRestricted, DateTime OpenedUtc, int DocumentCount, int EmailCount);

public sealed record MatterDetail(int Id, string Number, string Title, string AreaOfLaw, string ClientNumber, string ClientName, bool IsRestricted, DateTime OpenedUtc, bool CanEdit);

public sealed record DocumentSummary(int Id, int MatterId, string MatterNumber, string Title, string DocumentType, string AuthorCode, string? Keywords, DateTime ModifiedUtc, int VersionCount, long Version);

public sealed record DocumentDetail(int Id, int MatterId, string MatterNumber, string Title, string DocumentType, string AuthorCode, string? Keywords, string? Notes, DateTime CreatedUtc, DateTime ModifiedUtc, long Version, IReadOnlyList<DocumentVersionDto> Versions);

public sealed record DocumentVersionDto(int VersionNumber, string CreatedByCode, DateTime CreatedUtc, string? Comment);

public sealed class CreateDocumentRequest
{
    [Required, StringLength(300, MinimumLength = 1)] public string Title { get; set; } = "";
    [Required, StringLength(40)] public string DocumentType { get; set; } = "";
    [StringLength(500)] public string? Keywords { get; set; }
    [StringLength(4000)] public string? Notes { get; set; }
    [Required, StringLength(1000)] public string StoragePath { get; set; } = "";
}

public sealed class UpdateDocumentProfileRequest
{
    [Required, StringLength(300, MinimumLength = 1)] public string Title { get; set; } = "";
    [Required, StringLength(40)] public string DocumentType { get; set; } = "";
    [StringLength(500)] public string? Keywords { get; set; }
    [StringLength(4000)] public string? Notes { get; set; }
}

public sealed record EmailSummary(int Id, int MatterId, string Subject, string FromAddress, DateTime ReceivedUtc, string? BodyPreview, string ProfiledByCode, bool MarkerSet);

public sealed record SearchHit(string Kind, int Id, int MatterId, string MatterNumber, string Title, string? Snippet, DateTime WhenUtc);

public sealed record MailSyncResult(int Scanned, int Profiled, int AlreadyProfiled, int MarkersSet, bool DeltaResumed, string? NextDeltaCursorPreview);
