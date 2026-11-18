#!/usr/bin/env bash
#
# Fails when Helpers/ or Models/ differ between the two extension class projects.
#
# The two projects each keep their own copy of these folders, and the copies must be identical except for the
# project name (the namespace). A change made to one copy and not the other can still compile, so the build
# alone doesn't catch it.
#
# Run from the repository root:  bash .github/scripts/compare-shared-code.sh

set -euo pipefail

base="src/IDO Extension Class Custom Assemblies"
projects=("ue_AIR_IDOReplicationRules_ECA" "ue_AIR_IDOReplicationConfig_ECA")
folders=("Helpers" "Models")

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Copy each project's shared folders with the project name replaced by a placeholder, so that only real
# differences are left to compare.
for project in "${projects[@]}"; do
  source_dir="$base/$project/$project"

  for folder in "${folders[@]}"; do
    if [ ! -d "$source_dir/$folder" ]; then
      echo "::error::Folder not found: $source_dir/$folder"
      exit 1
    fi

    while IFS= read -r -d '' file; do
      relative="${file#"$source_dir/"}"
      mkdir -p "$work/$project/$(dirname "$relative")"
      sed "s/$project/PROJECT/g" "$file" > "$work/$project/$relative"
    done < <(find "$source_dir/$folder" -type f -print0)
  done
done

if diff --recursive --unified "$work/${projects[0]}" "$work/${projects[1]}"; then
  echo "Helpers/ and Models/ match in both projects."
else
  echo "::error::Helpers/ and Models/ differ between ${projects[0]} and ${projects[1]}. Apply the same change to both copies. The differences are listed above."
  exit 1
fi
