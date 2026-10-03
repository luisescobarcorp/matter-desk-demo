using System.Globalization;
using System.Text;
using MatterDesk.Api.Activities;
using MatterDesk.Api.Domain;
using MatterDesk.Api.Search;
using Microsoft.EntityFrameworkCore;

namespace MatterDesk.Api.Data;

/// <summary>
/// The rest of the firm's book, as data. Each matter is added only if its number is not yet in the database, so
/// this layer is safe to run on every start against SQLite or SQL Server. Dates are absolute (nothing here depends on
/// "today") except the activity rows, which are written relative to now so the Activity panel opens with recent history.
/// </summary>
public static partial class Seed
{
    private sealed record Doc(string Title, string Type, string Author, string Keywords, int Day, int Versions = 1);
    private sealed record Mail(string Subject, string From, string Preview, int Day, string FiledBy, bool Marker = true);
    private sealed record Spec(string Number, string Title, string Area, string Status, string Responsible, string Opened, bool Restricted, string Grants, Doc[] Documents, Mail[] Emails);

    private const bool General = false, Restricted = true;
    private const string Open = MatterStatus.Open, OnHold = MatterStatus.OnHold, Closed = MatterStatus.Closed;
    private static readonly Mail[] NoMail = [];

    private static readonly (string Number, string Name)[] FirmClients =
    [
        ("10104", "Harborview Logistics LLC"),
        ("10111", "Castellano Family Trust"),
        ("10118", "Meridian Health Partners"),
        ("10123", "City of Coral Gables"),
        ("10130", "Reyes & Okafor PA (referral)"),
        ("10137", "Northstar Marine Insurance"),
        ("10142", "Palmetto Ridge HOA"),
        ("10151", "Brightline Dental Group"),
    ];

