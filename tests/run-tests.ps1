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

    -Hosting starts the test world as a player hosting it ("Start server" ticked, not listed publicly).
    -Server starts the Valheim dedicated server (from Steam, with BepInEx installed in its folder) with the mod and the
    test plugin, then joins it with the test character. The server's world is kept in tests\.server (ignored by git), and
    the server's own copy of the mod and its config files are put back afterwards. Only test projects that support these
    setups can use them (they report the setup they ran in).

    -Small runs the game in a small window with the lowest graphics, which needs less memory. The game's settings in the
    registry (window size, graphics) are backed up and put back afterwards, so normal games start as before.

.EXAMPLE
    .\tests\run-tests.ps1 StorageMaster

.EXAMPLE
    .\tests\run-tests.ps1 StorageMaster -With seneaL-SeneaL_UI

.EXAMPLE
    .\tests\run-tests.ps1 VillageWorkers -Server
    Runs the tests as a player on a dedicated server running the mod.

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
    # Start the test world with "Start server" ticked, as a player hosting it from the game.
    [switch]$Hosting,
    # Start the dedicated server with the mod and join it.
    [switch]$Server,
    # A small window and the lowest graphics, for when memory is short.
    [switch]$Small,
    # Found through Steam when left out.
    [string]$ValheimDir,
    # The dedicated server's folder for -Server; found through Steam when left out.
    [string]$ServerDir,
    # Away from the default 2456, so a server of your own can keep running.
    [int]$ServerPort = 2476,
    [int]$TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot "$Mod.Tests\$Mod.Tests.csproj"
if (-not (Test-Path $project)) { throw "No test project at $project" }

function Find-SteamApp([string]$folder, [string]$exe) {
    $steam = (Get-ItemProperty HKCU:\Software\Valve\Steam -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { $steam = "${env:ProgramFiles(x86)}\Steam" }
    $libraries = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $libraries += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
    }
    foreach ($lib in $libraries) {
        $dir = Join-Path $lib "steamapps\common\$folder"
        if (Test-Path (Join-Path $dir $exe)) { return $dir }
    }
    return $null
}

function Find-Valheim {
    $dir = Find-SteamApp 'Valheim' 'valheim.exe'
    if (-not $dir) { throw 'Could not find Valheim through Steam. Pass -ValheimDir "C:\path\to\Valheim".' }
    return $dir
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

if ($Hosting -and $Server) { throw 'Use -Hosting or -Server, not both.' }
if (-not $ValheimDir) { $ValheimDir = Find-Valheim }
$bepinex = Join-Path $ValheimDir 'BepInEx'
if (-not (Test-Path (Join-Path $bepinex 'core\BepInEx.dll'))) { throw "BepInEx isn't installed in $ValheimDir" }
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Valheim is already running. Close it first.' }
$serverPassword = 'modtest'
if ($Server) {
    if (-not $ServerDir) { $ServerDir = Find-SteamApp 'Valheim dedicated server' 'valheim_server.exe' }
    if (-not $ServerDir) { throw 'Could not find the Valheim dedicated server through Steam (install "Valheim Dedicated Server" from the Tools). Or pass -ServerDir.' }
    $serverBepinex = Join-Path $ServerDir 'BepInEx'
    if (-not (Test-Path (Join-Path $serverBepinex 'core\BepInEx.dll'))) {
        throw "BepInEx isn't installed in the dedicated server's folder ($ServerDir). Copy BepInExPack_Valheim's files there (see the mod's README)."
    }
    $running = Get-Process valheim_server -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($ServerDir) }
    if ($running) { throw 'The dedicated server is running. Stop it first: the test server uses the same folder.' }
}

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
# Mods write their config files on start; the ones that weren't there before are removed afterwards. The tests set the
# mods' settings to their defaults, which saves them, so the files that were there are put back as they were.
$configsBefore = @(Get-ChildItem $configDir -Recurse | ForEach-Object FullName)
$configBackup = Join-Path ([System.IO.Path]::GetTempPath()) "$Mod.Tests.config"
if (Test-Path $configBackup) { Remove-Item -Recurse -Force $configBackup }
Copy-Item $configDir $configBackup -Recurse

