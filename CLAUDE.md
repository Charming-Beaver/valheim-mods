# valheim-mods

Valheim (BepInEx 5) mods by Charming Beaver. Each mod lives in its own top-level folder (e.g. `StorageMaster/`), and the folder name is used as the mod's ID across the repo.

## Issues

### Labels
- Every issue gets the label of the mod it's about: `mod: <FolderName>`, e.g. `mod: StorageMaster`.
- Type: `bug`, `enhancement`, `question`, etc. (GitHub defaults).
- Area: `ui` (layout, rendering, usability of windows and buttons), `sorting` (Sort, Tidy up, Gather here, Quick stack, locked slots).
- `mod-compatibility`: conflicts or integration with *other people's* mods or modpacks.
- Source: `reddit-feedback` for reports from the r/ModdedValheim release thread. Create a similar label for other sources (e.g. Nexus) when needed.
- Create a new label when an issue needs one rather than squeezing it into an existing one.

### Issue body
Use these sections, skipping any that don't apply:
- **Problem** (bug) or **Request** (enhancement): what's wrong or wanted, including what any screenshot shows
- **Reporter's words**: the original feedback, quoted
- **Reporter's mod setup**: the reporter's known installed mods, noting that the full list is unknown
- **Next steps** / **Things to check**: checklist, when useful
- **Related issues**: `#N` plus why they're related
- **Source**: who reported it and where, with a link

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

## Adding a new mod
1. Create the mod folder (its name becomes the mod ID).
2. Create the `mod: <FolderName>` label.
3. Add the folder name to the "Which mod?" dropdown in `.github/ISSUE_TEMPLATE/bug_report.yml` and `feature_request.yml`. `.github/workflows/label-mod.yml` uses the answer to label new issues.
4. Releases are tagged `<foldername lowercase>-v<version>` (e.g. `storagemaster-v1.0.1`). `.github/workflows/thunderstore.yml` uses the part before `-v` to find the mod folder, which needs a `thunderstore.toml`.
