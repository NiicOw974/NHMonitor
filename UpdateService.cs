using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NHMonitor;

public sealed record UpdateInfo(Version Version, string Tag, string ReleaseUrl, string Notes);

/// <summary>
/// Mises à jour depuis les releases GitHub.
///
/// Sécurité : l'application tourne en administrateur, donc un installeur exécuté par erreur aurait tous
/// les droits sur la machine. Avant tout lancement :
/// 1. SHA256SUMS.txt doit être signé (ECDSA P-256) par la clé privée de l'auteur ; seule la clé publique
///    est intégrée ici. Même un compte GitHub compromis ne peut donc pas publier une mise à jour acceptée ;
/// 2. l'installeur téléchargé doit avoir l'empreinte SHA-256 listée dans ce fichier signé, sous le nom
///    attendu pour cette version (pas de rejeu d'un ancien installeur signé) ;
/// 3. le téléchargement se fait dans Program Files\NHMonitor\updates, modifiable uniquement par un
///    administrateur : le fichier vérifié ne peut pas être échangé avant son exécution.
/// </summary>
public static class UpdateService
{
    public const string Owner = "NiicOw974";
    public const string Repo = "NHMonitor";

    private const long MaxInstallerBytes = 300L * 1024 * 1024;
    private const int MaxSmallFileBytes = 1024 * 1024;

    private static readonly Regex TagPattern = new(@"^v(\d{1,4})\.(\d{1,4})\.(\d{1,4})$", RegexOptions.CultureInvariant);

    // Ordre important : les initialiseurs statiques s'exécutent dans l'ordre de déclaration,
    // et CreateClient() utilise CurrentVersion.
    public static Version CurrentVersion { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Installation automatique possible : version installée dans Program Files et lancée en administrateur.</summary>
    public static bool CanInstallAutomatically =>
        StartupManager.IsInstalledInProtectedLocation()
        && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true, // github.com redirige les téléchargements vers son CDN (HTTPS uniquement).
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NHMonitor", CurrentVersion.ToString(3)));
        return client;
    }

    // ---------- Vérification ----------

    /// <summary>Renvoie la dernière release si elle est plus récente que la version en cours, sinon null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // aucune release publiée
        response.EnsureSuccessStatusCode();

        byte[] json = await ReadLimitedAsync(response, MaxSmallFileBytes, timeout.Token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        var match = TagPattern.Match(tag);
        if (!match.Success) return null;

        var version = new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
        if (version <= CurrentVersion) return null;

        string notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new UpdateInfo(version, tag, $"https://github.com/{Owner}/{Repo}/releases/tag/{tag}", notes);
    }

    // ---------- Téléchargement et installation ----------

    /// <summary>Télécharge et vérifie l'installeur. Renvoie son chemin, prêt à être exécuté.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<int>? progress, CancellationToken ct)
    {
        if (!CanInstallAutomatically)
            throw new InvalidOperationException("Installation automatique indisponible pour cette copie de NHMonitor.");

        string fileName = $"NHMonitor-Setup-{update.Version.ToString(3)}.exe";
        string baseUrl = $"https://github.com/{Owner}/{Repo}/releases/download/{update.Tag}/";

        // 1. Liste des empreintes et sa signature.
        byte[] sums = await DownloadSmallAsync(baseUrl + "SHA256SUMS.txt", ct);
        byte[] signature = await DownloadSmallAsync(baseUrl + "SHA256SUMS.txt.sig", ct);
        string expectedHash = ReleaseSignature.VerifyChecksums(sums, signature, fileName);

        // 2. Installeur, dans un dossier réservé aux administrateurs.
        string dir = Path.Combine(AppContext.BaseDirectory, "updates");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);

        using (var response = await Http.GetAsync(baseUrl + fileName, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            if (total > MaxInstallerBytes) throw new UpdateSecurityException("Installeur anormalement volumineux.");

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, ct)) > 0)
                {
                    read += n;
                    if (read > MaxInstallerBytes) throw new UpdateSecurityException("Installeur anormalement volumineux.");
                    hash.AppendData(buffer, 0, n);
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    if (total > 0) progress?.Report((int)(read * 100 / total.Value));
                }
            }

            string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expectedHash)))
            {
                File.Delete(path);
                throw new UpdateSecurityException("L'empreinte de l'installeur téléchargé ne correspond pas à la release signée.");
            }
        }

        return path;
    }

    /// <summary>Lance l'installeur vérifié. Il ferme NHMonitor, met à jour les fichiers puis relance l'application.</summary>
    public static void LaunchInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /AUTOUPDATE=1")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        });

    /// <summary>
    /// Ouvre la page de la release dans le navigateur. Passer par explorer.exe délègue l'ouverture à
    /// l'Explorateur déjà lancé : le navigateur démarre sans les droits administrateur de NHMonitor.
    /// </summary>
    public static void OpenReleasePage(string url)
    {
        if (!url.StartsWith($"https://github.com/{Owner}/{Repo}/", StringComparison.Ordinal)) return;
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{url}\"")
            {
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    // ---------- Utilitaires ----------

    private static async Task<byte[]> DownloadSmallAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        return await ReadLimitedAsync(response, MaxSmallFileBytes, timeout.Token);
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[16384];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + n > maxBytes) throw new UpdateSecurityException("Réponse du serveur anormalement volumineuse.");
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
