using MatterDesk.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MatterDesk.Api.Data;

/// <summary>Deterministic demo data: two operators, one restricted matter that only one of them can see.</summary>
public static class Seed
{
    public static async Task ApplyAsync(MatterDeskDbContext db, CancellationToken ct = default)
    {
        if (await db.Operators.AnyAsync(ct)) return;

        var les = new Operator { Code = "LES", DisplayName = "Luis Escobar", Email = "les@firm.example" };
        var jdu = new Operator { Code = "JDU", DisplayName = "J. Duncan", Email = "jdu@firm.example" };
        var par = new Operator { Code = "PAR", DisplayName = "Paralegal One", Email = "par@firm.example" };
        db.Operators.AddRange(les, jdu, par);

        var acme = new Client { Number = "10042", Name = "Acme Insurance Group" };
        var brightline = new Client { Number = "10077", Name = "Brightline Patents LLC" };
        var confidential = new Client { Number = "10099", Name = "Confidential Client (Restricted)" };
        db.Clients.AddRange(acme, brightline, confidential);

        var t0 = new DateTime(2026, 1, 15, 14, 0, 0, DateTimeKind.Utc);

        var m1 = new Matter { Client = acme, Number = "10042-0003", Title = "Acme v. Rodriguez — auto liability defense", AreaOfLaw = "Insurance Defense", OpenedUtc = t0 };
        var m2 = new Matter { Client = brightline, Number = "10077-0001", Title = "Brightline — US 18/123,456 office action response", AreaOfLaw = "Intellectual Property", OpenedUtc = t0.AddDays(10) };
        var m3 = new Matter { Client = confidential, Number = "10099-0001", Title = "Project Falcon — partner departure dispute", AreaOfLaw = "Employment", IsRestricted = true, OpenedUtc = t0.AddDays(20) };
        db.Matters.AddRange(m1, m2, m3);

        // Restricted matter: only JDU is granted. LES and PAR must never see it, including through search.
        db.MatterAccess.AddRange(
            new MatterAccess { Matter = m3, Operator = jdu, CanEdit = true },
            new MatterAccess { Matter = m1, Operator = les, CanEdit = true },
            new MatterAccess { Matter = m2, Operator = les, CanEdit = true },
            new MatterAccess { Matter = m1, Operator = par, CanEdit = false });

        Document Doc(Matter m, string title, string type, string author, string? keywords, int days)
        {
            var d = new Document { Matter = m, Title = title, DocumentType = type, AuthorCode = author, Keywords = keywords, CreatedUtc = t0.AddDays(days), ModifiedUtc = t0.AddDays(days), Version = 1 };
            d.Versions.Add(new DocumentVersion { Document = d, VersionNumber = 1, StoragePath = $"/dms/{m.Number}/{Guid.NewGuid():N}.docx", CreatedByCode = author, CreatedUtc = t0.AddDays(days) });
            return d;
        }

        db.Documents.AddRange(
            Doc(m1, "Answer and Affirmative Defenses", "Pleading", "LES", "answer; affirmative defenses; comparative negligence", 2),
            Doc(m1, "Letter to adjuster re reserves", "Correspondence", "LES", "adjuster; reserves; Acme", 5),
            Doc(m1, "Plaintiff's first set of interrogatories", "Discovery", "PAR", "interrogatories; discovery", 9),
            Doc(m2, "Office action response draft", "Prosecution", "LES", "office action; 35 USC 103; claims", 12),
            Doc(m3, "Falcon — separation term sheet (PRIVILEGED)", "Agreement", "JDU", "Falcon; separation; privileged; Acme", 22),
            Doc(m3, "Falcon — board memo", "Memo", "JDU", "Falcon; board; confidential", 24));

        db.ProfiledEmails.Add(new ProfiledEmail
        {
            Matter = m1, ExternalMessageId = "AAMkSeed001", InternetMessageId = "<seed001@acme.example>",
            Subject = "RE: [10042-0003] Rodriguez — deposition dates", FromAddress = "adjuster@acme.example",
            ReceivedUtc = t0.AddDays(6), BodyPreview = "Can we hold the 14th and 15th?", ProfiledByCode = "LES", ProfiledUtc = t0.AddDays(6), MarkerSet = true
        });

        await db.SaveChangesAsync(ct);
    }
}