    // Grants: "CODE:edit" or "CODE:view". A general matter with grants is editable only by its edit grantees; a restricted
    // matter is visible only to its grantees. LES is granted on no restricted matter (the permission boundary the tests
    // and the CLI demo walk), JDU is on all of them, PAR on all but 10099-0001 — so the three operators see three different
    // lists. Restricted matters are opened before 10099-0001 so that one stays first in JDU's list.
    private static readonly Spec[] FirmMatters =
    [
        new("10104-0001", "Harborview v. Port Everglades Stevedoring — breach of carriage contract", "Commercial Litigation", Open, "LES", "2025-03-12", General, "LES:edit PAR:edit",
            [
                new("Engagement letter — Harborview Logistics", "Correspondence", "LES", "engagement; retainer; conflict check", 0, 2),
                new("Complaint for breach of contract and damages", "Pleading", "LES", "complaint; breach of contract; carriage; damages", 9, 3),
                new("Defendant's motion to dismiss", "Pleading", "PAR", "motion to dismiss; 12(b)(6); forum selection", 41),
                new("Response in opposition to motion to dismiss", "Pleading", "LES", "opposition; motion to dismiss", 58, 2),
                new("Harborview's responses to first request for production", "Discovery", "PAR", "request for production; responses; bills of lading", 120),
                new("Deposition transcript — R. Mendez (operations manager)", "Transcript", "PAR", "deposition; transcript; Mendez; operations", 190),
                new("Mediation statement", "Memo", "LES", "mediation; settlement authority; damages model", 260, 2),
            ],
            [
                new("RE: [10104-0001] Mendez deposition — exhibits", "counsel@pestevedoring.example", "Exhibits 4 through 11 attached; please confirm receipt before Thursday.", 185, "PAR"),
                new("[10104-0001] Mediation date confirmed 14 Nov", "scheduler@miamimediation.example", "Confirmed for 9:30 am at the Brickell office. Statements due five days prior.", 250, "LES"),
            ]),

        new("10104-0002", "Harborview — Doral warehouse lease renewal (NW 25th St)", "Real Estate", Closed, "LES", "2024-09-05", General, "LES:edit",
            [
                new("Lease renewal term sheet", "Agreement", "LES", "lease; renewal; term sheet; Doral", 3),
                new("Second amendment to lease — redline", "Agreement", "LES", "lease amendment; redline; CAM cap; renewal option", 14, 4),
                new("Landlord estoppel certificate", "Agreement", "PAR", "estoppel; landlord; lease", 30),
                new("Closing memo to client — lease executed", "Correspondence", "LES", "closing memo; executed lease; key dates", 38),
            ],
            [new("RE: [10104-0002] Executed second amendment", "leasing@doralindustrial.example", "Fully executed copy attached. Rent commencement remains 1 November.", 37, "LES")]),

        new("10111-0001", "Castellano Family Trust — restatement and pour-over wills", "Estate Planning", Open, "JDU", "2025-11-18", Restricted, "JDU:edit PAR:view",
            [
                new("Estate planning questionnaire (intake)", "Intake", "PAR", "intake; questionnaire; assets; beneficiaries", 0),
                new("Memo — GST allocation and homestead devise (PRIVILEGED)", "Memo", "JDU", "privileged; generation-skipping; homestead; Florida", 12, 2),
                new("Restated revocable trust — draft", "Agreement", "JDU", "revocable trust; restatement; trustee succession", 30, 3),
                new("Pour-over wills — A. and M. Castellano", "Agreement", "JDU", "pour-over will; personal representative", 30),
                new("Durable powers of attorney and health care surrogate designations", "Agreement", "PAR", "power of attorney; health care surrogate; living will", 45),
            ],
            [new("[10111-0001] Signing appointment — 12 Jan", "a.castellano@example.com", "We can both come in at 10. Should we bring the deed to the Coral Way property?", 50, "PAR")]),

        new("10111-0002", "Castellano — Key Biscayne condominium purchase (Unit 1402)", "Real Estate", Closed, "LES", "2026-01-08", General, "LES:edit PAR:edit",
            [
                new("Purchase and sale contract (FAR/BAR AS IS)", "Agreement", "PAR", "purchase contract; AS IS; inspection period", 0),
                new("Title commitment — Old Republic", "Title", "PAR", "title commitment; Schedule B; exceptions; survey", 9),
                new("Condominium association estoppel letter", "Correspondence", "PAR", "estoppel; association; assessments", 14),
                new("Closing disclosure and settlement statement", "Closing", "PAR", "closing disclosure; settlement statement; prorations", 27, 2),
                new("Closing binder — Unit 1402", "Closing", "PAR", "closing binder; warranty deed; title policy; survey; bill of sale", 31),
                new("Letter to client enclosing recorded deed", "Correspondence", "LES", "recorded deed; closing; homestead exemption reminder", 40),
            ],
            [
                new("[10111-0002] Estoppel — Ocean Club Tower", "mgmt@oceanclubtower.example", "Estoppel attached. Special assessment balance is $4,120 through March.", 13, "PAR", Marker: false),
                new("RE: [10111-0002] Wire instructions — verified by phone", "closer@oldrepublictitle.example", "Confirmed by call-back to the number on file. Do not accept changes by email.", 25, "LES"),
            ]),

        new("10118-0001", "Meridian Health Partners — physician separation and non-compete (Dr. Alvarez)", "Employment", Open, "JDU", "2025-08-21", Restricted, "JDU:edit PAR:view",
            [
                new("Physician employment agreement (2021)", "Agreement", "PAR", "employment agreement; physician; restrictive covenants", 0),
                new("Memo — enforceability of 25-mile non-compete under § 542.335 (PRIVILEGED)", "Memo", "JDU", "privileged; non-compete; 542.335; legitimate business interest", 6, 2),
                new("Demand letter to Dr. Alvarez re solicitation of patients", "Correspondence", "JDU", "demand; non-solicitation; patients; cease and desist", 20),
                new("Separation and release agreement — draft", "Agreement", "JDU", "separation; release; tail coverage; non-disparagement", 48, 5),
                new("Email chain — Alvarez counsel response", "Correspondence", "PAR", "opposing counsel; counter-proposal; tail coverage", 55),
            ],
            [new("RE: [10118-0001] Alvarez — counter-proposal on tail coverage", "counsel@alvarezlaw.example", "Our client will accept the 12-month radius if Meridian funds the tail policy.", 52, "JDU")]),

        new("10118-0002", "Meridian — acquisition of Sunrise Imaging Center LLC", "Corporate / M&A", OnHold, "JDU", "2025-12-02", Restricted, "JDU:edit PAR:view",
            [
                new("Letter of intent — executed", "Agreement", "JDU", "letter of intent; exclusivity; purchase price", 0),
                new("Board minutes — special meeting approving LOI", "Minutes", "PAR", "board minutes; resolution; letter of intent", 2),
                new("Due diligence request list", "Checklist", "PAR", "due diligence; request list; payor contracts; licensure", 5, 2),
                new("Memo — Stark and anti-kickback diligence findings (PRIVILEGED)", "Memo", "JDU", "privileged; Stark; anti-kickback; referral arrangements", 33),
                new("Membership interest purchase agreement — draft 3", "Agreement", "JDU", "purchase agreement; membership interests; indemnification; escrow", 40, 3),
            ],
            [
                new("[10118-0002] Sunrise — Q3 financials (data room)", "cfo@sunriseimaging.example", "Uploaded to folder 4.2. Note the one-time equipment lease buyout in September.", 20, "PAR"),
                new("RE: [10118-0002] Deal paused pending CON review", "gc@meridianhealth.example", "Board wants to wait for the certificate-of-need decision. Please hold drafting.", 70, "JDU"),
            ]),

        new("10123-0001", "City of Coral Gables — Miracle Mile streetscape contractor dispute", "Construction", Open, "LES", "2025-06-10", General, "LES:edit PAR:edit",
            [
                new("Contract for construction services", "Agreement", "PAR", "construction contract; public works; liquidated damages", 0),
                new("Contractor's claim of lien", "Lien", "PAR", "claim of lien; 713.08; retainage", 15),
                new("Notice of contest of lien", "Lien", "LES", "notice of contest; 713.22; lien", 22),
                new("Change order log and schedule analysis", "Exhibit", "PAR", "change orders; schedule; critical path", 60, 2),
                new("Expert report — delay analysis", "Report", "PAR", "expert report; delay; concurrent delay; damages", 150),
                new("Letter to City Attorney re settlement authority", "Correspondence", "LES", "settlement authority; commission; shade meeting", 170),
            ],
            [new("RE: [10123-0001] Change orders 14–19 — supporting documents", "procurement@coralgables.example", "Backup for CO 14 through 19 attached; CO 17 was never approved by the commission.", 58, "PAR")]),

        new("10130-0001", "Reyes & Okafor referral — Okafor v. Biscayne Capital Partners (fee dispute)", "Commercial Litigation", Open, "JDU", "2025-10-07", Restricted, "JDU:edit PAR:view",
            [
                new("Referral and co-counsel agreement", "Agreement", "JDU", "referral; co-counsel; fee division; Rule 4-1.5", 0),
                new("Conflict check and ethical screen memo", "Memo", "JDU", "conflict check; ethical screen; Rule 4-1.10; LES screened", 1),
                new("Complaint — breach of fee agreement", "Pleading", "JDU", "complaint; fee agreement; account stated; quantum meruit", 21, 2),
                new("Defendant's answer and counterclaim", "Pleading", "PAR", "answer; counterclaim; malpractice allegation", 50),
            ],
            [new("[10130-0001] Referral — Okafor fee matter", "m.reyes@reyesokafor.example", "Sending the engagement file and the unpaid invoices. Our firm is conflicted out of litigating it.", 0, "JDU")]),

        new("10137-0001", "Northstar Marine — Delgado v. Bayside Charters (passenger injury defense)", "Insurance Defense", Open, "JDU", "2025-01-28", General, "JDU:edit PAR:edit",
            [
                new("Reservation of rights letter", "Correspondence", "JDU", "reservation of rights; coverage; insured", 4),
                new("Answer and affirmative defenses — Bayside Charters", "Pleading", "JDU", "answer; affirmative defenses; assumption of risk; Jones Act", 18, 2),
                new("Plaintiff's responses to interrogatories", "Discovery", "PAR", "interrogatories; responses; medical providers", 95),
                new("Deposition transcript — Captain L. Ortiz", "Transcript", "PAR", "deposition; transcript; Ortiz; vessel operation", 160),
                new("Independent medical examination report", "Report", "PAR", "IME; orthopaedic; causation; prior injury", 200),
                new("Pre-trial status report to adjuster", "Correspondence", "JDU", "status report; adjuster; exposure; trial setting", 240),
            ],
            [
                new("[10137-0001] Ortiz deposition transcript (certified)", "orders@southfloridacourtreporting.example", "Certified transcript and exhibits attached. Errata due in 30 days.", 165, "PAR"),
                new("RE: [10137-0001] Delgado — IME scheduled 8/14", "claims@northstarmarine.example", "Approved. Please send the IME report as soon as it comes in; reserves review is in September.", 180, "PAR"),
            ]),

        new("10137-0002", "Northstar Marine — coverage opinion, Marina del Sol fuel spill claim", "Insurance Coverage", Closed, "JDU", "2024-11-14", Restricted, "JDU:edit PAR:view",
            [
                new("Policy and endorsements — Marina del Sol", "Exhibit", "PAR", "policy; endorsements; pollution exclusion; marina operators", 0),
                new("Coverage opinion letter (PRIVILEGED)", "Memo", "JDU", "privileged; coverage opinion; pollution exclusion; sudden and accidental", 16, 3),
                new("Declination letter to insured", "Correspondence", "JDU", "declination; coverage; pollution exclusion", 24),
            ],
            NoMail),

        new("10142-0001", "Palmetto Ridge HOA — covenant enforcement, 4471 Palmetto Ridge Dr", "Community Association", Open, "LES", "2025-09-30", General, "LES:edit PAR:edit",
            [
                new("Declaration of covenants (recorded)", "Exhibit", "PAR", "declaration; covenants; architectural control; rentals", 0),
                new("Demand letter — unapproved fence and short-term rental", "Correspondence", "LES", "demand; fence; short-term rental; fines", 7, 2),
                new("Board minutes — enforcement vote", "Minutes", "PAR", "board minutes; enforcement; fining committee", 12),
                new("Pre-suit mediation notice (§ 720.311)", "Correspondence", "LES", "pre-suit mediation; 720.311; covenant enforcement", 40),
            ],
            [new("RE: [10142-0001] 4471 Palmetto Ridge — owner response", "owner4471@example.com", "The fence was approved verbally by the previous board president. I have the texts.", 21, "PAR")]),

        new("10142-0002", "Palmetto Ridge HOA — board inquiry into former property manager", "Community Association", OnHold, "JDU", "2025-07-15", Restricted, "JDU:edit PAR:view",
            [
                new("Engagement letter — board investigation", "Correspondence", "JDU", "engagement; investigation; board; scope", 0),
                new("Interview notes — treasurer (PRIVILEGED)", "Memo", "JDU", "privileged; interview; treasurer; reserve account", 9),
                new("Forensic accountant proposal", "Correspondence", "PAR", "forensic accountant; proposal; scope; budget", 15),
                new("Preliminary findings memo (PRIVILEGED)", "Memo", "JDU", "privileged; findings; vendor payments; reserve transfers", 30, 2),
            ],
            NoMail),

        new("10151-0001", "Brightline Dental — associate dentist employment agreements (three offices)", "Employment", Closed, "JDU", "2024-06-18", General, "JDU:edit PAR:edit",
            [
                new("Associate employment agreement — template", "Agreement", "JDU", "employment agreement; associate dentist; compensation; template", 10, 4),
                new("Non-solicitation rider", "Agreement", "JDU", "non-solicitation; patients; staff; rider", 12),
                new("Executed agreements — Kendall, Doral, Aventura", "Agreement", "PAR", "executed; Kendall; Doral; Aventura", 30),
                new("Closing memo to client", "Correspondence", "JDU", "closing memo; renewal dates; compensation review", 31),
            ],
            NoMail),

        new("10151-0002", "Brightline Dental — Aventura office lease and build-out", "Real Estate", Open, "LES", "2026-04-21", General, "LES:edit PAR:edit",
            [
                new("Letter of intent — Aventura Commons, suite 210", "Agreement", "LES", "letter of intent; lease; tenant improvement allowance", 0),
                new("Lease draft — landlord form with tenant comments", "Agreement", "LES", "lease; landlord form; exclusive use; relocation clause", 14, 3),
                new("Work letter and build-out allowance schedule", "Exhibit", "PAR", "work letter; build-out; allowance; plans", 20),
                new("SNDA — lender form", "Agreement", "PAR", "SNDA; subordination; non-disturbance; lender", 33),
            ],
            [new("RE: [10151-0002] Aventura Commons — revised work letter", "leasing@aventuracommons.example", "Revised work letter attached with the plumbing allowance increased as discussed.", 19, "LES")]),

        new("10151-0003", "Brightline Dental — H-1B petition, Dr. Priya Nair (Kendall office)", "Immigration", Open, "JDU", "2026-05-12", General, "JDU:edit PAR:edit",
            [
                new("Labor condition application (ETA-9035) — certified", "Filing", "PAR", "LCA; ETA-9035; prevailing wage; Kendall", 8),
                new("Credential evaluation and licensure exhibits", "Exhibit", "PAR", "credential evaluation; DDS; Florida license; exhibits", 18),
                new("Form I-129 petition and employer support letter", "Filing", "JDU", "I-129; H-1B; specialty occupation; support letter", 20, 2),
                new("USCIS receipt notice", "Correspondence", "PAR", "receipt notice; I-797C; premium processing", 35),
            ],
            [new("FW: [10151-0003] USCIS receipt notice — premium processing", "mailroom@firm.example", "Scanned from today's mail. Receipt number ends in 4471; filed under premium processing.", 35, "PAR")]),
    ];

