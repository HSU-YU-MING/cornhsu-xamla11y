#!/usr/bin/env bash
# CI 守門：示範專案是刻意做壞的，所以預期 exit 1，而且每一種 finding 的數量固定。
# 同時驗證兩件事：工具真的會擋，而且擋的數量沒有莫名其妙變動（改了規則導致誤判變多，CI 立刻紅）。
# 計數用 node 讀，不用 jq：本機（Windows）沒有 jq，CI 兩種都有。

set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
REPORT="demo-report.json"
trap 'rm -f "$REPORT"' EXIT

dotnet run --project src/XamlA11y.Cli -c Release --no-build -- audit samples/demo --json "$REPORT"
CODE=$?
[ "$CODE" -eq 1 ] || { echo "FAIL: expected exit 1 from the deliberately broken demo, got $CODE"; exit 1; }

node -e '
const r = JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"));
const want = { "missing-name": 1, "text-not-in-name": 1, "meaningless-name": 1, "symbol-name": 1, "input-no-name": 1,
               "focus-not-visible": 1, "mouse-only": 1, "not-in-tab-order": 1, "small-target": 2, "list-no-name": 1,
               "widget-no-name": 1, "item-type-name": 1, "silent-tab-stop": 1, "same-name": 1 };
let bad = 0;
const check = (name, got, expected) => {
  if (got === expected) console.log("ok: " + name + " = " + got);
  else { console.log("FAIL: expected " + expected + " " + name + ", got " + got); bad = 1; }
};
for (const [k, n] of Object.entries(want)) check(k, r.summary.counts[k], n);
check("suppressed", r.summary.suppressed, 1);
check("blocked", r.summary.blocked, 1);
check("unknown control", r.summary.unknownControls.map(u => u.element).join(), "FancyButton");
check("kinds in the report", Object.keys(r.summary.counts).length, Object.keys(want).length);
process.exit(bad);
' "$REPORT"
