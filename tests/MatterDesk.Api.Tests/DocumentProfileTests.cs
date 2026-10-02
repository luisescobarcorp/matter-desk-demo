using System.Net;
using System.Net.Http.Json;
using MatterDesk.Api.Contracts;

namespace MatterDesk.Api.Tests;

public sealed class DocumentProfileTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public DocumentProfileTests(ApiFactory f) => _f = f;

    private async Task<int> AcmeMatterId() =>
        (await _f.As("LES").GetFromJsonAsync<PagedResult<MatterSummary>>("/api/matters"))!.Items.Single(m => m.Number == "10042-0003").Id;

    [Fact]
    public async Task Create_returns_201_with_location_version_1_and_first_version_row()
    {
        var matterId = await AcmeMatterId();
        var r = await _f.As("LES").PostAsJsonAsync($"/api/matters/{matterId}/documents",
            new CreateDocumentRequest { Title = "Motion for summary judgment", DocumentType = "Pleading", Keywords = "MSJ; summary judgment", StoragePath = "/dms/10042-0003/msj.docx" });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.NotNull(r.Headers.Location);
        var d = await r.ReadAs<DocumentDetail>();
        Assert.Equal(1, d.Version);
        Assert.Single(d.Versions);
        Assert.Equal("LES", d.AuthorCode);
    }

    [Fact]
    public async Task Validation_errors_use_problem_details_shape()
    {
        var matterId = await AcmeMatterId();
        var r = await _f.As("LES").PostAsJsonAsync($"/api/matters/{matterId}/documents",
            new CreateDocumentRequest { Title = "", DocumentType = "", StoragePath = "" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"errors\"", body);
        Assert.Contains("Title", body);
    }

    [Fact]
    public async Task Update_requires_if_match_and_rejects_stale_version_with_409()
    {
        var matterId = await AcmeMatterId();
        var created = await (await _f.As("LES").PostAsJsonAsync($"/api/matters/{matterId}/documents",
            new CreateDocumentRequest { Title = "Expert report", DocumentType = "Discovery", StoragePath = "/dms/x.pdf" })).ReadAs<DocumentDetail>();

        var body = new UpdateDocumentProfileRequest { Title = "Expert report (final)", DocumentType = "Discovery", Keywords = "expert" };

        // No If-Match → 428
        var noHeader = await _f.As("LES").PutAsJsonAsync($"/api/documents/{created.Id}/profile", body);
        Assert.Equal(HttpStatusCode.PreconditionRequired, noHeader.StatusCode);

        // First editor succeeds with version 1 → document is now version 2
        var c1 = _f.As("LES"); c1.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");
        var first = await c1.PutAsJsonAsync($"/api/documents/{created.Id}/profile", body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(2, (await first.ReadAs<DocumentDetail>()).Version);

        // Second editor still holding version 1 → 409 with a readable problem
        var c2 = _f.As("LES"); c2.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");
        var second = await c2.PutAsJsonAsync($"/api/documents/{created.Id}/profile", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("version", (await second.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);

        // Reload, retry with version 2 and a real change → OK, version 3
        var c3 = _f.As("LES"); c3.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"2\"");
        body.Keywords = "expert; damages";
        var third = await c3.PutAsJsonAsync($"/api/documents/{created.Id}/profile", body);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.Equal(3, (await third.ReadAs<DocumentDetail>()).Version);
    }

    [Fact]
    public async Task Get_document_exposes_etag_matching_version()
    {
        var matterId = await AcmeMatterId();
        var docs = await _f.As("LES").GetFromJsonAsync<PagedResult<DocumentSummary>>($"/api/matters/{matterId}/documents");
        var r = await _f.As("LES").GetAsync($"/api/documents/{docs!.Items[0].Id}");
        var d = await r.ReadAs<DocumentDetail>();
        Assert.Equal($"\"{d.Version}\"", r.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task Paging_is_clamped_and_reports_has_more()
    {
        var matterId = await AcmeMatterId();
        var page = await _f.As("LES").GetFromJsonAsync<PagedResult<DocumentSummary>>($"/api/matters/{matterId}/documents?page=1&pageSize=1");
        Assert.Single(page!.Items);
        Assert.True(page.HasMore);
        Assert.True(page.Total >= 3);

        var huge = await _f.As("LES").GetFromJsonAsync<PagedResult<DocumentSummary>>($"/api/matters/{matterId}/documents?pageSize=100000");
        Assert.Equal(200, huge!.PageSize);
    }
}
