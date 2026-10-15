# Construit les fichiers de release dans dist\ :
#   NHMonitor-Setup-<version>.exe          installeur (Program Files, demarrage auto, PawnIO optionnel)
#   NHMonitor-<version>-portable-win-x64.zip  version portable (sans demarrage automatique)
#   SHA256SUMS.txt                          empreintes pour verifier les telechargements
#
# Usage : powershell -ExecutionPolicy Bypass -File build\build-release.ps1 [-Version 1.0.0]
# Prerequis : SDK .NET 10, Inno Setup 6 (winget install JRSoftware.InnoSetup)
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $Version = ([xml](Get-Content "$root\NHMonitor.csproj" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version invalide : '$Version' (attendu : X.Y.Z)" }
Write-Host "== NHMonitor $Version =="

$publish = Join-Path $root 'publish'
$dist = Join-Path $root 'dist'
foreach ($d in $publish, $dist) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory $d | Out-Null
}

# 1. Publication autonome (runtime .NET inclus).
dotnet publish "$root\NHMonitor.csproj" -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a echoue" }

# 2. Version portable.
$zip = Join-Path $dist "NHMonitor-$Version-portable-win-x64.zip"
Copy-Item "$root\LICENSE", "$root\THIRD-PARTY-NOTICES.md" $publish
Compress-Archive -Path "$publish\*" -DestinationPath $zip -CompressionLevel Optimal

# 3. Installeur.
$iscc = @(
    $env:ISCC,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 introuvable (winget install JRSoftware.InnoSetup)" }

& $iscc "/DAppVersion=$Version" "/DSourceDir=$publish" "$root\installer\NHMonitor.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC a echoue" }

# 4. Empreintes SHA-256.
Get-ChildItem $dist -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content "$dist\SHA256SUMS.txt" -Encoding ascii

Write-Host ""
Get-ChildItem $dist | Format-Table Name, @{n='Taille (Mo)'; e={ '{0:N1}' -f ($_.Length / 1MB) }} -AutoSize
