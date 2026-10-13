using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Forms = System.Windows.Forms;

namespace WordLens;

internal sealed class RegionSelector : Forms.Form
{
    private readonly Bitmap desktop;
    private readonly Rectangle screen;
    private Point? start;
    private Rectangle selection;
    private Capture? result;
    private RegionSelector()
    {
        screen = Forms.SystemInformation.VirtualScreen;
        desktop = new Bitmap(screen.Width, screen.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(desktop))
            graphics.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
        AutoScaleMode = Forms.AutoScaleMode.None;
        FormBorderStyle = Forms.FormBorderStyle.None;
        StartPosition = Forms.FormStartPosition.Manual;
        Bounds = screen; TopMost = true; ShowInTaskbar = false; KeyPreview = true;
        DoubleBuffered = true; Cursor = Forms.Cursors.Cross;
        Text = Localization.Translate("框选翻译 · 拖动选择，Esc 或右键取消");
        KeyDown += (_, e) => { if (e.KeyCode == Forms.Keys.Escape) Close(); };
        MouseDown += (_, e) => {
            if (e.Button == Forms.MouseButtons.Right) { Close(); return; }
            if (e.Button == Forms.MouseButtons.Left) { start = e.Location; Capture = true; }
        };
        MouseMove += (_, e) => {
            if (start is not { } a) return;
            selection = Rectangle.Intersect(ClientRectangle, Rectangle.FromLTRB(Math.Min(a.X,e.X), Math.Min(a.Y,e.Y), Math.Max(a.X,e.X), Math.Max(a.Y,e.Y)));
            Invalidate();
        };
        MouseUp += (_, e) => {
            if (e.Button != Forms.MouseButtons.Left || start == null) return;
            var a = start.Value;
            selection = Rectangle.Intersect(ClientRectangle, Rectangle.FromLTRB(Math.Min(a.X,e.X), Math.Min(a.Y,e.Y), Math.Max(a.X,e.X), Math.Max(a.Y,e.Y)));
            Capture = false;
            if (selection.Width < 4 || selection.Height < 4) { start = null; selection = Rectangle.Empty; Invalidate(); return; }
            double scale = Math.Min(1, Math.Min(2400.0/selection.Width,1600.0/selection.Height));
            using var cropped = new Bitmap(Math.Max(1,(int)(selection.Width*scale)),Math.Max(1,(int)(selection.Height*scale)));
            using (var graphics = Graphics.FromImage(cropped)) {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(desktop,new Rectangle(Point.Empty,cropped.Size),selection,GraphicsUnit.Pixel);
            }
            using var stream = new MemoryStream(); cropped.Save(stream,ImageFormat.Png);
            result = new Capture(Convert.ToBase64String(stream.ToArray()),0,0,screen.Left+selection.Left,screen.Top+selection.Top,
                new Native.Point(screen.Left+selection.Right,screen.Top+selection.Bottom));
            Close();
        };
    }
    protected override void OnPaint(Forms.PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(desktop,0,0);
        using var shade = new SolidBrush(Color.FromArgb(110,0,0,0));
        e.Graphics.FillRectangle(shade,ClientRectangle);
        if (!selection.IsEmpty) {
            e.Graphics.DrawImage(desktop,selection,selection,GraphicsUnit.Pixel);
            using var pen = new Pen(Color.FromArgb(233,185,73),2);
            e.Graphics.DrawRectangle(pen,selection);
        }
        Forms.TextRenderer.DrawText(e.Graphics,Localization.Translate("拖动框选文字 · 松开识别 · Esc / 右键取消"),Font,new Point(24,24),Color.White,Color.FromArgb(30,40,50));
    }
    protected override void Dispose(bool disposing) { if (disposing) desktop.Dispose(); base.Dispose(disposing); }
    internal static Capture? SelectRegion() { using var selector = new RegionSelector(); selector.ShowDialog(); return selector.result; }
}
