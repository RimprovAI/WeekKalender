<#
  Bouwt dist\WeekKalender.exe en voert de tests uit.
  Nodig: Windows 10/11 (.NET Framework 4.8 zit erin) en de C#-compiler van Visual Studio (Build Tools).
  Gebruik:  powershell -ExecutionPolicy Bypass -File build.ps1 [-SkipTests]
#>
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Find-Csc {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' 2>$null | Select-Object -First 1
        if ($found -and (Test-Path $found)) { return $found }
    }
    throw 'Geen moderne C#-compiler gevonden. Installeer Visual Studio Build Tools (onderdeel "MSBuild"), of bouw op een pc waar die al staat.'
}

$csc = Find-Csc
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path $fx)) { $fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$wpf = Join-Path $fx 'WPF'

$coreRefs = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'Microsoft.CSharp.dll') | ForEach-Object { "/r:$(Join-Path $fx $_)" }
$appRefs = $coreRefs + @(
    "/r:$(Join-Path $fx 'System.Drawing.dll')",
    "/r:$(Join-Path $fx 'System.Xaml.dll')",
    "/r:$(Join-Path $wpf 'WindowsBase.dll')",
    "/r:$(Join-Path $wpf 'PresentationCore.dll')",
    "/r:$(Join-Path $wpf 'PresentationFramework.dll')",
    "/r:$(Join-Path $wpf 'UIAutomationProvider.dll')",
    "/r:$(Join-Path $wpf 'UIAutomationTypes.dll')"
)
$common = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:latest', '/optimize+', '/deterministic', '/warn:4', '/utf8output', '/platform:anycpu32bitpreferred')

# Alleen-lezen-controle: de Outlook-koppeling mag nooit iets opslaan, verwijderen, versturen of wijzigen.
$forbidden = @(
    '\.(Save|SaveAs|Delete|Move|Send|Copy|ClearRecurrencePattern|Respond|Close)\s*\(',
    '\.(Subject|Body|Start|End|Location|AllDayEvent|BusyStatus|Categories|ReminderSet|Sensitivity|Importance)\s*=[^=]'
)
$hits = Select-String -Path 'src\Providers\OutlookComProvider.cs' -Pattern $forbidden
if ($hits) {
    $hits | ForEach-Object { Write-Host "  $($_.LineNumber): $($_.Line.Trim())" -ForegroundColor Red }
    throw 'Alleen-lezen-controle mislukt: OutlookComProvider bevat een schrijvende aanroep.'
}
Write-Host 'Alleen-lezen-controle: geen schrijvende Outlook-aanroepen gevonden.'

New-Item -ItemType Directory -Force dist, build | Out-Null

$appSources = Get-ChildItem src -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
& $csc @common /target:winexe /win32manifest:src\app.manifest /win32icon:src\app.ico /out:dist\WeekKalender.exe @appRefs @appSources
if ($LASTEXITCODE -ne 0) { throw 'Bouwen van WeekKalender.exe mislukt.' }
Write-Host ("Gebouwd: dist\WeekKalender.exe ({0:N0} KB)" -f ((Get-Item dist\WeekKalender.exe).Length / 1KB))

if (-not $SkipTests) {
    $testSources = @(Get-ChildItem src\Core, src\Providers -Recurse -Filter *.cs) + @(Get-ChildItem tests -Filter *.cs) | ForEach-Object { $_.FullName }
    & $csc @common /target:exe /out:build\WeekKalender.Tests.exe @coreRefs @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Bouwen van de tests mislukt.' }
    & build\WeekKalender.Tests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Er zijn tests mislukt.' }
}
