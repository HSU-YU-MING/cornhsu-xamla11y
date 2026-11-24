# How to fix each finding

[中文](修法.md)

One section per finding: what it looks like, what to write instead. Why it is a finding and which criterion it goes with is in
[standards.md](standards.md); what WPF does was measured ([WPF實測.md](WPF實測.md), Chinese).

## missing-name

A control with an icon and nothing else. A ToolTip is not the name.

```xml
<!-- before -->
<Button ToolTip="Delete"><Path Data="{StaticResource TrashIcon}"/></Button>
<!-- after -->
<Button ToolTip="Delete" AutomationProperties.Name="Delete"><Path Data="{StaticResource TrashIcon}"/></Button>
```

Many icon buttons that all have a ToolTip: one style can use it as the name.

```xml
<Style TargetType="Button" x:Key="IconButton">
  <Setter Property="AutomationProperties.Name" Value="{Binding ToolTip, RelativeSource={RelativeSource Self}}"/>
</Style>
```

The same control on every row: bind the name, so that the rows do not sound alike.

```xml
<CheckBox IsChecked="{Binding Notify}" AutomationProperties.Name="{Binding FolderLabel, StringFormat=Notify for {0}}"/>
```

A disabled Button that is only a picture next to a text: do not name it (the screen reader would say the same thing twice). Use a `Path`
or an `Image` instead, or suppress it with the reason.

