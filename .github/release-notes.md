## Installation

1. Téléchargez **`NHMonitor-Setup-x.y.z.exe`** ci-dessous et lancez-le.
2. Windows SmartScreen peut afficher « Windows a protégé votre ordinateur » : l'installeur n'est pas encore signé numériquement. Cliquez sur **Informations complémentaires**, puis **Exécuter quand même**.
3. Laissez cochée l'option **PawnIO** pour avoir les températures CPU.

Version portable (sans installation, démarrage ni mise à jour automatiques) : `NHMonitor-x.y.z-portable-win-x64.zip`.

Déjà installé ? NHMonitor vous propose cette version automatiquement (clic droit > **Rechercher les mises à jour**).

## Vérifier le téléchargement

Comparez l'empreinte du fichier avec celle de `SHA256SUMS.txt` :

```powershell
Get-FileHash .\NHMonitor-Setup-x.y.z.exe -Algorithm SHA256
```

`SHA256SUMS.txt.sig` est la signature ECDSA de `SHA256SUMS.txt` ; la clé publique est `assets/release-signing-public.pem` dans le dépôt. Ces fichiers sont construits automatiquement par GitHub Actions à partir du code source de ce tag.
