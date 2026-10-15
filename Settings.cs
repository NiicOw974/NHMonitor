using System.Text.Json;
using System.Text.Json.Serialization;

namespace NHMonitor;

public enum WidgetPosition { Left, Right }

public sealed class Settings
{
    public bool ShowOverlay { get; set; } = true;
    public int IntervalMs { get; set; } = 1000;

    [JsonConverter(typeof(JsonStringEnumConverter<WidgetPosition>))]
    public WidgetPosition Position { get; set; } = WidgetPosition.Right;

    /// <summary>Position droite : décalage (px logiques, 96 dpi) vers la gauche depuis la zone de notification. 0 = collé aux icônes.</summary>
    public int OffsetFromTray { get; set; }

    /// <summary>Position gauche : décalage (px logiques, 96 dpi) vers la droite depuis le bord gauche de la barre des tâches.</summary>
    public int OffsetFromLeft { get; set; }

    // Éléments affichés dans le widget.
    public bool ShowCpuLoad { get; set; } = true;
    public bool ShowCpuTemp { get; set; } = true;
    public bool ShowGpuLoad { get; set; } = true;
    public bool ShowGpuTemp { get; set; } = true;
    public bool ShowRamLoad { get; set; } = true;
    public bool ShowRamUsed { get; set; } = true;
    public bool ShowNetDown { get; set; } = true;
    public bool ShowNetUp { get; set; } = true;

    // Mises à jour : une vérification au plus toutes les 24 h (une requête vers api.github.com).
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NHMonitor", "settings.json");

    public static Settings Load()
    {
        try
        {
            // Fichier anormalement gros : on l'ignore plutôt que de le charger en mémoire.
            var info = new FileInfo(FilePath);
            if (info.Exists && info.Length < 64 * 1024)
                return (JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings()).Sanitized();
        }
        catch
        {
            // Fichier corrompu : on repart des valeurs par défaut.
        }
        return new Settings();
    }

    /// <summary>Le fichier est modifiable à la main : on ramène chaque valeur dans une plage sûre.</summary>
    private Settings Sanitized()
    {
        if (IntervalMs is not (500 or 1000 or 2000 or 5000)) IntervalMs = 1000;
        if (!Enum.IsDefined(Position)) Position = WidgetPosition.Right;
        OffsetFromTray = Math.Clamp(OffsetFromTray, 0, 8000);
        OffsetFromLeft = Math.Clamp(OffsetFromLeft, 0, 8000);
        return this;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Non bloquant.
        }
    }
}
