$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$harness = Join-Path $projectRoot 'tests\PatchSmokeTest.cs'
$outputDir = Join-Path $projectRoot 'tests\bin'
$plugin = Join-Path $projectRoot 'src\VeinProtector\bin\Release\VeinProtector.dll'
$gameManaged = Join-Path $projectRoot 'src\VeinProtector\AssemblyFromGame'
$profileCore = Join-Path $projectRoot 'src\VeinProtector\AssemblyFromProfile'
$optionsPatch = Join-Path $projectRoot 'src\VeinProtector\Patches\OptionsProtectionTogglePatch.cs'
$referencePatch = Join-Path $projectRoot 'src\VeinProtector\Patches\ReferenceSpeedDetailExtensionPatch.cs'

if (-not (Test-Path -LiteralPath $plugin -PathType Leaf)) {
    throw "Release DLL not found. Run scripts\build.ps1 first: $plugin"
}
$optionsSource = Get-Content -LiteralPath $optionsPatch -Raw
if (($optionsSource -notmatch 'RowName = "VeinProtector-ProtectionRow"') -or ($optionsSource -notmatch 'ToggleName = "VeinProtector-ProtectionToggle"') -or ($optionsSource -notmatch 'GameTabIndex = 2') -or ($optionsSource -match 'MiscTabIndex|tabTweeners|ResolveMiscContent') -or ($optionsSource -notmatch 'window\.gameScrollContentRect') -or ($optionsSource -notmatch 'Instantiate\(templateRow\.gameObject, targetContent') -or ($optionsSource -notmatch 'FindNamedChild\(window\.transform, RowName\)') -or ($optionsSource -notmatch 'SetAsLastSibling\(\)') -or ($optionsSource -notmatch 'Mathf\.Abs\(row\.anchoredPosition\.y\) \+ row\.rect\.height \+ BottomMargin') -or ($optionsSource -notmatch 'LayoutUtility\.GetPreferredHeight\(content\)') -or ($optionsSource -notmatch 'Mathf\.Max\(content\.rect\.height, content\.sizeDelta\.y, previousHeight') -or ($optionsSource -notmatch 'GetDirectSettingRows\(content, row\)') -or ($optionsSource -notmatch 'GetLocalBounds\(a, content\)') -or ($optionsSource -notmatch 'RemoveAllListeners\(') -or ($optionsSource -match 'FooterName|OptionFooter|FindGameSettingsScroll')) {
    throw "Settings patch must append one native row to the confirmed Game content, place it after active setting rows, and grow the scroll content using the row height and bottom margin."
}
Write-Output 'SettingsGameRowPlacementAndHeightSourceCheckPassed=True'

$pluginSource = Get-Content -LiteralPath (Join-Path $projectRoot 'src\VeinProtector\Plugin.cs') -Raw
$manifestData = Get-Content -LiteralPath (Join-Path $projectRoot 'thunderstore\manifest.json') -Raw | ConvertFrom-Json
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($plugin).Version.ToString()
if (($pluginSource -notmatch 'PluginVersion = "0\.5\.1"') -or ($assemblyVersion -ne '0.5.1.0') -or ($manifestData.version_number -ne '0.5.1')) {
    throw "Plugin, assembly, and manifest versions must all match release 0.5.1."
}
if ((([regex]::Matches($pluginSource, 'Config\.Bind\(')).Count -ne 1) -or
    ($pluginSource -notmatch 'Logger\.LogInfo\("Version = " \+ PluginVersion\)') -or
    ($pluginSource -notmatch 'Logger\.LogInfo\("ProtectionEnabled = " \+ ProtectionEnabled\.Value\)') -or
    ($pluginSource -match 'MinerExecutionDiagnosticsPatch|Protected vein detected|Protected vein skipped successfully|Solid vein miner patch is executing|Reference detail node|hierarchy dump')) {
    throw "Release logging/configuration must contain one persisted protection setting and no development diagnostics."
}
Write-Output 'ReleaseMetadataAndDiagnosticCleanupCheckPassed=True'

