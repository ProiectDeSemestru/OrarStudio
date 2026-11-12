using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OrarStudio.Export;

/// <summary>Export PDF (QuestPDF): orare săptămânale (grupă/profesor/auditoriu) și orarul general.</summary>
public static class PdfExporter
{
    private static readonly object InitLock = new();
    private static bool _initialized;

    private const string Font = "Segoe UI";
    private const string Ink = "#0F172A";
    private const string Muted = "#64748B";
    private const string Line = "#E2E8F0";
    private const string HeadBg = "#F1F5F9";
    private const string Accent = "#4F46E5";

    private static void Init()
    {
        lock (InitLock)
        {
            if (_initialized) return;
            QuestPDF.Settings.License = LicenseType.Community;
            // Din QuestPDF 2026.9 fonturile de sistem nu mai sunt folosite implicit:
            // înregistrăm explicit familia Segoe UI (diacritice românești + chirilică).
            int registered = 0;
            var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var file in new[] { "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf", "seguisb.ttf", "seguisbi.ttf" })
            {
                var path = Path.Combine(fontsDir, file);
                if (!File.Exists(path)) continue;
                try
                {
                    using var fs = File.OpenRead(path);
                    QuestPDF.Drawing.FontManager.RegisterFontFromStream(fs);
                    registered++;
                }
                catch (Exception ex) { Log.Error(ex, "Înregistrare font " + file); }
            }
            if (registered == 0) QuestPDF.Settings.UseSystemFonts = true;
            _initialized = true;
        }
    }

    /// <summary>Culorile tipurilor de lecții (aceeași paletă validată ca în interfață).</summary>
    public static (string Strong, string Soft) TypeColors(LessonType t) => t switch
    {
        LessonType.Curs => ("#4F46E5", "#EEF0FF"),
        LessonType.Seminar => ("#EA580C", "#FFF1E8"),
        _ => ("#0D9488", "#E6F7F5"),
    };

    // ───────────────────────── Orare săptămânale ─────────────────────────

    public static Document BuildTimetables(Dataset d, IReadOnlyList<EntityTimetable> tables, string title)
    {
        Init();
        var doc = Document.Create(c =>
        {
            foreach (var t in tables)
                c.Page(page => TimetablePage(page, d, t));
        });
        return doc.WithMetadata(new DocumentMetadata
        {
            Title = title,
            Author = d.Settings.Faculty,
            Subject = $"Orar {d.Settings.AcademicYear}, semestrul {d.Settings.Semester}",
            Creator = "Orar Studio",
            Producer = "Orar Studio (QuestPDF)",
        });
    }

    public static byte[] Timetables(Dataset d, IReadOnlyList<EntityTimetable> tables, string title) =>
        BuildTimetables(d, tables, title).GeneratePdf();

    private static void TimetablePage(PageDescriptor page, Dataset d, EntityTimetable t)
    {
        var s = d.Settings;
        int days = TimetableBuilder.Days(d);
        page.Size(PageSizes.A4.Landscape());
        page.Margin(22);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(8).FontColor(Ink));

        page.Header().Element(h => Header(h, s, t.Title, t.Subtitle));

        page.Content().PaddingTop(10).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(56);
                for (int i = 0; i < days; i++) cols.RelativeColumn();
            });

            table.Header(h =>
            {
                h.Cell().Element(HeadCell).Text("Perechea").SemiBold().FontSize(8);
                for (int i = 0; i < days; i++)
                    h.Cell().Element(HeadCell).Text(Ro.Day(i)).SemiBold().FontSize(9);
            });

            foreach (var slot in s.TimeSlots.OrderBy(x => x.Number))
            {
                table.Cell().Element(SlotCell).Column(c =>
                {
                    c.Item().AlignCenter().Text(Ro.Roman(slot.Number)).FontSize(13).Bold().FontColor(Accent);
                    c.Item().AlignCenter().Text(slot.Start).FontSize(7).FontColor(Muted);
                    c.Item().AlignCenter().Text(slot.End).FontSize(7).FontColor(Muted);
                });
                for (int day = 0; day < days; day++)
                {
                    var items = t.At(day, slot.Number).ToList();
                    table.Cell().Element(BodyCell).Column(col =>
                    {
                        col.Spacing(3);
                        foreach (var lv in items)
                            col.Item().Element(x => LessonBox(x, lv, t.Mode));
                    });
                }
            }
        });

        page.Footer().Element(f => Footer(f, s));
    }

    private static void LessonBox(IContainer x, LessonView lv, ScheduleViewMode mode)
    {
        var (strong, soft) = TypeColors(lv.Type);
        x.Background(soft).BorderLeft(2.5f).BorderColor(strong).PaddingVertical(3).PaddingHorizontal(4).Column(c =>
        {
            c.Item().Text(lv.Subject).SemiBold().FontSize(7.8f);
            var second = mode switch
            {
                ScheduleViewMode.Group => lv.Teacher,
                ScheduleViewMode.Teacher => lv.Groups,
                _ => $"{lv.Groups} · {lv.Teacher}",
            };
            c.Item().Text(second).FontSize(7).FontColor("#334155");
            c.Item().DefaultTextStyle(st => st.FontSize(6.8f)).Text(text =>
            {
                text.Span(lv.TypeLabel).SemiBold().FontColor("#1E293B");
                if (mode != ScheduleViewMode.Room) text.Span($"  ·  aud. {lv.Room}").FontColor("#334155");
                if (lv.Week != WeekParity.Saptamanal) text.Span($"  ·  săpt. {Ro.WeekShort(lv.Week)}ă").Italic().SemiBold().FontColor("#334155");
                if (lv.Subgroup.Length > 0) text.Span($"  ·  {lv.Subgroup}").FontColor("#334155");
            });
        });
    }

    // ───────────────────────── Orarul general (matrice) ─────────────────────────

    public static Document BuildOverview(Dataset d, Lookups lk, IReadOnlyList<StudentGroup> groups, int perPage = 8)
    {
        Init();
        var s = d.Settings;
        int days = TimetableBuilder.Days(d);
        var chunks = groups.Chunk(Math.Max(1, perPage)).ToList();
        var doc = Document.Create(c =>
        {
            foreach (var chunk in chunks)
            {
                c.Page(page =>
                {
                    page.Size(PageSizes.A3.Landscape());
                    page.Margin(24);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(7.5f).FontColor(Ink));
                    page.Header().Element(h => Header(h, s, "Orarul general al lecțiilor",
                        chunk.Length == 1 ? $"Grupa {chunk[0].Code}" : $"Grupele {chunk.First().Code} – {chunk.Last().Code}"));

                    var codes = chunk.Select(g => g.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    int maxUsed = d.Lessons.Where(l => l.Groups.Any(code => codes.Contains(Audience.BaseCode(code))))
                                           .Select(l => l.Slot).DefaultIfEmpty(0).Max();
                    var slotList = s.TimeSlots.OrderBy(x => x.Number).Where(x => x.Number <= Math.Max(maxUsed, 4)).ToList();

                    page.Content().PaddingTop(8).Column(column =>
                    {
                        column.Spacing(12);
                        for (int dayIndex = 0; dayIndex < days; dayIndex++)
                        {
                            int day = dayIndex;
                            // fiecare zi = un tabel separat; dacă nu mai încape pe pagină, începe pe pagina următoare
                            column.Item().EnsureSpace(230).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(50);
                                foreach (var _ in chunk) cols.RelativeColumn();
                            });
                            table.Header(h =>
                            {
                                h.Cell().Element(HeadCell).Text(Ro.Day(day).ToUpperInvariant()).Bold().FontSize(8.5f).FontColor(Accent);
                                foreach (var g in chunk)
                                    h.Cell().Element(HeadCell).Column(col =>
                                    {
                                        col.Item().AlignCenter().Text(g.Code).Bold().FontSize(9.5f);
                                        col.Item().AlignCenter().Text($"anul {g.Year} · {Ro.LangShort(g.Language)}").FontSize(6.5f).FontColor(Muted);
                                    });
                            });

                            foreach (var slot in slotList)
                            {
                                table.Cell().Element(SlotCell).Column(col =>
                                {
                                    col.Item().AlignCenter().Text(Ro.Roman(slot.Number)).Bold().FontSize(10).FontColor(Accent);
                                    col.Item().AlignCenter().Text(slot.Label).FontSize(6).FontColor(Muted);
                                });

                                var cells = chunk.Select(g => d.Lessons
                                    .Where(l => l.Day == day && l.Slot == slot.Number && Audience.Concerns(l.Groups, g.Code))
                                    .OrderBy(l => l.Week).ThenBy(l => Audience.Format(l.Groups)).ToList()).ToList();

                                int i = 0;
                                while (i < chunk.Length)
                                {
                                    int span = 1;
                                    if (cells[i].Count > 0)
                                        while (i + span < chunk.Length && SameLessons(cells[i], cells[i + span])) span++;
                                    var context = span == 1 ? chunk[i].Code : null;
                                    var views = cells[i].Select(l => TimetableBuilder.View(l, lk, context)).ToList();
                                    table.Cell().ColumnSpan((uint)span).Element(CompactCell).Column(col =>
                                    {
                                        col.Spacing(2);
                                        foreach (var v in views) col.Item().Element(x => CompactBox(x, v));
                                    });
                                    i += span;
                                }
                            }
                        });
                        }
                    });
                    page.Footer().Element(f => Footer(f, s));
                });
            }
        });
        return doc.WithMetadata(new DocumentMetadata { Title = "Orarul general", Creator = "Orar Studio", Producer = "Orar Studio (QuestPDF)" });
    }

    public static byte[] Overview(Dataset d, Lookups lk, IReadOnlyList<StudentGroup> groups) =>
        BuildOverview(d, lk, groups).GeneratePdf();

    private static bool SameLessons(List<Lesson> a, List<Lesson> b) =>
        a.Count == b.Count && a.Select(x => x.Id).OrderBy(x => x).SequenceEqual(b.Select(x => x.Id).OrderBy(x => x));

    private static void CompactBox(IContainer x, LessonView v)
    {
        var (strong, soft) = TypeColors(v.Type);
        x.Background(soft).BorderLeft(2).BorderColor(strong).PaddingVertical(2).PaddingHorizontal(3).Column(c =>
        {
            c.Item().Text(t =>
            {
                t.Span(v.SubjectShort).Bold().FontSize(7.5f);
                t.Span($" ({Ro.TypeShort(v.Type)})").FontSize(7).FontColor("#475569").SemiBold();
            });
            c.Item().Text($"aud. {v.Room} · {v.Teacher}").FontSize(6.5f).FontColor("#334155");
            var extra = new List<string>();
            if (v.Week != WeekParity.Saptamanal) extra.Add($"săpt. {Ro.WeekShort(v.Week)}ă");
            if (v.Subgroup.Length > 0) extra.Add(v.Subgroup);
            if (extra.Count > 0) c.Item().Text(string.Join(" · ", extra)).FontSize(6.2f).Italic().SemiBold().FontColor("#475569");
        });
    }

    // ───────────────────────── Elemente comune ─────────────────────────

    private static IContainer HeadCell(IContainer c) =>
        c.Background(HeadBg).BorderBottom(1).BorderRight(1).BorderColor(Line).PaddingVertical(6).PaddingHorizontal(4).AlignCenter().AlignMiddle();

    private static IContainer SlotCell(IContainer c) =>
        c.BorderBottom(1).BorderRight(1).BorderColor(Line).Background("#FAFBFD").Padding(4).AlignMiddle();

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(1).BorderRight(1).BorderColor(Line).Padding(3).MinHeight(54);

    private static IContainer CompactCell(IContainer c) =>
        c.BorderBottom(1).BorderRight(1).BorderColor(Line).Padding(2).MinHeight(26);

    private static void Header(IContainer h, ScheduleSettings s, string title, string subtitle)
    {
        h.BorderBottom(1.5f).BorderColor(Accent).PaddingBottom(8).Row(r =>
        {
            r.RelativeItem().Column(c =>
            {
                c.Item().Text($"{s.Institution} · {s.Faculty}").FontSize(7.5f).SemiBold().FontColor(Muted);
                c.Item().PaddingTop(2).Text(title).FontSize(18).Bold().FontColor(Ink);
                if (!string.IsNullOrWhiteSpace(subtitle)) c.Item().Text(subtitle).FontSize(9).FontColor(Muted);
            });
            r.ConstantItem(260).AlignRight().AlignBottom().Column(c =>
            {
                c.Item().AlignRight().Text($"Anul universitar {s.AcademicYear} · semestrul {s.Semester}").FontSize(8.5f).SemiBold();
                c.Item().AlignRight().PaddingTop(5).Row(lg =>
                {
                    Legend(lg, "Curs", LessonType.Curs);
                    Legend(lg, "Seminar", LessonType.Seminar);
                    Legend(lg, "Laborator", LessonType.Laborator);
                });
            });
        });
    }

    private static void Legend(RowDescriptor row, string label, LessonType type)
    {
        var (strong, soft) = TypeColors(type);
        row.AutoItem().PaddingLeft(8).Row(x =>
        {
            x.ConstantItem(10).Height(10).Background(soft).BorderLeft(2.5f).BorderColor(strong);
            x.AutoItem().PaddingLeft(3).Text(label).FontSize(7.5f).FontColor(Muted);
        });
    }

    private static void Footer(IContainer f, ScheduleSettings s)
    {
        f.PaddingTop(6).Row(r =>
        {
            r.RelativeItem().Text($"{s.Faculty} · {s.Department}").FontSize(7).FontColor(Muted);
            r.RelativeItem().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(7).FontColor(Muted));
                t.Span("Pagina ");
                t.CurrentPageNumber();
                t.Span(" din ");
                t.TotalPages();
            });
            r.RelativeItem().AlignRight().Text($"Generat la {DateTime.Now:dd.MM.yyyy HH:mm} · Orar Studio").FontSize(7).FontColor(Muted);
        });
    }

    /// <summary>Imagini PNG ale paginilor (pentru previzualizare în aplicație).</summary>
    public static IReadOnlyList<byte[]> RenderImages(Document doc, int dpi = 60) =>
        doc.GenerateImages(new ImageGenerationSettings { RasterDpi = dpi, ImageFormat = ImageFormat.Png }).ToList();
}
