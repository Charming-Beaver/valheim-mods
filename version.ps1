<#
.SYNOPSIS
    Bumps a mod's version, like "npm version": sets it in Plugin.cs and thunderstore.toml, turns the changelog's
    "## Unreleased" into the new version, and opens a PR for it on a new branch.

.DESCRIPTION
    Run it on an up-to-date main without uncommitted changes. After the PR is merged, release.ps1 creates the release.

.EXAMPLE
    .\version.ps1 StorageMaster patch
    1.2.0 -> 1.2.1 (fixes only)

.EXAMPLE
    .\version.ps1 StorageMaster minor
    1.2.0 -> 1.3.0 (new features)

.EXAMPLE
    .\version.ps1 StorageMaster 2.0.0 -DryRun
    Shows what would change, without changing anything.
#>
param(
    [Parameter(Mandatory = $true)][string]$Mod,
    # major, minor, patch, or an exact version like 1.3.0.
    [Parameter(Mandatory = $true)][string]$Bump,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root $Mod
$pluginPath = Join-Path $project 'src\Plugin.cs'
$tomlPath = Join-Path $project 'thunderstore.toml'
$changelogPath = Join-Path $project 'CHANGELOG.md'
if (-not (Test-Path $pluginPath)) { throw "No mod found at $project" }

# Keeps a file's encoding (with or without BOM) and only changes what's asked.
function Read-Text([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    return @{ Text = [Text.Encoding]::UTF8.GetString($bytes, $(if ($bom) { 3 } else { 0 }), $bytes.Length - $(if ($bom) { 3 } else { 0 })); Bom = $bom }
}
function Write-Text([string]$path, $file, [string]$text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($file.Bom)))
}

$plugin = Read-Text $pluginPath
if ($plugin.Text -notmatch 'Version\s*=\s*"(\d+)\.(\d+)\.(\d+)"') { throw 'Could not find a Version = "x.y.z" constant in Plugin.cs' }
$old = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
$major, $minor, $patch = [int]$Matches[1], [int]$Matches[2], [int]$Matches[3]
switch -Regex ($Bump) {
    '^major$' { $new = "$($major + 1).0.0" }
    '^minor$' { $new = "$major.$($minor + 1).0" }
    '^patch$' { $new = "$major.$minor.$($patch + 1)" }
    '^\d+\.\d+\.\d+$' { $new = $Bump }
    default { throw "Use major, minor, patch or a version like 1.3.0 (got '$Bump')." }
}
if ([version]$new -le [version]$old) { throw "$new isn't higher than the current $old. Mod managers only update to higher versions." }

$changelog = Read-Text $changelogPath
# Headings are matched up to the line end, which may be \r\n (the files are checked out with Windows line endings).
if ($changelog.Text -notmatch '(?m)^## Unreleased[ \t]*\r?$') { throw "CHANGELOG.md has no '## Unreleased' section to release." }
if ($changelog.Text -match "(?m)^## $([regex]::Escape($new))[ \t]*\r?$") { throw "CHANGELOG.md already has a '## $new' section." }
$notes = [regex]::Match($changelog.Text, '(?ms)^## Unreleased[ \t]*\r?\n(.*?)(?=^## |\z)').Groups[1].Value.Trim()
if (-not $notes) { throw "The '## Unreleased' section is empty." }

$tag = "$($Mod.ToLowerInvariant())-v$new"
$branch = "release-$($Mod.ToLowerInvariant())-$new"
Write-Host "$Mod $old -> $new" -ForegroundColor Green
Write-Host "Changelog for $new`:`n$notes`n"

if ($DryRun) {
    Write-Host "Dry run: would set the version in Plugin.cs and thunderstore.toml, rename '## Unreleased' to '## $new', and open a PR from branch $branch."
    return
}

Push-Location $root
try {
    if (git status --porcelain) { throw 'There are uncommitted changes. Commit or stash them first.' }
    if ((git branch --show-current) -ne 'main') { throw 'Run it on main.' }
    git fetch -q origin
    if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { throw 'main differs from GitHub. Pull (or push) first.' }
    $ErrorActionPreference = 'Continue'
    gh release view $tag *> $null
    $exists = $LASTEXITCODE -eq 0
    $ErrorActionPreference = 'Stop'
    if ($exists) { throw "A release $tag already exists." }

    git checkout -q -b $branch
    Write-Text $pluginPath $plugin ($plugin.Text -replace '(Version\s*=\s*")\d+\.\d+\.\d+(")', "`${1}$new`${2}")
    if (Test-Path $tomlPath) {
        $toml = Read-Text $tomlPath
        Write-Text $tomlPath $toml ($toml.Text -replace '(?m)^(versionNumber\s*=\s*")[^"]*(")', "`${1}$new`${2}")
    }
    Write-Text $changelogPath $changelog ($changelog.Text -replace '(?m)^## Unreleased(?=[ \t]*\r?$)', "## $new")

    git add -A
    git commit -q -m "$Mod $new" -m "Version $new in Plugin.cs and thunderstore.toml; the changelog's Unreleased section becomes $new."
    git push -q -u origin $branch
    $body = New-TemporaryFile
    [IO.File]::WriteAllText($body.FullName, "Version **$new** (was $old).`n`n## Changelog`n$notes`n`n## After merging`nRun ``.\release.ps1 $Mod`` on main. It creates the draft release; publishing it uploads to Thunderstore and Nexus.", (New-Object Text.UTF8Encoding($false)))
    gh pr create --base main --head $branch --title "$Mod $new" --body-file $body.FullName
    Remove-Item $body
}
finally {
    Pop-Location
}