$referenceSource = Get-Content -LiteralPath $referencePatch -Raw
if (($referenceSource -notmatch 'tip\.totalSpeedText') -or ($referenceSource -notmatch 'StartCoroutine\(ApplyReferenceDetailLayoutNextFrame') -or ($referenceSource -notmatch 'StopCoroutine\(_detailLayoutCoroutine\)') -or ($referenceSource -notmatch '_layoutGeneration') -or ($referenceSource -notmatch '_baseTableHeaderBounds = GetLocalBounds\(tip\.tableHeaderTrans') -or ($referenceSource -notmatch '_extensionRoot\.sizeDelta = new Vector2\(_baseTableHeaderBounds\.size\.x, AddedHeight\)') -or ($referenceSource -notmatch '_baseTableHeaderPosition\.y - GapAbove') -or ($referenceSource -notmatch 'AddedHeight = 44f') -or ($referenceSource -notmatch 'GapAbove = 2f') -or ($referenceSource -notmatch 'GapBelow = 3f') -or ($referenceSource -notmatch 'state\.BasePosition\.y - _layoutOffset') -or ($referenceSource -match 'ReferenceSpeedTipUpdatePatch|UIReferenceSpeedTip", "_OnUpdate|LogTextState|Reference detail node')) {
    throw "Reference details must clone visible vanilla text, use the 44+2+3 geometry from the vanilla header anchor, use a generation-guarded coroutine, and avoid the nonexistent UIReferenceSpeedTip update hook."
}
Write-Output 'ReferenceTooltipCoroutineSourceCheckPassed=True'

$logicSource = Get-Content -LiteralPath (Join-Path $projectRoot 'src\VeinProtector\Core\VeinProtectionLogic.cs') -Raw
if (($logicSource -notmatch 'LDB\.veins\.Select\(veinTypeId\)') -or ($logicSource -notmatch 'proto\.MiningItem') -or ($logicSource -notmatch 'EVeinType\.Oil') -or ($logicSource -notmatch 'ShouldShowReferenceExtension')) {
    throw "Tooltip item eligibility must be based on LDB.veins MiningItem mapping and exclude oil."
}
if (($referenceSource -notmatch 'CaptureVanillaBaseline\(__instance\)') -or ($referenceSource -notmatch 'state\.Hide\(\)') -or ($referenceSource -notmatch 'RestoreBaseLayout\(\)') -or ($referenceSource -notmatch 'CaptureRect\(tip\.headerTrans, false\)') -or ($referenceSource -notmatch 'CaptureRect\(tip\.tableHeaderTrans, true\)')) {
    throw "Tooltip lifecycle must capture vanilla geometry, hide/restore for ineligible tips, and shift only table content."
}
Write-Output 'ReferenceTooltipEligibilityAndBaselineSourceCheckPassed=True'

Add-Type -Path (Join-Path $profileCore 'Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $plugin).Path)
foreach ($type in $assembly.MainModule.Types) {
    if ($type.FullName -match 'ProductionExtraInfoCalculatorPatch|UIReferenceSpeedTipPatch') {
        throw "Obsolete reference-statistics patch remains in plugin: $($type.FullName)"
    }
    foreach ($method in $type.Methods) {
        if (-not $method.HasBody) { continue }
        foreach ($instruction in $method.Body.Instructions) {
            $called = $instruction.Operand -as [Mono.Cecil.MethodReference]
            if ($called -and $called.Name -match 'AddRef(Product|Consume)Speed|ResetRef(Product|Consume)Speed') {
                throw "VeinProtector still changes vanilla reference statistics: $($type.FullName).$($method.Name) -> $($called.Name)"
            }
        }
    }
}
$assembly.Dispose()
Write-Output 'VanillaReferenceStatisticsUntouchedCheckPassed=True'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$testExe = Join-Path $outputDir 'PatchSmokeTest.exe'
& $compiler /nologo /platform:x64 "/out:$testExe" $harness
if ($LASTEXITCODE -ne 0) {
    throw "Test harness compilation failed with exit code $LASTEXITCODE"
}

& $testExe $plugin $gameManaged $profileCore
if ($LASTEXITCODE -ne 0) {
    throw "Logic or Harmony patch smoke checks failed with exit code $LASTEXITCODE"
}
