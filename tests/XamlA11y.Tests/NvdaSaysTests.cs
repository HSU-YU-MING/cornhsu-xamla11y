using XamlA11y.Core.Analysis;
using XamlA11y.Core.Report;

namespace XamlA11y.Tests;

/// <summary>報告裡的「NVDA 會念」：聽過的才寫，沒聽過的不猜。</summary>
public sealed class NvdaSaysTests
{
    /// <summary>會念描述的是這幾種，一個不多一個不少（清單從表上拿掉一項，別的測試看不出來）。</summary>
    [Fact]
    public void The_controls_whose_description_was_heard() => Assert.Equal(
        ["Button", "CheckBox", "ComboBox", "ListBox", "PasswordBox", "RadioButton", "RepeatButton", "RichTextBox", "Slider", "TextBox", "ToggleButton", "TreeView"],
        Nvda.WithDescription.Order(StringComparer.Ordinal));

    [Fact]
    public void The_controls_that_were_heard() => Assert.Equal(
        ["Button", "CheckBox", "ComboBox", "DataGrid", "DatePicker", "ListBox", "ListView", "MenuItem", "PasswordBox", "RadioButton", "RepeatButton", "RichTextBox", "Slider", "TabItem", "TextBox", "ToggleButton", "TreeView"],
        Nvda.Controls.Order(StringComparer.Ordinal));

    /// <summary>自己的類別、treatAs 宣告的：UIA 的型別可以是別的，不猜。</summary>
    [Fact]
    public void NVDA_is_not_guessed_for_other_classes()
    {
        var f = Assert.Single(Kit.Audit("<local:MyToggle/>", treatAs: new Dictionary<string, string> { ["MyToggle"] = "CheckBox" }).Findings);
        Assert.Null(Nvda.Says(f, Lang.En));
    }

    [Theory]
    [InlineData("<Button>[ICON]</Button>", "button", "按鈕")]
    [InlineData("<Button ToolTip=\"Delete\">[ICON]</Button>", "button, Delete", "按鈕, Delete")]
    [InlineData("<Button ToolTip=\"{Binding T}\">[ICON]</Button>", null, null)]          // 執行時才知道
    [InlineData("<Button ToolTip=\"a\" AutomationProperties.HelpText=\"b\">[ICON]</Button>", "button, b", "按鈕, b")]
    [InlineData("<TextBox/>", "edit", "編輯區")]
    [InlineData("<ListBox/>", "list", "清單")]
    [InlineData("<ListView><ListView.View><GridView/></ListView.View></ListView>", "data grid", "資料格")]
    [InlineData("<TabControl><TabItem><TabItem.Header>[ICON]</TabItem.Header></TabItem></TabControl>", "System dot Windows dot Controls dot Tab Item…, tab", "System dot Windows dot Controls dot Tab Item…, 索引標籤")]
    [InlineData("<Button Content=\"×\"/>", null, null)]                                  // 怎麼念看 NVDA 的符號字典
    [InlineData("<Button FontFamily=\"Segoe MDL2 Assets\" Content=\"&#xE74D;\"/>", null, null)]
    [InlineData("<TabControl><TabItem Header=\"&#xE74D;\"/></TabControl>", null, null)]   // 名稱是字元，不是型別名
    [InlineData("<Border MouseDown=\"x\"/>", null, null)]
    [InlineData("<RichTextBox/>", "document", "文件")]
    [InlineData("<RepeatButton>[ICON]</RepeatButton>", "button", "按鈕")]
    [InlineData("<ListView/>", "list", "清單")]
    [InlineData("<ListView View=\"{x:Null}\"/>", "list", "清單")]
    [InlineData("<ListView><ListView.View><local:Tiles/></ListView.View></ListView>", null, null)]       // 別的 View 沒跑過
    [InlineData("<Button><AccessText Text=\"_Open\"/></Button>", null, null)]                              // NVDA 還會念快速鍵
    [InlineData("<Button AutomationProperties.AccessKey=\"Alt+D\">[ICON]</Button>", null, null)]
    [InlineData("<Button IsEnabled=\"False\" Width=\"9\">[ICON]</Button>", null, null)]                   // Tab 到不了
    [InlineData("<Button ToolTip=\"Save&#xE74D;\">[ICON]</Button>", null, null)]                           // 符號怎麼念看字典
    [InlineData("<Button ToolTip=\"×\">[ICON]</Button>", null, null)]
    [InlineData("<Button ToolTip=\"Find, then replace.\">[ICON]</Button>", "button, Find, then replace.", "按鈕, Find, then replace.")]
    [InlineData("<Button ToolTip=\"Tip\" AutomationProperties.HelpText=\" \">[ICON]</Button>", null, null)] // 空白的 HelpText 就是它
    [InlineData("<Button ToolTip=\"Tip\" AutomationProperties.HelpText=\"\">[ICON]</Button>", "button, Tip", "按鈕, Tip")]
    [InlineData("<Button><Button.ToolTip><ToolTip/></Button.ToolTip>[ICON]</Button>", null, null)]
    [InlineData("<TabControl><TabItem ToolTip=\"Home\"><TabItem.Header>[ICON]</TabItem.Header></TabItem></TabControl>", null, null)]
    [InlineData("<Expander/>", null, null)]                                              // 念的是裡面的切換按鈕
    public void What_NVDA_says(string xaml, string? en, string? zh)
    {
        var f = Assert.Single(Kit.Audit(xaml.Replace("[ICON]", Kit.Icon, StringComparison.Ordinal)).Findings, x => x.Kind.Check is "control-names" or "input-names" or "mouse" or "list-names");
        Assert.Equal(en, Nvda.Says(f, Lang.En));
        Assert.Equal(zh, Nvda.Says(f, Lang.ZhHant));
    }
}
