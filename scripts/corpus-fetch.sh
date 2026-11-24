#!/usr/bin/env bash
# 抓公開 WPF 專案（docs/corpus.tsv，commit 釘死）：只要 XAML、C#、專案檔，sparse + partial clone。
#
#   scripts/corpus-fetch.sh [名稱…]
#
# 放在 ~/.xamla11y-corpus（或 XAMLA11Y_CORPUS）。已經在那個 commit 的不重抓。
# ⚠ 只是讀文字：不建置、不執行裡面的任何東西。
set -uo pipefail
HERE=$(cd "$(dirname "$0")/.." && pwd)
CORPUS=${XAMLA11Y_CORPUS:-$HOME/.xamla11y-corpus}
mkdir -p "$CORPUS"
tail -n +2 "$HERE/docs/corpus.tsv" | tr -d '\r' | while IFS=$'\t' read -r name repo commit _; do
  if [ $# -gt 0 ] && ! printf '%s\n' "$@" | grep -qx "$name"; then continue; fi
  dir="$CORPUS/$name"
  if [ "$(git -C "$dir" rev-parse HEAD 2>/dev/null)" = "$commit" ]; then echo "have $name"; continue; fi
  mkdir -p "$dir"
  if [ ! -d "$dir/.git" ]; then
    git -C "$dir" init -q
    git -C "$dir" remote add origin "https://github.com/$repo"
    git -C "$dir" config extensions.partialClone origin
    git -C "$dir" config remote.origin.partialclonefilter blob:none
    git -C "$dir" sparse-checkout set --no-cone '/**/*.xaml' '/**/*.cs' '/**/*.vb' '/**/*.csproj' '/**/*.vbproj' '/**/*.props' '/**/*.targets' '/**/packages.config'
  fi
  for try in 1 2 3; do
    git -C "$dir" fetch -q --depth 1 --filter=blob:none origin "$commit" 2>/dev/null &&
      git -C "$dir" -c core.longpaths=true -c advice.detachedHead=false checkout -q "$commit" 2>/dev/null && { echo "ok   $name"; break; }
    [ "$try" = 3 ] && echo "FAIL $name"
    sleep 5
  done
done
