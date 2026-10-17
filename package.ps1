<#
.SYNOPSIS
    Builds a mod in Release and packs it into dist\<Mod>-<version>.zip, ready for GitHub releases and Nexus Mods.

.EXAMPLE
    .\package.ps1 StorageMaster
#>
param(
    [Parameter(Mandatory = $true)][string]$Mod
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
