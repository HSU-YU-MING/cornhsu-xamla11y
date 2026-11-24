# <img src="https://raw.githubusercontent.com/HSU-YU-MING/cornhsu-xamla11y/main/icon.png" width="28" alt=""/> XamlA11y

**WPF XAML 的靜態無障礙稽核。不開 App 就找出螢幕閱讀器念不出名字、鍵盤到不了的地方，並在 CI 擋住新增的。**

[English](README.md)

```bash
dotnet tool install -g Cornhsu.XamlA11y

xamla11y audit ./src                                    # 先看有什麼
xamla11y audit ./src --write-baseline a11y-baseline.json   # 把今天的記成已知
xamla11y audit ./src --baseline a11y-baseline.json         # 之後只擋新增的

xamla11y fix ./src                                      # 能替你接上名稱的（--write 才會改）
xamla11y walk ./src/Views/Main.xaml                     # 按 Tab 依序停在哪裡、念什麼
```

> 需要 **.NET 10 runtime**。只讀 XML、不相依 WPF，Linux、Windows、macOS 都能跑。
> 還在 **0.x**：請釘確切版本，新版可能改變你會拿到哪些結果。
>
> **只檢查 WPF。** WinUI／UWP、Avalonia、MAUI 的檔案會認出來並回報「沒檢查」。

**既有的專案，第二行要在接 CI 之前做。** 多數 WPF 專案的輸入框一個名稱都沒寫，第一次跑會一口氣列出幾十筆。
一上來就紅的閘門會被關掉；先記成已知，之後不再變多、改到哪個畫面就順手修。

---

## 找什麼

### 念不出來

| 種類 | WCAG 2.2（等級） | 預設 | |
|---|---|---|---|
| `missing-name` | 4.1.2 (A), 1.1.1 (A) | fail | 按鈕、核取方塊、頁籤、選單…，螢幕閱讀器沒有名稱可以念。**ToolTip 不是名稱**，WPF 把它當說明文字。 |
| `text-not-in-name` | 4.1.2 (A), 2.5.3 (A) | fail | 畫面上**有**字，但字在 `StackPanel`、`Border`、`AccessText` 裡。WPF 只從字串、`TextBlock`、`ContentControl` 取名稱。頁籤、清單項目念成型別名，超連結什麼都不念。 |
| `meaningless-name` | 4.1.2 (A), 1.1.1 (A) | fail | 名稱是圖示字型的字元、空白、`System.Windows.Controls.TabItem`、或 Geometry 路徑。 |
| `input-no-name` | 4.1.2 (A), 1.3.1 (A) | fail | `TextBox`、`PasswordBox`、`RichTextBox`、`ComboBox`、`Slider`、`DatePicker` 沒有名稱。**`<Label Target=…>` 不會給名稱**（見下）。 |
| `list-no-name` | 4.1.2 (A), 1.3.1 (A) | warn | `ListBox`、`ListView`、`TreeView`、`DataGrid` 沒有名稱。外面包的 `GroupBox` 不算。 |
| `widget-no-name` | 4.1.2 (A) | warn | `ProgressBar`、`Calendar` 沒有名稱（`Menu`、`ToolBar` 是 info）。 |
| `item-type-name` | 4.1.2 (A) | warn | 資料產生的項目念成 `MyApp.Models.Note`：類別沒覆寫 `ToString()`，容器樣式也沒設名稱。**`DisplayMemberPath` 不會給名稱。** |
| `silent-tab-stop` | 4.1.2 (A) | warn | Tab 停得到、螢幕閱讀器沒聲音：沒設 `Focusable="False"` 的 `ContentControl`、沒有 AutomationPeer 的自訂控制項。 |
| `name-differs` | 2.5.3 (A) | warn | 畫面寫「儲存」，名稱卻是「保留這份文件」。用說的操作的人照畫面念。 |
| `symbol-name` | 1.1.1 (A) | info | 名稱只有 `×`、`…` 或表情符號。 |
| `same-name` | 2.4.6 (AA) | info | 並排的控制項名稱一樣、做的事不同。 |

### 鍵盤到不了

| 種類 | WCAG 2.2（等級） | 預設 | |
|---|---|---|---|
| `mouse-only` | 2.1.1 (A) | fail | `Border`、`Image`、`TextBlock`… 掛了點按（寫在 XAML，或在它的 code-behind 加的），鍵盤沒有路。 |
| `not-in-tab-order` | 2.1.1 (A) | warn | `IsTabStop="False"` 或 `Focusable="False"`，又沒有存取鍵、`IsDefault`／`IsCancel` 這些看得到的入口。 |

### 看不到焦點

| 種類 | WCAG 2.2（等級） | 預設 | |
|---|---|---|---|
| `focus-not-visible` | 2.4.7 (AA) | fail | `FocusVisualStyle` 關掉了，範本與樣式也沒有別的東西畫出焦點。報在關掉的那一行，一次。 |