Microsoft: [setting the accessible name on a button](https://learn.microsoft.com/en-us/accessibility-tools-docs/items/wpf/button_name).

## text-not-in-name

The text is inside a panel, and WPF only takes the name from a string, a `TextBlock` or a `ContentControl`.

```xml
<!-- before -->
<Button><StackPanel Orientation="Horizontal"><Image Source="save.png"/><TextBlock Text="Save"/></StackPanel></Button>
<!-- after -->
<Button AutomationProperties.Name="Save"><StackPanel Orientation="Horizontal"><Image Source="save.png"/><TextBlock Text="Save"/></StackPanel></Button>
```

When the text is bound, bind the name to the same property.

## meaningless-name

```xml
<!-- before: NVDA reads nothing, or "System dot Windows dot Controls dot Tab Item" -->
<Button FontFamily="Segoe MDL2 Assets" Content="&#xE74D;"/>
<TabItem><TabItem.Header><Image Source="home.png"/></TabItem.Header></TabItem>
<!-- after -->
<Button FontFamily="Segoe MDL2 Assets" Content="&#xE74D;" AutomationProperties.Name="Delete"/>
<TabItem AutomationProperties.Name="Home"><TabItem.Header><Image Source="home.png"/></TabItem.Header></TabItem>
```

A link whose content is an element, not text:

```xml
<TextBlock><Hyperlink Command="{Binding Open}"><InlineUIContainer><TextBlock Text="{Binding Title}"/></InlineUIContainer></Hyperlink></TextBlock>
<!-- either -->
<TextBlock><Hyperlink Command="{Binding Open}"><Run Text="{Binding Title, Mode=OneWay}"/></Hyperlink></TextBlock>
<!-- or -->
<TextBlock><Hyperlink Command="{Binding Open}" AutomationProperties.Name="{Binding Title}"><InlineUIContainer><TextBlock Text="{Binding Title}"/></InlineUIContainer></Hyperlink></TextBlock>
```

`Mode=OneWay` matters: `Run.Text` binds TwoWay by default and throws on a read-only property.

## input-no-name

`Label.Target` only moves focus with the access key. Give the field the label as its name.

```xml
<!-- before -->
<Label Content="_Name" Target="{Binding ElementName=NameBox}"/>
<TextBox x:Name="NameBox"/>
<!-- after -->
<Label x:Name="NameLabel" Content="_Name" Target="{Binding ElementName=NameBox}"/>
<TextBox x:Name="NameBox" AutomationProperties.LabeledBy="{Binding ElementName=NameLabel}"/>
```

Without a visible label (a search box): `AutomationProperties.Name="Search"`.
Microsoft: [edit field](https://learn.microsoft.com/en-us/accessibility-tools-docs/items/wpf/edit_name),
[combo box](https://learn.microsoft.com/en-us/accessibility-tools-docs/items/wpf/combobox_name).

## list-no-name

```xml
<!-- before -->
<TextBlock Text="Files"/><ListBox ItemsSource="{Binding Files}"/>
<!-- after -->
<TextBlock x:Name="FilesLabel" Text="Files"/>
<ListBox ItemsSource="{Binding Files}" AutomationProperties.LabeledBy="{Binding ElementName=FilesLabel}"/>
```

## widget-no-name

The same for a `ProgressBar`, a `Calendar`, a `Menu` or a `ToolBar` (a string in `ToolBar.Header` is its name too).

## item-type-name

Items made from data are named by `ToString()`. Every item of a class without one is read as `MyApp.Models.Note`, also with
`DisplayMemberPath`.

Simplest, and it works for every list of that class:

```csharp
public override string ToString() => Title;
```

Or per list, in the style of the items:

```xml
<!-- before -->
<ListBox ItemsSource="{Binding Notes}" DisplayMemberPath="Title"/>
<!-- after -->
<ListBox ItemsSource="{Binding Notes}" DisplayMemberPath="Title">
  <ListBox.ItemContainerStyle>
    <Style TargetType="ListBoxItem"><Setter Property="AutomationProperties.Name" Value="{Binding Title}"/></Style>
  </ListBox.ItemContainerStyle>
</ListBox>
```

Careful with the style:

- If the list, or a style of the list, **already has an `ItemContainerStyle`** (`RowStyle` for a `DataGrid`), add the Setter to that one. A
  second one replaces it and the list loses its look.
- If a theme styles the items, base yours on it: `BasedOn="{StaticResource {x:Type ListBoxItem}}"`. For a `ListView` with a
  `GridView` that is `BasedOn="{StaticResource {x:Static GridView.GridViewItemContainerStyleKey}}"`.
- The container is `ListViewItem`, `ComboBoxItem`, `TreeViewItem`, `TabItem`, `MenuItem` or `DataGridRow`.
- A closed `ComboBox` says its selected item the same way (run with NVDA): it is not only the open list. Both fixes work there.

## silent-tab-stop

`ContentControl` and `Control` take keyboard focus by default and have no AutomationPeer.

```xml
<!-- before -->
<ContentControl Content="{Binding CurrentPage}"/>
<!-- after: it only holds other controls -->
<ContentControl Content="{Binding CurrentPage}" Focusable="False"/>
```

When your code calls `Focus()` on it (after navigating, say), use `IsTabStop="False"` instead.

A control of your own that people operate needs a peer (or derive from `Button`, which has one):

```csharp
protected override AutomationPeer OnCreateAutomationPeer() => new CardAutomationPeer(this);

sealed class CardAutomationPeer(Card owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    protected override string GetClassNameCore() => nameof(Card);
    public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPattern(pattern);
    public void Invoke() => ((Card)Owner).Open();
}
```

## symbol-name

```xml
<!-- before: NVDA reads "times, button" -->
<Button Content="×"/>
<!-- after -->
<Button Content="×" AutomationProperties.Name="Close"/>
```

## same-name

```xml
<!-- before -->
<Button Content="Browse…" Command="{Binding BrowseInput}"/>
<Button Content="Browse…" Command="{Binding BrowseOutput}"/>
<!-- after -->
<Button Content="Browse…" AutomationProperties.Name="Browse for the input folder" Command="{Binding BrowseInput}"/>
<Button Content="Browse…" AutomationProperties.Name="Browse for the output folder" Command="{Binding BrowseOutput}"/>
```

The name should start with the visible text, so that speech input still works (2.5.3).

## mouse-only

```xml
<!-- before -->
<Border MouseLeftButtonUp="OnOpen"><TextBlock Text="Open"/></Border>
<!-- after: a Button without the look of one -->
<Button Click="OnOpen" Content="Open">
  <Button.Template><ControlTemplate TargetType="Button"><Border Background="Transparent"><ContentPresenter/></Border></ControlTemplate></Button.Template>
</Button>
```

Change the handler's second parameter to `RoutedEventArgs`: `Click` does not take a `MouseButtonEventArgs` handler. With a command, the
same thing inside a `DataTemplate`:

```xml
<Button Command="{Binding OpenCommand}" AutomationProperties.Name="{Binding Title}">
  <Button.Template><ControlTemplate TargetType="Button"><Border Background="Transparent"><ContentPresenter/></Border></ControlTemplate></Button.Template>
  <TextBlock Text="{Binding Title}" Style="{StaticResource LinkText}"/>
</Button>
```

Keep the look by keeping the element with the style inside the Button; keep the focus visual (see above).

## not-in-tab-order

Remove `IsTabStop="False"` / `Focusable="False"`, or give the same action a keyboard shortcut (`KeyBinding`) or another control that
Tab reaches.

## focus-not-visible

Leave `FocusVisualStyle` alone, or draw focus in the template.

```xml
<!-- before -->
<Setter Property="FocusVisualStyle" Value="{x:Null}"/>
<!-- after: in the ControlTemplate -->
<Trigger Property="IsKeyboardFocused" Value="True">
  <Setter TargetName="Frame" Property="BorderBrush" Value="{DynamicResource {x:Static SystemColors.HighlightBrushKey}}"/>
</Trigger>
```

## small-target

Make it at least 24 × 24, or leave room around it (24 between the centres of two small targets).
