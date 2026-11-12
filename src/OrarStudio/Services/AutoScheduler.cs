using System.Diagnostics;

namespace OrarStudio.Services;

public sealed class AutoScheduleOptions
{
    /// <summary>Șterge lecțiile neblocate și refă orarul de la zero.</summary>
    public bool RebuildAll { get; set; }
    public int MaxPairsPerDay { get; set; } = 4;
    public int LastComfortSlot { get; set; } = 5;
    public bool RespectUnavailability { get; set; } = true;
    public bool AvoidGaps { get; set; } = true;
    /// <summary>Dacă este setat, se plasează doar aceste activități.</summary>
    public HashSet<Guid>? OnlyActivities { get; set; }
}

public sealed class AutoScheduleResult
{
    public List<Lesson> Added { get; } = new();
    public List<Guid> Removed { get; } = new();
    public List<string> Unplaced { get; } = new();
    public int Tasks { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Generator automat (euristic, „greedy") pentru activitățile nealocate.
/// Respectă conflictele de profesor/grupă/sală, destinația și capacitatea sălilor, doleanțele profesorilor,
/// și încearcă să evite ferestrele, zilele supraîncărcate și perechile târzii.
/// </summary>
public static class AutoScheduler
{
    private sealed record PlacementTask(TeachingActivity Activity, bool Biweekly, int Students, string SubjectName);

    private sealed class Occupancy
    {
        public readonly Dictionary<(int Day, int Slot), List<Lesson>> Grid = new();
        public readonly Dictionary<(string Group, int Day), HashSet<int>> GroupSlots = new();
        public readonly Dictionary<(string Group, int Day), HashSet<Guid>> GroupSubjects = new();
        public readonly Dictionary<(Guid Teacher, int Day), int> TeacherDay = new();

        public void Add(Lesson l)
        {
            if (!Grid.TryGetValue((l.Day, l.Slot), out var list)) Grid[(l.Day, l.Slot)] = list = new List<Lesson>();
            list.Add(l);
            foreach (var g in l.Groups.Select(Audience.BaseCode).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var key = (g.ToUpperInvariant(), l.Day);
                if (!GroupSlots.TryGetValue(key, out var slots)) GroupSlots[key] = slots = new HashSet<int>();
                slots.Add(l.Slot);
                if (!GroupSubjects.TryGetValue(key, out var subj)) GroupSubjects[key] = subj = new HashSet<Guid>();
                subj.Add(l.SubjectId);
            }
            if (l.TeacherId is Guid t)
                TeacherDay[(t, l.Day)] = TeacherDay.TryGetValue((t, l.Day), out var c) ? c + 1 : 1;
        }

        public List<Lesson>? At(int day, int slot) => Grid.TryGetValue((day, slot), out var l) ? l : null;

        public int GroupCount(string g, int day) =>
            GroupSlots.TryGetValue((g.ToUpperInvariant(), day), out var s) ? s.Count : 0;

        public bool GroupBusy(string g, int day, int slot) =>
            GroupSlots.TryGetValue((g.ToUpperInvariant(), day), out var s) && s.Contains(slot);

        public bool GroupHasSubject(string g, int day, Guid subject) =>
            GroupSubjects.TryGetValue((g.ToUpperInvariant(), day), out var s) && s.Contains(subject);

        public int TeacherCount(Guid? t, int day) =>
            t is Guid id && TeacherDay.TryGetValue((id, day), out var c) ? c : 0;
    }

    public static AutoScheduleResult Run(Dataset d, AutoScheduleOptions o)
    {
        var sw = Stopwatch.StartNew();
        var res = new AutoScheduleResult();
        var lk = new Lookups(d);
        int days = Math.Clamp(d.Settings.DaysPerWeek, 1, 7);
        var slots = d.Settings.TimeSlots.Select(s => s.Number).Distinct().OrderBy(n => n).ToList();

        var occ = new Occupancy();
        var placed = new Dictionary<Guid, int>();
        foreach (var l in d.Lessons)
        {
            bool inScope = o.OnlyActivities == null || (l.ActivityId is Guid aid && o.OnlyActivities.Contains(aid));
            if (o.RebuildAll && !l.Locked && inScope)
            {
                res.Removed.Add(l.Id);
                continue;
            }
            occ.Add(l);
            if (l.ActivityId is Guid id) placed[id] = (placed.TryGetValue(id, out var h) ? h : 0) + l.Hours;
        }

        var teacherLoad = d.Activities.Where(a => a.TeacherId != null)
                                      .GroupBy(a => a.TeacherId!.Value)
                                      .ToDictionary(g => g.Key, g => g.Sum(a => a.HoursPerWeek));

        var tasks = new List<PlacementTask>();
        foreach (var a in d.Activities)
        {
            if (o.OnlyActivities != null && !o.OnlyActivities.Contains(a.Id)) continue;
            if (a.Groups.Count == 0) continue;
            int remaining = a.HoursPerWeek - (placed.TryGetValue(a.Id, out var h) ? h : 0);
            int students = a.Students > 0 ? a.Students : lk.Students(a.Groups);
            var name = lk.SubjectName(a.SubjectId);
            while (remaining >= 2) { tasks.Add(new PlacementTask(a, false, students, name)); remaining -= 2; }
            if (remaining == 1) tasks.Add(new PlacementTask(a, true, students, name));
        }
        res.Tasks = tasks.Count;

        tasks = tasks
            .OrderBy(t => t.Activity.Type switch { LessonType.Curs => 0, LessonType.Laborator => 1, _ => 2 })
            .ThenBy(t => t.Biweekly ? 1 : 0)
            .ThenByDescending(t => t.Students)
            .ThenByDescending(t => t.Activity.TeacherId is Guid tid && teacherLoad.TryGetValue(tid, out var x) ? x : 0)
            .ThenBy(t => t.SubjectName, StringComparer.Ordinal)
            .ToList();

        var rooms = d.Rooms.ToList();
        foreach (var task in tasks)
        {
            var a = task.Activity;
            var teacher = lk.Teacher(a.TeacherId);
            var candidates = rooms
                .Where(r => r.Allows(a.Type) && (task.Students <= 0 || r.Capacity >= task.Students))
                .OrderBy(r => PreferredKind(a.Type, r.Kind) ? 0 : 1)
                .ThenBy(r => r.Capacity)
                .ToList();
            if (candidates.Count == 0)
                candidates = rooms.Where(r => r.Allows(a.Type)).OrderByDescending(r => r.Capacity).ToList();

            var baseGroups = a.Groups.Select(Audience.BaseCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var weeks = task.Biweekly ? new[] { WeekParity.Impara, WeekParity.Para } : new[] { WeekParity.Saptamanal };

            double bestScore = double.MaxValue;
            (int Day, int Slot, WeekParity Week, Room? Room)? best = null;

            for (int day = 0; day < days; day++)
            {
                foreach (var slot in slots)
                {
                    if (o.RespectUnavailability && teacher?.IsUnavailable(day, slot) == true) continue;
                    var here = occ.At(day, slot);
                    foreach (var week in weeks)
                    {
                        if (!Free(here, a.TeacherId, a.Groups, week)) continue;
                        Room? room = null;
                        if (candidates.Count > 0)
                        {
                            room = candidates.FirstOrDefault(r => RoomFree(here, r.Id, week));
                            if (room == null) continue;
                        }

                        double score = SlotPenalty(slot, o.LastComfortSlot);
                        foreach (var g in baseGroups)
                        {
                            int count = occ.GroupCount(g, day);
                            if (count >= o.MaxPairsPerDay) score += 25;
                            score += count * 1.2;
                            if (o.AvoidGaps && count > 0 && !occ.GroupBusy(g, day, slot - 1) && !occ.GroupBusy(g, day, slot + 1))
                                score += 3.5;
                            if (occ.GroupHasSubject(g, day, a.SubjectId)) score += 4;
                        }
                        int tc = occ.TeacherCount(a.TeacherId, day);
                        score += tc * 0.6 + (tc >= 5 ? 10 : 0);
                        if (week != WeekParity.Saptamanal && here != null &&
                            here.Any(x => x.Week != WeekParity.Saptamanal && x.Week != week && Audience.FirstOverlap(a.Groups, x.Groups) != null))
                            score -= 3; // completează „fracția" impar/par din aceeași celulă
                        score += (day * 7 + slot) * 31 % 17 * 0.01;

                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = (day, slot, week, room);
                        }
                    }
                }
            }

            if (best is { } b)
            {
                var lesson = new Lesson
                {
                    ActivityId = a.Id,
                    SubjectId = a.SubjectId,
                    TeacherId = a.TeacherId,
                    Type = a.Type,
                    Groups = a.Groups.ToList(),
                    RoomId = b.Room?.Id,
                    Day = b.Day,
                    Slot = b.Slot,
                    Week = b.Week,
                };
                res.Added.Add(lesson);
                occ.Add(lesson);
            }
            else
            {
                res.Unplaced.Add($"{task.SubjectName} — {Ro.TypeLabel(a.Type).ToLowerInvariant()}, {Audience.Format(a.Groups)} ({lk.TeacherName(a.TeacherId)})");
            }
        }

        res.Duration = sw.Elapsed;
        return res;
    }

    private static double SlotPenalty(int slot, int lastComfort)
    {
        double p = slot switch { 1 => 0.8, 2 => 0, 3 => 0, 4 => 0.4, 5 => 1.8, 6 => 6, 7 => 12, _ => 16 };
        if (slot > lastComfort) p += 8;
        return p;
    }

    private static bool PreferredKind(LessonType type, RoomKind kind) => type switch
    {
        LessonType.Curs => kind == RoomKind.Amfiteatru,
        LessonType.Seminar => kind is RoomKind.SalaSeminar or RoomKind.Universala,
        _ => kind is RoomKind.LaboratorInformatica or RoomKind.LaboratorSpecializat,
    };

    private static bool Free(List<Lesson>? here, Guid? teacherId, List<string> groups, WeekParity week)
    {
        if (here == null) return true;
        foreach (var other in here)
        {
            if (!ConflictService.WeeksOverlap(week, other.Week)) continue;
            if (teacherId != null && other.TeacherId == teacherId) return false;
            if (Audience.FirstOverlap(groups, other.Groups) != null) return false;
        }
        return true;
    }

    private static bool RoomFree(List<Lesson>? here, Guid roomId, WeekParity week) =>
        here == null || !here.Any(o => o.RoomId == roomId && ConflictService.WeeksOverlap(week, o.Week));

    /// <summary>Alege cel mai potrivit auditoriu liber pentru o lecție (folosit la drag &amp; drop).</summary>
    public static Room? SuggestRoom(Dataset d, Lookups lk, Lesson lesson, Guid? preferred = null, ICollection<Guid>? ignore = null)
    {
        int students = lk.StudentsOf(lesson);
        var here = d.Lessons.Where(x => x.Day == lesson.Day && x.Slot == lesson.Slot && x.Id != lesson.Id
                                        && (ignore == null || !ignore.Contains(x.Id))).ToList();
        bool IsFree(Room r) => !here.Any(o => o.RoomId == r.Id && ConflictService.WeeksOverlap(lesson.Week, o.Week));

        if (preferred is Guid p && lk.Room(p) is { } pr && IsFree(pr) && pr.Allows(lesson.Type) && (students <= 0 || pr.Capacity >= students))
            return pr;

        return d.Rooms.Where(r => r.Allows(lesson.Type) && (students <= 0 || r.Capacity >= students) && IsFree(r))
                      .OrderBy(r => PreferredKind(lesson.Type, r.Kind) ? 0 : 1)
                      .ThenBy(r => r.Capacity)
                      .FirstOrDefault();
    }
}
