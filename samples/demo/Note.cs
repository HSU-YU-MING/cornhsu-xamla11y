using System.Collections.ObjectModel;

namespace Demo;

/// <summary>Has no ToString(): a screen reader says "Demo.Note" for every item (see item-type-name in MainWindow.xaml).</summary>
public class Note
{
    public string Title { get; set; } = "";
}

public class MainViewModel
{
    public ObservableCollection<Note> Notes { get; } = new();
}