    // Responsible attorney for the core matters, back-filled on databases created before the column existed.
    private static readonly Dictionary<string, string> CoreResponsible = new() { ["10042-0003"] = "LES", ["10077-0001"] = "LES", ["10099-0001"] = "JDU" };

    private static async Task ApplyFirmAsync(MatterDeskDbContext db, CancellationToken ct)
    {
        foreach (var m in await db.Matters.Where(m => m.ResponsibleCode == null).ToListAsync(ct))
            if (CoreResponsible.TryGetValue(m.Number, out var code)) m.ResponsibleCode = code;
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);

        var present = await db.Matters.Select(m => m.Number).ToListAsync(ct);
        var missing = FirmMatters.Where(s => !present.Contains(s.Number)).ToList();
        if (missing.Count > 0)
        {
            var operators = await db.Operators.ToDictionaryAsync(o => o.Code, ct);
            var clients = await db.Clients.ToDictionaryAsync(c => c.Number, ct);
            foreach (var (number, name) in FirmClients)
                if (!clients.ContainsKey(number)) clients[number] = db.Clients.Add(new Client { Number = number, Name = name }).Entity;

            foreach (var s in missing)
            {
                var opened = DateTime.SpecifyKind(DateTime.ParseExact(s.Opened, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc).AddHours(14);
                var matter = new Matter
                {
                    Client = clients[s.Number[..5]], Number = s.Number, Title = s.Title, AreaOfLaw = s.Area,
                    Status = s.Status, ResponsibleCode = s.Responsible, IsRestricted = s.Restricted, OpenedUtc = opened,
                };
                db.Matters.Add(matter);

                foreach (var grant in s.Grants.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var (code, level) = (grant[..3], grant[4..]);
                    db.MatterAccess.Add(new MatterAccess { Matter = matter, Operator = operators[code], CanEdit = level == "edit" });
                }

                foreach (var d in s.Documents) db.Documents.Add(BuildDocument(matter, d, opened));

                var n = 0;
                foreach (var e in s.Emails)
                {
                    n++;
                    var received = opened.AddDays(e.Day).AddHours(2.5);
                    db.ProfiledEmails.Add(new ProfiledEmail
                    {
                        Matter = matter, ExternalMessageId = $"AAMkSeed-{s.Number}-{n:00}", InternetMessageId = $"<seed-{s.Number}-{n:00}@{e.From[(e.From.IndexOf('@') + 1)..]}>",
                        Subject = e.Subject, FromAddress = e.From, ReceivedUtc = received, BodyPreview = e.Preview,
                        ProfiledByCode = e.FiledBy, ProfiledUtc = received.AddMinutes(40), MarkerSet = e.Marker,
                    });
                }
            }
            await db.SaveChangesAsync(ct);
        }

        if (!await db.ActivityEvents.AnyAsync(ct)) await SeedActivityAsync(db, ct);
    }

