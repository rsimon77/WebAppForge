"""Moves literal UI text out of the desktop AXAML files into Localization/Strings.resx.

Every literal Text, Header, Content, ToolTip.Tip, PlaceholderText, Watermark, Title, AutomationProperties.Name and
TabItem Tag value is replaced by {l:Tr Key}, and the English text is added to Strings.resx. Run it again after adding
views or text; existing keys are kept and reused. Values listed in desktop-strings-ignore.txt (brand names, program
names, key captions) stay as they are. LocalizationGuardTests fails while any other literal text is left.

Usage: python scripts/extract-desktop-strings.py [--check] [--prune]
  --check  only list the literal text that would be moved; exit code 1 when there is any.
  --prune  also drop Strings.resx entries whose key no .axaml or .cs file of the desktop app names. Keys that code builds
           at run time (such as "Tab_" + id) must also be written out somewhere, or they are dropped.
"""

import html
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DESKTOP = ROOT / "src" / "WebAppForge.Desktop"
RESX = DESKTOP / "Localization" / "Strings.resx"
IGNORE = Path(__file__).resolve().parent / "desktop-strings-ignore.txt"
XMLNS = 'xmlns:l="using:WebAppForge.Desktop.Localization"'

# The same list is in LocalizationGuardTests.
ATTRIBUTES = r"Text|Header|Content|ToolTip\.Tip|PlaceholderText|Watermark|Title|AutomationProperties\.Name|AutomationProperties\.HelpText"
LITERAL = re.compile(r'(?<=\s)(' + ATTRIBUTES + r')="([^"{][^"]*)"')
TAB_TAG = re.compile(r'(<TabItem\b[^>]*?\sTag=)"([^"{][^"]*)"')

RESX_HEADER = """<?xml version="1.0" encoding="utf-8"?>
<!-- English texts of the desktop app. Generated and updated by scripts/extract-desktop-strings.py; edit the values freely. -->
<root>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
"""


def view_files():
    return sorted((DESKTOP / "Views").glob("*.axaml"))


def prefix(path):
    stem = path.stem
    for suffix in ("View", "Window"):
        if stem.endswith(suffix) and stem != suffix:
            stem = stem[: -len(suffix)]
    return stem


def slug(text):
    words = re.findall(r"[A-Za-z0-9]+", text)
    return "".join(w[:1].upper() + w[1:] for w in words[:6]) or "Text"


def is_text(value, ignored):
    return re.search(r"[A-Za-z]", value) is not None and value not in ignored


def load_ignored():
    if not IGNORE.exists():
        return set()
    return {line.rstrip("\n") for line in IGNORE.read_text(encoding="utf-8").splitlines() if line and not line.startswith("#")}


def load_resx():
    if not RESX.exists():
        return {}
    entries = {}
    for data in ET.parse(RESX).getroot().findall("data"):
        entries[data.get("name")] = data.findtext("value") or ""
    return entries


def save_resx(entries, path=RESX, header=RESX_HEADER):
    lines = [header]
    for key in sorted(entries, key=str.lower):
        value = html.escape(entries[key], quote=False)
        lines.append(f'  <data name="{key}" xml:space="preserve">\n    <value>{value}</value>\n  </data>\n')
    lines.append("</root>\n")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(lines), encoding="utf-8", newline="\n")


def literals(source, ignored):
    found = [html.unescape(m.group(2)) for m in LITERAL.finditer(source)]
    found += [html.unescape(m.group(2)) for m in TAB_TAG.finditer(source)]
    return [v for v in found if is_text(v, ignored)]


def main():
    check = "--check" in sys.argv
    ignored = load_ignored()
    files = {path: path.read_text(encoding="utf-8") for path in view_files()}

    if check:
        left = [(p.name, v) for p, s in files.items() for v in literals(s, ignored)]
        for name, value in left:
            print(f"{name}: {value}")
        return 1 if left else 0

    entries = load_resx()
    by_value = {}
    for key, value in entries.items():
        by_value.setdefault(value, key)

    # A text used in two or more views gets one Common_ key, so it is translated once.
    usage = Counter()
    for source in files.values():
        for value in set(literals(source, ignored)):
            usage[value] += 1

    def key_for(path, value):
        if value in by_value:
            return by_value[value]
        base = ("Common" if usage[value] > 1 else prefix(path)) + "_" + slug(value)
        key, n = base, 2
        while key in entries and entries[key] != value:
            key, n = f"{base}{n}", n + 1
        entries[key] = value
        by_value[value] = key
        return key

    changed = 0
    for path, source in files.items():
        def replace(match):
            value = html.unescape(match.group(2))
            if not is_text(value, ignored):
                return match.group(0)
            return f'{match.group(1)}="{{l:Tr {key_for(path, value)}}}"'

        def replace_tag(match):
            value = html.unescape(match.group(2))
            if not is_text(value, ignored):
                return match.group(0)
            return f'{match.group(1)}"{{l:Tr {key_for(path, value)}}}"'

        updated = TAB_TAG.sub(replace_tag, LITERAL.sub(replace, source))
        if updated != source:
            if XMLNS not in updated:
                updated = re.sub(r'(xmlns:x="http://schemas\.microsoft\.com/winfx/2006/xaml")', r"\1\n        " + XMLNS, updated, count=1)
            path.write_text(updated, encoding="utf-8", newline="")
            changed += 1

    if "--prune" in sys.argv:
        sources = [p.read_text(encoding="utf-8") for p in DESKTOP.rglob("*") if p.suffix in (".axaml", ".cs") and "obj" not in p.parts and "bin" not in p.parts]
        used = set(re.findall(r"[A-Za-z0-9]+_[A-Za-z0-9_]+", "\n".join(sources)))
        for key in [k for k in entries if k not in used]:
            print(f"Dropped unused key {key}")
            del entries[key]

    save_resx(entries)
    print(f"{changed} view(s) updated, {len(entries)} string(s) in {RESX.relative_to(ROOT)}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
