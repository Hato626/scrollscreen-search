using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScrollSearch.Core;

namespace ScrollSearch.App;

internal static class Acceptance
{
    public static async Task<int> Run(string output)
    {
        output = System.IO.Path.GetFullPath(output); Directory.CreateDirectory(output);
        var checks = new List<object>(); var notes = new List<string>();
        var rows = new StackPanel();
        for (var i = 0; i < 35; i++)
            rows.Children.Add(new TextBlock { Text = i is 5 or 13 ? "TARGET common repeated phrase" : $"Reference row {i:000} distinct anchor record", Height = 58,
                FontSize = 24, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.Black, Padding = new Thickness(24, 10, 0, 0) });
        var viewer = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.White };
        var testWindow = new Window { Title = "ScrollScreen Search — controlled acceptance fixture", Width = 820, Height = 700, Left = 80, Top = 80,
            WindowStartupLocation = WindowStartupLocation.Manual, Content = viewer, Background = Brushes.White, Topmost = true };
        var appWindow = new MainWindow(System.IO.Path.Combine(output, "profile"), true);
        Native.GetCursorPos(out var originalPointer);
        void Check(string name, bool passed, string detail = "")
        {
            checks.Add(new { name, passed, detail });
            if (!passed) throw new InvalidOperationException(name + " — " + detail);
        }
        try
        {
            var languages = LocalOcr.Languages.Select(l => l.LanguageTag).ToArray(); notes.Add("Installed OCR languages: " + string.Join(", ", languages));
            var english = languages.FirstOrDefault(l => l.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            Check("English OCR available", english != null);
            testWindow.Show(); testWindow.Activate(); await Task.Delay(700);
            var tl = viewer.PointToScreen(new Point(0, 0)); var br = viewer.PointToScreen(new Point(viewer.ActualWidth - 24, viewer.ActualHeight));
            var region = new Box(Math.Ceiling(tl.X), Math.Ceiling(tl.Y), Math.Floor(br.X - tl.X), Math.Floor(br.Y - tl.Y));
            var dpi = VisualTreeHelper.GetDpi(viewer); notes.Add($"Actual display DPI: {dpi.PixelsPerInchX:0} × {dpi.PixelsPerInchY:0}; controlled capture region {region}");
            var picker = new RegionPicker();
            async Task DragSelection()
            {
                await Task.Delay(400);
                Native.SetCursorPos((int)region.X + 20, (int)region.Y + 20);
                await Task.Delay(160);
                Native.SetCursorPos((int)region.X + 20, (int)region.Y + 20);
                Native.mouse_event(0x0002, 0, 0, 0, 0);
                await Task.Delay(240);
                Native.SetCursorPos((int)region.Right - 20, (int)region.Bottom - 20);
                await Task.Delay(240); Native.SetCursorPos((int)region.Right - 20, (int)region.Bottom - 20); Native.mouse_event(0x0004, 0, 0, 0, 0);
            }
            var drag = DragSelection(); var selected = picker.ShowDialog(); await drag;
            Check("Real mouse drag selects physical screen rectangle", selected == true && picker.Selection is Box picked &&
                Math.Abs(picked.X - region.X - 20) < 2 && Math.Abs(picked.Width - region.Width + 40) < 2 && Math.Abs(picked.Height - region.Height + 40) < 2,
                picker.Selection?.ToString() ?? "cancelled");
            Native.SetCursorPos(originalPointer.X, originalPointer.Y); await Task.Delay(200);
            var raw = ScreenCapture.Take(region); var frame = await new LocalOcr(english!).Recognize(raw, CancellationToken.None);
            File.WriteAllBytes(System.IO.Path.Combine(output, "controlled-initial.png"), raw.Png);
            Check("Real screen capture and Windows OCR find fixture", TextSearch.Find(frame, "TARGET").Count == 1, string.Join(" | ", frame.Lines.Select(l => l.Text)));
            var match = TextSearch.Find(frame, "TARGET").Single();
            Check("OCR bounds inside captured physical pixels", match.Bounds.X >= 0 && match.Bounds.Y >= 0 && match.Bounds.Right <= region.Width && match.Bounds.Bottom <= region.Height);
            appWindow.TestStart(region, "TARGET", english!);
            await Wait(() => appWindow.ProcessedCount >= 1, "initial loop observation");
            Check("Continuous application pipeline records first hit", appWindow.TestHistory.Count == 1);
            foreach (var position in new[] { 170d, 340d, 510d })
            {
                viewer.ScrollToVerticalOffset(position); await Task.Delay(300);
                var processed = appWindow.ProcessedCount;
                await Wait(() => appWindow.ProcessedCount > processed, "scroll observation");
            }
            Check("Different identical text rows retained; overlap deduplicated", appWindow.TestHistory.Count == 2, "history count " + appWindow.TestHistory.Count);
            foreach (var position in new[] { 340d, 170d, 0d })
            {
                viewer.ScrollToVerticalOffset(position); await Task.Delay(300);
                var processed = appWindow.ProcessedCount; await Wait(() => appWindow.ProcessedCount > processed, "scroll back observation");
            }
            Check("Returning to earlier rows creates no duplicate", appWindow.TestHistory.Count == 2, "history count " + appWindow.TestHistory.Count);
            appWindow.Pause(false); var captures = appWindow.CaptureCount; var records = appWindow.TestHistory.Count;
            await appWindow.PendingScan; await Task.Delay(1200);
            Check("Pause stops capture and history updates", captures == appWindow.CaptureCount && records == appWindow.TestHistory.Count);
            appWindow.TestContinue(); var prior = appWindow.ProcessedCount; await Wait(() => appWindow.ProcessedCount > prior, "resume");
            Check("Resume preserves deduplication", appWindow.TestHistory.Count == 2);
            appWindow.Pause(false); await appWindow.PendingScan;
            appWindow.ExportTo(System.IO.Path.Combine(output, "export.zip"));
            using (var zip = ZipFile.OpenRead(System.IO.Path.Combine(output, "export.zip")))
                Check("Portable export contains metadata and controlled screenshots", zip.GetEntry("history.csv") != null && zip.GetEntry("history.json") != null && zip.Entries.Count(e => e.FullName.StartsWith("screenshots/")) >= 1);
            var persisted = LocalStore.Load<HistoryItem[]>(System.IO.Path.Combine(output, "profile", "history.json"));
            Check("History persisted with exact screenshot bounds", persisted?.Length == 2 && persisted.All(i => i.Bounds.Width > 0 && File.Exists(System.IO.Path.Combine(output, "profile", "screenshots", i.Screenshot))));
            // Render only our own WPF visual tree; this can never include another app or the desktop.
            appWindow.Show(); appWindow.UpdateLayout();
            var appImage = new RenderTargetBitmap((int)appWindow.ActualWidth, (int)appWindow.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            appImage.Render(appWindow); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(appImage));
            using (var imageFile = File.Create(System.IO.Path.Combine(output, "application-ui.png"))) encoder.Save(imageFile);
            appWindow.Hide(); testWindow.Activate(); await Task.Delay(200);
            appWindow.TestContinue(); await Wait(() => appWindow.IsScanning, "hotkey scan start");
            Native.keybd_event(0x11, 0, 0, 0); Native.keybd_event(0x12, 0, 0, 0); Native.keybd_event(0x77, 0, 0, 0);
            Native.keybd_event(0x77, 0, 2, 0); Native.keybd_event(0x12, 0, 2, 0); Native.keybd_event(0x11, 0, 2, 0);
            await Wait(() => !appWindow.IsScanning, "global hotkey pause"); await appWindow.PendingScan;
            var afterHotkey = appWindow.CaptureCount; await Task.Delay(700);
            Check("Real registered Ctrl+Alt+F8 hotkey stops capture", !appWindow.IsScanning && appWindow.CaptureCount == afterHotkey);
            appWindow.TestTrayAction(1); var beforeTray = appWindow.ProcessedCount;
            await Wait(() => appWindow.ProcessedCount > beforeTray, "tray continue command");
            appWindow.TestTrayAction(0); await appWindow.PendingScan; var afterTray = appWindow.CaptureCount; await Task.Delay(650);
            Check("Tray menu command wiring resumes and pauses collection", !appWindow.IsScanning && appWindow.CaptureCount == afterTray);
            var target = new RegionTarget(region, true); var before = target.Resolve(); testWindow.Left += 70; await Task.Delay(250); var after = target.Resolve();
            Check("Real target window motion tracked in physical pixels", after.X > before.X + 40 && Math.Abs(after.Width - before.Width) <= 2, $"before {before}; after {after}");
            var chinese = languages.FirstOrDefault(l => l.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
            if (chinese != null)
            {
                rows.Children.Clear(); rows.Children.Add(new TextBlock { Text = "屏幕找字器  本地识别  测试成功", FontSize = 36, FontFamily = new FontFamily("Microsoft YaHei UI"), Foreground = Brushes.Black, Padding = new Thickness(24) });
                viewer.ScrollToHome(); await Task.Delay(400);
                var chineseCapture = ScreenCapture.Take(target.Resolve()); var chineseFrame = await new LocalOcr(chinese).Recognize(chineseCapture, CancellationToken.None);
                Check("Real Chinese OCR query", TextSearch.Find(chineseFrame, "找字器").Count > 0, string.Join(" | ", chineseFrame.Lines.Select(l => l.Text)));
            }
            else notes.Add("Chinese OCR not installed: Chinese recognition not exercised on this machine.");
            appWindow.TestClear();
            Check("Clear deletes retained screenshot files", appWindow.TestHistory.Count == 0 && !Directory.EnumerateFiles(System.IO.Path.Combine(output, "profile", "screenshots"), "*.png").Any());
            notes.Add("Mixed-DPI physical displays, tray flyout interaction, remote/secure desktops and arbitrary third-party apps require manual verification; not claimed as exercised.");
            LocalStore.Save(System.IO.Path.Combine(output, "acceptance.json"), new { success = true, checks, notes });
            return 0;
        }
        catch (Exception e)
        {
            LocalStore.Save(System.IO.Path.Combine(output, "acceptance.json"), new { success = false, checks, notes, error = e.ToString() }); return 1;
        }
        finally { Native.SetCursorPos(originalPointer.X, originalPointer.Y); appWindow.Pause(false); appWindow.Close(); testWindow.Close(); }
    }
    static async Task Wait(Func<bool> condition, string name)
    {
        var end = DateTime.UtcNow.AddSeconds(25);
        while (!condition()) { if (DateTime.UtcNow > end) throw new TimeoutException(name); await Task.Delay(120); }
    }
}
