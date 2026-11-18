#!/usr/bin/env python3
"""Writes each form's script out of the FormSync exports, one .cs file per form.

A form's script is stored in its export as one <ScriptLine> element per line. The Build workflow compiles the
extracted files, so a form script that doesn't compile is caught before it is imported into Syteline, where one
compile error stops every script on that form.

Usage, from the repository root:  python .github/scripts/extract-form-scripts.py <output folder>
"""

import glob
import os
import sys
import xml.etree.ElementTree as ElementTree

FORMS_FOLDER = os.path.join("dist", "3 - Mongoose Objects (FormSync)")


def main():
    if len(sys.argv) != 2:
        sys.exit("Usage: extract-form-scripts.py <output folder>")

    output_folder = sys.argv[1]
    os.makedirs(output_folder, exist_ok=True)

    written = 0

    for export_path in sorted(glob.glob(os.path.join(FORMS_FOLDER, "*.XML"))):
        root = ElementTree.parse(export_path).getroot()

        for form in root.iter("Form"):
            form_name = form.get("Name") or os.path.splitext(os.path.basename(export_path))[0]

            for number, script in enumerate(form.iter("Script")):
                language = script.get("Language") or ""
                lines = [line.text or "" for line in script.iter("ScriptLine")]

                if not any(line.strip() for line in lines):
                    continue

                if language != "Visual C#":
                    sys.exit(f"{export_path}: form '{form_name}' has a script in '{language}'. Only Visual C# scripts are checked.")

                suffix = "" if number == 0 else f"_{number + 1}"
                output_path = os.path.join(output_folder, f"{form_name}{suffix}.cs")

                with open(output_path, "w", encoding="utf-8", newline="\n") as output_file:
                    output_file.write("\n".join(lines) + "\n")

                print(f"{output_path}: {len(lines)} lines, from {export_path}")
                written += 1

    if written == 0:
        sys.exit(f"No form scripts were found in {FORMS_FOLDER}.")


if __name__ == "__main__":
    main()
