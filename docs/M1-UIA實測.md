# M1：WPF 實際怎麼做（UIA Oracle 實測）

2026-11-24。每一條都是把 XAML 開成真的 WPF 視窗、從**另一條執行緒的 UIA client** 讀出來的，並用 Axe-Windows 2.4.2 覆核。
案例檔在 `prototype/UiaOracle/facts/`，重跑：`uiaoracle names --axe`、`templates`、`themes`、`tabs`、`focus`、`label`。

執行期：.NET 8.0.34、9.0.23、10.0.15、11.0.0（146 案逐字相同）、.NET Framework 4.8（同一份案例，見第 6 節）。

---

## 1. 規劃書的假設，哪些被推翻

| 規劃書（第 3 節） | 實測 |
|---|---|
| `Label Target` 可能會給 TextBox 名稱，「規則要認得」 | **不會。** Target 有設上、WPF 內部的 `Label.LabeledByProperty` 也指到 Label，但 `UIElementAutomationPeer.GetLabeledByCore` 只讀 `AutomationProperties.LabeledBy`。TextBox、PasswordBox、ComboBox、Slider、DatePicker、Button 都一樣。Axe 也報。 |
| 範本裡有文字 → 不知道 | **範本裡的文字從不成為名稱**（ControlTemplate 的 TextBlock、ContentTemplate 的文字都是空名稱）。所以範本換成什麼樣不影響名稱判定。 |
| 只有圖示的按鈕沒有名稱 | 對，而且**範圍更大**：`<Button><StackPanel>圖示 + TextBlock</StackPanel></Button>` 也沒有名稱。WPF 只認字串、TextBlock、以及層層 ContentControl；面板、Border、Viewbox、`AccessText` 一律是空的。 |
| 圖示字型的名稱是不是那個私用區字元 | 是（``）。Axe 規則 `NameExcludesPrivateUnicodeCharacters`。 |
| ToolTip 只給 HelpText | 對。 |
| 浮水印會不會變成名稱 | **看佈景**：MaterialDesign 5.3.0 的 `HintAssist.Hint` **會**（TextBox、PasswordBox、ComboBox）；MahApps 2.4.11 `TextBoxHelper.Watermark`、HandyControl 3.5.1 `InfoElement.Placeholder／Title`、WPF-UI 4.3.0 `PlaceholderText` **都不會**；自己範本裡的 TextBlock 不會。 |
| Image 在 control view 嗎 | 在（`ctl=1`），名稱空。Axe 對沒名稱的 Image **不報**名稱問題。→ 第一版不做（裝飾還是資訊判斷不了）。 |

## 2. 名稱怎麼來（由先到後）

1. `AutomationProperties.Name`（本地、樣式、觸發器、範本觸發器都算；空字串 = 沒設；空白 = 名稱是空白，Axe 報 `NameNotWhiteSpace`）。
2. `AutomationProperties.LabeledBy` 指到的元素的名稱。**贏過自己的文字**。指到沒有 peer 的元素（StackPanel、Border）→ 等於沒有。指到收合的 TextBlock 照樣有效。
3. 自己的純文字：
   - ContentControl（Button、CheckBox、Label、UserControl…）：Content 是字串、TextBlock（含 Run、收合的也算）、另一個 ContentControl（遞迴）。其他物件用 `ToString()`。
   - HeaderedContentControl／HeaderedItemsControl（TabItem、Expander、MenuItem、TreeViewItem）：看 Header，規則同上。
   - TextBox、PasswordBox、RichTextBox、ComboBox、Slider、DatePicker：**沒有這一步**。
   - Hyperlink：行內的 Run。只有 `InlineUIContainer` 時名稱是一個空白（等於沒有），裡面放 TextBlock 也一樣。
4. `Command` 是有文字的 RoutedUICommand（`ApplicationCommands.Copy`）而 Content／Header 沒設 → 用命令的文字。

**會念出垃圾的名稱**（UIA 的 Name 不是空的，Axe 多半不報）：

| 寫法 | UIA 名稱 |
|---|---|
| `Content="{StaticResource 某個 Geometry}"` | `M0,0L10,10 0,10z` |
| `Content="{StaticResource 某個 DrawingImage}"` | `System.Windows.Media.DrawingImage` |
| TabItem 的 Header 是面板或圖 | `System.Windows.Controls.TabItem 標頭: 內容:` |
| ListBoxItem／ListViewItem／TreeViewItem 的內容是面板或圖 | `System.Windows.Controls.ListBoxItem` |
| 圖示字型 | 私用區字元 |

## 3. 範本裡的零件

- ComboBox 範本裡的零件**不在 UIA 樹上**（peer 只回項目與編輯框）→ 不判。
- ScrollBar、Slider 範本裡的 RepeatButton：在樹上，Axe 不報 → 不判。
- Expander、TreeViewItem、TabItem、ListBoxItem、MenuItem、TextBox、Window… 範本裡的按鈕：在樹上、Axe 會報 → 照判。
- `Focusable="False"` 的無名按鈕：在樹上，Axe **不報**（它只看鍵盤可聚焦的）→ 範本裡的不判、內容裡的降一級。
- `Visibility` 收合、隱藏：不在 control view。靜態的 Collapsed 通常是程式之後打開 → 照判。

