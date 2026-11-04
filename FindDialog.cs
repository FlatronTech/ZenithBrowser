using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Zenith.App;

/// <summary>Minimal "Find on page" input dialog.</summary>
public class FindDialog : Window
{
    public string? SearchText { get; private set; }

    private readonly TextBox _box;

    public FindDialog()
    {
        Title         = "Find on page";
        Width         = 360;
        Height        = 120;
        ResizeMode    = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily    = new System.Windows.Media.FontFamily("Segoe UI");

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _box = new TextBox { Margin = new Thickness(0, 0, 0, 8), FontSize = 13 };
        _box.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { SearchText = _box.Text; DialogResult = true; }
            if (e.Key == Key.Escape) { DialogResult = false; }
        };
        Grid.SetRow(_box, 0);

        var buttons = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var find = new Button
        {
            Content = "Find", Width = 75, Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        find.Click += (s, e) => { SearchText = _box.Text; DialogResult = true; };

        var cancel = new Button { Content = "Cancel", Width = 75, IsCancel = true };

        buttons.Children.Add(find);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 1);

        grid.Children.Add(_box);
        grid.Children.Add(buttons);
        Content = grid;

        Loaded += (s, e) => _box.Focus();
    }
}
