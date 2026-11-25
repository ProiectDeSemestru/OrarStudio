using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrarStudio.Services;

public static class Json
{
    public static readonly JsonSerializerOptions Options = Create(true);
    public static readonly JsonSerializerOptions Compact = Create(false);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        WriteIndented = indented,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize<T>(T value, bool indented = true) =>
        JsonSerializer.Serialize(value, indented ? Options : Compact);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static T Clone<T>(T value) => Deserialize<T>(Serialize(value, false))!;
}

public static class AppPaths
{
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrarStudio");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string WorkspaceFile => Path.Combine(DataDir, "orar-curent.orar");
    public static string SettingsFile => Path.Combine(DataDir, "setari.json");
    public static string LogFile => Path.Combine(DataDir, "jurnal.log");

    public static string DocumentsDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Orar Studio");
            try { Directory.CreateDirectory(dir); } catch { /* ignorat */ }
            return dir;
        }
    }

    /// <summary>Scriere atomică: întâi într-un fișier temporar, apoi înlocuire.</summary>
    public static void WriteAllTextAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new System.Text.UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(Exception ex, string? context = null) =>
        Write("EROARE", (context != null ? context + ": " : "") + ex);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var file = AppPaths.LogFile;
                if (File.Exists(file) && new FileInfo(file).Length > 2_000_000) File.Delete(file);
                File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level}: {message}{Environment.NewLine}");
            }
        }
        catch { /* jurnalizarea nu trebuie să oprească aplicația */ }
    }
}

/// <summary>Preferințele utilizatorului (nu țin de un anumit proiect de orar).</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Light";
    public string? LastFile { get; set; }
    public int ApiPort { get; set; } = 5080;
    public bool ApiLan { get; set; }
    public bool ApiAutoStart { get; set; }
    public bool ColorBySubject { get; set; }
    public string WebhookUrl { get; set; } = "";
    public string WebhookToken { get; set; } = "";
    public string BoardMode { get; set; } = "rooms";
    public bool OpenAfterExport { get; set; } = true;
    public List<string> RecentFiles { get; set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return Json.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new AppSettings();
        }
        catch (Exception ex) { Log.Error(ex, "Citirea setărilor"); }
        return new AppSettings();
    }

    public void Save()
    {
        try { AppPaths.WriteAllTextAtomic(AppPaths.SettingsFile, Json.Serialize(this)); }
        catch (Exception ex) { Log.Error(ex, "Salvarea setărilor"); }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 8) RecentFiles.RemoveRange(8, RecentFiles.Count - 8);
    }
}