## 4. 鍵盤（Tab）

- `IsTabStop="False"`、`KeyboardNavigation.IsTabStop="False"`、`Focusable="False"`：Tab 不會停。
- **例外**：可編輯的 ComboBox、DatePicker 設了 `IsTabStop="False"`，裡面的輸入框照樣停；ListBox 設了，項目照樣停。
- Border／TextBlock／Image 加 `Focusable="True"` 就進 Tab 順序；但 **Border 就算 `Focusable` + `AutomationProperties.Name` 也不在 UIA 樹上** ——
  鍵盤修好了、螢幕閱讀器還是看不到。所以 R4 的建議是換成 Button，不是加 Focusable。
- Label、UserControl、GroupBox 預設不進 Tab。`TabNavigation="None"` 的容器裡面都不停。

## 5. 焦點看得見

做法：先把焦點放在第一顆按鈕拍一張，送 Tab 移到目標拍第二張，比目標那一塊的像素。
⚠ 第一輪全部是 0（連預設的 Button 也是）：模擬的按鍵不會讓 WPF 認為「最後的輸入是鍵盤」，焦點框根本沒畫。
打開 WPF 內部的 `KeyboardNavigation.AlwaysShowFocusVisual` 之後正向對照才成立（Button 預設 485 px、FVS 關掉 0）。**沒有正向對照的量測不能用。**

`FocusVisualStyle="{x:Null}"` 之後：

- **還是看得見**：TextBox、PasswordBox、RichTextBox（預設範本的框線會變 + 插入點）、可編輯 ComboBox（插入點）、MenuItem（預設範本會反白）、Expander（標題列的按鈕有自己的焦點框）、DatePicker。
- **完全看不見**：Button、ToggleButton、RepeatButton、CheckBox、RadioButton、ComboBox、Slider、Hyperlink、ListBoxItem、ListViewItem、TabItem。
- 自己的範本裡有 `IsKeyboardFocused`／`IsFocused` 觸發器、或 VisualState `Focused`：看得見。樣式觸發器改 Foreground：看得見；只改 Cursor：看不見。
- `FocusVisualStyle` 設成**空的 Style** 或範本是空的 ControlTemplate：跟 null 一樣看不見。
- 換了範本但沒動 FocusVisualStyle：看得見（焦點框照畫）。

## 5b. 第二輪（2026-11-24）：樣式怎麼套上去、文字怎麼組

案例在 `facts/round3-names.txt`。

- **ToolBar 的直接子元素**：Button、CheckBox、ComboBox、Menu、RadioButton、Separator、TextBox、ToggleButton（確切的型別）的**隱含樣式不套**，
  換成 `ToolBar.ButtonStyleKey` 那一組；明寫的 `Style` 照套。隔了一層面板就照平常。PasswordBox、RepeatButton 不在名單上，照平常。
  工具列裡 Menu 的 MenuItem 也套不到隱含樣式。StatusBar 沒有這件事。
- **`ItemContainerStyle`** 會套到直接寫在清單裡的 TabItem、ListBoxItem、TreeViewItem、MenuItem，**贏過隱含樣式**、輸給自己的 Style；
  TreeView 的不會往下傳給巢狀的 TreeViewItem。
- **RoutedUICommand 資源**：`Command="{StaticResource X}"`、`<Button.Command>`、樣式設的都算，有 `Text` 才有名稱。`SystemCommands.*` 沒有文字。
- **存取鍵**：只拿掉第一個後面還有字的底線。`_` → `_`、`__` → `_`、`___` → `__`、`a_` → `a_`、`_ ` → 空白。
- **Run** 各自有字型，名稱是全部接起來的字；Run 的文字可以來自它明寫的樣式與觸發器，按鈕內容裡的**隱含樣式套不到 Run**。
- **本地寫的內容贏過樣式觸發器的 Content**。
- 放在按鈕裡的 UserControl、ContentControl 的 `AutomationProperties.Name` **不會**變成按鈕的名稱。
- `ToolTipService.ToolTip` 跟 `ToolTip` 是同一個屬性。
- 沒有 AutomationPeer 的自訂控制項（MahApps／HandyControl／MaterialDesign 的 NumericUpDown、HandyControl 的 PasswordBox）設了名稱也沒有用：
  UIA 樹上只有裡面那個沒名稱的 TextBox。

## 5c. 清單、表格、樹（facts/lists.txt，R7）

- ListBox、ListView、TreeView、DataGrid 沒設名稱：UIA 的 Name 是空的，Axe-Windows 報 NameNotNull。外面的 GroupBox、`Label.Target`、ToolTip 都不算。
- Axe **也報**：ItemsControl（有項目時）、Menu、ToolBar、ProgressBar、Calendar。**不報**：TabControl、StatusBar、GroupBox、ScrollBar。
- 鍵盤到不了的（`Focusable="False"`、停用）Axe 不報 → 這支降成 info。