    private static Document BuildDocument(Matter matter, Doc d, DateTime opened)
    {
        var created = opened.AddDays(d.Day).AddHours(1.5);
        var doc = new Document { Matter = matter, Title = d.Title, DocumentType = d.Type, AuthorCode = d.Author, Keywords = d.Keywords, CreatedUtc = created, Version = d.Versions };
        for (var v = 1; v <= d.Versions; v++)
        {
            var at = created.AddDays((v - 1) * 3);
            doc.Versions.Add(new DocumentVersion
            {
                Document = doc, VersionNumber = v, CreatedByCode = d.Author, CreatedUtc = at,
                StoragePath = $"/dms/{matter.Number}/{Slug(d.Title)}{(v == 1 ? "" : $"-v{v}")}.{Extension(d.Type)}",
                Comment = v == 1 ? null : v == d.Versions ? "Final" : "Comments incorporated",
            });
            doc.ModifiedUtc = at;
        }
        return doc;
    }

    private static string Extension(string type) => type is "Transcript" or "Title" or "Exhibit" or "Report" or "Closing" or "Filing" or "Lien" or "Minutes" ? "pdf" : "docx";

    private static string Slug(string title)
    {
        var sb = new StringBuilder();
        foreach (var ch in title.ToLowerInvariant())
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        var slug = sb.ToString();
        if (slug.Length > 60) slug = slug[..60];
        return slug.Trim('-');
    }

