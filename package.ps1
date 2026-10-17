<#
.SYNOPSIS
    Builds a mod in Release and packs dist\<Mod>-<version>.zip (GitHub releases, Nexus Mods) and, when the mod has
    thunderstore.json and icon.png, dist\<Team>-<Mod>-<version>.zip for Thunderstore.

.EXAMPLE
    .\package.ps1 StorageMaster
#>
param(
    [Parameter(Mandatory = $true)][string]$Mod,
    # Thunderstore team name (letters, digits and _ only).
    [string]$Team = 'CharmingBeaver'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root $Mod
if (-not (Test-Path (Join-Path $project "$Mod.csproj"))) { throw "No project found at $project" }

# The version lives in Plugin.cs, so there's only one place to change it.
$plugin = Get-Content (Join-Path $project 'src\Plugin.cs') -Raw
if ($plugin -notmatch 'Version\s*=\s*"([^"]+)"') { throw 'Could not find the Version constant in Plugin.cs' }
$version = $Matches[1]

dotnet build $project -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$dll = Join-Path $project "bin\Release\netstandard2.1\$Mod.dll"

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zipPath = Join-Path $dist "$Mod-$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }

# Entries are added one by one with forward slashes: Compress-Archive in Windows PowerShell writes
# backslashes, which some mod managers can't read.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    $files = [ordered]@{
        "BepInEx/plugins/$Mod/$Mod.dll" = $dll
        "BepInEx/plugins/$Mod/README.md" = (Join-Path $project 'README.md')
        "BepInEx/plugins/$Mod/CHANGELOG.md" = (Join-Path $project 'CHANGELOG.md')
        "BepInEx/plugins/$Mod/LICENSE" = (Join-Path $root 'LICENSE')
    }
    foreach ($entry in $files.Keys) {
        if (Test-Path $files[$entry]) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $files[$entry], $entry) | Out-Null
        }
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Packed $zipPath"

# Thunderstore: manifest.json, icon.png, README.md and CHANGELOG.md at the root, the DLL in plugins/.
# Mod managers install plugins/ into BepInEx/plugins/<Team>-<Mod>/.
$tsInfoPath = Join-Path $project 'thunderstore.json'
$icon = Join-Path $project 'icon.png'
if (-not (Test-Path $tsInfoPath)) {
    Write-Host "No thunderstore.json, skipping the Thunderstore package"
    return
}
if (-not (Test-Path $icon)) {
    Write-Warning "No icon.png (256x256) in $project, skipping the Thunderstore package"
    return
}
$tsInfo = Get-Content $tsInfoPath -Raw | ConvertFrom-Json
if ($tsInfo.description.Length -gt 250) { throw "Thunderstore description is $($tsInfo.description.Length) characters, the limit is 250" }
$manifest = [ordered]@{
    name           = $Mod
    version_number = $version
    website_url    = $tsInfo.website_url
    description    = $tsInfo.description
    dependencies   = @($tsInfo.dependencies)
}
$manifestJson = $manifest | ConvertTo-Json -Depth 3

$tsZipPath = Join-Path $dist "$Team-$Mod-$version.zip"
if (Test-Path $tsZipPath) { Remove-Item $tsZipPath }
$zip = [System.IO.Compression.ZipFile]::Open($tsZipPath, 'Create')
try {
    $entry = $zip.CreateEntry('manifest.json')
    $writer = New-Object System.IO.StreamWriter($entry.Open(), (New-Object System.Text.UTF8Encoding($false)))
    $writer.Write($manifestJson)
    $writer.Dispose()
    $files = [ordered]@{
        'icon.png'           = $icon
        'README.md'          = (Join-Path $project 'README.md')
        'CHANGELOG.md'       = (Join-Path $project 'CHANGELOG.md')
        "plugins/$Mod.dll"   = $dll
    }
    foreach ($name in $files.Keys) {
        if (Test-Path $files[$name]) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $files[$name], $name) | Out-Null
        }
    }
}
finally {
    $zip.Dispose()
}
Write-Host "Packed $tsZipPath"