New-Item -ItemType Directory -Force $pluginDir | Out-Null
Copy-Item (Join-Path $PSScriptRoot "$Mod.Tests\bin\Debug\netstandard2.1\$Mod.Tests.dll") $pluginDir
foreach ($package in $packages) {
    Write-Host "With $(Split-Path $package -Leaf)"
    Install-Package $package $testModPlugins $testModPatchers $configDir
}
if (Test-Path $results) { Remove-Item $results }
if (Test-Path $screenshots) { Remove-Item -Recurse $screenshots }
$setup = if ($Server) { 'server' } elseif ($Hosting) { 'host' } else { $null }
if (-not $setup) {
    Set-Content -Path $marker -Value $(if ($Play) { 'play' } else { 'Run the tests on the next start' }) -Encoding utf8
}
else {
    # Settings for the test plugin, one key=value per line.
    $options = [ordered]@{ mode = $(if ($Play) { 'play' } else { 'test' }); setup = $setup; password = $serverPassword }
    if ($Server) { $options += [ordered]@{ host = '127.0.0.1'; port = $ServerPort; 'server-dir' = $ServerDir } }
    Set-Content -Path $marker -Value ($options.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -Encoding utf8
}

# The dedicated server for -Server: the freshly built mod and the test plugin go into its plugins folder; its own copy of
# the mod is set aside and its config files are backed up, to be put back afterwards.
$serverProcess = $null
if ($Server) {
    $serverPlugins = Join-Path $serverBepinex 'plugins'
    $serverConfig = Join-Path $serverBepinex 'config'
    $serverBackup = Join-Path $serverBepinex '_test-backup'
    $serverStatus = Join-Path $serverBepinex "$Mod.Tests.server.txt"
    $serverCommands = Join-Path $serverBepinex "$Mod.Tests.server.cmd"
    if (Test-Path $serverBackup) { throw "$serverBackup is left over from a test run that didn't finish. Put its plugins and config back into BepInEx by hand, then delete it." }
    New-Item -ItemType Directory -Force (Join-Path $serverBackup 'plugins') | Out-Null
    if (Test-Path (Join-Path $serverPlugins $Mod)) { Move-Item (Join-Path $serverPlugins $Mod) (Join-Path $serverBackup "plugins\$Mod") }
    if (Test-Path $serverConfig) { Copy-Item $serverConfig (Join-Path $serverBackup 'config') -Recurse }
    New-Item -ItemType Directory -Force (Join-Path $serverPlugins $Mod), (Join-Path $serverPlugins "$Mod.Tests"), $serverConfig | Out-Null
    Copy-Item (Join-Path $bepinex "plugins\$Mod\$Mod.dll") (Join-Path $serverPlugins $Mod)
    Copy-Item (Join-Path $PSScriptRoot "$Mod.Tests\bin\Debug\netstandard2.1\$Mod.Tests.dll") (Join-Path $serverPlugins "$Mod.Tests")
    Set-Content -Path (Join-Path $serverConfig "$Mod.Tests.run") -Value 'mode=server' -Encoding utf8
    Remove-Item $serverStatus, $serverCommands -ErrorAction SilentlyContinue
}

$failed = $true
try {
    if ($Server) {
        $saveDir = Join-Path $PSScriptRoot '.server'
        New-Item -ItemType Directory -Force $saveDir | Out-Null
        Write-Host "Starting the dedicated server from $ServerDir on port $ServerPort"
        $env:SteamAppId = '892970'
        $serverProcess = Start-Process (Join-Path $ServerDir 'valheim_server.exe') -WorkingDirectory $ServerDir -PassThru -WindowStyle Minimized -ArgumentList @(
            '-nographics', '-batchmode', '-name', "`"$Mod tests`"", '-port', $ServerPort, '-world', 'modtest',
            '-password', $serverPassword, '-public', '0', '-savedir', "`"$saveDir`"")
        Remove-Item Env:\SteamAppId
        # A new world generates its locations first, which takes a while.
        $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
        while (-not ((Test-Path $serverStatus) -and (Select-String -Path $serverStatus -Pattern '^READY' -Quiet))) {
            Start-Sleep -Seconds 2
            if ($serverProcess.HasExited) { throw "The dedicated server stopped while starting. See $serverBepinex\LogOutput.log." }
            if ((Get-Date) -gt $deadline) { throw "The dedicated server didn't get ready within $TimeoutMinutes minutes." }
        }
        Write-Host 'The dedicated server is ready'
    }

    Write-Host "Starting Valheim from $ValheimDir"
    $gameArgs = @()
    if ($Small) {
        # Unity remembers the window size, and the test plugin lowers the graphics: both are undone from this backup.
        $prefsBackup = Join-Path ([System.IO.Path]::GetTempPath()) "$Mod.Tests.valheim-prefs.reg"
        # Through cmd: with $ErrorActionPreference = 'Stop', anything reg writes to stderr would stop this script.
        cmd /c "reg export HKCU\Software\IronGate\Valheim `"$prefsBackup`" /y >nul 2>&1"
        if ($LASTEXITCODE -ne 0) { throw "Couldn't back up Valheim's settings from the registry" }
        # -test-low-graphics is read by the test plugins, Unity ignores it.
        $gameArgs = @('-screen-fullscreen', '0', '-window-mode', 'windowed', '-screen-width', '800', '-screen-height', '450', '-test-low-graphics')
    }
    if ($gameArgs) { Start-Process (Join-Path $ValheimDir 'valheim.exe') -WorkingDirectory $ValheimDir -ArgumentList $gameArgs | Out-Null }
    else { Start-Process (Join-Path $ValheimDir 'valheim.exe') -WorkingDirectory $ValheimDir | Out-Null }

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
                if ($line -match '^SETUP (\w+)') { $reportedSetup = $Matches[1] }
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
    if ($setup -and $reportedSetup -ne $setup) {
        Write-Host "The $Mod tests don't support -$(if ($Server) { 'Server' } else { 'Hosting' }): they ran in singleplayer." -ForegroundColor Red
        $failed = $true
    }
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

    if ($Server) {
        # Asked to quit, the server saves the world first, so the next run doesn't have to generate it again.
        if ($serverProcess -and -not $serverProcess.HasExited) {
            Add-Content -Path $serverCommands -Value 'quit'
            if (-not $serverProcess.WaitForExit(60000)) { Stop-Process -Id $serverProcess.Id -Force }
        }
        if (Test-Path $serverStatus) {
            Write-Host "`nThe dedicated server wrote:" -ForegroundColor Gray
            Get-Content $serverStatus | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
        }
        Remove-Item -Recurse -Force (Join-Path $serverPlugins $Mod), (Join-Path $serverPlugins "$Mod.Tests") -ErrorAction SilentlyContinue
        Remove-Item -Force (Join-Path $serverConfig "$Mod.Tests.run"), $serverStatus, $serverCommands -ErrorAction SilentlyContinue
        if (Test-Path (Join-Path $serverBackup "plugins\$Mod")) { Move-Item (Join-Path $serverBackup "plugins\$Mod") (Join-Path $serverPlugins $Mod) }
        if (Test-Path (Join-Path $serverBackup 'config')) {
            Remove-Item -Recurse -Force $serverConfig
            Move-Item (Join-Path $serverBackup 'config') $serverConfig
        }
        Remove-Item -Recurse -Force $serverBackup
    }
    # Deepest first, so new folders are emptied before they're removed.
    Get-ChildItem $configDir -Recurse | Where-Object { $_.FullName -notin $configsBefore } |
        Sort-Object { $_.FullName.Length } -Descending | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $configBackup '*') $configDir -Recurse -Force
    Remove-Item -Recurse -Force $configBackup
    # Last, so a problem here can't keep the rest from being cleaned up.
    if ($prefsBackup -and (Test-Path $prefsBackup) -and (Get-Item $prefsBackup).Length -gt 0) {
        cmd /c "reg delete HKCU\Software\IronGate\Valheim /f >nul 2>&1 & reg import `"$prefsBackup`" >nul 2>&1"
        if ($LASTEXITCODE -eq 0) { Remove-Item $prefsBackup }
        else { Write-Host "Couldn't put Valheim's settings back; import $prefsBackup by hand (double-click it)." -ForegroundColor Red }
    }
}
if ($failed) { exit 1 }
