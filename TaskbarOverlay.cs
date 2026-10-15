using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;
using static NHMonitor.Native;

namespace NHMonitor;

/// <summary>
/// Petite fenêtre transparente, toujours au premier plan, posée sur la barre des tâches
/// juste à gauche de la zone de notification. Windows 11 ne supporte plus les "deskbands",
/// c'est donc la méthode utilisée par les outils de ce type.
/// Glisser avec le clic gauche pour la déplacer, double-clic pour ouvrir le Gestionnaire des tâches.
/// </summary>
public sealed class TaskbarOverlay : Form
{
    private readonly Settings _settings;
    private readonly System.Windows.Forms.Timer _zOrderTimer;
    private readonly ToolTip _tip = new() { InitialDelay = 500, ReshowDelay = 200, AutoPopDelay = 15000, ShowAlways = true };

    private Snapshot? _snap;
    private bool _hover;
    private bool _dragging;
    private int _dragStartCursorX;
    private int _dragStartOffset;
    private string _lastTip = "";

    private float _fontScale;
    private Font? _labelFont;
    private Font? _valueFont;
    private readonly Dictionary<string, int> _measureCache = new();
    private readonly Bitmap _measureBitmap = new(1, 1);
    private string _lastRenderKey = "";

    public TaskbarOverlay(Settings settings, ContextMenuStrip menu)
    {
        _settings = settings;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ContextMenuStrip = menu;
        Text = "NHMonitor";

        // La barre des tâches repasse au-dessus dès qu'on clique dessus : on se remet au premier plan régulièrement.
        _zOrderTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _zOrderTimer.Tick += (_, _) => Render();
        _zOrderTimer.Start();

        _ = Handle; // Crée la fenêtre tout de suite : Render() en a besoin avant le premier Show().
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style = WS_POPUP;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }

    public void UpdateSnapshot(Snapshot snap)
    {
        _snap = snap;
        Render();

        string tip = BuildTooltip(snap);
        if (tip != _lastTip)
        {
            _tip.SetToolTip(this, tip);
            _lastTip = tip;
        }
    }

    /// <summary>Redessine immédiatement (après un changement de réglage).</summary>
    public void Redraw() => Render();

    public static string BuildTooltip(Snapshot s) =>
        $"{s.CpuName}\n  Utilisation : {Pct(s.CpuLoad)}   Température : {Temp(s.CpuTemp)}\n" +
        (s.GpuName is null ? "" : $"{s.GpuName}\n  Utilisation : {Pct(s.GpuLoad)}   Température : {Temp(s.GpuTemp)}\n") +
        $"Mémoire\n  {s.RamUsedGb:0.0} Go / {s.RamTotalGb:0.0} Go ({s.RamLoad:0} %)" +
        (s.NetName is null ? "" : $"\nRéseau : {s.NetName}\n  ↓ {NetworkMonitor.FormatRate(s.NetDownBps)}   ↑ {NetworkMonitor.FormatRate(s.NetUpBps)}");

    public static string Pct(float? v) => v is { } f ? $"{f:0} %" : "--";
    public static string Temp(float? v) => v is { } f ? $"{f:0} °C" : "--";

    // ---------- Position ----------

    private static bool TryGetTaskbar(out Rectangle taskbar, out Rectangle tray, out IntPtr hwnd)
    {
        taskbar = tray = Rectangle.Empty;
        hwnd = FindWindow("Shell_TrayWnd", null);
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return false;
        taskbar = r.ToRectangle();

        var trayHwnd = FindWindowEx(hwnd, IntPtr.Zero, "TrayNotifyWnd", null);
        tray = trayHwnd != IntPtr.Zero && GetWindowRect(trayHwnd, out var t) && t.Right > t.Left
            ? t.ToRectangle()
            : new Rectangle(taskbar.Right, taskbar.Top, 0, taskbar.Height);
        return true;
    }

    private static bool FullscreenAppActive()
    {
        if (SHQueryUserNotificationState(out var state) != 0) return false;
        return state is QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
            or QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
            or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;
    }

