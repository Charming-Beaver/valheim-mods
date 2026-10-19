<#
.SYNOPSIS
    Builds a mod and its in-game tests, starts Valheim, waits for the tests to finish and prints the results.

.DESCRIPTION
    The tests run inside the game: tests\<Mod>.Tests is a BepInEx plugin that loads a test character and world
    (both called "modtest", created on the first run), runs every test and quits the game. Steam must be running.

    -With runs the tests alongside other people's mods from Thunderstore, to check compatibility. They're downloaded
    with their dependencies into tests\.mods (once, ignored by git) and installed only for this run.

    Screenshots taken by the tests end up in BepInEx\<Mod>.Tests.screenshots in the Valheim folder.
    The test plugin and any -With mods are removed again afterwards, so normal games aren't affected.

.EXAMPLE
    .\tests\run-tests.ps1 StorageMaster

.EXAMPLE
    .\tests\run-tests.ps1 StorageMaster -With seneaL-SeneaL_UI

.EXAMPLE
    .\tests\run-tests.ps1 StorageMaster -Play
    Loads the test world with a few chests and leaves the game to you instead of running the tests. The test plugin
    and any -With mods are removed when you close the game.
#>
param(
    [string]$Mod = 'StorageMaster',
    # Thunderstore packages as Namespace-Name, optionally with -Version (latest when left out).
    [string[]]$With = @(),
    # Only load the test world (with a few chests), for trying things by hand. Waits until the game is closed.
    [switch]$Play,
    # Found through Steam when left out.
    [string]$ValheimDir,
    [int]$TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'
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

# Downloads a Thunderstore package and its dependencies into tests\.mods; returns their folders.
# BepInExPack is skipped (it's already installed), and dependencies use their latest version.
function Get-ThunderstorePackage([string]$spec, [hashtable]$seen) {
    if ($spec -notmatch '^([^-]+)-(.+?)(?:-(\d+\.\d+\.\d+))?$') { throw "Not a Thunderstore package (Namespace-Name[-Version]): $spec" }
    $namespace, $name, $version = $Matches[1], $Matches[2], $Matches[3]
    $key = "$namespace-$name"
    if ($seen.ContainsKey($key) -or $key -eq 'denikson-BepInExPack_Valheim') { return @() }
    $seen[$key] = $true

    if (-not $version) {
        $info = Invoke-RestMethod "https://thunderstore.io/api/experimental/package/$namespace/$name/"
        $version = $info.latest.version_number
    }
    $dir = Join-Path $PSScriptRoot ".mods\$key-$version"
    if (-not (Test-Path (Join-Path $dir 'manifest.json'))) {
        Write-Host "Downloading $key $version"
        $zip = "$dir.zip"
        New-Item -ItemType Directory -Force (Split-Path $dir) | Out-Null
        Invoke-WebRequest "https://thunderstore.io/package/download/$namespace/$name/$version/" -OutFile $zip
        if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
        Expand-Archive $zip $dir
        Remove-Item $zip
    }
    $manifest = Get-Content (Join-Path $dir 'manifest.json') -Raw | ConvertFrom-Json
    $folders = @()
    foreach ($dependency in $manifest.dependencies) {
        $folders += Get-ThunderstorePackage ($dependency -replace '-\d+\.\d+\.\d+$', '') $seen
    }
    return $folders + @($dir)
}

# Installs an unpacked Thunderstore package the way mod managers do: plugins\ and patchers\ into BepInEx's folders,
# config\ into BepInEx\config, and a package without those folders straight into plugins.
function Install-Package([string]$dir, [string]$pluginsRoot, [string]$patchersRoot, [string]$configDir) {
    $name = Split-Path $dir -Leaf
    $layout = @{ plugins = $pluginsRoot; patchers = $patchersRoot; config = $configDir }
    $special = $false
    foreach ($folder in $layout.Keys) {
        $source = Get-ChildItem $dir -Directory | Where-Object { $_.Name -eq $folder }
        if (-not $source) { continue }
        $special = $true
        $target = if ($folder -eq 'config') { $configDir } else { Join-Path $layout[$folder] $name }
        New-Item -ItemType Directory -Force $target | Out-Null
        Copy-Item (Join-Path $source.FullName '*') $target -Recurse -Force
    }
    if (-not $special) {
        $target = Join-Path $pluginsRoot $name
        New-Item -ItemType Directory -Force $target | Out-Null
        Get-ChildItem $dir | Where-Object { $_.Name -notin 'manifest.json', 'icon.png', 'README.md', 'CHANGELOG.md' } |
            Copy-Item -Destination $target -Recurse -Force
    }
}

if (-not $ValheimDir) { $ValheimDir = Find-Valheim }
$bepinex = Join-Path $ValheimDir 'BepInEx'
if (-not (Test-Path (Join-Path $bepinex 'core\BepInEx.dll'))) { throw "BepInEx isn't installed in $ValheimDir" }
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Valheim is already running. Close it first.' }

$packages = @()
$seen = @{}
foreach ($spec in $With) { $packages += Get-ThunderstorePackage $spec $seen }

dotnet build $project -c Debug --nologo -v quiet "-p:ValheimDir=$ValheimDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$configDir = Join-Path $bepinex 'config'
$pluginDir = Join-Path $bepinex "plugins\$Mod.Tests"
$testModPlugins = Join-Path $bepinex 'plugins\_test-mods'
$testModPatchers = Join-Path $bepinex 'patchers\_test-mods'
$marker = Join-Path $configDir "$Mod.Tests.run"
$results = Join-Path $bepinex "$Mod.Tests.results.txt"
$screenshots = Join-Path $bepinex "$Mod.Tests.screenshots"
# Mods write their config files on start; the ones that weren't there before are removed afterwards.
$configsBefore = @(Get-ChildItem $configDir -File -Recurse | ForEach-Object FullName)

New-Item -ItemType Directory -Force $pluginDir | Out-Null
Copy-Item (Join-Path $PSScriptRoot "$Mod.Tests\bin\Debug\netstandard2.1\$Mod.Tests.dll") $pluginDir
foreach ($package in $packages) {
    Write-Host "With $(Split-Path $package -Leaf)"
    Install-Package $package $testModPlugins $testModPatchers $configDir
}
if (Test-Path $results) { Remove-Item $results }
if (Test-Path $screenshots) { Remove-Item -Recurse $screenshots }
Set-Content -Path $marker -Value $(if ($Play) { 'play' } else { 'Run the tests on the next start' }) -Encoding utf8

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
                $color = switch -Regex ($line) { '^PASS' { 'Green' } '^(FAIL|ABORT)' { 'Red' } '^SKIP' { 'Yellow' } default { 'Gray' } }
                Write-Host $line -ForegroundColor $color
                if ($line -match '^(DONE|ABORT|READY)') { $done = $line }
            }
        }
        if ($done) { break }
        if (Get-Process valheim -ErrorAction SilentlyContinue) { $lastSeen = Get-Date }
        elseif (((Get-Date) - $lastSeen).TotalSeconds -gt 30) { throw 'Valheim closed before the tests finished. See BepInEx\LogOutput.log.' }
        if ((Get-Date) -gt $deadline) { throw "The tests didn't finish within $TimeoutMinutes minutes." }
    }
    if ($done -match '^READY') {
        Write-Host 'Close Valheim when you are done; the test plugin is removed then.'
        while (Get-Process valheim -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 2 }
    }
    $failed = $done -notmatch '^(DONE \d+ passed, 0 failed|READY)'
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
    Remove-Item -Recurse -Force $pluginDir, $testModPlugins, $testModPatchers -ErrorAction SilentlyContinue
    Remove-Item -Force $marker -ErrorAction SilentlyContinue
    Get-ChildItem $configDir -File -Recurse | Where-Object { $_.FullName -notin $configsBefore } | Remove-Item -Force
}
if ($failed) { exit 1 }
