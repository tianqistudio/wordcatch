using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TianqiStudio.DesktopShortcuts;
using TianqiStudio.DesktopShortcuts.Wpf;
using Forms = System.Windows.Forms;

namespace WordLens;
public partial class MainWindow : Window
{
    private readonly WorkerClient worker = new();
    private readonly ShortcutManager shortcuts;
    private readonly Forms.NotifyIcon tray;
    private readonly ResultWindow result;
    private readonly TranslationService translation = new();
    private readonly UpdateService updates = new();
    private readonly System.Windows.Threading.DispatcherTimer updateTimer = new() { Interval=TimeSpan.FromMinutes(15) };
    private string? notifiedVersion;
    private bool exiting, capturing, recording;
    private bool initialized;
    public MainWindow(bool enableUpdates = true)
    {
        InitializeComponent(); Localization.Attach(this); Loaded += (_, _) => Localization.Attach(this);
        result = new ResultWindow(worker, translation);
        shortcuts = new ShortcutManager(new LocalShortcutStore(ShortcutStorePaths.ForApplication("TianqiStudio", "WordLens")), new WindowsHotkeyRegistrar());
        shortcuts.ReplaceActions(new[] { new ShortcutAction("capture", "屏幕取词", "查词", ShortcutScopePolicy.RequiredGlobal,
            new ShortcutBinding(new ShortcutChord(0x44, ShortcutModifiers.Control | ShortcutModifiers.Alt), true), "优先查询选区，其次识别鼠标下的单词，同时查询剪贴板"),
            new ShortcutAction("region", "框选翻译", "查词", ShortcutScopePolicy.RequiredGlobal,
                new ShortcutBinding(new ShortcutChord(0x44, ShortcutModifiers.Control | ShortcutModifiers.Shift | ShortcutModifiers.Alt), true), "拖动选择屏幕区域并翻译") });
        shortcuts.ActionRequested += action => { if (action == "region") _ = CaptureRegionAsync(); else _ = CaptureAsync(); };
        shortcuts.ChordCaptured += chord => { if (recording && chord != null) SaveChord(chord); };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开单词捕手", null, (_, _) => Dispatcher.Invoke(OpenWindow));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Quit));
        tray = new Forms.NotifyIcon { Text = "单词捕手 · WordCatch", Icon = LoadTrayIcon(), ContextMenuStrip = menu, Visible = true };
        void RefreshTray()
        {
            menu.Items[0].Text = Localization.Translate("打开单词捕手");
            menu.Items[1].Text = Localization.Translate("退出");
            tray.Text = Localization.Translate("单词捕手 · WordCatch");
        }
        RefreshTray();
        Localization.LanguageChanged += RefreshTray;
        Application.Current.Exit += (_, _) => Localization.LanguageChanged -= RefreshTray;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenWindow);
        Closing += OnClosing; PreviewKeyDown += OnKey;
        Deactivated += (_, _) => { if (recording) EndRecording(); };
        updates.Changed += RefreshUpdates;
        updateTimer.Tick += async (_,_) => await updates.CheckAutomaticAsync();
        Loaded += async (_, _) => await InitializeAsync(enableUpdates);
        Application.Current.Exit += (_, _) => { tray.Dispose(); shortcuts.Dispose(); worker.Dispose(); };
        RefreshHotkey();
    }
    internal async Task InitializeAsync(bool enableUpdates = true)
    {
        if (initialized) return;
        initialized = true;
        await PrepareAsync();
        if (enableUpdates) { updateTimer.Start(); await updates.CheckAutomaticAsync(); }
    }
    private async Task PrepareAsync()
    {
        try { await worker.StartAsync(); Status.Text = "已就绪，可输入查询或使用快捷键取词。"; LookupButton.IsEnabled = true; }
        catch (Exception ex) { Status.Text = "初始化失败：" + ex.Message + " 可再次查词重试。"; LookupButton.IsEnabled = true; }
    }
    private async Task CaptureRegionAsync()
    {
        if (capturing || recording) return;
        Task? query = null;
        capturing = true;
        try
        {
            result.Dismiss();
            await Task.Delay(100);
            var capture = RegionSelector.SelectRegion();
            if (capture != null) query = result.CaptureRegionAsync(capture);
        }
        catch (Exception ex) { Status.Text = "框选失败：" + ex.Message; OpenWindow(); }
        finally { capturing = false; }
        if (query != null) await query;
    }
    private async Task CaptureAsync()
    {
        if (capturing) return;
        capturing = true;
        Task? query = null;
        try
        {
            result.Dismiss();
            Native.GetCursorPos(out var position);
            var clipboard = ClipboardText.Read();
            string? selected = null;
            Capture? capture = null;
            string? screenError = null;
            try
            {
                selected = await SelectedText.ReadAsync();
                if (string.IsNullOrWhiteSpace(selected))
                {
                    // Capture before showing either card, so the cards cannot enter OCR.
                    await Task.Delay(70);
                    capture = ScreenCapture.AroundPoint(position);
                }
            }
            catch (Exception ex) { screenError = "屏幕取词失败：" + ex.Message; }
            query = result.LookupSourcesAsync(selected, capture, clipboard, position, screenError);
        }
        catch (Exception ex) { Status.Text = "取词失败：" + ex.Message; OpenWindow(); }
        finally { capturing = false; }
        // A slow cloud request must not lock out the next local hotkey lookup.
        // ResultWindow cancels stale cloud requests and guards all result writes.
        if (query != null) await query;
    }
    private async void ManualLookup(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Query.Text)) { Query.Focus(); return; }
        Native.GetCursorPos(out var point);
        await result.LookupAsync(Query.Text.Trim(), point);
    }
    private void RefreshHotkey()
    {
        RegionStatus.Text = shortcuts.Errors.TryGetValue("region", out var regionError) ? "框选快捷键冲突：" + regionError : "Ctrl + Shift + Alt + D · 拖动框选，Esc 取消";
        var chord = shortcuts.Bindings["capture"].Chord;
        HotkeyLabel.Text = chord == null ? "未设置" : string.Join(" + ", new[] {
            chord.Modifiers.HasFlag(ShortcutModifiers.Control) ? "Ctrl" : null,
            chord.Modifiers.HasFlag(ShortcutModifiers.Alt) ? "Alt" : null,
            chord.Modifiers.HasFlag(ShortcutModifiers.Shift) ? "Shift" : null,
            chord.Modifiers.HasFlag(ShortcutModifiers.Windows) ? "Win" : null,
            KeyInterop.KeyFromVirtualKey(chord.VirtualKey).ToString() }.Where(x => x != null));
        HotkeyStatus.Text = shortcuts.Errors.TryGetValue("capture", out var error) ? error : shortcuts.StorageError ?? "已注册；关闭窗口后仍可使用。";
    }
    private void ChangeHotkey(object sender, RoutedEventArgs e)
    {
        recording = true; shortcuts.SuspendDispatch = true;
        HotkeyStatus.Text = "请按新组合键；Esc 取消。";
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { EndRecording(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var mods = ShortcutModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= ShortcutModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= ShortcutModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= ShortcutModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= ShortcutModifiers.Windows;
        SaveChord(new ShortcutChord(KeyInterop.VirtualKeyFromKey(key), mods));
    }
    private void SaveChord(ShortcutChord chord)
    {
        if (!shortcuts.TryUpdate("capture", new ShortcutBinding(chord, true), out var error)) { HotkeyStatus.Text = error + " 请换一个组合，或按 Esc 取消。"; return; }
        EndRecording();
    }
    private void EndRecording() { recording = false; shortcuts.SuspendDispatch = false; RefreshHotkey(); }
    private void OpenWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void HideWindow(object sender, RoutedEventArgs e) => Hide();
    private void OpenCloudSettings(object sender, RoutedEventArgs e)
    {
        result.Dismiss();
        new CloudSettingsWindow(translation, updates) { Owner = this }.ShowDialog();
    }
    private void RefreshUpdates()
    {
        UpdateNotice.Visibility=updates.HasUpdate?Visibility.Visible:Visibility.Collapsed;
        if(updates.HasUpdate && updates.LastResult?.Release is { } release)
        {
            UpdateNotice.Content="发现新版 "+release.VersionText+" · 查看更新";
            if(notifiedVersion!=release.VersionText)
            { notifiedVersion=release.VersionText; tray.ShowBalloonTip(5000,Localization.Translate("单词捕手有更新"),Localization.Translate("新版 "+release.VersionText+" 可用，请打开程序查看。"),Forms.ToolTipIcon.Info); }
        }
    }
    private void OpenUpdate(object sender,RoutedEventArgs e)
    { try { updates.OpenRelease(); } catch(Exception) { Status.Text="无法打开浏览器，请稍后在设置中重试。"; } }
    private static System.Drawing.Icon LoadTrayIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tray.ico"))!.Stream;
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }
    private void ExitApp(object sender, RoutedEventArgs e) => Quit();
    private void OnClosing(object? sender, CancelEventArgs e) { if (!exiting) { e.Cancel = true; Hide(); } }
    private void Quit()
    {
        exiting = true; updateTimer.Stop(); result.Dismiss(); tray.Dispose(); shortcuts.Dispose(); worker.Dispose(); Application.Current.Shutdown();
    }
}
