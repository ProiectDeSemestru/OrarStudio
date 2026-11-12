using ClosedXML.Excel;
using ClosedXML.Graphics;

namespace OrarStudio.Export;

/// <summary>
/// Inițializare ClosedXML: motorul grafic implicit scanează toate fonturile sistemului la prima deschidere
/// (≈1–2 s). Folosim un motor cu un set mic de fonturi, încărcat o singură dată.
/// </summary>
public static class ExcelSupport
{
    private static readonly object Gate = new();
    private static bool _done;

    public static void EnsureInit()
    {
        lock (Gate)
        {
            if (_done) return;
            _done = true;
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                var files = new[] { "calibri.ttf", "arial.ttf", "segoeui.ttf" }
                    .Select(f => Path.Combine(dir, f)).Where(File.Exists).ToList();
                if (files.Count == 0) return;
                var streams = files.Select(f => (Stream)new MemoryStream(File.ReadAllBytes(f))).ToList();
                LoadOptions.DefaultGraphicEngine = DefaultGraphicEngine.CreateOnlyWithFonts(streams[0], streams.Skip(1).ToArray());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Inițializare ClosedXML");
            }
        }
    }
}