### 太小

| 種類 | WCAG 2.2（等級） | 預設 | |
|---|---|---|---|
| `small-target` | 2.5.8 (AA) | warn | **你自己設的**大小小於 24 DIP，而且離鄰居太近、用不了間距例外。 |

`xamla11y rules` 會印同一份清單與修法；每一種的前後寫法在 [docs/修法.md](docs/修法.md)。

依據是 **WCAG 2.2 的 A 與 AA**：55 條裡的 8 條，照 [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) 的方式套到桌面軟體。
每一種為什麼是那一條、什麼情況不算違反、跟 EN 301 549／Section 508／台灣規範差在哪、還有什麼要人來看：[docs/標準對照.md](docs/標準對照.md)。
`--standard` 與 `--level` 可以換一個標準（WCAG 2.1、2.0、EN 301 549、Section 508）；HTML、JSON、SARIF 報告也帶 EN 301 549 與 Section 508 的條號。

這**不是** WCAG、EN 301 549 或歐盟無障礙法（EAA）的合規聲明：靜態工具只看得到其中一部分。

## `Label.Target` 不會給名稱

最常見的誤會，所以獨立一節。下面的寫法只讓存取鍵有目標，**TextBox 還是沒有無障礙名稱**：

```xml
<Label Content="_Name" Target="{Binding ElementName=NameBox}"/>
<TextBox x:Name="NameBox"/>
```

在 .NET Framework 4.8 與 .NET 8、9、10、11 用 UI Automation client 實測，Axe-Windows 也會報。要這樣寫：

```xml
<Label x:Name="NameLabel" Content="_Name" Target="{Binding ElementName=NameBox}"/>
<TextBox x:Name="NameBox" AutomationProperties.LabeledBy="{Binding ElementName=NameLabel}"/>
```

浮水印也不會變成名稱（MahApps.Metro 2.4、HandyControl 3.5、WPF-UI 4.3）；唯一的例外是
MaterialDesignThemes 5.3 會把 `HintAssist.Hint` 當名稱，工具認得。

## 自動接上已經有的名稱

```bash
xamla11y fix ./src            # 只說會改什麼，不寫
xamla11y fix ./src --write    # 真的改：先 commit，改完看 diff
```

接的是已經在那裡的：指到輸入框的 `Label`（用 `LabeledBy`）、按鈕畫面上唯一的那段字、短的 `ToolTip`。只加屬性，縮排、編碼、換行都不動。
每一處先在記憶體裡重新稽核，沒有用的、弄出別的問題的就不改。不確定的不接：程式碼或觸發器會換的字、兩段字、一整句說明、快速鍵。
`--guess` 連欄位前面那段字、綁定的字也接：那是猜的，要看過 diff。

## 走一遍畫面

`xamla11y walk Views/Main.xaml` 照順序列出按 Tab 停在哪裡：第幾行、名稱、NVDA 怎麼念、那裡有什麼問題、哪裡 Tab 進去就出不來。
順序跟真的 WPF 視窗逐案比過。範本、程式碼、資料加進來的不在裡面，要看它們的標成 `?`。

## 能信到什麼程度

- **每條規則都跟真的比過。** 大約 21,000 個答案，對照真的 WPF 視窗、UI Automation client、像素、Axe-Windows、NVDA，沒有一個說錯。
  錄下來的答案跟著測試走，每次改動 CI 都比一遍。
- **看到多少會講。** *可解析率* = 該有答案的元素裡，真的判得出來的比例。低於 `--min-coverage`（60）是 exit 3「結果不可信」，不是 0。
  49 個公開專案與 3 個 App 不設定直接跑，38 個在 85% 以上；沒人說過的控制項庫是「不知道」，用 [`treatAs`](docs/config-schema.md) 宣告。
- **報告上的「NVDA 會念」只寫跑過的。** 朗讀程式（Narrator）與 JAWS 沒跑過。

每個專案的數字：[docs/驗證.md](docs/驗證.md)。

**看不到的**

- C# 建的控制項與掛在上面的事件：會數出來寫在報告上，沒有檢查。只讀 XAML 檔自己的 code-behind，用來發現「它設了這個控制項的名稱或內容」（→ 不知道）、「它會把藏起來的打開」、「它對有名字的元素掛了點按」。
- 視窗資源裡的隱含樣式，套進它裡面放的 `UserControl`。
- 資料給的名稱。`ItemsSource` 產生的項目看得到有沒有東西給名稱，看不到名稱是什麼。
- 讀取順序、即時區域、高對比、名稱**好不好**、圖片是不是裝飾。
- 沒有名稱的 `ItemsControl`（Axe-Windows 會報，多半只是排版）。付費控制項（Telerik、DevExpress、Syncfusion…）都沒量過，一律不知道。
- 大小或鄰居要到執行期才知道的點擊範圍：不判。

