$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\VeinProtector\VeinProtector.csproj'
$msbuild = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'

if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) {
    throw "MSBuild.exe not found: $msbuild"
}

& $msbuild $project /t:Rebuild /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE"
}

Write-Output (Join-Path $projectRoot 'src\VeinProtector\bin\Release\VeinProtector.dll')
