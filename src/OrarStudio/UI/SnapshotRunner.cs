using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrarStudio.Import;
using OrarStudio.ViewModels;
using OrarStudio.Views;

namespace OrarStudio.UI;

/// <summary>
/// Mod de diagnostic (<c>OrarStudio.exe --snapshot &lt;dosar&gt;</c>): parcurge toate paginile și ferestrele,
/// le salvează ca imagini PNG și verifică serverul API. Util pentru testare automată și documentație.
/// </summary>
public static class SnapshotRunner
{
    public static async Task RunAsync(MainViewModel main, string dir)
    {
        var report = new StringBuilder();
        try
        {
            Directory.CreateDirectory(dir);
            var win = Application.Current.MainWindow!;
            win.WindowState = WindowState.Normal;
            win.Width = 1480;
            win.Height = 920;
            await Idle(900);

            int i = 1;
            foreach (var nav in main.NavItems.Where(n => n.Page != null))
            {
                main.Navigate(nav.Page!);
                await Idle(nav.Page is ExportViewModel ? 7000 : 700);
                Capture(win, Path.Combine(dir, $"{i++:00}-{nav.Key}.png"));
            }

            // editorul: alte perspective + o lecție selectată
            var store = AppServices.Store;
            var teacher = store.Data.Teachers.OrderByDescending(t => store.Data.Lessons.Count(l => l.TeacherId == t.Id)).FirstOrDefault();
            if (teacher != null)
            {
                main.Navigate(main.Timetable, new TimetableTarget(ScheduleViewMode.Teacher, teacher.Id.ToString()));
                await Idle(700);
                Capture(win, Path.Combine(dir, $"{i++:00}-orar-profesor.png"));
            }
            var room = store.Data.Rooms.FirstOrDefault(r => r.AllowsLaborator);
            if (room != null)
            {
                main.Navigate(main.Timetable, new TimetableTarget(ScheduleViewMode.Room, room.Id.ToString()));
                await Idle(700);
                Capture(win, Path.Combine(dir, $"{i++:00}-orar-auditoriu.png"));
            }
            var group = TimetableBuilder.OrderedGroups(store.Data).FirstOrDefault(g => g.Code.StartsWith("I2"));
            if (group != null)
            {
                var lesson = store.Data.Lessons.FirstOrDefault(l => Audience.Concerns(l.Groups, group.Code) && l.Type == LessonType.Laborator);
                main.Navigate(main.Timetable, new TimetableTarget(ScheduleViewMode.Group, group.Code, lesson?.Id));
                await Idle(700);
                Capture(win, Path.Combine(dir, $"{i++:00}-orar-grupa-selectie.png"));

                // simulare drag & drop (evidențierea celulelor)
                var pending = store.Data.Activities.FirstOrDefault(a => Audience.Concerns(a.Groups, group.Code));
                if (pending != null && main.Timetable.Pending.Count == 0)
                    main.Timetable.ShowCompleted = true;
                var item = main.Timetable.Pending.FirstOrDefault();
                if (item != null)
                {
                    main.Timetable.BeginDrag(item);
                    await Idle(400);
                    Capture(win, Path.Combine(dir, $"{i++:00}-orar-drag.png"));
                    main.Timetable.EndDrag();
                }

                if (lesson != null)
                {
                    var editor = new LessonEditorWindow { DataContext = LessonEditorViewModel.ForExisting(lesson) };
                    editor.Show();
                    await Idle(700);
                    Capture(editor, Path.Combine(dir, $"{i++:00}-dialog-editare.png"));
                    editor.Close();
                }
            }

            // import (previzualizare docx)
            var docx = Directory.Exists(AppPaths.SamplesDir) ? Directory.GetFiles(AppPaths.SamplesDir, "*.docx").FirstOrDefault() : null;
            if (docx != null)
            {
                var preview = LoadImporter.Parse(new[] { docx }, store.Data.Settings);
                var import = new ImportWindow { DataContext = new ImportViewModel(preview, store.Data) };
                import.Show();
                await Idle(900);
                Capture(import, Path.Combine(dir, $"{i++:00}-dialog-import.png"));
                import.Close();
                report.AppendLine($"import docx: {preview.Rows.Count} rânduri, {preview.Messages.Count} mesaje, doleanțe: {preview.SuggestedUnavailable.Count}");
            }

            var auto = new AutoScheduleWindow { DataContext = new AutoScheduleViewModel() };
            auto.Show();
            await Idle(500);
            Capture(auto, Path.Combine(dir, $"{i++:00}-dialog-generare.png"));
            auto.Close();

            var msg = new MessageWindow("Ștergere lecție", "Scoateți „Fundamentele Programării” din orar?", "Șterge", "Anulează", MessageKind.Danger);
            msg.Show();
            await Idle(300);
            Capture(msg, Path.Combine(dir, $"{i++:00}-dialog-confirmare.png"));
            msg.Close();

            // panoul informativ (mod demonstrativ: o zi din semestru, ora 10:00)
            var s = store.Data.Settings;
            var bvm = new BoardViewModel();
            bvm.SimDate = SemesterCalendar.MondayOf(s.SemesterStart).AddDays(7 * 4 + 1);
            bvm.SimTime = "10:05";
            bvm.Simulate = true;
            var board = new BoardWindow { DataContext = bvm, Width = 1600, Height = 920 };
            board.Show();
            await Idle(1200);
            Capture(board, Path.Combine(dir, $"{i++:00}-panou.png"));
            board.Close();

            // tema întunecată
            var originalTheme = ThemeService.Current;
            ThemeService.Apply("Dark");
            main.IsDark = true;
            main.Navigate(main.Dashboard);
            await Idle(700);
            Capture(win, Path.Combine(dir, $"{i++:00}-dark-dashboard.png"));
            main.Navigate(main.Timetable);
            await Idle(700);
            Capture(win, Path.Combine(dir, $"{i++:00}-dark-orar.png"));
            ThemeService.Apply(originalTheme);
            main.IsDark = originalTheme == "Dark";

            // API
            const int port = 5099;
            if (await AppServices.Api.StartAsync(port, false))
            {
                using var http = new HttpClient();
                var g = group?.Code ?? "I2301";
                foreach (var (name, url) in new[]
                         {
                             ("health", "/api/health"), ("info", "/api/info"), ("groups", "/api/groups"),
                             ("schedule-group", $"/api/schedule/group/{g}"), ("now", "/api/now?mode=rooms&at=" + Uri.EscapeDataString($"{bvm.SimDate:yyyy-MM-dd}T10:05")),
                             ("lessons-filter", $"/api/lessons?group={g}&day=1"), ("index", "/"), ("board", "/board"), ("docs", "/docs"),
                         })
                {
                    try
                    {
                        var resp = await http.GetAsync($"http://localhost:{port}{url}");
                        var body = await resp.Content.ReadAsStringAsync();
                        File.WriteAllText(Path.Combine(dir, $"api-{name}.txt"), body.Length > 20000 ? body[..20000] : body);
                        report.AppendLine($"API {url}: {(int)resp.StatusCode} {resp.Content.Headers.ContentType} {body.Length} bytes");
                    }
                    catch (Exception ex) { report.AppendLine($"API {url}: EROARE {ex.Message}"); }
                }
                try
                {
                    var pdf = await http.GetByteArrayAsync($"http://localhost:{port}/api/pdf/group/{g}");
                    File.WriteAllBytes(Path.Combine(dir, "api-group.pdf"), pdf);
                    report.AppendLine($"API pdf: {pdf.Length} bytes");
                }
                catch (Exception ex) { report.AppendLine($"API pdf: EROARE {ex.Message}"); }
                await AppServices.Api.StopAsync();
            }
            else report.AppendLine("API: nu a pornit");

            report.AppendLine($"lecții: {store.Data.Lessons.Count}, conflicte: {store.Report.Errors}, avertismente: {store.Report.Warnings}");
            report.AppendLine("OK");
        }
        catch (Exception ex)
        {
            report.AppendLine("EROARE: " + ex);
            Log.Error(ex, "Snapshot");
        }
        finally
        {
            File.WriteAllText(Path.Combine(dir, "raport.txt"), report.ToString());
            Application.Current.Shutdown();
        }
    }

    private static async Task Idle(int ms)
    {
        await Task.Delay(ms);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        if (window.Content is not FrameworkElement root || root.ActualWidth < 1) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        var m = root.Margin;
        double fullW = root.ActualWidth + m.Left + m.Right, fullH = root.ActualHeight + m.Top + m.Bottom;
        int w = (int)Math.Ceiling(fullW * dpi.DpiScaleX);
        int h = (int)Math.Ceiling(fullH * dpi.DpiScaleY);
        var rtb = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (window.Background is SolidColorBrush bg && bg.Color.A > 0)
                dc.DrawRectangle(bg, null, new Rect(0, 0, fullW, fullH));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(m.Left, m.Top, root.ActualWidth, root.ActualHeight));
        }
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
