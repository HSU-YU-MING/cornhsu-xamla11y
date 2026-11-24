# XamlA11y — 給接手的人

一句話：**不開 App，就找出鍵盤按不到、螢幕閱讀器念不出來的地方。** 完整設計在 `XamlA11y專案規畫書.md`，
WPF 的事實在 `docs/M1-UIA實測.md`。這份只記**踩過的坑與不要「修正」的設計**，每一條都有實查來源。

## 憲法

1. **靜態分析 + 全量掃描 + CI 退出碼。**（三件套同一條）
2. **「工具沒看到」與「看到了沒問題」分得開。** 讀不出來 = `Blocked`（進可解析率分母）；靜態不可能知道 = `NotJudged`（不進分母、但要數）。
3. **每條規則都拿真的 WPF 對照過。** 加規則、改規則之前先在 `prototype/UiaOracle/facts/` 加案例。
4. **永遠開火的閘門會被關掉。** 預設只擋確定的；R5、R6 是 warn，符號名稱是 info。

## 改規則的流程

1. 在 `prototype/UiaOracle/facts/*.txt` 加案例（一行一案，引號寫單引號）。
2. `dotnet build prototype/UiaOracle -c Release`，跑 `uiaoracle check`（約 4 分鐘，會在螢幕左上角開小視窗、搶焦點）。**任何一個 WRONG 都不能放過。**
3. `uiaoracle check-corpus <專案資料夾…>`：真實專案的形狀。`uiaoracle check-windows <專案資料夾…>`：整個檔案連樣式、資源載進真的 WPF（視窗在螢幕外，不搶焦點；50 個專案約 40 分鐘）。
   別人的 XAML 一律過 `SafeMarkup`（白名單）才交給 XamlReader；改它之後跑 `uiaoracle check-hostile`。
4. `uiaoracle record tests/XamlA11y.Tests/uia-facts.json`：把 WPF 的答案錄給跨平台的測試。**只錄真值，不錄工具的答案。**
5. `scripts/corpus-fetch.sh` 抓語料（`docs/corpus.tsv`，commit 釘死），`scripts/corpus-run.sh <輸出資料夾>` 前後各一次，`node scripts/corpus-diff.mjs 前 後` 逐類抽樣看原始碼，`node scripts/readme-corpus.mjs <輸出> --update` 重貼 README 的表。Oracle 抓規則想錯，語料抓規則想漏 —— 兩個都要。

## 不要「修正」的設計

- **`Label.Target` 不算名稱。** 很多人（包括規劃書第一版）以為會。實測五個執行期都不會，原始碼也確認：`GetLabeledByCore` 只讀 `AutomationProperties.LabeledBy`。
- **範本裡的文字不算名稱**，所以「範本被換掉」不影響 R1／R2。不要為了「保守」把它改成不知道，那會讓可解析率掉一大截而且是錯的。
- **R3 報在樣式的 Setter，不報在每個用到的元素**：修的地方只有一個。
- **`DataBound` 不進分母**：`Content="{Binding}"` 專案改不了「靜態看不到」。
- **C# 的 `class X : Button` 會自動跟**，但只要那個檔案提到 `AutomationProperties` 或 `OnCreateAutomationPeer` 就整個算不知道。
- **訊息裡的文字一律當不可信輸入**：console 走 `Text.Safe`、Markdown 走 `Code`。新增輸出點時照做。
- baseline 的鍵不含行號。

## 實查踩過的坑

