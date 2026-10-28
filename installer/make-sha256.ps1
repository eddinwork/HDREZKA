# Computes SHA-256 for the built installer and writes
# HDREZKA-Setup-X.Y.Z.exe.sha256 next to it. The app downloads this sidecar
# during in-app update and compares hashes, so an unsigned installer can be
# verified without a manual confirmation prompt.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File installer/make-sha256.ps1
#   powershell -File installer/make-sha256.ps1 -Setup "C:\path\HDREZKA-Setup-1.8.0.exe"
#
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 reads .ps1 as ANSI
# when there is no BOM, and non-ASCII text breaks parsing.

param(
    [string]$Setup = ""
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $Setup) {
    $Setup = Get-ChildItem -LiteralPath (Join-Path $here 'Output') -Filter 'HDREZKA-Setup-*.exe' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $Setup -or -not (Test-Path -LiteralPath $Setup)) {
    throw "Installer not found. Build it with ISCC first."
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Setup).Hash.ToLowerInvariant()
$name = Split-Path -Leaf $Setup
$out = "$Setup.sha256"

"$hash  $name" | Set-Content -LiteralPath $out -Encoding ASCII

Write-Host "SHA-256: $hash"
Write-Host "Written: $out"
Write-Host "Upload it next to the installer in the GitHub Release."
