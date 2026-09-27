$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginDll = Join-Path $repoRoot 'src\VeinProtector\bin\Release\VeinProtector.dll'
$manifest = Join-Path $repoRoot 'thunderstore\manifest.json'
$readme = Join-Path $repoRoot 'README.md'
$changelog = Join-Path $repoRoot 'CHANGELOG.md'
$license = Join-Path $repoRoot 'LICENSE'
$icon = Join-Path $repoRoot 'thunderstore\icon.png'
$images = Join-Path $repoRoot 'image'
$candidateRoot = Join-Path $repoRoot 'artifacts\release-candidate-final'
$expectedVersion = '0.5.1'
$stageRoot = Join-Path $candidateRoot "staging-$expectedVersion"
$packagePath = Join-Path $candidateRoot "skyedrem-VeinProtector-$expectedVersion.zip"

foreach ($file in @($pluginDll, $manifest, $readme, $changelog, $license, $icon)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required release-candidate file not found: $file"
    }
}

if (-not (Test-Path -LiteralPath $images -PathType Container)) {
    throw "Required release image directory not found: $images"
}
$expectedImages = @('参考面板英文.png', '参考面板中文.png', '设置英文.png', '设置中文.png') | Sort-Object
$actualImages = @(Get-ChildItem -LiteralPath $images -File | Select-Object -ExpandProperty Name | Sort-Object)
if (Compare-Object -ReferenceObject $expectedImages -DifferenceObject $actualImages) {
    throw "Release image directory must contain exactly: $($expectedImages -join ', ')"
}

if (Test-Path -LiteralPath $packagePath) {
    throw "Refusing to overwrite existing release-candidate package: $packagePath"
}

$manifestData = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if ($manifestData.version_number -ne $expectedVersion) {
    throw "Manifest version $($manifestData.version_number) does not match expected $expectedVersion"
}
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($pluginDll).Version.ToString()
if ($assemblyVersion -ne "$expectedVersion.0") {
    throw "Release DLL assembly version $assemblyVersion does not match expected $expectedVersion.0"
}

New-Item -ItemType Directory -Path $candidateRoot -Force | Out-Null
if (Test-Path -LiteralPath $stageRoot) {
    throw "Refusing to overwrite existing staging directory: $stageRoot"
}

$pluginStage = Join-Path $stageRoot 'BepInEx\plugins\VeinProtector'
New-Item -ItemType Directory -Path $pluginStage -Force | Out-Null
Copy-Item -LiteralPath $manifest -Destination (Join-Path $stageRoot 'manifest.json')
Copy-Item -LiteralPath $readme -Destination (Join-Path $stageRoot 'README.md')
Copy-Item -LiteralPath $changelog -Destination (Join-Path $stageRoot 'CHANGELOG.md')
Copy-Item -LiteralPath $license -Destination (Join-Path $stageRoot 'LICENSE')
Copy-Item -LiteralPath $pluginDll -Destination (Join-Path $pluginStage 'VeinProtector.dll')

Add-Type -AssemblyName System.Drawing
$image = [Drawing.Image]::FromFile($icon)
try {
    if ($image.Width -ne 256 -or $image.Height -ne 256 -or $image.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) {
        throw "Thunderstore icon must be a 256x256 PNG: $icon ($($image.Width)x$($image.Height))"
    }
} finally {
    $image.Dispose()
}
Copy-Item -LiteralPath $icon -Destination (Join-Path $stageRoot 'icon.png')

Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $packagePath -CompressionLevel Optimal
$packagedDll = Join-Path $pluginStage 'VeinProtector.dll'
$sourceHash = (Get-FileHash -LiteralPath $pluginDll -Algorithm SHA256).Hash
$packageDllHash = (Get-FileHash -LiteralPath $packagedDll -Algorithm SHA256).Hash
if ($sourceHash -ne $packageDllHash) {
    throw 'Release DLL staging hash differs from the built DLL.'
}

Write-Output "Created release candidate: $packagePath"
Write-Output "AssemblyVersion=$assemblyVersion; DLLBytes=$((Get-Item -LiteralPath $pluginDll).Length); SHA256=$($sourceHash.Substring(0, 16))"
