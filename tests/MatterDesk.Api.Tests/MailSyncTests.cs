using System.Net;
using System.Net.Http.Json;
using MatterDesk.Api.Contracts;

namespace MatterDesk.Api.Tests;

/// <summary>
/// Graph sync behaviour without a tenant: delta paging, idempotency, the Outlook marker,
/// and — most importantly — no filing into matters the operator cannot see.
/// Each test gets a fresh factory because the fake inbox is stateful.
/// </summary>
public sealed class MailSyncTests
{
    private static async Task<Dictionary<string, int>> MatterIds(ApiFactory f, string op) =>
        (await f.As(op).GetFromJsonAsync<PagedResult<MatterSummary>>("/api/matters"))!.Items.ToDictionary(m => m.Number, m => m.Id);

    [Fact]
    public async Task First_sync_pages_through_inbox_files_tagged_messages_and_sets_markers()
    {
        using var f = new ApiFactory();
        f.Mail.PageSize = 2;
        f.Mail.Add("m1", "RE: [10042-0003] Rodriguez — IME scheduling");
        f.Mail.Add("m2", "Lunch?");
        f.Mail.Add("m3", "FW: [10077-0001] Office action — examiner interview");
        f.Mail.Add("m4", "[10042-0003] and [10077-0001] joint status");
        f.Mail.Add("m5", "no tag here");

        var r = await f.As("LES").PostAsync("/api/mail/sync", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var result = await r.ReadAs<MailSyncResult>();

        Assert.Equal(5, result.Scanned);
        Assert.Equal(4, result.Profiled);           // m1, m3, m4 (twice: two matters)
        Assert.Equal(4, result.MarkersSet);
        Assert.False(result.DeltaResumed);
        Assert.NotNull(result.NextDeltaCursorPreview);

        var ids = await MatterIds(f, "LES");
        var acme = await f.As("LES").GetFromJsonAsync<PagedResult<EmailSummary>>($"/api/matters/{ids["10042-0003"]}/emails");
        Assert.Equal(3, acme!.Total);               // seed email + m1 + m4
        Assert.All(acme.Items, e => Assert.True(e.MarkerSet));
        Assert.Equal(["Filed: 10042-0003", "Filed: 10077-0001"], f.Mail.Markers["m4"].Order());
    }

    [Fact]
    public async Task Second_sync_resumes_from_delta_link_and_is_idempotent()
    {
        using var f = new ApiFactory();
        f.Mail.Add("m1", "[10042-0003] first");
        await f.As("LES").PostAsync("/api/mail/sync", null);

        // Nothing new: zero scanned, nothing re-filed.
        var again = await (await f.As("LES").PostAsync("/api/mail/sync", null)).ReadAs<MailSyncResult>();
        Assert.True(again.DeltaResumed);
        Assert.Equal(0, again.Scanned);
        Assert.Equal(0, again.Profiled);

        // One new message arrives: only it is scanned.
        f.Mail.Add("m2", "[10042-0003] second");
        var third = await (await f.As("LES").PostAsync("/api/mail/sync", null)).ReadAs<MailSyncResult>();
        Assert.Equal(1, third.Scanned);
        Assert.Equal(1, third.Profiled);
    }

    [Fact]
    public async Task Messages_tagged_with_a_restricted_matter_are_not_filed_for_an_ungranted_operator()
    {
        using var f = new ApiFactory();
        f.Mail.Add("m1", "[10099-0001] Falcon — term sheet comments");   // restricted; LES has no grant

        var les = await (await f.As("LES").PostAsync("/api/mail/sync", null)).ReadAs<MailSyncResult>();
        Assert.Equal(1, les.Scanned);
        Assert.Equal(0, les.Profiled);
        Assert.False(f.Mail.Markers.ContainsKey("m1"));

        // JDU, who is granted, files it (fresh delta round for a different operator).
        var jdu = await (await f.As("JDU").PostAsync("/api/mail/sync", null)).ReadAs<MailSyncResult>();
        Assert.Equal(1, jdu.Profiled);

        var ids = await MatterIds(f, "JDU");
        var emails = await f.As("JDU").GetFromJsonAsync<PagedResult<EmailSummary>>($"/api/matters/{ids["10099-0001"]}/emails");
        Assert.Single(emails!.Items);
        Assert.Equal("JDU", emails.Items[0].ProfiledByCode);
    }

    [Fact]
    public async Task Marker_failure_keeps_the_profile_and_reports_marker_not_set()
    {
        using var f = new ApiFactory();
        f.Mail.FailMarkers = true;
        f.Mail.Add("m1", "[10042-0003] marker will fail");

        var r = await (await f.As("LES").PostAsync("/api/mail/sync", null)).ReadAs<MailSyncResult>();
        Assert.Equal(1, r.Profiled);
        Assert.Equal(0, r.MarkersSet);

        var ids = await MatterIds(f, "LES");
        var emails = await f.As("LES").GetFromJsonAsync<PagedResult<EmailSummary>>($"/api/matters/{ids["10042-0003"]}/emails");
        var filed = emails!.Items.Single(e => e.Subject.Contains("marker will fail"));
        Assert.False(filed.MarkerSet);
    }

    [Fact]
    public async Task Per_matter_sync_only_files_into_that_matter_and_respects_edit_rights()
    {
        using var f = new ApiFactory();
        f.Mail.Add("m1", "[10042-0003] for acme");
        f.Mail.Add("m2", "[10077-0001] for brightline");
        var ids = await MatterIds(f, "LES");

        var r = await (await f.As("LES").PostAsync($"/api/matters/{ids["10042-0003"]}/emails/sync", null)).ReadAs<MailSyncResult>();
        Assert.Equal(2, r.Scanned);
        Assert.Equal(1, r.Profiled);

        // PAR can view Acme but not edit → cannot trigger filing into it.
        var denied = await f.As("PAR").PostAsync($"/api/matters/{ids["10042-0003"]}/emails/sync", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
