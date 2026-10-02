using System.Net;
using System.Net.Http.Json;
using MatterDesk.Api.Contracts;

namespace MatterDesk.Api.Tests;

/// <summary>
/// The tests that matter most in a DMS: a user outside a restricted matter must get nothing —
/// not a hidden row, not a title in search, not a 200 on a guessed id.
/// </summary>
public sealed class PermissionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    public PermissionTests(ApiFactory f) => _f = f;

    private async Task<int> RestrictedMatterId()
    {
        // JDU is the only operator granted on the restricted "Project Falcon" matter.
        var page = await _f.As("JDU").GetFromJsonAsync<PagedResult<MatterSummary>>("/api/matters");
        return page!.Items.Single(m => m.IsRestricted).Id;
    }

    [Fact]
    public async Task Unauthenticated_request_is_401()
    {
        var r = await _f.CreateClient().GetAsync("/api/matters");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Unknown_operator_code_is_403()
    {
        var r = await _f.As("NOPE").GetAsync("/api/matters");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Matter_list_excludes_restricted_matters_for_ungranted_operator()
    {
        var les = await _f.As("LES").GetFromJsonAsync<PagedResult<MatterSummary>>("/api/matters");
        var jdu = await _f.As("JDU").GetFromJsonAsync<PagedResult<MatterSummary>>("/api/matters");

        Assert.DoesNotContain(les!.Items, m => m.IsRestricted);
        Assert.Contains(jdu!.Items, m => m.IsRestricted);
        Assert.Equal(2, les.Total);
        Assert.Equal(3, jdu.Total);
    }

    [Fact]
    public async Task Granted_operator_gets_200_and_ungranted_gets_403_on_same_matter()
    {
        var id = await RestrictedMatterId();

        var a = await _f.As("JDU").GetAsync($"/api/matters/{id}");
        var b = await _f.As("LES").GetAsync($"/api/matters/{id}");
        var missing = await _f.As("LES").GetAsync("/api/matters/999999");

        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, b.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Documents_and_emails_of_forbidden_matter_are_403()
    {
        var id = await RestrictedMatterId();
        Assert.Equal(HttpStatusCode.Forbidden, (await _f.As("LES").GetAsync($"/api/matters/{id}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _f.As("LES").GetAsync($"/api/matters/{id}/emails")).StatusCode);
    }

    [Fact]
    public async Task Document_on_forbidden_matter_is_404_not_403_so_existence_is_not_revealed()
    {
        var id = await RestrictedMatterId();
        var docs = await _f.As("JDU").GetFromJsonAsync<PagedResult<DocumentSummary>>($"/api/matters/{id}/documents");
        var docId = docs!.Items.First().Id;

        var ok = await _f.As("JDU").GetAsync($"/api/documents/{docId}");
        var denied = await _f.As("LES").GetAsync($"/api/documents/{docId}");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task Search_never_leaks_restricted_titles_or_keywords()
    {
        // "Falcon" appears only on the restricted matter; "Acme" appears on both a visible matter and a restricted document's keywords.
        var falconLes = await _f.As("LES").GetFromJsonAsync<PagedResult<SearchHit>>("/api/search?q=Falcon");
        var falconJdu = await _f.As("JDU").GetFromJsonAsync<PagedResult<SearchHit>>("/api/search?q=Falcon");
        var acmeLes = await _f.As("LES").GetFromJsonAsync<PagedResult<SearchHit>>("/api/search?q=Acme");

        Assert.Equal(0, falconLes!.Total);
        Assert.True(falconJdu!.Total >= 3);                       // matter + two documents
        Assert.True(acmeLes!.Total > 0);
        Assert.DoesNotContain(acmeLes.Items, h => h.Title.Contains("PRIVILEGED") || h.Title.Contains("Falcon"));
        Assert.All(acmeLes.Items, h => Assert.NotEqual("10099-0001", h.MatterNumber));
    }

    [Fact]
    public async Task Viewer_without_edit_grant_cannot_profile_documents()
    {
        // PAR can see Acme v. Rodriguez but was granted CanEdit = false.
        var matters = await _f.As("PAR").GetFromJsonAsync<PagedResult<MatterSummary>>("/api/matters");
        var acme = matters!.Items.Single(m => m.Number == "10042-0003");

        var r = await _f.As("PAR").PostAsJsonAsync($"/api/matters/{acme.Id}/documents",
            new CreateDocumentRequest { Title = "Sneaky", DocumentType = "Memo", StoragePath = "/x" });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
