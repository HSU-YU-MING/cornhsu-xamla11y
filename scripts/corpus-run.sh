#!/usr/bin/env bash
# 語料重跑：公開專案（XamlContrast 的 ~/.xamlcontrast-corpus，commit 釘在那邊）與作者的 App。
#
#   scripts/corpus-run.sh <輸出目錄> [名稱…]
#
# 印一張表（Markdown）：每個專案的候選、可解析率、每一種 finding 的筆數。完整 JSON 在輸出目錄。
# 改規則前後各跑一次，用 scripts/corpus-diff.mjs 看哪些 finding 變了。
set -euo pipefail
OUT=$1; shift
HERE=$(cd "$(dirname "$0")/.." && pwd)
CORPUS=${XAMLA11Y_CORPUS:-$HOME/.xamlcontrast-corpus}
APPS=${XAMLA11Y_APPS:-}
EXE="$HERE/src/XamlA11y.Cli/bin/Release/net10.0/xamla11y"
[ -x "$EXE" ] || EXE="$EXE.exe"
mkdir -p "$OUT"
for dir in "$CORPUS"/*/ ${APPS:+"$APPS"/*/}; do
  name=$(basename "$dir")
  if [ $# -gt 0 ] && ! printf '%s\n' "$@" | grep -qx "$name"; then continue; fi
  "$EXE" audit "$dir" --json "$OUT/$name.json" --min-coverage 0 > "$OUT/$name.txt" 2>&1 || true
done
node "$HERE/scripts/corpus-table.mjs" "$OUT"
