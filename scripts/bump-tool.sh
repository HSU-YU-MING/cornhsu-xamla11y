#!/usr/bin/env bash
# 把四件套裡某一支（或四合一的 Action）換成新版：docs/four-tools.md 與 ci.yml 的 four-tools job 一起改。
#   bash scripts/bump-tool.sh XamlContrast 1.12.0
#   bash scripts/bump-tool.sh xaml-audit 0.1.2
# 改完由 FourToolsTests 核對。只改檔案，不 commit。
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

usage() { echo "usage: bash scripts/bump-tool.sh XamlContrast|XamlKeys|XamlFit|XamlA11y|xaml-audit <x.y.z>" >&2; exit 2; }
[ $# -eq 2 ] || usage
case "$1" in
  XamlContrast | XamlKeys | XamlFit | XamlA11y | xaml-audit) tool=$(tr 'A-Z' 'a-z' <<< "$1") ;;
  *) usage ;;
esac
[[ "$2" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || usage

changed=0
for f in docs/four-tools.md .github/workflows/ci.yml; do
  # uses 那一行換 tag；它後面、下一個 uses 之前的 version 換成同一版
  awk -v tool="$tool" -v ver="$2" '
    /uses:/ { on = 0 }
    $0 ~ ("uses:[ ]*HSU-YU-MING/cornhsu-" tool "@v[0-9]") { sub(/@v[0-9]+\.[0-9]+\.[0-9]+/, "@v" ver); on = 1 }
    on && /version: *[0-9]/ { sub(/version: *[0-9]+\.[0-9]+\.[0-9]+/, "version: " ver); on = 0 }
    { print }
  ' "$f" > "$f.tmp"
  if cmp -s "$f" "$f.tmp"; then rm -f "$f.tmp"; else cat "$f.tmp" > "$f"; rm -f "$f.tmp"; changed=1; echo "$f"; fi
done
[ "$changed" -eq 1 ] || { echo "bump-tool: $1 is already $2" >&2; exit 1; }
