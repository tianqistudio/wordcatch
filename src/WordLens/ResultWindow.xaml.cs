using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WordLens;

public partial class ResultWindow : Window
{
    internal ResultCard Primary { get; }
    internal ResultCard ClipboardResult { get; }
    private bool combined;

    public ResultWindow(WorkerClient worker, TranslationService? translation = null)
    {
        InitializeComponent(); Localization.Attach(this); Loaded += (_, _) => Localization.Attach(this);
        PrimaryHost.Content = Primary = new ResultCard(worker, translation);
        ClipboardHost.Content = ClipboardResult = new ResultCard(worker, translation);
        Closing += (_, e) => { e.Cancel = true; Dismiss(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; } };
        SizeChanged += (_, _) => ArrangeCards();
        CardScroll.SizeChanged += (_, _) => ArrangeCards();
    }

    private void Present(Native.Point point, bool hasClipboard)
    {
        Primary.Dismiss();
        ClipboardResult.Dismiss();
        bool wasCombined = combined;
        combined = hasClipboard;
        ClipboardBorder.Visibility = combined ? Visibility.Visible : Visibility.Collapsed;
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        var monitor = Native.MonitorFromPoint(point, 2);
        Native.GetMonitorInfo(monitor, ref info);
        Native.GetDpiForMonitor(monitor, 0, out uint dpi, out _);
        double scale = (dpi == 0 ? 96 : dpi) / 96.0;
        double availableWidth = (info.Work.Right - info.Work.Left) / scale;
        double availableHeight = (info.Work.Bottom - info.Work.Top) / scale;
        if (combined)
        {
            Width = Math.Max(MinWidth, Math.Min(920, availableWidth));
            Height = Math.Max(MinHeight, Math.Min(Width < 760 ? 880 : 460, availableHeight));
        }
        if (wasCombined && !combined) { Width = 460; Height = 460; }
        ArrangeCards();
        Show();
        ScreenCapture.Place(this, point);
    }

    private void ArrangeCards()
    {
        bool stacked = combined && Width < 760;
        ClipboardColumn.Width = combined && !stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ClipboardRow.Height = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(ClipboardBorder, stacked ? 0 : 1);
        Grid.SetRow(ClipboardBorder, stacked ? 1 : 0);
        ClipboardBorder.BorderThickness = stacked ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        PrimaryHost.MinHeight = ClipboardHost.MinHeight = 400;
        Cards.MinHeight = stacked ? 800 : 400;
        Cards.Height = Math.Max(Cards.MinHeight, CardScroll.ActualHeight);
    }

    public Task LookupAsync(string text, Native.Point point, string origin = "输入文字")
    {
        Present(point, false);
        return Primary.LookupAsync(text, point, origin);
    }

    public Task CaptureRegionAsync(Capture capture)
    {
        Present(capture.Cursor, false);
        return Primary.CaptureRegionAsync(capture);
    }

    public Task CaptureAsync(Capture capture)
    {
        Present(capture.Cursor, false);
        return Primary.CaptureAsync(capture);
    }

    public async Task LookupSourcesAsync(string? selected, Capture? capture, ClipboardRead clipboard,
        Native.Point point, string? screenError = null)
    {
        Present(point, clipboard.Text != null || clipboard.Error != null);
        Task screen;
        if (!string.IsNullOrWhiteSpace(selected)) screen = Primary.LookupAsync(selected, point, "选中文字");
        else if (capture != null) screen = Primary.CaptureAsync(capture);
        else
        {
            Primary.ShowError(screenError ?? "未读取到屏幕文字，请重新取词。", "屏幕识别");
            screen = Task.CompletedTask;
        }
        Task copied = Task.CompletedTask;
        if (clipboard.Error != null) ClipboardResult.ShowError(clipboard.Error, "剪贴板");
        else if (clipboard.Text != null) copied = ClipboardResult.LookupAsync(clipboard.Text, point, "剪贴板");
        await Task.WhenAll(screen, copied);
    }

    public void Dismiss()
    {
        Primary.Dismiss();
        ClipboardResult.Dismiss();
        Hide();
    }
}
