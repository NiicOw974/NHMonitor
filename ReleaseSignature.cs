using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NHMonitor;

/// <summary>Une mise à jour a été refusée pour une raison de sécurité (signature, empreinte...).</summary>
public sealed class UpdateSecurityException(string message) : Exception(message);

/// <summary>Vérification de la signature des releases (sans dépendance, testable isolément).</summary>
public static class ReleaseSignature
{
    // Clé publique de signature des releases (copie : assets\release-signing-public.pem).
    // La clé privée n'est jamais dans le dépôt.
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEXeyM5gn8ttFvWKwWq7ffmHFWNHz+
        S6nchdAqZOSclc9rp2cae7SdFSVQdto0ZWn254tCJm4972/o9csOxJEN8Q==
        -----END PUBLIC KEY-----
        """;

    /// <summary>Vérifie la signature de SHA256SUMS.txt et renvoie l'empreinte attendue pour <paramref name="fileName"/>.</summary>
    public static string VerifyChecksums(byte[] sums, byte[] signatureFile, string fileName)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(Encoding.ASCII.GetString(signatureFile).Trim());
        }
        catch (FormatException)
        {
            throw new UpdateSecurityException("Signature de la release illisible.");
        }

        using (var ecdsa = ECDsa.Create())
        {
            ecdsa.ImportFromPem(PublicKeyPem);
            // Format DER, celui produit par "openssl dgst -sha256 -sign" (build\build-release.ps1).
            if (!ecdsa.VerifyData(sums, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                throw new UpdateSecurityException("Signature de la release invalide : mise à jour refusée.");
        }

        foreach (string line in Encoding.ASCII.GetString(sums).Split('\n'))
        {
            string[] parts = line.Trim().Split("  ", 2, StringSplitOptions.None);
            if (parts.Length == 2 && parts[1] == fileName && Regex.IsMatch(parts[0], "^[0-9a-f]{64}$"))
                return parts[0];
        }
        throw new UpdateSecurityException($"{fileName} est absent de la liste signée.");
    }
}