| # | 坑 | 後果 | 來源 |
|---|---|---|---|
| 1 | 在行程內直接建 AutomationPeer 讀名稱 | `LabeledBy` 與項目容器的答案不對（沒有 client 走過樹，標籤的 peer 還不存在）。要從**另一條執行緒的 UIA client** 讀 | net48 比對 12 案不同 |
| 2 | 模擬按鍵不會讓 WPF 認為「最後的輸入是鍵盤」 | 焦點框根本沒畫，整組量測是 0 —— 連預設的 Button 也是。要打開 `KeyboardNavigation.AlwaysShowFocusVisual`。**沒有正向對照的量測不能用** | focus 第一輪 |
| 3 | Axe-Windows 刻意不掃自己的行程 | 例外訊息是「desktopElements 不能是空的」，看不出原因。開子行程掃 | names --axe |
| 4 | 內容是圖示的 Button 有 `Command=ApplicationCommands.Copy` | 名稱是命令的文字。第一版報 missing-name | Oracle（手寫難例） |
| 5 | TextBox 當 Button 的內容 | 它的 Text 是純文字、會變名稱 | 窮舉 |
| 6 | 只有 InlineUIContainer 的 TextBlock／Hyperlink | 名稱是**一個空白**，不是空的 —— 所以命令的文字也不會補上 | 窮舉 |
| 7 | 項目容器（TabItem、ListBoxItem、TreeViewItem）沒文字 | 名稱是 `ToString()` 的型別名，UIA 的 Name **不是空的** | names |
| 8 | 附加屬性截成最後一段（`hc:IconElement.Height` → `Height`） | 166 筆「按鈕 12 高」誤報。擁有者不是元素自己的基底型別就要整個留著。**XamlFit 的 StyleIndex 也有這個問題** | HandyControl |
| 9 | 「讀不出來」當成「有設」 | 樣式在專案外時 `Resolve` 對任何屬性都回 Unreadable；880 個輸入框因此被當成有 MaterialDesign 的 Hint。要看 `Origin` 是不是 null | NETworkManager |
| 10 | 佈景底下，專案自己的樣式沒寫 BasedOn | 佈景的隱含樣式**整個被取代**，用的是 WPF 內建範本，不是佈景的 | Oracle（check-theme-focus） |
| 11 | 焦點畫在範本引用的另一個範本裡 | MaterialDesign Slider 的光圈在 Thumb 的範本、用 DataTrigger 綁祖先的 IsFocused | focus-themes |
| 12 | `<Hyperlink.Inlines>`、`<FrameworkElement.Style>`、`<ContentControl.Content>` | 屬性元素的擁有者不一定跟元素同名 | Playnite、ILSpy |
| 13 | 互相引用的資源別名 | 堆疊溢位。遞迴的地方都要有深度上限 | CliTests |
| 14 | 佈景的焦點常是動畫 | 拍照前要等（`SettleMilliseconds`），不然看起來沒變 | focus-themes |
| 15 | Bash 的 heredoc 會吃反斜線、指令太長會被截斷 | 寫程式碼檔用 Write 工具；改既有檔案寫一支 `.js` 找錨點取代，找不到要丟例外 | 這一輪踩了四次 |
| 16 | PowerShell 5.1 把沒有 BOM 的檔案當 Big5 讀 | 中文註解吃掉下一行的 `param`。`net48.ps1` 只能用 ASCII | net48.ps1 |
| 17 | XamlContrast 1.8.3 與 XamlFit 0.12.0 以前只認最近的那個 ignore 註解 | 兩支疊在同一個元素上，遠的那個失效。1.9.0、0.13.0（與 XamlKeys 0.4.0）起四支同一條規則、不挑順序，見第 32 條與 docs/four-tools.md | 四支實跑 |
| 18 | 設計期屬性（`d:Content`）、別的 XAML 前綴（`xaml:Null`） | 屬性只比區域名稱就會把設計期的當真。載入時統一處理（`SafeXml.Normalize`） | 獨立審查 |
| 19 | 樣式的型別只比區域名稱 | `TargetType="ui:Button"` 套到 WPF 的 Button。一律用 `Markup.TypeId` | 獨立審查 |
| 20 | 沒人合併的資源字典 | 裡面的隱含樣式套不到任何人。`Project.Link` 算合併關係，程式碼提到檔名的當成看得到 | 獨立審查 |
| 21 | `ElementName` 看進別的範本、指到沒有 peer 的元素 | 名稱範圍一層一層往外找；`Wpf.HasPeer` | 獨立審查 |
| 22 | 內容只在觸發器裡設 | 觸發器沒成立時是空的，基底狀態要一起算 | 獨立審查 |
| 23 | 專案自己的 `local:ApplicationCommands`（Prism 慣例） | 不是 WPF 的命令，要看前綴的命名空間 | 獨立審查 |
| 24 | R3 只看樣式不看用它的元素 | 元素可能換了範本、加回焦點框、不能聚焦。現在逐一看使用者，找不到使用者才看樣式自己 | 獨立審查 |
| 25 | 深度有上限、分支沒有 | 600 位元組的 XAML 跑不完。`NameResolver.MaxSteps` | 獨立審查 |
| 26 | 每個控制項都自動通過 R5 | 可解析率被灌水，名稱 75% 看不到還是過。R5 只算有設屬性的；可解析率只看 R1～R4 | 獨立審查 |
| 27 | 讀 code-behind 沒去掉註解與字串、分不出讀和寫 | 註解掉的程式也算。`CSharp.Strip` + 要有 `=`（不是 `==`） | 獨立審查 |
| 28 | 降過級的 finding 變嚴重了，baseline 還是蓋住 | 鍵加「~」 | 獨立審查 |
| 29 | MaterialDesign 的 Hint 名稱來自**樣式** | 樣式被換掉、佈景沒合併就沒有。光看套件版本不夠 | 獨立審查 → Oracle 確認 |
| 30 | 疊在按鈕上的 TextBlock 做同一個動作 | 鍵盤本來就有路。同檔有鍵盤控制項綁同一個命令／處理器就不報 | MAA |
| 31 | xunit 的字串 Assert 預設用文化比對 | 軟連字號是可忽略字元，`DoesNotContain` 永遠失敗。要 `StringComparison.Ordinal` | ReviewTests |
| 32 | 被註解掉的元素在 XML 裡也是註解 | 「註解都跳過、元素才停」會讓前面的 ignore 改壓下一個元素。註解以 `<` 開頭就停，文字也停（處理指令跳過），而且要警告（四件套一致，使用者 2026-11-24 定） | 四支對照實跑 |
| 33 | ToolBar 把直接子元素的隱含樣式換掉 | 隱含樣式給的名稱在工具列裡沒有 —— 偏偏圖示按鈕都在那裡（假通過） | 第二輪審查 → Oracle 確認 |
| 34 | `ItemContainerStyle` 沒算 | 名稱寫在那裡的 TabItem、ListBoxItem 被報「念不出來」 | 第二輪審查 → Oracle 確認 |
| 35 | 樣式觸發器的 Content 壓過本地內容 | 本地值才是贏的 | panuon 語料 |
| 36 | Run 一起判 | 每個 Run 有自己的字型、可以是綁定、文字可以來自它的樣式 | 第二輪審查、check-windows（NuGet Package Explorer） |
| 37 | 「Content = x」就是類別設自己的內容 | 物件初始設定式 `new ToolTip { Content = x, }` 也長這樣；看等號後面先遇到 `;` 還是 `,`／`}` | Playnite |
| 38 | 測試全綠 ≠ 有人守 | 變異測試 120 處只抓到 60。多數是清單少一項：`PinnedTests` 一項一個案例 | 變異測試 |
| 39 | 深度有上限、沒記住答案 | 同名類別互相當基底，142 KB 的 .cs 跑不完。`DerivedFrom` 有記憶 | 第二輪審查 |
| 40 | LINQ to XML 的 `PreviousNode` 是從頭數 | 三萬個兄弟元素 39 秒。用 `Markup.Previous` | 第二輪審查 |
| 41 | XamlReader 會建立 XAML 指名的任何型別 | Oracle 讀的是別人的 XAML。`SafeMarkup` 是白名單、不看清理做了什麼，自己從頭檢查 | 自動安全審查 |

## 沒做的、知道的

- `ListBox`、`DataGrid`、`TreeView`、`Calendar`、`ProgressBar` 沒有名稱：Axe 會報，這支不判（第一版範圍；會讓第一次跑的清單再長一倍）。2026-11-24 實跑 QuillNest：Axe 在真的畫面上報的按鈕、輸入框名稱全部對得上，多出來的就是清單與「兄弟同名」兩類。
- 高度引擎：R6 只用明寫的大小與固定的 Grid 格子。沒設大小的是平台預設，WCAG 2.5.8 有例外，所以不需要算預設高度。
- PresentationFramework.Fluent（.NET 9+ 的 ThemeMode）量過了（`focus-themes`），但沒有套件版本可比對，所以當沒量過。
- `.vb` 的類別繼承不跟（code-behind 的 `.vb` 有讀）。
