<#
.SYNOPSIS
    Builds a mod and its in-game tests, starts Valheim, waits for the tests to finish and prints the results.

.DESCRIPTION
    The tests run inside the game: tests\<Mod>.Tests is a BepInEx plugin that loads a test character and world
    (both called "modtest", created on the first run), runs every test and quits the game. Steam must be running.

    Screenshots taken by the tests end up in BepInEx\<Mod>.Tests.screenshots in the Valheim folder.
    The test plugin is removed again afterwards, so normal games aren't affected.

.EXAMPLE
    .\tests\run-tests.ps1 StorageMaster
#>
param(
    [string]$Mod = 'StorageMaster',
    # Found through Steam when left out.
    [string]$ValheimDir,
    [int]$TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $PSScriptRoot "$Mod.Tests\$Mod.Tests.csproj"
if (-not (Test-Path $project)) { throw "No test project at $project" }

function Find-Valheim {
    $steam = (Get-ItemProperty HKCU:\Software\Valve\Steam -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { $steam = "${env:ProgramFiles(x86)}\Steam" }
    $libraries = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $libraries += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
    }
    foreach ($lib in $libraries) {
        $dir = Join-Path $lib 'steamapps\common\Valheim'
        if (Test-Path (Join-Path $dir 'valheim.exe')) { return $dir }
    }
    throw 'Could not find Valheim through Steam. Pass -ValheimDir "C:\path\to\Valheim".'
}

if (-not $ValheimDir) { $ValheimDir = Find-Valheim }
$bepinex = Join-Path $ValheimDir 'BepInEx'
if (-not (Test-Path (Join-Path $bepinex 'core\BepInEx.dll'))) { throw "BepInEx isn't installed in $ValheimDir" }
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Valheim is already running. Close it first.' }

dotnet build $project -c Debug --nologo -v quiet "-p:ValheimDir=$ValheimDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$pluginDir = Join-Path $bepinex "plugins\$Mod.Tests"
$marker = Join-Path $bepinex "config\$Mod.Tests.run"
$results = Join-Path $bepinex "$Mod.Tests.results.txt"
$screenshots = Join-Path $bepinex "$Mod.Tests.screenshots"
New-Item -ItemType Directory -Force $pluginDir | Out-Null
Copy-Item (Join-Path $PSScriptRoot "$Mod.Tests\bin\Debug\netstandard2.1\$Mod.Tests.dll") $pluginDir
if (Test-Path $results) { Remove-Item $results }
if (Test-Path $screenshots) { Remove-Item -Recurse $screenshots }
Set-Content -Path $marker -Value 'Run the tests on the next start' -Encoding utf8

$failed = $true
try {
    Write-Host "Starting Valheim from $ValheimDir"
    Start-Process (Join-Path $ValheimDir 'valheim.exe') -WorkingDirectory $ValheimDir | Out-Null

    # Steam may restart the game as a new process, so watch for any valheim process rather than the one started.
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    $lastSeen = Get-Date
    $shown = 0
    $done = $null
    while (-not $done) {
        Start-Sleep -Seconds 2
        if (Test-Path $results) {
            $lines = @(Get-Content $results)
            for (; $shown -lt $lines.Count; $shown++) {
                $line = $lines[$shown]
                $color = if ($line -match '^PASS') { 'Green' } elseif ($line -match '^(FAIL|ABORT)') { 'Red' } else { 'Gray' }
                Write-Host $line -ForegroundColor $color
                if ($line -match '^(DONE|ABORT)') { $done = $line }
            }
        }
        if ($done) { break }
        if (Get-Process valheim -ErrorAction SilentlyContinue) { $lastSeen = Get-Date }
        elseif (((Get-Date) - $lastSeen).TotalSeconds -gt 30) { throw 'Valheim closed before the tests finished. See BepInEx\LogOutput.log.' }
        if ((Get-Date) -gt $deadline) { throw "The tests didn't finish within $TimeoutMinutes minutes." }
    }
    $failed = $done -notmatch '^DONE \d+ passed, 0 failed'
    if (Test-Path $screenshots) { Write-Host "Screenshots: $screenshots" }
}
finally {
    if ($failed) {
        $log = Join-Path $bepinex 'LogOutput.log'
        if (Test-Path $log) {
            Write-Host "`nErrors in LogOutput.log:" -ForegroundColor Yellow
            Select-String -Path $log -Pattern '^\[(Error|Fatal)' -Context 0, 4 | Select-Object -Last 10 | ForEach-Object { Write-Host $_ }
        }
    }
    # The game quits by itself after the tests; give it a moment to save, then make sure it's gone.
    for ($i = 0; $i -lt 30 -and (Get-Process valheim -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 1 }
    Get-Process valheim -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item -Recurse -Force $pluginDir -ErrorAction SilentlyContinue
    Remove-Item -Force $marker -ErrorAction SilentlyContinue
}
if ($failed) { exit 1 }
