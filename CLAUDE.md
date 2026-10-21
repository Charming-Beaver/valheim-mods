# valheim-mods

Valheim (BepInEx 5) mods by Charming Beaver. Each mod lives in its own top-level folder (e.g. `StorageMaster/`), and the folder name is used as the mod's ID across the repo.

## Issues

### Fields
Every issue gets these organization issue fields (one value each):
- **Mod** (public): the mod folder name, e.g. `StorageMaster`. Set automatically for issues opened from the templates.
- **Source** (public), where it came from: `Reddit` (r/ModdedValheim threads), `Nexus` (the mod's posts on Nexus Mods), `GitHub` (opened by a player here; set automatically), `Tests` (found by the in-game tests), `CharmingBeaver` (the maintainer's own findings and ideas). When several places reported it, use the first one and mention the others in the body.
- **Priority** (members only): `Urgent` (crashes, or items lost for everyone), `High` (items end up where they shouldn't, broken promises like locked slots), `Medium` (noticeable for many players, or a player is waiting for it), `Low` (nice to have, or waiting for the reporter or more requests).

`gh` has no command for fields, so use the REST API with the fields' IDs (from `gh api /orgs/Charming-Beaver/issue-fields`; Mod 48751818, Source 48751819, Priority 48541243) and the option's name. One call can set several fields and leaves the others alone:
```sh
gh api -X POST /repos/Charming-Beaver/valheim-mods/issues/<n>/issue-field-values --input body.json
# body.json: {"issue_field_values":[{"field_id":48751818,"value":"StorageMaster"},{"field_id":48751819,"value":"Reddit"},{"field_id":48541243,"value":"High"}]}
```
Adding a field or an option (a new mod or source) needs the `admin:org` scope (`gh auth refresh -h github.com -s admin:org`); ask the maintainer.

### Project board
Issues and PRs are added automatically to the org project [CharmingBeaver's Valheim Mods](https://github.com/orgs/Charming-Beaver/projects/1) (one project for all mods; views split them by Mod). Keep its **Status** current:
- `Inbox` (new, fields not set), `Backlog` (triaged), `Up next` (the next 1–3 things), `In progress`, `In review` (PR open), `Waiting` (for the reporter or more requests), `Done` (set automatically when closed or merged).
- Creating an issue: set the fields and labels, then `Backlog`, `Up next` or `Waiting`. Starting work: `In progress`. Opening its PR: `In review`.
- **Reply** (project field): `Pending` when an issue from Reddit or Nexus is fixed and the reporter should be told after the release; `Done` once they are. The "To reply" view lists the pending ones.

`gh project item-edit` works but needs IDs; get them with `gh project field-list 1 --owner Charming-Beaver` and `gh project item-list 1 --owner Charming-Beaver`.

### Milestones
Each mod has an open milestone `<Mod> next` (e.g. `StorageMaster next`) for what goes into its next release, the same as the changelog's `Unreleased` section. Add an issue to it when its PR is opened (or when it's planned in). At release, rename the milestone to the version (`StorageMaster 1.2.1`), close it, and open a new `<Mod> next`.

### Labels
- Type: `bug`, `enhancement`, `question`, etc. (GitHub defaults).
- Area: `ui` (layout, rendering, usability of windows and buttons), `sorting` (Sort, Tidy up, Gather here, Quick stack, locked slots), `performance` (freezes, slowness), `accessibility`.
- `mod-compatibility`: conflicts or integration with *other people's* mods or modpacks.
- Create a new label when an issue needs one rather than squeezing it into an existing one. Mod, source and priority are fields, not labels.

### Issue body
Use these sections, skipping any that don't apply:
- **Problem** (bug) or **Request** (enhancement): what's wrong or wanted, including what any screenshot shows
- **Reporter's words**: the original feedback, quoted
- **Reporter's mod setup**: the reporter's known installed mods, noting that the full list is unknown
- **Next steps** / **Things to check**: checklist, when useful
- **Related issues**: `#N` plus why they're related
- **Source**: who reported it and where, linking the exact comment or post rather than the whole thread: a Reddit comment's permalink (`…/comments/<thread>/<slug>/<comment id>/`), or a Nexus post (`…/mods/<id>?tab=posts&jump_to_comment=<post id>`)

### Related issues
Link related issues both ways:
- mention them as `#N` in the text and in the **Related issues** section, **and**
- add the native "relates to" relationship. `gh` has no command for it, so use GraphQL. It is automatically set in both directions, so one call per pair is enough:
  ```sh
  gh api graphql -f query='mutation($a:ID!,$b:ID!){ addRelatesTo(input:{issueId:$a, relatedIssueId:$b}){ clientMutationId } }' -f a=<issue node id> -f b=<issue node id>
  ```
  Get node IDs with `gh issue view <n> --json id`.

### Screenshots
`gh` can't upload images to issues. The maintainer uploads them manually, so after creating issues, give a list of which screenshot goes in which issue.

## Building
- Needs the .NET SDK and Valheim with BepInEx installed. The build references the game's DLLs and copies the mod into `BepInEx/plugins/<Mod>/`.
- `Directory.Build.props` defaults `ValheimDir` to `D:\SteamLibrary\steamapps\common\Valheim`. Elsewhere pass `-p:ValheimDir="<path>"`. `tests/run-tests.ps1` and `package.ps1` (and so `release.ps1`) find Valheim through Steam by themselves.
- The mods use Krafs.Publicizer, so private game members (and, in the tests, the mod's own private members) can be used directly, without reflection.

## Reading the game's code
Decompile a class to see what the game actually does before patching or calling it:
```sh
ilspycmd -t <Type> "<ValheimDir>/valheim_Data/Managed/assembly_valheim.dll"
```
Install it with `dotnet tool install -g ilspycmd --version 8.2.0.7535`. Newer versions need a newer .NET than SDK 8.

## Mod code notes
- Mods are client-side only and must work in multiplayer. Chests can be owned by another player or the server. Storage Master takes ownership by reusing the game's "Place stacks" request (`ChestAccess`), so answers can arrive later than the request.
- Storage Master's saved-data keys start with `InventoryTools.` (the mod's working title). Don't rename them, or players lose their locked slots, chest names and skipped chests.

## Testing
In-game tests live in `tests/<Mod>.Tests/` (lowercase `tests`, so it isn't mistaken for a mod folder). Run them with `.\tests\run-tests.ps1 <Mod>` after changing a mod: it builds, starts Valheim, prints PASS/FAIL per test and exits non-zero on failure. A run takes a couple of minutes, needs Steam running and takes over the screen. Look at the screenshots it lists to check UI changes.
- Tests are static methods marked `[Test]` (void, or `IEnumerator` to wait for frames), using the helpers in `TestKit.cs`.
- Each test starts from a clean state (no test chests, empty inventory, no locks or skips, default settings, no messages). When a mod gets new state that could leak between tests, reset it in `TestKit.Reset`.
- For a bug, write a test that fails first, then fix it.
- `-With <Namespace-Name>` runs the tests alongside mods from Thunderstore (e.g. `-With seneaL-SeneaL_UI`), downloaded with their dependencies into the ignored `tests/.mods` and installed only for that run. Use it to reproduce `mod-compatibility` issues. Every test runs in every setup; mark a test that only makes sense with a mod `[RequiresMod("<BepInEx GUID>")]` (the results list the loaded mods' GUIDs), and one that another mod changes on purpose `[SkipWithMod("<GUID>", "<reason>")]`. Other people's mods never go into the repo or into `thunderstore.json`, unless the mod can't run without them.
- When the maintainer wants to try a change by hand, start the game with `-Play` (plus any `-With` mods) in the background: it loads the test world with a few chests and god mode, and cleans up when the game is closed. Rebuilding matters: the last test run may have built something else, and `-Play` builds the current code first.
- The tests run in single player, so multiplayer paths (chests owned by others, delayed answers) aren't covered. Check those by reading the code and say so.

## Changes and releases
- Work on a branch and open a PR. The maintainer merges them.
- When a PR is for an issue, start its title with the issue number, e.g. `#12: Quick stack pressed twice keeps the first press's summary`, and put `Fixes #12` (or `Closes #12` for non-bugs) in the body so merging closes the issue. PRs without an issue get a plain title.
- Commit messages: a short summary line, then a plain explanation of what changed and why.
- Add a line for every player-facing change under `## Unreleased` at the top of the mod's `CHANGELOG.md`, creating the section if it's missing. Don't add version sections: the maintainer picks the version, and `.\version.ps1 <Mod> patch|minor|major` (or an exact version) sets it, renames `Unreleased` and opens the release PR. `patch` is for fixes only, `minor` for new features.
- Inside a version, group the lines under `### Added` (new features), `### Changed` (existing behavior that works differently), `### Fixed` (bugs) and, when needed, `### Removed`, in that order, leaving out empty ones. A change that can break something for players (a renamed setting, a moved hotkey) goes first in Changed, starting with **Breaking:**.

## Adding a new mod
1. Create the mod folder (its name becomes the mod ID).
2. Add the folder name as an option of the **Mod** issue field (see Fields), and create its `<FolderName> next` milestone.
3. Add the folder name to the "Which mod?" dropdown in `.github/ISSUE_TEMPLATE/bug_report.yml` and `feature_request.yml`. `.github/workflows/issue-mod.yml` uses the answer to set new issues' Mod field.
4. Releases are tagged `<foldername lowercase>-v<version>` (e.g. `storagemaster-v1.0.1`). `.github/workflows/thunderstore.yml` uses the part before `-v` to find the mod folder, which needs a `thunderstore.toml`.
5. For Nexus: upload the first version by hand (that creates the mod page and its main file), then add `<FolderName>/nexus.json` with the mod ID, the file ID (Files tab, "Advanced"), the file's display name and description. From then on `.github/workflows/nexus.yml` uploads each published release as a new version of that file.