    private static bool TaskbarUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
    }

    // ---------- Rendu ----------

    private void HideOverlay()
    {
        if (Visible) Hide();
        _lastRenderKey = "";
    }

    private void EnsureFonts(float scale)
    {
        if (_labelFont is not null && Math.Abs(scale - _fontScale) < 0.01f) return;
        _labelFont?.Dispose();
        _valueFont?.Dispose();
        _labelFont = new Font("Segoe UI Semibold", 12f * scale, GraphicsUnit.Pixel);
        _valueFont = new Font("Segoe UI", 12f * scale, GraphicsUnit.Pixel);
        _fontScale = scale;
        _measureCache.Clear();
    }

    private void Render()
    {
        if (IsDisposed || !IsHandleCreated) return;

        if (!_settings.ShowOverlay
            || !TryGetTaskbar(out var taskbar, out var tray, out var tbHwnd)
            || !IsWindowVisible(tbHwnd)
            || FullscreenAppActive()
            || !Screen.FromRectangle(taskbar).Bounds.IntersectsWith(Rectangle.Inflate(taskbar, 0, -2)))
        {
            HideOverlay();
            return;
        }

        uint dpi = GetDpiForWindow(tbHwnd);
        float scale = (dpi == 0 ? 96 : dpi) / 96f;
        EnsureFonts(scale);

        bool light = TaskbarUsesLightTheme();
        Color fg = light ? Color.FromArgb(255, 20, 20, 20) : Color.White;
        Color dim = light ? Color.FromArgb(255, 90, 90, 90) : Color.FromArgb(255, 190, 190, 190);

        var s = _snap;
        var blocks = s is null ? [] : BuildBlocks(s, fg, light);
        if (s is not null && blocks.Count == 0)
        {
            // Tout a été décoché dans le menu : rien à afficher.
            HideOverlay();
            return;
        }

        int height = taskbar.Height;
        int pad = (int)(8 * scale);
        int gap = (int)(4 * scale);
        int blockGap = (int)(16 * scale);

        int labelW = new[] { "CPU", "GPU", "RAM", "NET" }.Max(t => Measure(t, _labelFont!));
        // Largeur de chaque bloc calculée sur un texte de référence ("100 %"...) pour éviter que le widget
        // ne "danse" quand les valeurs changent.
        var valueWidths = blocks.Select(b => b.Lines.Max(l => Measure(l.WidthRef, _valueFont!))).ToList();

        int width = s is null
            ? Measure("NHMonitor", _labelFont!) + 4 * pad
            : 2 * pad + blocks.Count * (labelW + gap) + valueWidths.Sum() + (blocks.Count - 1) * blockGap;

        int maxX = Math.Max(taskbar.Left, taskbar.Right - width);
        int x = _settings.Position == WidgetPosition.Left
            ? Math.Clamp(taskbar.Left + (int)(_settings.OffsetFromLeft * scale), taskbar.Left, maxX)
            : Math.Clamp(tray.Left - (int)(_settings.OffsetFromTray * scale) - width, taskbar.Left, maxX);
        int y = taskbar.Top;

        string renderKey = string.Join("|", x, y, width, height, scale, light, _hover || _dragging,
            string.Join(";", blocks.Select(b => b.Label + "=" + string.Join(",", b.Lines.Select(l => l.Text + "#" + l.Color.ToArgb())))));
        if (renderKey == _lastRenderKey && Visible)
        {
            KeepOnTop();
            return;
        }
        _lastRenderKey = renderKey;

        using var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // Fond quasi invisible (alpha 1) pour recevoir les clics ; plus visible au survol.
            int inset = (int)(4 * scale);
            var bgRect = new Rectangle(0, inset, width - 1, height - 2 * inset - 1);
            using (var bg = new SolidBrush(_hover || _dragging
                       ? (light ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255))
                       : Color.FromArgb(1, 0, 0, 0)))
            using (var path = RoundedRect(bgRect, (int)(5 * scale)))
                g.FillPath(bg, path);

            int lineH = _valueFont!.Height;
            using var right = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Far };

            if (s is null)
            {
                TextRenderer.DrawText(g, "NHMonitor", _labelFont, new Rectangle(0, 0, width, height), fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            int cx = pad;
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                int valueW = valueWidths[i];

                using (var lb = new SolidBrush(dim))
                    g.DrawString(b.Label, _labelFont!, lb, cx, (height - _labelFont!.Height) / 2f);
                cx += labelW + gap;

                // Une ou deux lignes, centrées verticalement.
                int top = (height - b.Lines.Count * lineH) / 2;
                foreach (var line in b.Lines)
                {
                    if (line.Prefix is not null)
                    {
                        using var prefixBrush = new SolidBrush(line.PrefixColor);
                        g.DrawString(line.Prefix, _valueFont, prefixBrush, cx, top, StringFormat.GenericTypographic);
                    }
                    using var brush = new SolidBrush(line.Color);
                    g.DrawString(line.Text, _valueFont, brush, new RectangleF(cx, top, valueW + 2, lineH), right);
                    top += lineH;
                }

                // Séparateur discret entre deux composants : trait vertical fin et semi-transparent.
                if (i < blocks.Count - 1)
                {
                    int sepX = cx + valueW + blockGap / 2;
                    int sepH = height / 2;
                    using var sep = new SolidBrush(Color.FromArgb(70, dim));
                    g.FillRectangle(sep, sepX, (height - sepH) / 2, Math.Max(1, (int)scale), sepH);
                }
                cx += valueW + blockGap;
            }
        }

        ApplyBitmap(bmp, x, y);

        if (!Visible) Show();
        KeepOnTop();
    }

    private void KeepOnTop() =>
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    private int Measure(string text, Font font)
    {
        string key = (ReferenceEquals(font, _labelFont) ? "L:" : "V:") + text;
        if (!_measureCache.TryGetValue(key, out int w))
        {
            using var g = Graphics.FromImage(_measureBitmap);
            w = (int)Math.Ceiling(g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width);
            _measureCache[key] = w;
        }
        return w;
    }

    private sealed record Line(string Text, Color Color, string WidthRef, string? Prefix = null, Color PrefixColor = default);
    private sealed record Block(string Label, List<Line> Lines);

    private List<Block> BuildBlocks(Snapshot s, Color fg, bool light)
    {
        var blocks = new List<Block>();

        void AddUnit(string label, bool showLoad, float? load, bool showTemp, float? temp)
        {
            var lines = new List<Line>();
            if (showLoad) lines.Add(new Line(Pct(load), LoadColor(load, fg), "100 %"));
            // Température indisponible (capteur absent) : on n'affiche que l'utilisation.
            if (showTemp && temp is not null) lines.Add(new Line(Temp(temp), TempColor(temp, fg), "100 °C"));
            if (lines.Count > 0) blocks.Add(new Block(label, lines));
        }

        AddUnit("CPU", _settings.ShowCpuLoad, s.CpuLoad, _settings.ShowCpuTemp, s.CpuTemp);
        if (s.GpuName is not null)
            AddUnit("GPU", _settings.ShowGpuLoad, s.GpuLoad, _settings.ShowGpuTemp, s.GpuTemp);

        var ram = new List<Line>();
        if (_settings.ShowRamLoad) ram.Add(new Line($"{s.RamLoad:0} %", LoadColor(s.RamLoad, fg), "100 %"));
        if (_settings.ShowRamUsed) ram.Add(new Line($"{s.RamUsedGb:0.0} Go", fg, "99.9 Go"));
        if (ram.Count > 0) blocks.Add(new Block("RAM", ram));

        // Réseau : flèche bleue = entrant (téléchargement), flèche verte = sortant (envoi).
        var net = new List<Line>();
        Color downColor = light ? Color.FromArgb(255, 0, 110, 210) : Color.FromArgb(255, 90, 175, 255);
        Color upColor = light ? Color.FromArgb(255, 20, 140, 60) : Color.FromArgb(255, 100, 215, 120);
        if (_settings.ShowNetDown)
            net.Add(new Line(NetworkMonitor.FormatRate(s.NetDownBps), fg, "↓ 999 Ko/s", "↓", downColor));
        if (_settings.ShowNetUp)
            net.Add(new Line(NetworkMonitor.FormatRate(s.NetUpBps), fg, "↑ 999 Ko/s", "↑", upColor));
        if (net.Count > 0 && s.NetName is not null) blocks.Add(new Block("NET", net));

        return blocks;
    }

    private static Color LoadColor(float? v, Color normal) => v switch
    {
        >= 90 => Color.FromArgb(255, 255, 90, 80),
        >= 70 => Color.FromArgb(255, 255, 170, 50),
        _ => normal,
    };

    private static Color TempColor(float? v, Color normal) => v switch
    {
        >= 85 => Color.FromArgb(255, 255, 90, 80),
        >= 70 => Color.FromArgb(255, 255, 170, 50),
        _ => normal,
    };

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private void ApplyBitmap(Bitmap bmp, int x, int y)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memDc, hBitmap);
        try
        {
            var size = new SIZE(bmp.Width, bmp.Height);
            var src = new POINT(0, 0);
            var dst = new POINT(x, y);
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA,
            };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ---------- Souris ----------

    private int AnchorOffset
    {
        get => _settings.Position == WidgetPosition.Left ? _settings.OffsetFromLeft : _settings.OffsetFromTray;
        set
        {
            if (_settings.Position == WidgetPosition.Left) _settings.OffsetFromLeft = value;
            else _settings.OffsetFromTray = value;
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Render();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Render();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragStartCursorX = Cursor.Position.X;
            _dragStartOffset = AnchorOffset;
            Capture = true;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            float scale = Math.Max(1f, _fontScale);
            int deltaLogical = (int)((Cursor.Position.X - _dragStartCursorX) / scale);
            // À droite, le décalage est compté vers la gauche depuis les icônes de notification.
            if (_settings.Position == WidgetPosition.Right) deltaLogical = -deltaLogical;
            AnchorOffset = Math.Max(0, _dragStartOffset + deltaLogical);
            Render();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            _settings.Save();
            Render();
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Program.OpenTaskManager();
        }
        base.OnMouseDoubleClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _zOrderTimer.Dispose();
            _tip.Dispose();
            _labelFont?.Dispose();
            _valueFont?.Dispose();
            _measureBitmap.Dispose();
        }
        base.Dispose(disposing);
    }
}
