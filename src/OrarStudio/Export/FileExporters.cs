using System.Text;
using ClosedXML.Excel;

namespace OrarStudio.Export;

/// <summary>Export Excel (.xlsx): orarul general, câte o foaie pentru fiecare grupă, lista lecțiilor și sarcina didactică.</summary>
public static class ExcelExporter
{
    private static readonly XLColor Border = XLColor.FromHtml("#D9E0EA");
    private static readonly XLColor HeadFill = XLColor.FromHtml("#EEF2FF");
    private static readonly XLColor HeadText = XLColor.FromHtml("#3730A3");

    public static void ExportWorkbook(Dataset d, Lookups lk, string path)
    {
        ExcelSupport.EnsureInit();
        using var wb = new XLWorkbook();
        OverviewSheet(wb, d, lk);
        foreach (var g in TimetableBuilder.OrderedGroups(d))
            TimetableSheet(wb, d, TimetableBuilder.ForGroup(d, lk, g));
        LessonsSheet(wb, d, lk);
        ActivitiesSheet(wb, d, lk);
        wb.SaveAs(path);
    }

    public static void ExportTimetables(Dataset d, IReadOnlyList<EntityTimetable> tables, string path)
    {
        ExcelSupport.EnsureInit();
        using var wb = new XLWorkbook();
        foreach (var t in tables) TimetableSheet(wb, d, t);
        wb.SaveAs(path);
    }