    /// <summary>A few days of history across all three surfaces, in the exact shape the live recorder writes.</summary>
    private static async Task SeedActivityAsync(MatterDeskDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ops = await db.Operators.ToDictionaryAsync(o => o.Code, o => o.Id, ct);
        var search = new SearchService(db);

        async Task<string> Hits(string q, string who)
        {
            var total = (await search.SearchAsync(q, 1, 1, ops[who], ct)).Total;
            return $"{total} result{(total == 1 ? "" : "s")}";
        }
        async Task<(int Id, string Title)> DocOf(string number, string titleStart)
        {
            var d = await db.Documents.AsNoTracking().Where(d => d.Matter.Number == number && d.Title.StartsWith(titleStart))
                .Select(d => new { d.Id, d.Title }).FirstAsync(ct);
            return (d.Id, d.Title);
        }

        var ime = await DocOf("10137-0001", "Independent medical");
        var receipt = await DocOf("10151-0003", "USCIS receipt");
        var closing = await DocOf("10111-0002", "Closing disclosure");

        ActivityEvent Ev(TimeSpan ago, string who, string channel, string action, string? target, int status, string summary, string? outcome = null) =>
            new() { OccurredUtc = now - ago, OperatorCode = who, Channel = channel, Action = action, Target = target, HttpStatus = status, Outcome = outcome ?? Outcomes.FromStatus(status), Summary = summary };

        db.ActivityEvents.AddRange(
            Ev(new TimeSpan(3, 2, 10, 0), "PAR", Channels.Web, "search", "deposition", 200, $"search \"deposition\" → {await Hits("deposition", "PAR")}"),
            Ev(new TimeSpan(3, 1, 55, 0), "PAR", Channels.Web, "document.create", "10137-0001", 201, $"document.create on 10137-0001 → 201 Created (\"{ime.Title}\", doc {ime.Id})"),
            Ev(new TimeSpan(2, 6, 0, 0), "LES", Channels.Cli, "search", "lease", 200, $"search \"lease\" → {await Hits("lease", "LES")}"),
            Ev(new TimeSpan(2, 5, 50, 0), "JDU", Channels.Mcp, "search_matters", "non-compete", 200, $"search_matters \"non-compete\" → {await Hits("non-compete", "JDU")}"),
            Ev(new TimeSpan(2, 5, 49, 0), "JDU", Channels.Mcp, "list_documents", "10118-0001", 200, "list_documents \"10118-0001\" → 5 documents"),
            Ev(new TimeSpan(1, 3, 0, 0), "LES", Channels.Mcp, "get_matter", "10118-0001", 200, "get_matter \"10118-0001\" → Matter 10118-0001 is restricted and operator LES has not been granted access", Outcomes.Denied),
            Ev(new TimeSpan(1, 2, 0, 0), "PAR", Channels.Web, "document.update", $"doc {closing.Id}", 409, $"document.update on doc {closing.Id} → 409 Conflict (saw v1, current v2)"),
            Ev(new TimeSpan(0, 5, 0, 0), "JDU", Channels.Cli, "document.create", "10151-0003", 201, $"document.create on 10151-0003 → 201 Created (\"{receipt.Title}\", doc {receipt.Id})"),
            Ev(new TimeSpan(0, 2, 0, 0), "LES", Channels.Mcp, "prompt.demo", null, 200, "prompt.demo → prompt delivered"),
            Ev(new TimeSpan(0, 0, 35, 0), "LES", Channels.Web, "search", "closing binder", 200, $"search \"closing binder\" → {await Hits("closing binder", "LES")}"));
        await db.SaveChangesAsync(ct);
    }
}
