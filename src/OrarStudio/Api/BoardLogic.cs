namespace OrarStudio.Api;

public sealed record BoardEntryDto(string Subject, string SubjectShort, string Type, string TypeLabel, string Teacher,
    string Room, string Groups, string Time, string Color, string Week);

public sealed record BoardRowDto(string Key, string Title, string Subtitle, BoardEntryDto[] Now, BoardEntryDto[] Next, bool Busy);

public sealed record BoardStateDto(
    string Timestamp, string Date, string Time, int DayIndex, string DayName,
    int Week, string Parity, string ParityLabel, bool InSemester,
    int? CurrentSlot, string? CurrentSlotLabel, int? NextSlot, string Status, int MinutesLeft,
    string Mode, BoardRowDto[] Rows, string Message);

/// <summary>
/// Logica panoului informativ („ce se întâmplă acum și ce urmează"), comună pentru fereastra Panou
/// din aplicație și pentru endpoint-ul /api/now folosit de panoul web.
/// </summary>
public static class BoardLogic
{
    public static BoardStateDto Compute(Dataset d, Lookups lk, DateTime now, string mode)
    {
        var s = d.Settings;
        int days = TimetableBuilder.Days(d);
        int dayIndex = Ro.DayIndex(now);
        int week = SemesterCalendar.WeekNumber(s, now);
        var parity = SemesterCalendar.Parity(s, week);
        bool inSemester = SemesterCalendar.InSemester(s, week);
        var slots = s.TimeSlots.OrderBy(x => x.Number).ToList();
        var t = now.TimeOfDay;

        var current = slots.FirstOrDefault(x => x.StartTime <= t && t < x.EndTime);
        var next = current != null
            ? slots.FirstOrDefault(x => x.Number > current.Number)
            : slots.FirstOrDefault(x => x.StartTime > t);

        string status;
        int minutesLeft = 0;
        bool workDay = dayIndex < days;
        if (!workDay)
        {
            status = "Astăzi nu sunt lecții";
            current = null;
            next = null;
        }
        else if (current != null)
        {
            minutesLeft = (int)Math.Ceiling((current.EndTime - t).TotalMinutes);
            status = $"Se încheie în {minutesLeft} min" +
                     (next != null ? $" · urmează perechea {Ro.Roman(next.Number)} la {next.Start}" : " · ultima pereche a zilei");
        }
        else if (next != null)
        {
            minutesLeft = (int)Math.Ceiling((next.StartTime - t).TotalMinutes);
            status = next == slots.FirstOrDefault()
                ? $"Lecțiile încep la {next.Start} (perechea {Ro.Roman(next.Number)})"
                : $"Pauză · urmează perechea {Ro.Roman(next.Number)} la {next.Start} (în {minutesLeft} min)";
        }
        else
        {
            status = "Lecțiile de astăzi s-au încheiat";
        }

        var today = workDay
            ? d.Lessons.Where(l => l.Day == dayIndex && SemesterCalendar.OccursIn(l.Week, parity)).ToList()
            : new List<Lesson>();
        int afterSlot = current?.Number ?? (next != null ? next.Number - 1 : int.MaxValue);

        BoardEntryDto Entry(Lesson l)
        {
            var slot = s.Slot(l.Slot);
            return new BoardEntryDto(lk.SubjectName(l.SubjectId), lk.SubjectShort(l.SubjectId),
                ApiSnapshot.TypeCode(l.Type), Ro.TypeLabel(l.Type), lk.TeacherName(l.TeacherId),
                lk.RoomName(l.RoomId), Audience.Format(l.Groups),
                slot != null ? $"{slot.Start}–{slot.End}" : "", lk.SubjectColor(l.SubjectId), ApiSnapshot.WeekCode(l.Week));
        }

        BoardRowDto Row(string key, string title, string subtitle, Func<Lesson, bool> filter)
        {
            var mine = today.Where(filter).ToList();
            var nowItems = current != null ? mine.Where(l => l.Slot == current.Number).Select(Entry).ToArray() : Array.Empty<BoardEntryDto>();
            var nextSlot = mine.Where(l => l.Slot > afterSlot).Select(l => l.Slot).DefaultIfEmpty(0).Min();
            var nextItems = nextSlot > 0 ? mine.Where(l => l.Slot == nextSlot).Select(Entry).ToArray() : Array.Empty<BoardEntryDto>();
            return new BoardRowDto(key, title, subtitle, nowItems, nextItems, nowItems.Length > 0);
        }

        var rows = new List<BoardRowDto>();
        switch (mode)
        {
            case "groups":
                foreach (var g in TimetableBuilder.OrderedGroups(d))
                    rows.Add(Row(g.Code, g.Code, $"{g.Specialty} · anul {g.Year}", l => Audience.Concerns(l.Groups, g.Code)));
                break;
            case "teachers":
                foreach (var tch in d.Teachers.Where(x => today.Any(l => l.TeacherId == x.Id))
                                              .OrderBy(x => x.Name, StringComparer.Create(Ro.Culture, true)))
                    rows.Add(Row(tch.Id.ToString(), tch.Name, tch.Title, l => l.TeacherId == tch.Id));
                break;
            default:
                foreach (var r in d.Rooms.OrderBy(x => x.Name, StringComparer.Create(Ro.Culture, true)))
                    rows.Add(Row(r.Id.ToString(), $"Aud. {r.Name}", $"{r.KindLabel} · {r.Capacity} locuri", l => l.RoomId == r.Id));
                mode = "rooms";
                break;
        }

        var dateText = now.ToString("dddd, d MMMM yyyy", Ro.Culture);
        dateText = char.ToUpper(dateText[0], Ro.Culture) + dateText[1..];
        return new BoardStateDto(
            now.ToString("yyyy-MM-ddTHH:mm:ss"), dateText, now.ToString("HH:mm"), dayIndex, Ro.Day(dayIndex),
            week, ApiSnapshot.WeekCode(parity), parity == WeekParity.Impara ? "săptămâna impară" : "săptămâna pară", inSemester,
            current?.Number, current != null ? $"Perechea {Ro.Roman(current.Number)} · {current.Label}" : null,
            next?.Number, status, minutesLeft, mode, rows.ToArray(), s.BoardMessage);
    }
}