    private static string SheetName(XLWorkbook wb, string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var clean = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        if (clean.Length == 0) clean = "Foaie";
        if (clean.Length > 28) clean = clean[..28];
        var candidate = clean;
        int i = 2;
        while (wb.Worksheets.Any(w => string.Equals(w.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"{clean} ({i++})";
        return candidate;
    }

    private static string LessonText(LessonView v, ScheduleViewMode mode)
    {
        var sb = new StringBuilder();
        sb.Append(v.Subject).Append(" (").Append(v.TypeLabel.ToLowerInvariant()).Append(')');
        sb.Append('\n').Append(mode switch
        {
            ScheduleViewMode.Group => v.Teacher,
            ScheduleViewMode.Teacher => v.Groups,
            _ => $"{v.Groups} · {v.Teacher}",
        });
        if (mode != ScheduleViewMode.Room) sb.Append(" · aud. ").Append(v.Room);
        if (v.Week != WeekParity.Saptamanal) sb.Append("\nsăptămâna ").Append(Ro.WeekShort(v.Week)).Append('ă');
        if (v.Subgroup.Length > 0) sb.Append(v.Week != WeekParity.Saptamanal ? " · " : "\n").Append(v.Subgroup);
        return sb.ToString();
    }

    private static void StyleHeader(IXLRange r)
    {
        r.Style.Font.Bold = true;
        r.Style.Font.FontColor = HeadText;
        r.Style.Fill.BackgroundColor = HeadFill;
        r.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        r.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        r.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        r.Style.Border.OutsideBorderColor = Border;
        r.Style.Border.InsideBorderColor = Border;
    }

    private static void TimetableSheet(XLWorkbook wb, Dataset d, EntityTimetable t)
    {
        var s = d.Settings;
        var ws = wb.Worksheets.Add(SheetName(wb, t.FileKey));
        int days = TimetableBuilder.Days(d);
        ws.Cell(1, 1).Value = t.Title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(2, 1).Value = t.Subtitle;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#64748B");
        ws.Cell(3, 1).Value = $"{s.Institution} · {s.Faculty} · anul universitar {s.AcademicYear}, semestrul {s.Semester}";
        ws.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#64748B");

        const int hr = 5;
        ws.Cell(hr, 1).Value = "Perechea";
        for (int day = 0; day < days; day++) ws.Cell(hr, 2 + day).Value = Ro.Day(day);
        StyleHeader(ws.Range(hr, 1, hr, 1 + days));

        int r = hr + 1;
        foreach (var slot in s.TimeSlots.OrderBy(x => x.Number))
        {
            var head = ws.Cell(r, 1);
            head.Value = $"{Ro.Roman(slot.Number)}\n{slot.Start}–{slot.End}";
            head.Style.Font.Bold = true;
            head.Style.Alignment.WrapText = true;
            head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            head.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            head.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            int maxLines = 2;
            for (int day = 0; day < days; day++)
            {
                var items = t.At(day, slot.Number).ToList();
                var cell = ws.Cell(r, 2 + day);
                var text = string.Join("\n\n", items.Select(v => LessonText(v, t.Mode)));
                cell.Value = text;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                if (items.Count > 0)
                {
                    var (_, soft) = PdfExporter.TypeColors(items[0].Type);
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml(soft);
                }
                maxLines = Math.Max(maxLines, text.Count(c => c == '\n') + 1);
            }
            ws.Row(r).Height = Math.Clamp(maxLines * 15, 45, 260);
            r++;
        }
        var grid = ws.Range(hr, 1, r - 1, 1 + days);
        grid.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        grid.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        grid.Style.Border.OutsideBorderColor = Border;
        grid.Style.Border.InsideBorderColor = Border;
        ws.Column(1).Width = 13;
        for (int day = 0; day < days; day++) ws.Column(2 + day).Width = 32;
        ws.SheetView.Freeze(hr, 1);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 1);
    }

    private static void OverviewSheet(XLWorkbook wb, Dataset d, Lookups lk)
    {
        var s = d.Settings;
        var ws = wb.Worksheets.Add("Orar general");
        var groups = TimetableBuilder.OrderedGroups(d).ToList();
        int days = TimetableBuilder.Days(d);
        ws.Cell(1, 1).Value = $"Orarul general · {s.Faculty} · {s.AcademicYear}, semestrul {s.Semester}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 15;

        const int hr = 3;
        ws.Cell(hr, 1).Value = "Ziua";
        ws.Cell(hr, 2).Value = "Perechea";
        for (int i = 0; i < groups.Count; i++) ws.Cell(hr, 3 + i).Value = groups[i].Code;
        StyleHeader(ws.Range(hr, 1, hr, 2 + groups.Count));

        int r = hr + 1;
        var slots = s.TimeSlots.OrderBy(x => x.Number).ToList();
        for (int day = 0; day < days; day++)
        {
            int dayStart = r;
            foreach (var slot in slots)
            {
                ws.Cell(r, 2).Value = $"{Ro.Roman(slot.Number)}  {slot.Start}–{slot.End}";
                ws.Cell(r, 2).Style.Font.Bold = true;
                var cells = groups.Select(g => d.Lessons.Where(l => l.Day == day && l.Slot == slot.Number && Audience.Concerns(l.Groups, g.Code))
                                                        .OrderBy(l => l.Week).ToList()).ToList();
                int i = 0;
                int maxLines = 1;
                while (i < groups.Count)
                {
                    int span = 1;
                    if (cells[i].Count > 0)
                        while (i + span < groups.Count && cells[i + span].Count == cells[i].Count &&
                               cells[i + span].Select(x => x.Id).OrderBy(x => x).SequenceEqual(cells[i].Select(x => x.Id).OrderBy(x => x)))
                            span++;
                    var context = span == 1 ? groups[i].Code : null;
                    var text = string.Join("\n", cells[i].Select(l =>
                    {
                        var v = TimetableBuilder.View(l, lk, context);
                        var extra = v.Week != WeekParity.Saptamanal ? $" [{Ro.WeekShort(v.Week)}]" : "";
                        var sg = v.Subgroup.Length > 0 ? $" {v.Subgroup}" : "";
                        return $"{v.SubjectShort} ({Ro.TypeShort(v.Type)}) {v.Room} · {v.Teacher}{extra}{sg}";
                    }));
                    var cell = ws.Cell(r, 3 + i);
                    cell.Value = text;
                    if (cells[i].Count > 0)
                    {
                        var (_, soft) = PdfExporter.TypeColors(cells[i][0].Type);
                        var range = ws.Range(r, 3 + i, r, 2 + i + span);
                        if (span > 1) range.Merge();
                        range.Style.Fill.BackgroundColor = XLColor.FromHtml(soft);
                        range.Style.Alignment.WrapText = true;
                        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                        range.Style.Alignment.Horizontal = span > 1 ? XLAlignmentHorizontalValues.Center : XLAlignmentHorizontalValues.Left;
                    }
                    maxLines = Math.Max(maxLines, cells[i].Count);
                    i += span;
                }
                ws.Row(r).Height = Math.Clamp(maxLines * 30, 30, 160);
                r++;
            }
            var dayRange = ws.Range(dayStart, 1, r - 1, 1);
            dayRange.Merge();
            dayRange.Value = Ro.Day(day);
            dayRange.Style.Font.Bold = true;
            dayRange.Style.Alignment.TextRotation = 90;
            dayRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            dayRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            dayRange.Style.Fill.BackgroundColor = HeadFill;
            ws.Range(r - 1, 1, r - 1, 2 + groups.Count).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        }
        var all = ws.Range(hr, 1, r - 1, 2 + groups.Count);
        all.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        all.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        all.Style.Border.InsideBorderColor = Border;
        all.Style.Border.OutsideBorderColor = Border;
        ws.Column(1).Width = 6;
        ws.Column(2).Width = 16;
        for (int i = 0; i < groups.Count; i++) ws.Column(3 + i).Width = 26;
        ws.SheetView.Freeze(hr, 2);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
    }

    private static void LessonsSheet(XLWorkbook wb, Dataset d, Lookups lk)
    {
        var ws = wb.Worksheets.Add("Lecții");
        string[] headers = { "Ziua", "Perechea", "Ora", "Săptămâna", "Disciplina", "Tip", "Profesor", "Grupe", "Auditoriu", "Studenți", "Notă" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        StyleHeader(ws.Range(1, 1, 1, headers.Length));
        int r = 2;
        foreach (var l in d.Lessons.OrderBy(x => x.Day).ThenBy(x => x.Slot).ThenBy(x => Audience.Format(x.Groups)))
        {
            var slot = d.Settings.Slot(l.Slot);
            ws.Cell(r, 1).Value = Ro.Day(l.Day);
            ws.Cell(r, 2).Value = l.Slot;
            ws.Cell(r, 3).Value = slot?.Label ?? "";
            ws.Cell(r, 4).Value = Ro.WeekLabel(l.Week);
            ws.Cell(r, 5).Value = lk.SubjectName(l.SubjectId);
            ws.Cell(r, 6).Value = Ro.TypeLabel(l.Type);
            ws.Cell(r, 7).Value = lk.TeacherName(l.TeacherId);
            ws.Cell(r, 8).Value = Audience.Format(l.Groups);
            ws.Cell(r, 9).Value = lk.RoomName(l.RoomId);
            ws.Cell(r, 10).Value = lk.StudentsOf(l);
            ws.Cell(r, 11).Value = l.Note;
            r++;
        }
        if (r > 2) ws.Range(1, 1, r - 1, headers.Length).SetAutoFilter();
        ws.Columns(1, headers.Length).AdjustToContents(1, Math.Min(r, 300), 8, 60);
        ws.SheetView.FreezeRows(1);
    }

    private static void ActivitiesSheet(XLWorkbook wb, Dataset d, Lookups lk)
    {
        var ws = wb.Worksheets.Add("Sarcina didactică");
        string[] headers = { "Disciplina", "Tip", "Ore/săpt.", "Grupe", "Studenți", "Profesor", "Limba", "Ore plasate", "Stare" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        StyleHeader(ws.Range(1, 1, 1, headers.Length));
        int r = 2;
        foreach (var a in d.Activities.OrderBy(x => lk.SubjectName(x.SubjectId)).ThenBy(x => x.Type))
        {
            int placed = lk.PlacedHoursOf(a.Id);
            ws.Cell(r, 1).Value = lk.SubjectName(a.SubjectId);
            ws.Cell(r, 2).Value = Ro.TypeLabel(a.Type);
            ws.Cell(r, 3).Value = a.HoursPerWeek;
            ws.Cell(r, 4).Value = Audience.Format(a.Groups);
            ws.Cell(r, 5).Value = a.Students;
            ws.Cell(r, 6).Value = lk.TeacherName(a.TeacherId);
            ws.Cell(r, 7).Value = Ro.LangShort(a.Language);
            ws.Cell(r, 8).Value = placed;
            ws.Cell(r, 9).Value = placed >= a.HoursPerWeek ? "Complet" : placed == 0 ? "Neplasat" : "Parțial";
            ws.Cell(r, 9).Style.Font.FontColor = placed >= a.HoursPerWeek ? XLColor.FromHtml("#059669") : XLColor.FromHtml("#DC2626");
            r++;
        }
        if (r > 2) ws.Range(1, 1, r - 1, headers.Length).SetAutoFilter();
        ws.Columns(1, headers.Length).AdjustToContents(1, Math.Min(r, 300), 8, 60);
        ws.SheetView.FreezeRows(1);
    }
}

/// <summary>Export CSV (separator „;", UTF-8 cu BOM — se deschide corect în Excel).</summary>
public static class CsvExporter
{
    public static string Build(Dataset d, Lookups lk, IEnumerable<Lesson>? lessons = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ziua;Perechea;Inceput;Sfarsit;Saptamana;Disciplina;Tip;Profesor;Grupe;Auditoriu;Studenti;Nota");
        foreach (var l in (lessons ?? d.Lessons).OrderBy(x => x.Day).ThenBy(x => x.Slot))
        {
            var slot = d.Settings.Slot(l.Slot);
            string[] cols =
            {
                Ro.Day(l.Day), l.Slot.ToString(), slot?.Start ?? "", slot?.End ?? "", Ro.WeekLabel(l.Week),
                lk.SubjectName(l.SubjectId), Ro.TypeLabel(l.Type), lk.TeacherName(l.TeacherId),
                Audience.Format(l.Groups), lk.RoomName(l.RoomId), lk.StudentsOf(l).ToString(), l.Note,
            };
            sb.AppendLine(string.Join(";", cols.Select(Escape)));
        }
        return sb.ToString();
    }

    private static string Escape(string v) =>
        v.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

    public static void Save(string content, string path) =>
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
}

/// <summary>Export iCalendar (.ics) — orarul poate fi importat în Google Calendar / Outlook / telefon.</summary>
public static class IcsExporter
{
    public static string Build(Dataset d, EntityTimetable t)
    {
        var s = d.Settings;
        var sb = new StringBuilder();
        void L(string line) => Fold(sb, line);
        L("BEGIN:VCALENDAR");
        L("VERSION:2.0");
        L("PRODID:-//Orar Studio//Orar universitar//RO");
        L("CALSCALE:GREGORIAN");
        L("METHOD:PUBLISH");
        L("X-WR-CALNAME:" + Esc($"Orar – {t.Title}"));
        L("X-WR-TIMEZONE:Europe/Chisinau");

        var monday1 = SemesterCalendar.MondayOf(s.SemesterStart);
        var until = SemesterCalendar.SemesterEnd(s);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        foreach (var lv in t.Lessons)
        {
            var slot = s.Slot(lv.Slot);
            if (slot == null) continue;
            int firstWeek = lv.Week switch
            {
                WeekParity.Impara => s.FirstWeekOdd ? 1 : 2,
                WeekParity.Para => s.FirstWeekOdd ? 2 : 1,
                _ => 1,
            };
            int step = lv.Week == WeekParity.Saptamanal ? 7 : 14;
            var date = monday1.AddDays(7 * (firstWeek - 1) + lv.Day);
            while (date < s.SemesterStart.Date) date = date.AddDays(step);
            var start = date + slot.StartTime;
            var end = date + slot.EndTime;
            L("BEGIN:VEVENT");
            L($"UID:{lv.Id}@orarstudio");
            L("DTSTAMP:" + stamp);
            L($"DTSTART;TZID=Europe/Chisinau:{start:yyyyMMdd'T'HHmmss}");
            L($"DTEND;TZID=Europe/Chisinau:{end:yyyyMMdd'T'HHmmss}");
            L($"RRULE:FREQ=WEEKLY;INTERVAL={(lv.Week == WeekParity.Saptamanal ? 1 : 2)};UNTIL={until:yyyyMMdd}T235959");
            L("SUMMARY:" + Esc($"{lv.Subject} ({lv.TypeLabel.ToLowerInvariant()})"));
            L("LOCATION:" + Esc($"aud. {lv.Room}"));
            var desc = $"Profesor: {lv.TeacherFull}\nGrupe: {lv.Groups}";
            if (lv.Week != WeekParity.Saptamanal) desc += $"\n{Ro.WeekLabel(lv.Week)}";
            L("DESCRIPTION:" + Esc(desc));
            L("CATEGORIES:" + Esc(lv.TypeLabel));
            L("END:VEVENT");
        }
        L("END:VCALENDAR");
        return sb.ToString();
    }

    private static string Esc(string v) =>
        v.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");

    private static void Fold(StringBuilder sb, string line)
    {
        const int max = 72;
        if (line.Length <= max) { sb.Append(line).Append("\r\n"); return; }
        sb.Append(line[..max]).Append("\r\n");
        for (int i = max; i < line.Length; i += max - 1)
            sb.Append(' ').Append(line.Substring(i, Math.Min(max - 1, line.Length - i))).Append("\r\n");
    }
}
