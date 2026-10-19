<#
.SYNOPSIS
    Packages a mod and creates its GitHub release as a draft, with both zips attached and the notes taken from
    the mod's CHANGELOG.md. Publishing the draft on GitHub then uploads it to Thunderstore automatically.

.EXAMPLE
    .\release.ps1 StorageMaster
#>
param(
    [Parameter(Mandatory = $true)][string]$Mod
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root $Mod
$repo = 'Charming-Beaver/valheim-mods'

# Releases are made from what's on GitHub, so everything must be committed and pushed first.
Push-Location $root
try {
    if (git status --porcelain) { throw 'There are uncommitted changes. Commit and push them first.' }
    git fetch -q origin
    if ((git rev-parse HEAD) -ne (git rev-parse '@{u}')) { throw 'Your branch and GitHub differ. Push (or pull) first.' }
}
finally {
    Pop-Location
}

$plugin = Get-Content (Join-Path $project 'src\Plugin.cs') -Raw
if ($plugin -notmatch 'Version\s*=\s*"([^"]+)"') { throw 'Could not find the Version constant in Plugin.cs' }
$version = $Matches[1]
$name = if ($plugin -match 'Name\s*=\s*"([^"]+)"') { $Matches[1] } else { $Mod }
$tag = "$($Mod.ToLowerInvariant())-v$version"

# "release not found" is the answer we hope for. Windows PowerShell 5.1 would treat that error output as a failure
# under 'Stop', so only the exit code counts here.
$ErrorActionPreference = 'Continue'
gh release view $tag --repo $repo *> $null
$exists = $LASTEXITCODE -eq 0
$ErrorActionPreference = 'Stop'
if ($exists) { throw "Release $tag already exists. Raise the version in Plugin.cs first." }

# The notes are this version's section of the changelog.
$changelog = Get-Content (Join-Path $project 'CHANGELOG.md') -Raw
$pattern = "(?ms)^## $([regex]::Escape($version))\s*$\r?\n(.*?)(?=^## |\z)"
if ($changelog -notmatch $pattern) { throw "CHANGELOG.md has no '## $version' section." }
$notes = $Matches[1].Trim()
$notes += "`n`n**Install:** extract ``$Mod-$version.zip`` into your Valheim folder (needs BepInExPack for Valheim), or use Vortex or r2modman."
$notes += "`n`nFull feature list: [README](https://github.com/$repo/tree/main/$Mod)"

& (Join-Path $root 'package.ps1') $Mod
$assets = @(Join-Path $root "dist\$Mod-$version.zip")
$tsZip = Get-ChildItem (Join-Path $root 'dist') -Filter "*-$Mod-$version.zip" | Select-Object -First 1
if ($tsZip) { $assets += $tsZip.FullName }

$notesFile = New-TemporaryFile
try {
    [System.IO.File]::WriteAllText($notesFile.FullName, $notes, (New-Object System.Text.UTF8Encoding($false)))
    gh release create $tag @assets --repo $repo --draft --target main --title "$name $version" --notes-file $notesFile
    if ($LASTEXITCODE -ne 0) { throw 'Creating the release failed' }
}
finally {
    Remove-Item $notesFile
}

Write-Host ""
Write-Host "Draft release $tag created. Test it, then publish it on GitHub; Thunderstore gets it automatically."
