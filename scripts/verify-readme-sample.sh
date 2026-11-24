#!/usr/bin/env bash
# README 裡貼的示範輸出要跟工具現在真的跑出來的一樣（兩份 README：英文的有範例，中文的沒有就跳過）。
# 不一致 exit 1；確認新輸出才是對的，用 --update 重貼。
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
OUT="$(dotnet run --project src/XamlA11y.Cli -c Release --no-build -- audit samples/demo 2>/dev/null)"
CODE=$?
[ "$CODE" -eq 1 ] || { echo "FAIL: expected exit 1 from the demo, got $CODE"; exit 1; }
SAMPLE="$OUT" node scripts/readme-sample.mjs "${1:-}"
