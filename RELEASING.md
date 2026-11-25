# Releasing

**推 tag 就是發版動作。** 其餘由 GitHub Actions 完成（`release.yml`，NuGet Trusted Publishing，沒有長效金鑰）。

## 版本規則：tag 是唯一真相源

`release.yml` 從 tag 導出版本號再用 `-p:Version=` 覆寫；`src/XamlA11y.Cli/XamlA11y.Cli.csproj` 固定寫 `0.0.0-dev`。
不要改成真的版本號（會跟 tag 漂開），也不要整格刪掉（退回 MSBuild 的 1.0.0）。`ActionContractTests` 守著。

0.x 期間沒有移動式的 major tag；README 教使用者釘確切版本。介面還沒凍結，但改變判定語意照樣升 minor 並在 CHANGELOG 寫 **Upgrading**。

## 每次發布前（CI 不會擋的那些）

- [ ] `CHANGELOG.md` 的版本段落寫完整，日期填好
- [ ] `bash scripts/verify-readme-facts.sh --update`（README 的 Action 範例釘新版；CI 也會擋不一致）
- [ ] 動過 console 輸出：`bash scripts/verify-readme-sample.sh --update`
- [ ] 動過規則：`uiaoracle check`、`check-corpus` 都 0 WRONG（Windows 本機；CI 跑不了），`record` 重錄，`docs/verification.md` 與 `驗證.md` 的表更新
- [ ] 佈景套件升版：重跑 `uiaoracle themes`、`focus-themes`，改 `data/known-controls.json` 的版本與 `THIRD-PARTY-NOTICES.md`
- [ ] `docs/four-tools.md` 與 `ci.yml` 的 `four-tools` job：`bash scripts/bump-tool.sh XamlA11y <這一版>`；三件套有新版也用它（改一半 `FourToolsTests` 會擋，不是最新版不會擋）

## 發版步驟

```bash
git tag v0.1.0
git push origin v0.1.0
```

## 首次發佈的一次性設定

1. GitHub 建 `HSU-YU-MING/cornhsu-xamla11y`（`Directory.Build.props` 的 `RepositoryUrl` 指這裡），推 main，等 CI 綠
2. nuget.org → Trusted Publishing 建一條政策：Package Owner `Cornhsu`、Repository Owner `HSU-YU-MING`、Workflow File `release.yml`、
   Environment 留空、Scope 的 glob `Cornhsu.XamlA11y*` 含「發布新套件」。**政策綁 repo + workflow，不能沿用別支的**
3. GitHub 建 issue 標籤 `false-alarm`、`missed`（issue 表單會用到，不要刪）
4. 新套件 ID 第一次上架約 5～6 分鐘才進安裝索引，別誤判成失敗

## 本機乾跑

```bash
v=0.0.0-local.$(date +%s)
dotnet pack src/XamlA11y.Cli/XamlA11y.Cli.csproj -c Release -o ./artifacts -p:Version=$v
dotnet tool install --tool-path ./artifacts/tool --add-source ./artifacts Cornhsu.XamlA11y --version $v
./artifacts/tool/xamla11y --version
```

一定要 `--version`（不指定會裝到 nuget.org 的正式版）、每次用唯一版本號（同號會拿快取）、用 `--tool-path` 不用 `--global`。

## 發完之後

作品集網站（`portfolio-release-update`）；`cornhsu-xaml-audit` 的 `action.yml` 與兩份 README 換新版；GitHub Release 由 `release.yml` 從 CHANGELOG 建；`D:\擴充功能\nuget\Xaml三件套-下一步.md` 與 `nuget-packages` skill 的版本表。
