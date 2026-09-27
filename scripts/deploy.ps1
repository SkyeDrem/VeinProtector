param(
    [string]$ProfileName = 'vein'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginDll = Join-Path $repoRoot 'src\VeinProtector\bin\Release\VeinProtector.dll'
$profilesRoot = Join-Path $env:APPDATA 'r2modmanPlus-local\DysonSphereProgram\profiles'

if (-not (Test-Path -LiteralPath $pluginDll -PathType Leaf)) {
    throw "Release DLL not found: $pluginDll. Build the project first."
}

if (-not (Test-Path -LiteralPath $profilesRoot -PathType Container)) {
    throw "DSP r2modman profiles directory not found: $profilesRoot"
}

$profiles = @(Get-ChildItem -LiteralPath $profilesRoot -Directory | Where-Object { $_.Name -eq $ProfileName })
if ($profiles.Count -ne 1) {
    $found = (Get-ChildItem -LiteralPath $profilesRoot -Directory | ForEach-Object { $_.Name }) -join ', '
    throw "Could not identify profile '$ProfileName'. Available profiles: $found"
}
$ProfilePath = $profiles[0].FullName

$profileFullPath = [IO.Path]::GetFullPath($ProfilePath)
$profilesFullPath = [IO.Path]::GetFullPath($profilesRoot).TrimEnd('\') + '\'
if (-not $profileFullPath.StartsWith($profilesFullPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to deploy outside the DSP r2modman profiles directory: $profileFullPath"
}

$bepInExPath = Join-Path $profileFullPath 'BepInEx'
$pluginsPath = Join-Path $bepInExPath 'plugins'
if (-not (Test-Path -LiteralPath (Join-Path $bepInExPath 'core\BepInEx.dll') -PathType Leaf)) {
    throw "Selected profile does not contain BepInEx: $profileFullPath"
}
if (-not (Test-Path -LiteralPath $pluginsPath -PathType Container)) {
    throw "Selected profile does not contain BepInEx\plugins: $profileFullPath"
}

$targetDir = Join-Path $pluginsPath 'VeinProtector'
$targetDirFull = [IO.Path]::GetFullPath($targetDir).TrimEnd('\') + '\'
$existingCopies = @(Get-ChildItem -LiteralPath $pluginsPath -Filter 'VeinProtector.dll' -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { -not $_.FullName.StartsWith($targetDirFull, [StringComparison]::OrdinalIgnoreCase) })
if ($existingCopies.Count -gt 0) {
    $paths = ($existingCopies | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
    throw "Found other VeinProtector DLL copies in profile '$ProfileName'. Remove the duplicate package/install first:`n$paths"
}
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
$targetDll = Join-Path $targetDir 'VeinProtector.dll'
Copy-Item -LiteralPath $pluginDll -Destination $targetDll -Force
$sourceHash = (Get-FileHash -LiteralPath $pluginDll -Algorithm SHA256).Hash
$targetHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
if ($sourceHash -ne $targetHash) {
    throw "Deployment hash mismatch for profile '$ProfileName': $targetDll"
}
Write-Output "Deployed VeinProtector $($sourceHash.Substring(0, 12)) to profile '$ProfileName': $targetDll"