## 5d. NVDA 怎麼念

2026-11-24，NVDA 2026.2，繁體中文與英文各一遍，用 Tab 走過 76 種寫法的真視窗（`prototype/UiaOracle/nvda.mjs` + `facts/nvda.txt`，逐句記在
`tests/XamlA11y.Tests/nvda-facts.json`）。工具判得出來的 72 案跟 NVDA 念的沒有一案矛盾，兩種語言一樣；報告上的「NVDA 會念」也逐案比過。第一次跑抓到一個判錯種類的：按鈕內容是 EllipseGeometry
時念的是型別名，工具說「沒有名稱」（已修，同類的漸層筆刷、Color 一起）。

| 寫法 | NVDA 念 |
|---|---|
| 只有圖示的按鈕 | 按鈕 |
| 同上，只加 ToolTip，或只加 HelpText | 按鈕, Delete |
| 同上，加 `AutomationProperties.Name` | Delete, 按鈕 |
| 同上，Name 與 ToolTip 都有 | Delete, 按鈕, Delete the file |
| 文字包在面板裡的按鈕 | 按鈕 |
| `×`、`…`、🗑 | 乘以, 按鈕／..., 按鈕／廢紙簍, 按鈕 |
| 內容是 EllipseGeometry | System dot Windows dot Media dot Ellipse Geometry, 按鈕 |
| 輸入框：什麼都沒有、前面有 TextBlock、有 `Label.Target` | 編輯區, 空白（三種一樣） |
| 輸入框：LabeledBy 或 Name | Name, 編輯區, 空白 |
| 清單沒名稱 | 清單. One, 沒選取, 1 之 1 |
| 清單沒名稱，外面有 GroupBox | Files, 群組. 清單. One, … |
| 清單有名稱 | Files, 清單. One, … |
| 只有圖示的 TabItem | 索引標籤控制項. System dot Windows dot Controls dot Tab Item 標頭: 內容:, 索引標籤… |
| 可聚焦的 Border 設了 Name | （什麼都沒念） |
| RichTextBox | 文件, 空白（不是「編輯區」） |
| ListView：沒有 View／有欄位 | 清單／資料格 |
| 內容是 AccessText 的按鈕 | 按鈕, O（快速鍵會念） |
| ToolTip 寫成 `<ToolTip Content=…>` | 按鈕, Delete（跟字串一樣） |
| HelpText 與 ToolTip 都有 | 按鈕, Help（HelpText 贏；HelpText 是空字串才念 ToolTip） |
| 下拉式方塊、滑桿、清單、樹只有 ToolTip | 下拉式方塊, One, 折疊, Name／滑桿, 0, Name／清單, Name／樹狀檢視, Name |
| DataGrid 的一列 | System dot Windows dot Controls dot Text Block, 資料項目（列的名稱是資料的 ToString()） |

- **GroupBox 的標題聽得到**（當成群組念），只是不是清單的名稱 —— R7 放在 warn 的另一個理由。
- **DataGrid 的列念的是資料物件的 `ToString()`**：沒覆寫就是型別名。資料的事，XAML 看不到，這支不判。
- 停用的命令按鈕 Tab 停不到，沒有比。Narrator 與 JAWS 沒跑過。

## 6. .NET Framework 4.8

同一份案例，Windows PowerShell 5.1（就是 .NET Framework 4.8）跑 `net48.ps1`。
⚠ 第一版從行程內直接建 peer 讀名稱，`LabeledBy` 與項目容器有 12 案不同 —— 那是量法的問題：沒有 UIA client 走過樹，標籤的 peer 還不存在。
改成從 client 讀之後：146 案裡只有 3 案不同，差在型別名稱的在地化文字（`標頭:` 對 `Header:`），語意相同。

## 7. 第三方控制項

| 控制項 | 名稱規則 |
|---|---|
| WPF-UI `ui:Button`、`HyperlinkButton`、`CardAction`、`ToggleSwitch`、`DropDownButton`、`SplitButton` | 照 Button（看 Content；`Icon`、`OnContent` 不算） |
| WPF-UI `ui:TextBox`、`PasswordBox`、`NumberBox` | 照 TextBox |
| MahApps `Tile` | 照 Button（`Title` **不算**名稱） |
| MahApps `ToggleSwitch` | `Header` 是名稱 |
| WPF-UI `CardExpander`、`Anchor`、`MenuItem`、`RichTextBox` | 照 Expander、Button、MenuItem、RichTextBox |
| MahApps `MetroTabItem`、HandyControl `TabItem` | 照 TabItem |
| HandyControl `CheckComboBox`、`DatePicker`、`ContextMenuButton`、`ProgressButton` | 照 ComboBox、DatePicker、Button、ToggleButton |
| MaterialDesign `SplitButton`、`AutoSuggestBox` | 照 Button、TextBox |
| HandyControl `hc:TextBox`、`SearchBar`、`ComboBox` | 照 TextBox／ComboBox（`Title`、`Placeholder` 不算） |
| MaterialDesign `PackIcon`（內容） | 不給名稱 |