## 選項

| | |
|---|---|
| `--html <檔>` | 給人看的一頁，沒有指令碼、不連外：是什麼、為什麼、怎麼修、NVDA 會怎麼念、條號、要人檢查的清單 |
| `--json`、`--sarif`、`--md <檔>` | 完整報告、code scanning、PR 留言用的 Markdown |
| `--standard <標準>`、`--level A\|AA` | `wcag22`（預設）、`wcag21`、`wcag20`、`en301549`（V3.2.1）、`en301549-v4`、`section508` —— [會變什麼](docs/標準對照.md#選一個標準) |
| `--lang en\|zh-Hant` | 主控台、Markdown、HTML 報告的語言。JSON、SARIF、MSBuild 與選項打錯的訊息一律英文 |
| `--title <字>` | HTML 報告的標題，預設是資料夾的名字 |
| `--only <檔\|資料夾>` | 只報這些；直接給 `.xaml` 也一樣。樣式與資源照樣從整個專案讀 |
| `--msbuild` | MSBuild 診斷：`XA0001` 擋下的、`XA0002` 不擋的、`XA0003` 結果不可信 |
| `--baseline`、`--write-baseline <檔>` | 只擋新增的。鍵不含行號 |
| `--prune-baseline` | 把 baseline 裡已經沒有的拿掉。重寫一次會連新的一起記成已知 |
| `--fail-on fail\|warn` | 擋到哪個嚴重度 |
| `--min-coverage <0-100>` | 可解析率低於它就 exit 3 |
| `--config <檔>` | 預設 root 裡的 `xamla11y.config.json`（[欄位](docs/config-schema.md)） |
| `--exclude <字>` | 路徑含這個字的跳過 |

退出碼：**0** 通過 · **1** 有 `--fail-on` 以上的 · **2** 用法、設定或輸入錯誤 · **3** 結果不可信。

要放過一個元素，在它前面寫註解，**理由必填**，沒寫理由等於沒寫。中間可以隔別的註解，不能隔別的元素或文字。
理由會列在報告上。沒寫理由、或停在文字、被註解掉的元素前面的沒有作用，執行時會講出來；已經沒有東西要放過的也會講：

```xml
<!-- xamla11y-ignore: 整列才是控制項，這個核取方塊只是顯示狀態 -->
<CheckBox IsChecked="{Binding IsSelected}"/>
```

## GitHub Action

```yaml
permissions:
  contents: read
  pull-requests: write     # PR 留言
  # security-events: write # sarif: true 才要

steps:
  - uses: actions/checkout@v4
  - uses: HSU-YU-MING/cornhsu-xamla11y@v0.1.0
    with:
      root: src
      version: 0.1.0
      baseline: a11y-baseline.json
```

每一筆標在那一行、Markdown 報告變成一則會原地更新的 PR 留言、JSON、Markdown、HTML 上傳成 artifact，全部出去之後最後才擋。

| 輸入 | 預設 | |
|---|---|---|
| `root` | `.` | 要掃的資料夾 |
| `working-directory` | `.` | `root`、`config`、`baseline` 相對於它 |
| `version` | 最新 | 工具版本，請釘 |
| `config` | root 裡的檔 | `xamla11y.config.json` |
| `baseline` | | 進 repo 的 baseline |
| `fail-on` | `fail` | `fail` 或 `warn` |
| `min-coverage` | 設定檔，沒有就 `60` | |
| `comment` | `true` | 貼 PR 留言 |
| `upload-report` | `true` | 上傳 JSON、Markdown、HTML |
| `sarif` | `false` | 上傳 code scanning |
| `artifact-name` | `xamla11y-report` | 也決定 PR 留言是哪一則 |

## 跟三件套一起用

| | 問的問題 |
|---|---|
| [XamlContrast](https://github.com/HSU-YU-MING/cornhsu-xamlcontrast) | 看不看得清楚（顏色對比） |
| [XamlKeys](https://github.com/HSU-YU-MING/cornhsu-xamlkeys) | 找不找得到（資源鍵） |
| [XamlFit](https://github.com/HSU-YU-MING/cornhsu-xamlfit) | 裝不裝得下（文字寬度） |
| **XamlA11y** | 按不按得到、念不念得出來 |

退出碼、Action 輸入都一樣；報告檔、artifact、PR 留言、SARIF 工具名各自分開，四支可以放在同一個 job：
[docs/four-tools.md](docs/four-tools.md)。`AutomationProperties.Name` 背後的資源鍵不存在是 XamlKeys 的事，這支把它當成有名稱。

## 授權

MIT。見 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
