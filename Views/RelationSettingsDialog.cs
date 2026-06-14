using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Apex.Models;

namespace Apex.Views;

public class RelationSettingsDialog : Window
{
    private readonly Slider _thicknessSlider;
    private readonly TextBlock _thicknessLabel;
    private Border? _activeColorSwatch;
    private string _selectedColor;

    public string ResultColor { get; private set; }
    public double ResultThickness { get; private set; }

    private static readonly (string Hex, string Label)[] Colors =
    {
        ("#CBA6F7", "Purple"),
        ("#89B4FA", "Blue"),
        ("#A6E3A1", "Green"),
        ("#F38BA8", "Pink"),
        ("#FAB387", "Peach"),
        ("#F9E2AF", "Yellow"),
        ("#94E2D5", "Teal"),
        ("#CDD6F4", "White"),
        ("#6C7086", "Gray"),
        ("#E24B4A", "Red"),
    };

    public RelationSettingsDialog(string currentColor = "#CBA6F7", double currentThickness = 1.5)
    {
        _selectedColor = currentColor;
        ResultColor = currentColor;
        ResultThickness = currentThickness;

        Title = "Relation Settings";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(16) };

        // Kolor linii
        root.Children.Add(MakeLabel("LINE COLOR"));
        var colorWrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 16) };
        foreach (var (hex, label) in Colors)
        {
            var sw = new Border
            {
                Width = 28, Height = 28,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                Tag = hex,
                ToolTip = label,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Background = ParseHexBrush(hex)
            };
            if (string.Equals(hex, currentColor, StringComparison.OrdinalIgnoreCase))
            {
                MarkActive(sw, true);
                _activeColorSwatch = sw;
            }
            sw.MouseLeftButtonUp += (_, _) =>
            {
                if (_activeColorSwatch != null) MarkActive(_activeColorSwatch, false);
                _activeColorSwatch = sw;
                _selectedColor = hex;
                MarkActive(sw, true);
            };
            colorWrap.Children.Add(sw);
        }
        root.Children.Add(colorWrap);

        // Grubość
        root.Children.Add(MakeLabel("LINE THICKNESS"));
        var sliderRow = new Grid { Margin = new Thickness(0, 8, 0, 16) };
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _thicknessSlider = new Slider
        {
            Minimum = 0.5, Maximum = 8, Value = currentThickness,
            TickFrequency = 0.5, IsSnapToTickEnabled = true,
        };
        _thicknessLabel = new TextBlock
        {
            Text = currentThickness + "px",
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            Width = 36, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        _thicknessSlider.ValueChanged += (_, _) => _thicknessLabel.Text = _thicknessSlider.Value + "px";

        Grid.SetColumn(_thicknessSlider, 0);
        Grid.SetColumn(_thicknessLabel, 1);
        sliderRow.Children.Add(_thicknessSlider);
        sliderRow.Children.Add(_thicknessLabel);
        root.Children.Add(sliderRow);

        // Przyciski
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = MakeButton("OK", Color.FromRgb(29, 158, 117), Brushes.White);
        ok.Click += (_, _) => Confirm();
        var cancel = MakeButton("Cancel", Color.FromRgb(49, 50, 68), new SolidColorBrush(Color.FromRgb(205, 214, 244)), true);
        cancel.Click += (_, _) => { DialogResult = false; };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        root.Children.Add(btnRow);

        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }

    private void Confirm()
    {
        ResultColor = _selectedColor;
        ResultThickness = Math.Round(_thicknessSlider.Value * 2) / 2;
        DialogResult = true;
    }

    private static void MarkActive(Border sw, bool active)
    {
        sw.BorderBrush = active ? Brushes.White : Brushes.Transparent;
        sw.Child = active ? new TextBlock { Text = "✓", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null;
    }

    private static TextBlock MakeLabel(string text) => new() { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)), Margin = new Thickness(0, 0, 0, 0) };

    private static Button MakeButton(string content, Color bg, Brush fg, bool border = false) => new() { Content = content, Width = 80, Height = 32, Margin = new Thickness(6, 0, 0, 0), Background = new SolidColorBrush(bg), Foreground = fg, BorderThickness = border ? new Thickness(1) : new Thickness(0), BorderBrush = border ? new SolidColorBrush(Color.FromRgb(69, 71, 90)) : Brushes.Transparent, Cursor = Cursors.Hand };

    private static Brush ParseHexBrush(string hex)
    {
        try { hex = hex.TrimStart('#'); if (hex.Length == 6) return new SolidColorBrush(Color.FromRgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16))); } catch { }
        return new SolidColorBrush(Color.FromRgb(136, 136, 136));
    }
}