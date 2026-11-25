using OrarStudio.Import;

namespace OrarStudio.Tests;

/// <summary>
/// Proiect de test: sarcina didactică reală a Departamentului Informatică (fișierele din TestData,
/// semestrele I, III, V) + auditoriile blocului IV + (opțional) orarul generat automat.
/// </summary>
internal static class SampleProject
{
    public static string DataDir => Path.Combine(AppContext.BaseDirectory, "TestData");

    public static Dataset Create(bool schedule = true)
    {
        var d = new Dataset();
        foreach (var r in Rooms()) d.Rooms.Add(r);

        var files = Directory.GetFiles(DataDir, "*.xlsx").OrderBy(f => f, StringComparer.Ordinal).ToList();
        var preview = LoadImporter.Parse(files, d.Settings);
        LoadImporter.Apply(preview, d, replaceLoad: false, applyWishes: false, updateSettings: true);

        if (schedule)
        {
            var result = AutoScheduler.Run(d, new AutoScheduleOptions());
            foreach (var l in result.Added) d.Lessons.Add(l);
        }
        return d;
    }

    public static IEnumerable<Room> Rooms()
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
