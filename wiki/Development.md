# Development

## Repository layout

| Path | Contents |
| --- | --- |
| `dist/` | The install package. Folders are numbered in install order: SQL, IDOs and extension classes, forms. |
| `src/IDO Extension Class Custom Assemblies/` | Two C# class library solutions, one per assembly, and `Lib/` with the Infor DLLs they reference |
| `wiki/` | The source for this wiki |

## Building

There is no test suite, and nothing runs outside Syteline.

You need Windows, Visual Studio or MSBuild, and the .NET Framework 4.8 targeting pack. Build each solution on its own:

```
nuget restore "src/IDO Extension Class Custom Assemblies/ue_AIR_IDOReplicationRules_ECA/ue_AIR_IDOReplicationRules_ECA.sln"
msbuild "src/IDO Extension Class Custom Assemblies/ue_AIR_IDOReplicationRules_ECA/ue_AIR_IDOReplicationRules_ECA.sln" /p:Configuration=Release
```

```
nuget restore "src/IDO Extension Class Custom Assemblies/ue_AIR_IDOReplicationConfig_ECA/ue_AIR_IDOReplicationConfig_ECA.sln"
msbuild "src/IDO Extension Class Custom Assemblies/ue_AIR_IDOReplicationConfig_ECA/ue_AIR_IDOReplicationConfig_ECA.sln" /p:Configuration=Release
```

### The build check

Every pull request, and every push to `main`, runs the **Build** workflow (`.github/workflows/build.yml`). It:
- builds both assemblies on a Windows runner, the same way as the commands above
- fails if `Helpers/` or `Models/` differ between the two projects by more than the project name. To run that check yourself: `bash .github/scripts/compare-shared-code.sh`
- compiles every form script. A form's script is stored inside its FormSync export and compiled by Syteline when the form opens, where one compile error stops every script on the form. `.github/scripts/extract-form-scripts.py` writes each script to a `.cs` file, and the workflow compiles it against the DLLs in `Lib/`.

Each run also publishes the two built DLLs. Open the run from the pull request's **Checks** tab and download them under **Artifacts**. They are kept for 14 days, and you can upload them to Syteline in place of a local build.

The check only proves the code compiles. Behavior, forms and IDOs still have to be tested in Syteline.

The language version is the .NET Framework default (C# 7.3). Avoid newer syntax such as collection expressions, target-typed `new()`, `??=`, nullable annotations and file-scoped namespaces.

## Deploying a change

1. Build the assembly, or download it from the pull request's Build run.
2. Upload the DLL on Syteline's IDO Extension Class Assemblies form.
3. If a method's signature changed, use **Resync Parameters** on the IDO method. If you added a method, add it to the IDO first.
4. Run **Refresh Form Metadata**.
5. Re-export the IDOs with their extension classes to refresh `dist/`. The export embeds both compiled assemblies, so `dist/` only matches the source after this step.

## Rules for changes

- **Helpers and models are duplicated.** `Helpers/` and `Models/` exist in both projects and are identical except for the namespace. Make every change in both.
- **Never throw from replication.** See [Architecture](Architecture#the-never-throw-rule).
- **No company-specific values.** This is an open-source project for any Syteline instance. Make URLs, names and sites settings on a credential or rule.
- **Go through IDOs.** Use `SytelineInternalAPI`, not direct SQL.
- **Keep exported files as exported.** The files in `dist/` come from Mongoose export tools. Edit them as little as possible and keep the exporter's formatting: 3-space indentation, element order and any UTF-8 BOM. Never edit the embedded assembly images by hand.
- **Bump the revision when you hand-edit an IDO.** Add 1 to that IDO's `<RevisionNo>` and set `<RevisionDate>` to the current time, or the IDO Import Wizard may not treat it as changed.
- **Keep the docs in step.** Update `README.md` and the pages in `wiki/` when behavior, options or credential values change.

## Adding a target type

1. Add a `case` for the new target type in `SendRule` in the Rules assembly. Return a `ReplicationRuleResult` with one `ReplicationRecordOutcome` per record.
2. Add a helper under `Helpers/`, and models under a folder for the target in `Models/`. Add them to both projects.
3. Add the target's credential keys to the Credentials form's `UpdateValueLabels` script.
4. Add its option keys to the Rules form's `UpdateVariableUI` script. An option is free text unless you give it choices, as in `Option("DeleteBehavior", Choice("Skip", "Skip deletes."), Choice("Delete", "Send deletes through."))`. Each choice is the value that is stored and a description shown beside it. An option with choices shows as a combobox on the form. A value or description can't contain a comma, a backslash or a parenthesis.
5. Make `SendRule` read the same positions the forms label.
6. Document it: a row in the README tables and a `Target-<Name>` page here.

## Changing the trigger action

The trigger action's `Parameters` string is built in `SetupTriggers` in the Config assembly and compared exactly in two IDO derived properties. Change all three together. See [Architecture](Architecture#triggers). After the change, every IDO with triggers shows **Broken** until **Fix Triggers** is run.

## Editing this wiki

The pages live in the repository's `wiki/` folder and are published by the `Publish wiki` GitHub Actions workflow when a change to `wiki/` reaches `main`.

- Edit the files in `wiki/` and open a pull request. Don't edit pages in the wiki's web editor: the next publish overwrites them.
- A file's name is its page name, with hyphens for spaces. `_Sidebar.md` is the navigation.
- Link to another page by its name without `.md`, for example `[Installation](Installation)`.
