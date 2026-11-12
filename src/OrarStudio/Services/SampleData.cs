using System.Reflection;
using OrarStudio.Import;

namespace OrarStudio.Services;

/// <summary>
/// Date demonstrative: auditoriile facultății + sarcina didactică importată din fișierele-exemplu
/// (DI_sem_1/3/5 … .xlsx) + un orar generat automat.
/// </summary>
public static class SampleData
{
    public static Dataset Create(bool schedule = true)
    {
        var d = new Dataset();
        foreach (var r in DemoRooms()) d.Rooms.Add(r);

        var preview = new ImportPreview();
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("Samples/") && n.EndsWith(".xlsx")).OrderBy(n => n))
        {
            using var s = asm.GetManifestResourceStream(name);
            if (s == null) continue;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            try { LoadImporter.ParseStream(ms, name["Samples/".Length..], preview, d.Settings); }
            catch (Exception ex) { Log.Error(ex, "Date demo: " + name); }
        }
        LoadImporter.Apply(preview, d, replaceLoad: false, applyWishes: false, updateSettings: true);

        if (schedule)
        {
            var result = AutoScheduler.Run(d, new AutoScheduleOptions());
            foreach (var l in result.Added) d.Lessons.Add(l);
        }
        return d;
    }

    public static IEnumerable<Room> DemoRooms()
    {
        Room R(string name, int floor, int cap, RoomKind kind, bool projector, int pcs, string notes = "")
        {
            var (c, s, l) = Room.DefaultUsage(kind);
            return new Room
            {
                Name = name, Building = "Blocul IV", Floor = floor, Capacity = cap, Kind = kind,
                AllowsCurs = c, AllowsSeminar = s, AllowsLaborator = l,
                HasProjector = projector, HasWhiteboard = true, Computers = pcs, Notes = notes,
            };
        }

        yield return R("101", 1, 120, RoomKind.Amfiteatru, true, 0, "Amfiteatru mare, sistem de sonorizare");
        yield return R("102", 1, 100, RoomKind.Amfiteatru, true, 0);
        yield return R("201", 2, 90, RoomKind.Amfiteatru, true, 0);
        yield return R("301", 3, 64, RoomKind.Amfiteatru, true, 0);
        yield return R("203", 2, 36, RoomKind.SalaSeminar, true, 0);
        yield return R("204", 2, 36, RoomKind.SalaSeminar, false, 0);
        yield return R("205", 2, 32, RoomKind.SalaSeminar, true, 0);
        yield return R("206", 2, 30, RoomKind.SalaSeminar, false, 0, "Tablă interactivă");
        yield return R("302", 3, 32, RoomKind.SalaSeminar, true, 0);
        yield return R("303", 3, 30, RoomKind.SalaSeminar, false, 0);
        yield return R("305", 3, 40, RoomKind.Universala, true, 0, "Sală polivalentă");
        yield return R("401", 4, 18, RoomKind.LaboratorInformatica, true, 16);
        yield return R("402", 4, 18, RoomKind.LaboratorInformatica, true, 16);
        yield return R("403", 4, 20, RoomKind.LaboratorInformatica, true, 18);
        yield return R("404", 4, 20, RoomKind.LaboratorInformatica, true, 18);
        yield return R("405", 4, 24, RoomKind.LaboratorInformatica, true, 22);
        yield return R("406", 4, 18, RoomKind.LaboratorInformatica, false, 16);
        yield return R("407", 4, 20, RoomKind.LaboratorInformatica, true, 18);
        yield return R("408", 4, 24, RoomKind.LaboratorInformatica, true, 22);
        yield return R("410", 4, 24, RoomKind.LaboratorSpecializat, true, 22, "Laborator dezvoltare jocuri (stații grafice)");
        yield return R("411", 4, 20, RoomKind.LaboratorSpecializat, true, 18, "Laborator rețele de calculatoare");
    }
}
