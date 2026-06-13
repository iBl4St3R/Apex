using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Apex.Views;

public class TitleCardDialog : Window
{
    private readonly TextBox _textBox;
    private readonly ComboBox _fontFamilyBox;
    private readonly Slider _fontSizeSlider;
    private readonly TextBlock _fontSizeLabel;
    private readonly TextBox _fontColorBox;
    private readonly TextBox _bgColorBox;
    private readonly TextBlock _preview;

    public string ResultText { get; private set; } = "";
    public string ResultFontFamily { get; private set; } = "Segoe UI";
    public double ResultFontSize { get; private set; } = 24;
    public string ResultFontColor { get; private set; } = "#CDD6F4";
    public string ResultBackgroundColor { get; private set; } = "#00000000";

    private static readonly string[] FontOptions =
    {
        "Segoe UI", "Consolas", "Arial", "Georgia",
        "Times New Roman", "Trebuchet MS", "Verdana", "Impact"
    };

    public TitleCardDialog(
        string text = "",
        string fontFamily = "Segoe UI",
        double fontSize = 24,
        string fontColor = "#CDD6F4",
        string backgroundColor = "#00000000")
    {
        Title = "Title Card";
        Width = 440;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
        FontFamily = new FontFamily("Segoe UI");

        var stack = new StackPanel { Margin = new Thickness(20) };

        // Tekst
        stack.Children.Add(MakeLabel("Text (max 200 chars):"));
        _textBox = new TextBox
        {
            Text = text,
            MaxLength = 200,
            FontSize = 13,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 12),
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            AcceptsReturn = false
        };
        _textBox.TextChanged += (_, _) => UpdatePreview();
        stack.Children.Add(_textBox);

        // Font family
        stack.Children.Add(MakeLabel("Font:"));
        _fontFamilyBox = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
        };
        foreach (var f in FontOptions) _fontFamilyBox.Items.Add(f);
        _fontFamilyBox.SelectedItem = FontOptions.Contains(fontFamily) ? fontFamily : "Segoe UI";
        _fontFamilyBox.SelectionChanged += (_, _) => UpdatePreview();
        stack.Children.Add(_fontFamilyBox);

        // Font size
        var sizeRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        stack.Children.Add(MakeLabel("Font size:"));
        _fontSizeSlider = new Slider
        {
            Minimum = 10, Maximum = 96,
            Value = fontSize,
            TickFrequency = 2,
            IsSnapToTickEnabled = true
        };
        _fontSizeSlider.ValueChanged += (_, _) => { _fontSizeLabel.Text = ((int)_fontSizeSlider.Value).ToString(); UpdatePreview(); };
        _fontSizeLabel = new TextBlock
        {
            Text = ((int)fontSize).ToString(),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(_fontSizeSlider, 0);
        Grid.SetColumn(_fontSizeLabel, 1);
        sizeRow.Children.Add(_fontSizeSlider);
        sizeRow.Children.Add(_fontSizeLabel);
        stack.Children.Add(sizeRow);

        // Kolory w jednym rzędzie
        var colorRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fontColorStack = new StackPanel();
        fontColorStack.Children.Add(MakeLabel("Font color (#hex):"));
        _fontColorBox = MakeHexBox(fontColor.TrimStart('#'));
        _fontColorBox.TextChanged += (_, _) => UpdatePreview();
        fontColorStack.Children.Add(_fontColorBox);
        Grid.SetColumn(fontColorStack, 0);
        colorRow.Children.Add(fontColorStack);

        var bgColorStack = new StackPanel();
        bgColorStack.Children.Add(MakeLabel("Background (#hex):"));
        _bgColorBox = MakeHexBox(backgroundColor.TrimStart('#').Length >= 6
            ? backgroundColor.TrimStart('#')[^6..]
            : "00000000");
        _bgColorBox.TextChanged += (_, _) => UpdatePreview();
        bgColorStack.Children.Add(_bgColorBox);
        Grid.SetColumn(bgColorStack, 2);
        colorRow.Children.Add(bgColorStack);

        stack.Children.Add(colorRow);

        // Preview
        stack.Children.Add(MakeLabel("Preview:"));
        var previewBorder = new Border
        {
            Height = 60,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 16),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1)
        };
        _preview = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(8)
        };
        previewBorder.Child = _preview;
        stack.Children.Add(previewBorder);

        // Przyciski
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = MakeButton("OK", Color.FromRgb(29, 158, 117), Brushes.White);
        okBtn.Click += (_, _) => Confirm();
        var cancelBtn = MakeButton("Cancel", Color.FromRgb(49, 50, 68), new SolidColorBrush(Color.FromRgb(205, 214, 244)));
        cancelBtn.Click += (_, _) => { DialogResult = false; };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        stack.Children.Add(btnRow);

        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _textBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) Confirm();
            if (e.Key == System.Windows.Input.Key.Escape) DialogResult = false;
        };

        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
            UpdatePreview();
        };
    }

    private void UpdatePreview()
    {
        try
        {
            _preview.Text = _textBox.Text;
            _preview.FontFamily = new FontFamily(_fontFamilyBox.SelectedItem?.ToString() ?? "Segoe UI");
            _preview.FontSize = Math.Clamp(_fontSizeSlider.Value, 10, 48); // ogranicz w preview
            _preview.Foreground = ParseColor(_fontColorBox.Text, Color.FromRgb(205, 214, 244));

            var bgColor = ParseColorRaw(_bgColorBox.Text);
            if (_preview.Parent is Border b)
                b.Background = new SolidColorBrush(bgColor);
        }
        catch { }
    }

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(_textBox.Text)) return;

        ResultText = _textBox.Text.Trim();
        ResultFontFamily = _fontFamilyBox.SelectedItem?.ToString() ?? "Segoe UI";
        ResultFontSize = (int)_fontSizeSlider.Value;
        ResultFontColor = "#" + _fontColorBox.Text.TrimStart('#').ToUpperInvariant().PadLeft(6, '0');
        ResultBackgroundColor = "#" + _bgColorBox.Text.TrimStart('#').ToUpperInvariant().PadLeft(8, '0');
        DialogResult = true;
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static TextBox MakeHexBox(string value) => new()
    {
        Text = value,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        MaxLength = 8,
        Padding = new Thickness(6, 3, 6, 3),
        Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
        BorderThickness = new Thickness(1),
        CaretBrush = new SolidColorBrush(Color.FromRgb(205, 214, 244))
    };

    private static Button MakeButton(string text, Color bg, Brush fg) => new()
    {
        Content = text,
        Width = 80,
        Height = 32,
        Margin = new Thickness(4, 0, 0, 0),
        Background = new SolidColorBrush(bg),
        Foreground = fg,
        BorderThickness = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static SolidColorBrush ParseColor(string hex, Color fallback)
    {
        try { return new SolidColorBrush(ParseColorRaw(hex)); }
        catch { return new SolidColorBrush(fallback); }
    }

    private static Color ParseColorRaw(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            return Color.FromRgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        if (hex.Length == 8)
            return Color.FromArgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16));
        throw new FormatException();
    }
}