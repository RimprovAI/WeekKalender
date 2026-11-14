<#
  Bouwt dist\WeekKalender.exe en voert de tests uit.
  Nodig: Windows 10/11 (.NET Framework 4.8 zit erin) en de C#-compiler van Visual Studio (Build Tools).
  Gebruik:  powershell -ExecutionPolicy Bypass -File build.ps1 [-SkipTests]

  dist\WeekKalender.exe is ook het programma dat je dagelijks gebruikt (autostart en Startmenu wijzen ernaar).
  Draait het, dan stopt dit script het even en start het na afloop weer.
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

$coreRefs = @('mscorlib.dll', 'System.dll', 'System.Core.dll') | ForEach-Object { "/r:$(Join-Path $fx $_)" }
$appRefs = $coreRefs + @(
    "/r:$(Join-Path $fx 'System.Drawing.dll')",
    "/r:$(Join-Path $fx 'System.Xaml.dll')",
    "/r:$(Join-Path $wpf 'WindowsBase.dll')",
    "/r:$(Join-Path $wpf 'PresentationCore.dll')",
    "/r:$(Join-Path $wpf 'PresentationFramework.dll')",
    "/r:$(Join-Path $wpf 'UIAutomationProvider.dll')",
    "/r:$(Join-Path $wpf 'UIAutomationTypes.dll')"
)
$common = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:latest', '/optimize+', '/deterministic', '/warn:4', '/warnaserror+', '/utf8output', '/platform:anycpu32bitpreferred')

# Alleen-lezen-controle. Outlook wordt uitsluitend aangeroepen via OutlookCom.cs, dat alleen een vaste lijst
# lees- en zoekacties doorlaat. Hier wordt bewaakt dat niemand daar omheen kan:
#  - nergens 'dynamic' (late binding zonder lijst) en nergens InvokeMember buiten OutlookCom.cs;
#  - de lijst zelf bevat geen schrijvende namen.
$sources = Get-ChildItem src -Recurse -Filter *.cs
$bypass = @($sources | Select-String -Pattern '\bdynamic\b' -CaseSensitive) +
          @($sources | Where-Object { $_.Name -ne 'OutlookCom.cs' } | Select-String -Pattern 'InvokeMember|GetActiveObject\(|CreateInstance\(' | Where-Object { $_.Path -notlike '*OutlookComProvider.cs' -or $_.Line -match 'InvokeMember' })
$writers = @(Select-String -Path 'src\Providers\OutlookCom.cs' -Pattern '"(Save|SaveAs|Delete|Move|Send|Copy|Close|Respond|Forward|Reply|ClearRecurrencePattern|Quit|Body|Categories)"')
if ($bypass.Count -gt 0 -or $writers.Count -gt 0) {
    @($bypass) + @($writers) | ForEach-Object { Write-Host "  $($_.Filename):$($_.LineNumber): $($_.Line.Trim())" -ForegroundColor Red }
    throw 'Alleen-lezen-controle mislukt.'
}
Write-Host 'Alleen-lezen-controle: Outlook is alleen bereikbaar via de vaste leeslijst.'

New-Item -ItemType Directory -Force dist, build | Out-Null
$exe = Join-Path $PSScriptRoot 'dist\WeekKalender.exe'

# Een draaiend programma houdt het exe-bestand vast: netjes laten stoppen en straks weer starten.
$running = @(Get-Process WeekKalender -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ([IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($exe)) })
if ($running.Count -gt 0) {
    Write-Host 'WeekKalender draait; het wordt even gestopt voor het bouwen.'
    Start-Process $exe -ArgumentList '--afsluiten'
    foreach ($p in $running) {
        if (-not $p.WaitForExit(8000)) { throw 'De draaiende WeekKalender stopt niet. Sluit hem af via het menu van het icoon en bouw opnieuw.' }
    }
}

try {
    $appSources = Get-ChildItem src -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
    & $csc @common /target:winexe /win32manifest:src\app.manifest /win32icon:src\app.ico /out:dist\WeekKalender.exe @appRefs @appSources
    if ($LASTEXITCODE -ne 0) { throw 'Bouwen van WeekKalender.exe mislukt.' }
    Write-Host ("Gebouwd: dist\WeekKalender.exe ({0:N0} KB)" -f ((Get-Item dist\WeekKalender.exe).Length / 1KB))
    $hash = (Get-FileHash dist\WeekKalender.exe -Algorithm SHA256).Hash
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'dist\SHA256.txt'), "$hash  WeekKalender.exe`r`n")
    Write-Host "SHA-256: $hash"

    if (-not $SkipTests) {
        $testSources = @(Get-ChildItem src\Core, src\Providers -Recurse -Filter *.cs) + @(Get-ChildItem tests -Filter *.cs) | ForEach-Object { $_.FullName }
        & $csc @common /target:exe /out:build\WeekKalender.Tests.exe @coreRefs @testSources
        if ($LASTEXITCODE -ne 0) { throw 'Bouwen van de tests mislukt.' }
        & build\WeekKalender.Tests.exe
        if ($LASTEXITCODE -ne 0) { throw 'Er zijn tests mislukt.' }
    }
}
finally {
    if ($running.Count -gt 0 -and (Test-Path $exe)) {
        # Via Verkenner starten, zodat het programma als gewone gebruikerstoepassing draait, ook als dit script
        # vanuit een afgeschermde omgeving (zoals een AI-assistent) wordt uitgevoerd.
        Start-Process explorer.exe -ArgumentList "`"$exe`""
        Write-Host 'WeekKalender is opnieuw gestart.'
    }
}
