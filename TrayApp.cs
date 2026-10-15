using System.Drawing.Drawing2D;
using static NHMonitor.Native;

namespace NHMonitor;

/// <summary>Contexte principal : icône de notification, menu, boucle de mesure et widget de barre des tâches.</summary>
public sealed class TrayApp : ApplicationContext
{
    private static readonly int[] Intervals = [500, 1000, 2000, 5000];

    private readonly Settings _settings = Settings.Load();
    private readonly HardwareService _hardware;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly TaskbarOverlay _overlay;
    private readonly CancellationTokenSource _cts = new();
    private readonly SynchronizationContext _ui;

    private ToolStripMenuItem _showOverlayItem = null!;
    private ToolStripMenuItem _startupItem = null!;
    private ToolStripMenuItem _intervalMenu = null!;
    private ToolStripMenuItem _displayMenu = null!;
    private ToolStripMenuItem _positionMenu = null!;
    private bool _keepMenuOpen;
    private Icon? _currentIcon;
    private string _currentIconKey = "";
    private ReportForm? _reportForm;

    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 60_000 };
    private ToolStripMenuItem _updateItem = null!;
    private ToolStripMenuItem _autoUpdateItem = null!;
    private UpdateInfo? _availableUpdate;
    private Version? _notifiedVersion;
    private bool _updateBusy;

    public TrayApp()
    {
        _hardware = new HardwareService();
        _menu = BuildMenu();
        _overlay = new TaskbarOverlay(_settings, _menu);
        // Le contexte de synchronisation WinForms est installé à la création du premier contrôle.
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _tray = new NotifyIcon
        {
            Text = "NHMonitor",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        SetTrayIcon(null);
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleOverlay();
        };

        _tray.BalloonTipClicked += (_, _) => InstallUpdate();
        // Première vérification une minute après le démarrage, puis contrôle toutes les heures
        // (une requête réelle au plus toutes les 24 h).
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = 3_600_000;
            if (_settings.CheckForUpdates
                && (_settings.LastUpdateCheckUtc is not { } last || DateTime.UtcNow - last > TimeSpan.FromHours(24)))
                _ = CheckForUpdatesAsync(manual: false);
        };
        _updateTimer.Start();

        _ = Task.Run(() => MeasureLoop(_cts.Token));
    }

    // ---------- Mises à jour ----------

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        try
        {
            var update = await UpdateService.CheckAsync(_cts.Token);
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settings.Save();

            if (update is null)
            {
                if (manual)
                    MessageBox.Show($"NHMonitor {UpdateService.CurrentVersion.ToString(3)} est à jour.", "NHMonitor",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _availableUpdate = update;
            _updateItem.Text = $"Installer la mise à jour {update.Version.ToString(3)}…";
            _updateItem.Visible = true;

            if (manual)
            {
                _updateBusy = false;
                InstallUpdate();
            }
            else if (_notifiedVersion != update.Version)
            {
                _notifiedVersion = update.Version;
                _tray.ShowBalloonTip(15000, "Mise à jour disponible",
                    $"NHMonitor {update.Version.ToString(3)} est disponible. Cliquez ici pour l'installer.", ToolTipIcon.Info);
            }
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            Program.Log(ex);
            if (manual)
                MessageBox.Show($"Impossible de vérifier les mises à jour :\n{ex.Message}", "NHMonitor",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _updateBusy = false;
        }
    }

    private async void InstallUpdate()
    {
        var update = _availableUpdate;
        if (update is null || _updateBusy) return;
        string newVersion = update.Version.ToString(3);

        if (!UpdateService.CanInstallAutomatically)
        {
            if (MessageBox.Show(
                    $"NHMonitor {newVersion} est disponible.\n\nCette copie (version portable, ou lancée sans droits administrateur) " +
                    "ne peut pas se mettre à jour elle-même. Ouvrir la page de téléchargement ?",
                    "NHMonitor", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                UpdateService.OpenReleasePage(update.ReleaseUrl);
            return;
        }

        string notes = update.Notes.Length > 1200 ? update.Notes[..1200] + "…" : update.Notes;
        if (MessageBox.Show(
                $"NHMonitor {newVersion} est disponible (version actuelle : {UpdateService.CurrentVersion.ToString(3)}).\n\n" +
                $"{notes}\n\nTélécharger et installer maintenant ? NHMonitor redémarrera automatiquement.",
                "Mise à jour de NHMonitor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _updateBusy = true;
        using var form = new UpdateProgressForm(update.Version);
        form.Show();
        try
        {
            string installer = await UpdateService.DownloadAsync(update, new Progress<int>(form.Report), form.Cancellation.Token);
            form.SetStatus("Lancement de l'installation…");
            UpdateService.LaunchInstaller(installer);
            ExitThread(); // L'installeur remplace les fichiers puis relance NHMonitor.
        }
        catch (OperationCanceledException) when (form.Cancellation.IsCancellationRequested)
        {
            // Annulé par l'utilisateur.
        }
        catch (UpdateSecurityException ex)
        {
            Program.Log(ex);
            form.Hide();
            MessageBox.Show($"Mise à jour refusée pour une raison de sécurité :\n{ex.Message}\n\nAucun fichier n'a été exécuté.",
                "NHMonitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            form.Hide();
            MessageBox.Show($"Échec de la mise à jour :\n{ex.Message}", "NHMonitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _updateBusy = false;
        }
    }

    // ---------- Mesures ----------

    private async Task MeasureLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snap = _hardware.Read();
                _ui.Post(_ => Apply(snap), null);
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }

            try { await Task.Delay(_settings.IntervalMs, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private void Apply(Snapshot s)
    {
        if (_cts.IsCancellationRequested) return;
        _overlay.UpdateSnapshot(s);
        SetTrayIcon(s);

        string text = $"CPU {TaskbarOverlay.Pct(s.CpuLoad)} {TaskbarOverlay.Temp(s.CpuTemp)}\n" +
                      (s.GpuName is null ? "" : $"GPU {TaskbarOverlay.Pct(s.GpuLoad)} {TaskbarOverlay.Temp(s.GpuTemp)}\n") +
                      $"RAM {s.RamLoad:0} % ({s.RamUsedGb:0.0}/{s.RamTotalGb:0.0} Go)" +
                      (s.NetName is null ? "" : $"\nNET ↓ {NetworkMonitor.FormatRate(s.NetDownBps)} ↑ {NetworkMonitor.FormatRate(s.NetUpBps)}");
        _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    /// <summary>Icône dynamique : trois barres verticales CPU / GPU / RAM.</summary>
    private void SetTrayIcon(Snapshot? s)
    {
        var size = SystemInformation.SmallIconSize;
        float[] values = s is null ? [0, 0, 0] : [s.CpuLoad ?? 0, s.GpuLoad ?? 0, s.RamLoad];
        int[] heights = values.Select(v => Math.Max(1, (int)Math.Round(size.Height * Math.Clamp(v, 0, 100) / 100f))).ToArray();

        string key = $"{size.Width}x{size.Height}:{string.Join(",", heights)}";
        if (key == _currentIconKey) return;
        _currentIconKey = key;

        using var bmp = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Color.Transparent);

            Color[] colors = [Color.FromArgb(80, 170, 255), Color.FromArgb(90, 210, 110), Color.FromArgb(200, 130, 255)];

            int n = heights.Length;
            float barW = size.Width / (float)n;
            using var frame = new SolidBrush(Color.FromArgb(110, 128, 128, 128));
            for (int i = 0; i < n; i++)
            {
                float x = i * barW + 1;
                float w = barW - 2;
                g.FillRectangle(frame, x, 0, w, size.Height);
                using var b = new SolidBrush(colors[i]);
                g.FillRectangle(b, x, size.Height - heights[i], w, heights[i]);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        _tray.Icon = icon;

        if (_currentIcon is not null)
        {
            DestroyIcon(_currentIcon.Handle);
            _currentIcon.Dispose();
        }
        _currentIcon = icon;
    }

    // ---------- Menu ----------

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var title = new ToolStripMenuItem("NHMonitor") { Enabled = false };
        title.Font = new Font(title.Font, FontStyle.Bold);
        menu.Items.Add(title);
        _updateItem = new ToolStripMenuItem("Installer la mise à jour…", null, (_, _) => InstallUpdate()) { Visible = false };
        _updateItem.Font = new Font(_updateItem.Font, FontStyle.Bold);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());

        _showOverlayItem = new ToolStripMenuItem("Afficher dans la barre des tâches", null, (_, _) => ToggleOverlay())
        {
            Checked = _settings.ShowOverlay,
        };
        menu.Items.Add(_showOverlayItem);

        _intervalMenu = new ToolStripMenuItem("Fréquence de mise à jour");
        foreach (int ms in Intervals)
        {
            var item = new ToolStripMenuItem(ms < 1000 ? $"{ms / 1000.0:0.0} s" : $"{ms / 1000} s") { Tag = ms };
            item.Click += (_, _) =>
            {
                _settings.IntervalMs = ms;
                _settings.Save();
                RefreshMenuChecks();
            };
            _intervalMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(_intervalMenu);

        _displayMenu = new ToolStripMenuItem("Éléments affichés");
        AddDisplayOption("CPU : utilisation", () => _settings.ShowCpuLoad, v => _settings.ShowCpuLoad = v);
        AddDisplayOption("CPU : température", () => _settings.ShowCpuTemp, v => _settings.ShowCpuTemp = v);
        _displayMenu.DropDownItems.Add(new ToolStripSeparator());
        AddDisplayOption("GPU : utilisation", () => _settings.ShowGpuLoad, v => _settings.ShowGpuLoad = v);
        AddDisplayOption("GPU : température", () => _settings.ShowGpuTemp, v => _settings.ShowGpuTemp = v);
        _displayMenu.DropDownItems.Add(new ToolStripSeparator());
        AddDisplayOption("RAM : utilisation (%)", () => _settings.ShowRamLoad, v => _settings.ShowRamLoad = v);
        AddDisplayOption("RAM : mémoire utilisée (Go)", () => _settings.ShowRamUsed, v => _settings.ShowRamUsed = v);
        _displayMenu.DropDownItems.Add(new ToolStripSeparator());
        AddDisplayOption("Réseau : débit entrant (↓)", () => _settings.ShowNetDown, v => _settings.ShowNetDown = v);
        AddDisplayOption("Réseau : débit sortant (↑)", () => _settings.ShowNetUp, v => _settings.ShowNetUp = v);
        // Garde le menu ouvert pour cocher plusieurs éléments d'affilée.
        _displayMenu.DropDown.ItemClicked += (_, _) =>
        {
            _keepMenuOpen = true;
            menu.BeginInvoke(() => _keepMenuOpen = false);
        };
        _displayMenu.DropDown.Closing += KeepOpenOnItemClick;
        menu.Closing += KeepOpenOnItemClick;
        menu.Items.Add(_displayMenu);

        _positionMenu = new ToolStripMenuItem("Position");
        foreach (var (label, pos) in new[]
                 {
                     ("À gauche de la barre des tâches", WidgetPosition.Left),
                     ("À droite, près des icônes de notification", WidgetPosition.Right),
                 })
        {
            var item = new ToolStripMenuItem(label) { Tag = pos };
            item.Click += (_, _) =>
            {
                _settings.Position = pos;
                SaveAndRedraw();
            };
            _positionMenu.DropDownItems.Add(item);
        }
        _positionMenu.DropDownItems.Add(new ToolStripSeparator());
        _positionMenu.DropDownItems.Add(new ToolStripMenuItem("Réinitialiser le décalage", null, (_, _) =>
        {
            if (_settings.Position == WidgetPosition.Left) _settings.OffsetFromLeft = 0;
            else _settings.OffsetFromTray = 0;
            SaveAndRedraw();
        }));
        menu.Items.Add(_positionMenu);

        _startupItem = new ToolStripMenuItem("Démarrer avec Windows", null, (_, _) =>
        {
            bool target = !_startupItem.Checked;
            if (!StartupManager.SetEnabled(target))
                MessageBox.Show("Impossible de modifier le démarrage automatique.", "NHMonitor",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _startupItem.Checked = StartupManager.IsEnabled();
        });
        if (!StartupManager.IsInstalledInProtectedLocation())
        {
            _startupItem.Enabled = false;
            _startupItem.Text = "Démarrer avec Windows (version installée uniquement)";
            _startupItem.ToolTipText = "Disponible quand NHMonitor est installé dans Program Files avec l'installeur.";
        }
        menu.Items.Add(_startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Rechercher les mises à jour…", null, (_, _) => _ = CheckForUpdatesAsync(manual: true)));
        _autoUpdateItem = new ToolStripMenuItem("Vérifier automatiquement les mises à jour", null, (_, _) =>
        {
            _settings.CheckForUpdates = !_settings.CheckForUpdates;
            SaveAndRedraw();
        });
        menu.Items.Add(_autoUpdateItem);
        menu.Items.Add(new ToolStripMenuItem($"À propos (version {UpdateService.CurrentVersion.ToString(3)})", null, (_, _) =>
            UpdateService.OpenReleasePage($"https://github.com/{UpdateService.Owner}/{UpdateService.Repo}/")));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Rapport des capteurs…", null, (_, _) => ShowReport()));
        menu.Items.Add(new ToolStripMenuItem("Gestionnaire des tâches", null, (_, _) => Program.OpenTaskManager()));
        menu.Items.Add(new ToolStripMenuItem("Quitter", null, (_, _) => ExitThread()));

        menu.Opening += (_, _) =>
        {
            RefreshMenuChecks();
            if (_startupItem.Enabled) _startupItem.Checked = StartupManager.IsEnabled();
        };
        return menu;
    }

    private void ShowReport()
    {
        try
        {
            if (_reportForm is { IsDisposed: false })
            {
                _reportForm.Activate();
                return;
            }
            _reportForm = new ReportForm(_hardware.GetReport());
            _reportForm.Show();
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    private void KeepOpenOnItemClick(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (_keepMenuOpen && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
    }

    private void AddDisplayOption(string label, Func<bool> get, Action<bool> set)
    {
        var item = new ToolStripMenuItem(label) { Tag = get };
        item.Click += (_, _) =>
        {
            set(!get());
            SaveAndRedraw();
        };
        _displayMenu.DropDownItems.Add(item);
    }

    private void RefreshMenuChecks()
    {
        _showOverlayItem.Checked = _settings.ShowOverlay;
        _autoUpdateItem.Checked = _settings.CheckForUpdates;
        foreach (ToolStripMenuItem item in _intervalMenu.DropDownItems)
            item.Checked = (int)item.Tag! == _settings.IntervalMs;
        foreach (var item in _displayMenu.DropDownItems.OfType<ToolStripMenuItem>())
            item.Checked = ((Func<bool>)item.Tag!)();
        foreach (var item in _positionMenu.DropDownItems.OfType<ToolStripMenuItem>().Where(i => i.Tag is WidgetPosition))
            item.Checked = (WidgetPosition)item.Tag! == _settings.Position;
    }

    private void SaveAndRedraw()
    {
        _settings.Save();
        RefreshMenuChecks();
        _overlay.Redraw();
    }

    private void ToggleOverlay()
    {
        _settings.ShowOverlay = !_settings.ShowOverlay;
        SaveAndRedraw();
    }

    // ---------- Fermeture ----------

    protected override void ExitThreadCore()
    {
        _cts.Cancel();
        _updateTimer.Dispose();
        _reportForm?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _overlay.Close();
        _overlay.Dispose();
        if (_currentIcon is not null)
        {
            DestroyIcon(_currentIcon.Handle);
            _currentIcon.Dispose();
        }
        _hardware.Dispose();
        base.ExitThreadCore();
    }
}
