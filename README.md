# NHMonitor

Moniteur léger pour Windows 10 / 11 qui affiche directement **dans la barre des tâches** :

- **CPU** : utilisation (%) et température (°C)
- **GPU** : utilisation (%) et température (°C), en privilégiant le GPU dédié (NVIDIA / AMD), sinon le GPU intégré Intel
- **RAM** : utilisation (%) et mémoire utilisée (Go)
- **Réseau** : débit entrant (↓, flèche bleue) et sortant (↑, flèche verte) de la carte active

```
CPU  23 %  │  GPU  10 %  │  RAM  62 %  │  NET ↓  1.2 Mo/s
     54 °C │       45 °C │     9.9 Go  │      ↑   85 Ko/s
```

Gratuit, open source, sans publicité, sans télémétrie : NHMonitor n'établit **aucune connexion réseau**.

## Installation

1. Téléchargez **`NHMonitor-Setup-x.y.z.exe`** depuis la [dernière release](https://github.com/NiicOw974/NHMonitor/releases/latest).
2. Lancez-le. Si Windows SmartScreen affiche « Windows a protégé votre ordinateur », cliquez sur **Informations complémentaires**, puis **Exécuter quand même**. L'installeur n'est pas encore signé numériquement ; vous pouvez vérifier son empreinte avec `SHA256SUMS.txt` (voir [SECURITY.md](SECURITY.md)).
3. Gardez les options cochées :
   - **Démarrer NHMonitor avec Windows** ;
   - **Installer le driver PawnIO** : nécessaire pour les températures CPU. Il est téléchargé depuis sa source officielle et vérifié avant installation.

Le runtime .NET est inclus : il n'y a rien d'autre à installer. Pour désinstaller : **Paramètres > Applications > NHMonitor**.

Une **version portable** (`.zip`) est aussi disponible. Elle n'a pas de démarrage automatique, car celui-ci n'est autorisé que pour la version installée dans Program Files (voir [SECURITY.md](SECURITY.md)).

## Utilisation

- **Glisser** le widget avec le clic gauche pour l'ajuster le long de la barre. Le décalage est mémorisé.
- **Double-clic** sur le widget : ouvre le Gestionnaire des tâches.
- **Survol** : détail complet (modèle du CPU et du GPU, mémoire totale, nom de la carte réseau).
- **Clic gauche** sur l'icône de notification : affiche ou masque le widget.
- **Clic droit** (widget ou icône) :
  - **Éléments affichés** : chaque valeur se coche séparément (utilisation et température CPU/GPU, RAM en % et en Go, débits réseau) ;
  - **Position** : à gauche de la barre, ou à droite près des icônes de notification ;
  - **Fréquence de mise à jour** : de 0,5 s à 5 s ;
  - **Démarrer avec Windows** ;
  - **Rapport des capteurs** : tout ce que LibreHardwareMonitor détecte, utile pour un signalement de bug.

Le widget suit le thème clair ou sombre et la mise à l'échelle de l'écran. Il se masque automatiquement pendant une application en plein écran (jeu, vidéo, présentation). Les valeurs passent en orange puis en rouge quand elles deviennent élevées : charge ≥ 70 % / 90 %, température ≥ 70 °C / 85 °C.

### Bon à savoir

- **Températures CPU à `--`** : le driver PawnIO n'est pas installé. Lancez `winget install namazso.PawnIO`, puis redémarrez NHMonitor.
- **Pas de température GPU** : les GPU intégrés Intel (UHD, Iris) n'ont pas de capteur. Seule l'utilisation est alors affichée.
- **Carte réseau suivie** : celle qui est connectée et possède une passerelle par défaut. Si plusieurs cartes en ont une, le filaire passe avant le Wi-Fi.
- **Droits administrateur** : nécessaires pour lire les températures. Une seule demande UAC à l'installation ; ensuite, le démarrage automatique n'en demande plus. Sur un compte standard, NHMonitor fonctionne sans les températures.
- **Ressources** : environ 1 % d'un cœur CPU et 17 Mo de mémoire privée, avec une mise à jour par seconde.

## Sécurité

Voir [SECURITY.md](SECURITY.md) : ce que fait l'application sur le système, les mesures de durcissement et comment signaler une vulnérabilité.

## Compiler depuis les sources

Prérequis : [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0), et [Inno Setup 6](https://jrsoftware.org/isinfo.php) pour l'installeur.

```powershell
dotnet build -c Release                       # compilation simple
.\build\build-release.ps1 -Version 1.0.0      # installeur + zip portable + SHA256SUMS dans dist\
```

Les releases officielles sont construites par GitHub Actions ([.github/workflows/release.yml](.github/workflows/release.yml)) lors de la création d'un tag `vX.Y.Z`.

| Fichier | Rôle |
|---|---|
| `HardwareService.cs` | Lecture des capteurs (LibreHardwareMonitor pour CPU/GPU, `GlobalMemoryStatusEx` pour la RAM) |
| `NetworkMonitor.cs` | Choix de la carte réseau active et calcul des débits |
| `TaskbarOverlay.cs` | Widget transparent dans la barre des tâches (fenêtre « layered » au premier plan) |
| `TrayApp.cs` | Icône de notification, menu et boucle de mesure |
| `StartupManager.cs` | Démarrage automatique via l'API du Planificateur de tâches |
| `ReportForm.cs` | Fenêtre du rapport des capteurs |
| `Settings.cs` | Préférences dans `%APPDATA%\NHMonitor\settings.json` |
| `installer/NHMonitor.iss` | Script de l'installeur Inno Setup |

Ligne de commande : `NHMonitor.exe --dump rapport.txt` (rapport des capteurs), `--enable-startup` / `--disable-startup` (utilisés par l'installeur).

## Licence

[MIT](LICENSE). Composants tiers : voir [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Ils incluent notamment LibreHardwareMonitor (MPL-2.0).
