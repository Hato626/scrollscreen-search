using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using ScrollSearch.Core;
using Frame = ScrollSearch.Core.Frame;

namespace ScrollSearch.App;

internal sealed record Settings(string Query = "", string Language = "", int Interval = 900, bool Follow = true);

internal sealed class MainWindow : Window
{
    readonly TextBox query = new() { MinWidth = 260, FontSize = 20, Padding = new Thickness(9), ToolTip = "忽略大小写与空格；支持普通文本，不使用正则表达式" };
    readonly ComboBox language = new() { MinWidth = 220, DisplayMemberPath = "NativeName", SelectedValuePath = "LanguageTag" };
    readonly ComboBox interval = new() { MinWidth = 110, ItemsSource = new[] { 600, 900, 1500, 2500 }, SelectedItem = 900 };
    readonly CheckBox follow = new() { Content = "框选时绑定目标窗口（跟随移动/缩放）", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock regionLabel = new() { Text = "尚未框选。请把目标内容放到前台，再框选。", TextWrapping = TextWrapping.Wrap };
    readonly TextBlock status = new() { Text = "准备就绪", TextWrapping = TextWrapping.Wrap };
    readonly TextBlock stats = new();
    readonly Button toggle = new() { Content = "开始找字", Padding = new Thickness(18, 9, 18, 9), Background = new SolidColorBrush(Color.FromRgb(17, 94, 89)), Foreground = Brushes.White };
    readonly ListBox historyList = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    readonly ObservableCollection<HistoryItem> history = new();
    readonly ScrollTracker tracker = new();
    readonly ScanLoop<Observation> loop = new();
    readonly HighlightOverlay overlay = new();
    readonly string dataRoot;
    readonly bool testing;
    System.Windows.Forms.NotifyIcon? tray;
    RegionTarget? target;
    LocalOcr? ocr;
    string? lastHash;
    string runningQuery = "";
    string? previousQuery;
    string? previousLanguage;
    bool closing;
    int frames;
    public int CaptureCount { get; private set; }
    public int ProcessedCount { get; private set; }
    public IReadOnlyList<HistoryItem> TestHistory => history.ToArray();
    public Task PendingScan => loop.Completion;
    public bool IsScanning => loop.Running;

    public MainWindow(string? storage = null, bool testing = false)
    {
        this.testing = testing;
        dataRoot = storage ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScrollScreenSearch");
        Directory.CreateDirectory(System.IO.Path.Combine(dataRoot, "screenshots"));
        Title = "屏幕找字器 · ScrollScreen Search"; Width = 850; Height = 720; MinWidth = 690; MinHeight = 520;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)); FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 14;
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "跟着滚动，继续找字", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray });
        header.Children.Add(new TextBlock { Text = "选区域 → 输入词 → 开始 → 手动滚动。完全在本机识别。", Margin = new Thickness(0, 8, 0, 20), Foreground = Brushes.DimGray });
        var queryRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(toggle, Dock.Right); toggle.Margin = new Thickness(12, 0, 0, 0); queryRow.Children.Add(toggle); queryRow.Children.Add(query); header.Children.Add(queryRow);
        var options = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        options.Children.Add(Label("识别语言")); language.Margin = new Thickness(8, 0, 18, 0); options.Children.Add(language);
        options.Children.Add(Label("间隔（毫秒）")); interval.Margin = new Thickness(8, 0, 0, 0); options.Children.Add(interval); header.Children.Add(options);
        var regionRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        regionRow.Children.Add(Button("框选屏幕区域", async () => await PickRegion())); follow.Margin = new Thickness(14, 0, 0, 0); regionRow.Children.Add(follow); header.Children.Add(regionRow);
        regionLabel.Margin = new Thickness(0, 0, 0, 12); header.Children.Add(regionLabel);
        var statePanel = new Border { Background = new SolidColorBrush(Color.FromRgb(226, 238, 237)), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12), Child = status };
        header.Children.Add(statePanel);
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(new TextBlock { Text = "命中历史", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center });
        actions.Children.Add(Button("打开所选截图", OpenSelected));
        actions.Children.Add(Button("导出 ZIP", Export)); actions.Children.Add(Button("清空历史与截图", ClearHistory));
        header.Children.Add(actions); stats.Margin = new Thickness(0, 0, 0, 8); header.Children.Add(stats);
        var footer = new TextBlock { Text = "Ctrl + Alt + F8：开始/暂停并查看历史。开始后窗口最小化到任务栏，可通过托盘恢复。\n识别有误差；快速滚动请短暂停顿。这里只搜索已实际显示的内容，不会自动滚动。",
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 12 };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        historyList.ItemsSource = history;
        var template = new DataTemplate(typeof(HistoryItem));
        var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 7, 5, 7));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Summary"));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); panel.AppendChild(title);
        var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Detail")); detail.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        detail.SetValue(TextBlock.ForegroundProperty, Brushes.DimGray); detail.SetValue(FrameworkElement.MaxWidthProperty, 720d); panel.AppendChild(detail); template.VisualTree = panel; historyList.ItemTemplate = template;
        historyList.MouseDoubleClick += (_, _) => OpenSelected(); root.Children.Add(historyList);
        toggle.Click += (_, _) => { if (loop.Running) Pause(true); else Start(); };
        language.ItemsSource = LocalOcr.Languages;
        Load(); UpdateStats();
        if (language.Items.Count == 0)
        {
            status.Text = "未找到 Windows OCR 语言。请在 设置 → 时间和语言 → 语言和区域 中安装语言的 OCR 功能，然后重启本程序。";
            toggle.IsEnabled = false;
        }
        SourceInitialized += (_, _) =>
        {
            Native.Exclude(this);
            var handle = Native.Handle(this);
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
            if (!Native.RegisterHotKey(handle, 1, 0x4003, 0x77)) status.Text += " 全局快捷键已被占用，请使用托盘菜单暂停。";
        };
        StateChanged += (_, _) => { if (WindowState != WindowState.Minimized && loop.Running) Pause(false); };
        Closing += (_, _) =>
        {
            closing = true; loop.Stop(); overlay.Close(); Save();
            Native.UnregisterHotKey(Native.Handle(this), 1); tray?.Dispose();
        };
        if (!testing) CreateTray();
    }
    static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };
    static Button Button(string text, Action action)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand };
        b.Click += (_, _) => action(); return b;
    }
    void CreateTray()
    {
        tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "屏幕找字器 · 已暂停", Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("暂停并查看历史", null, (_, _) => Dispatcher.Invoke(() => Pause(true)));
        menu.Items.Add("继续找字", null, (_, _) => Dispatcher.Invoke(Start));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => Pause(true));
    }
    nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == 1) { if (loop.Running) Pause(true); else Start(); handled = true; }
        return 0;
    }
    async Task PickRegion()
    {
        Pause(false); WindowState = WindowState.Minimized;
        await Task.Delay(250);
        var picker = new RegionPicker();
        var accepted = picker.ShowDialog() == true;
        if (accepted && picker.Selection is Box region)
        {
            await Task.Delay(100);
            target = new RegionTarget(region, follow.IsChecked == true);
            regionLabel.Text = $"已选区域：{region.Width:0} × {region.Height:0} px，坐标 ({region.X:0}, {region.Y:0})。{(target.FollowsWindow ? "跟随目标窗口。" : "固定屏幕位置。")}";
            tracker.Reset(); lastHash = null;
        }
        WindowState = WindowState.Normal; Activate();
    }
    void Start()
    {
        if (closing || loop.Running) return;
        if (string.IsNullOrWhiteSpace(query.Text)) { status.Text = "先输入要查找的词。"; query.Focus(); return; }
        if (target == null) { status.Text = "请先框选屏幕区域。"; return; }
        try
        {
            var selected = language.SelectedItem as Windows.Globalization.Language;
            if (selected == null) throw new InvalidOperationException("请选择已安装的 OCR 语言。 ");
            ocr = new LocalOcr(selected.LanguageTag); runningQuery = query.Text.Trim();
            if (previousQuery != runningQuery || previousLanguage != selected.LanguageTag) { tracker.Reset(); lastHash = null; }
            previousQuery = runningQuery; previousLanguage = selected.LanguageTag;
            query.IsEnabled = language.IsEnabled = interval.IsEnabled = false; toggle.Content = "暂停";
            status.Text = "正在找字。请手动滚动目标内容；Ctrl + Alt + F8 暂停并查看历史。";
            Save();
            if (!testing) WindowState = WindowState.Minimized;
            if (tray != null) tray.Text = "屏幕找字器 · 正在识别";
            loop.Start(Observe, Publish, e => { Pause(true); status.Text = "已暂停：" + e.Message; }, TimeSpan.FromMilliseconds((int)(interval.SelectedItem ?? 900)));
        }
        catch (Exception e) { Pause(false); status.Text = "无法开始：" + e.Message; }
    }
    async Task<Observation> Observe(CancellationToken token)
    {
        overlay.Hide(); Native.DwmFlush();
        await Task.Delay(80, token);
        token.ThrowIfCancellationRequested();
        var region = target!.Resolve();
        var first = CaptureRegion(region);
        await Task.Delay(120, token);
        token.ThrowIfCancellationRequested();
        var currentRegion = target.Resolve();
        if (currentRegion != region) return new(first, new(Array.Empty<TextLine>(), first.Width, first.Height), region, true);
        var capture = CaptureRegion(region);
        if (capture.Difference(first) > .025) return new(capture, new(Array.Empty<TextLine>(), capture.Width, capture.Height), region, true);
        if (capture.Hash == lastHash) return new(capture, lastFrame!, region);
        var frame = await ocr!.Recognize(capture, token);
        token.ThrowIfCancellationRequested();
        var verifyRegion = target.Resolve();
        if (verifyRegion != region) return new(capture, frame, region, true);
        var verify = CaptureRegion(region);
        if (verify.Difference(capture) > .025) return new(capture, frame, region, true);
        return new(capture, frame, region);
    }
    Capture CaptureRegion(Box region) { CaptureCount++; return ScreenCapture.Take(region); }
    Frame? lastFrame;
    void Publish(Observation observation)
    {
        if (observation.Unchanged) { status.Text = "画面仍在移动，等待短暂停顿…"; return; }
        ProcessedCount++;
        frames++; lastHash = observation.Capture.Hash; lastFrame = observation.Frame;
        var all = TextSearch.Find(observation.Frame, runningQuery);
        var fresh = tracker.Update(observation.Frame, runningQuery);
        if (fresh.Count > 0)
        {
            var name = Guid.NewGuid().ToString("N") + ".png";
            File.WriteAllBytes(System.IO.Path.Combine(dataRoot, "screenshots", name), observation.Capture.Png);
            foreach (var item in fresh)
            {
                history.Insert(0, new(Guid.NewGuid().ToString("N"), DateTimeOffset.Now, runningQuery, item.Match.Text, item.Match.Context,
                    name, item.Match.Bounds, item.Segment, item.DocumentY, item.Uncertain));
            }
            while (history.Count > 200) history.RemoveAt(history.Count - 1);
            Save(); PruneScreenshots(); UpdateStats();
        }
        overlay.Display(observation.ScreenRegion, all);
        status.Text = $"当前画面 {all.Count} 处匹配；已处理 {frames} 帧。";
        if (tray != null) tray.Text = $"屏幕找字器 · 历史 {history.Count} 条";
    }
    internal void Pause(bool show)
    {
        loop.Stop(); overlay.Hide();
        query.IsEnabled = language.IsEnabled = interval.IsEnabled = true; toggle.Content = "继续找字";
        status.Text = "已暂停；不会继续截图或记录。选择历史可查看命中时的截图。";
        if (tray != null) tray.Text = "屏幕找字器 · 已暂停";
        if (show && !testing) { Show(); WindowState = WindowState.Normal; Activate(); }
    }
    void UpdateStats() => stats.Text = $"保留最近 {history.Count} 条命中（最多 200 条）。历史与截图仅存本机。双击查看原始截图。";
    void OpenSelected()
    {
        Pause(false);
        if (historyList.SelectedItem is not HistoryItem item) { status.Text = "请先选择一条命中。"; return; }
        var path = ScreenshotPath(item.Screenshot);
        if (!File.Exists(path)) { status.Text = "这条历史的截图已被删除。"; return; }
        try
        {
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
            var grid = new Grid { Width = bitmap.PixelWidth, Height = bitmap.PixelHeight };
            grid.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Fill });
            var canvas = new Canvas(); var box = new Rectangle { Width = item.Bounds.Width, Height = item.Bounds.Height, Stroke = Brushes.OrangeRed, StrokeThickness = 3 };
            Canvas.SetLeft(box, item.Bounds.X); Canvas.SetTop(box, item.Bounds.Y); canvas.Children.Add(box); grid.Children.Add(canvas);
            var popup = new Window { Title = $"命中截图 · {item.Query} · {item.Time:HH:mm:ss}", Width = Math.Min(1100, bitmap.PixelWidth + 50), Height = 750, Owner = this,
                Content = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            popup.SourceInitialized += (_, _) => Native.Exclude(popup); popup.ShowDialog();
        }
        catch (Exception e) { status.Text = "截图无法打开：" + e.Message; }
    }
    string ScreenshotPath(string name) => System.IO.Path.Combine(dataRoot, "screenshots", System.IO.Path.GetFileName(name));
    void Export()
    {
        Pause(false);
        if (history.Count == 0) { status.Text = "还没有可导出的命中。"; return; }
        var dialog = new SaveFileDialog { Filter = "搜索记录 ZIP|*.zip", FileName = "屏幕找字记录-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { ExportTo(dialog.FileName); status.Text = "已导出 CSV、JSON 和原始截图。"; }
        catch (Exception e) { status.Text = "导出失败：" + e.Message; }
    }
    internal void ExportTo(string destination)
    {
        var temporary = destination + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                void Text(string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(true)); writer.Write(content); }
                var portable = history.Select(i => i with { Screenshot = "screenshots/" + System.IO.Path.GetFileName(i.Screenshot) }).ToArray();
                Text("history.csv", LocalStore.Csv(portable)); Text("history.json", System.Text.Json.JsonSerializer.Serialize(portable, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                Text("README.txt", "屏幕找字记录。截图框坐标为原始 PNG 的物理像素。Uncertain 表示无法关联到之前的滚动位置。CSV 已防止公式注入。截图仅在本机生成，请在分享前确认内容。\r\n");
                foreach (var name in history.Select(h => h.Screenshot).Distinct())
                    if (File.Exists(ScreenshotPath(name))) zip.CreateEntryFromFile(ScreenshotPath(name), "screenshots/" + System.IO.Path.GetFileName(name));
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    void ClearHistory()
    {
        Pause(false);
        if (!testing && MessageBox.Show(this, "清空所有历史和本机命中截图？此操作不可撤销。", "清空历史", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        history.Clear(); tracker.Reset(); lastHash = null; Save(); PruneScreenshots(); UpdateStats();
    }
    void PruneScreenshots()
    {
        var used = history.Select(h => System.IO.Path.GetFileName(h.Screenshot)).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(System.IO.Path.Combine(dataRoot, "screenshots"), "*.png"))
            if (!used.Contains(System.IO.Path.GetFileName(file))) File.Delete(file);
    }
    void Load()
    {
        try
        {
            var settings = LocalStore.Load<Settings>(System.IO.Path.Combine(dataRoot, "settings.json")) ?? new();
            query.Text = settings.Query; follow.IsChecked = settings.Follow;
            language.SelectedValue = settings.Language; if (language.SelectedIndex < 0 && language.Items.Count > 0) language.SelectedIndex = 0;
            interval.SelectedItem = new[] { 600, 900, 1500, 2500 }.Contains(settings.Interval) ? settings.Interval : 900;
            foreach (var item in (LocalStore.Load<List<HistoryItem>>(System.IO.Path.Combine(dataRoot, "history.json")) ?? new()).Take(200))
                history.Add(item with { Screenshot = System.IO.Path.GetFileName(item.Screenshot) });
        }
        catch (Exception e) { status.Text = "旧配置无法读取，已使用默认值：" + e.Message; if (language.Items.Count > 0) language.SelectedIndex = 0; }
    }
    void Save()
    {
        LocalStore.Save(System.IO.Path.Combine(dataRoot, "settings.json"), new Settings(query.Text, (language.SelectedItem as Windows.Globalization.Language)?.LanguageTag ?? "", (int)(interval.SelectedItem ?? 900), follow.IsChecked == true));
        LocalStore.Save(System.IO.Path.Combine(dataRoot, "history.json"), history.ToArray());
    }
    internal void TestStart(Box box, string text, string lang)
    {
        target = new RegionTarget(box, false); query.Text = text; language.SelectedValue = lang; interval.SelectedItem = 600;
        regionLabel.Text = $"受控验收区域：{box.Width:0} × {box.Height:0} px"; Start();
    }
    internal void TestContinue() => Start();
    internal void TestClear() => ClearHistory();
    internal void TestTrayAction(int index)
    {
        if (tray == null) CreateTray();
        ((System.Windows.Forms.ToolStripMenuItem)tray!.ContextMenuStrip!.Items[index]).PerformClick();
    }
}
