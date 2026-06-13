using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Apex.Views;

public class TitleCardDialog : Window
{
    private readonly TextBox _textBox;
    private readonly ListBox _fontFamilyBox;
    private readonly Slider _fontSizeSlider;
    private readonly TextBlock _fontSizeLabel;
    private readonly TextBlock _previewText;
    private readonly Border _previewBox;

    public string ResultText { get; private set; } = "";
    public string ResultFontFamily { get; private set; } = "Segoe UI";
    public double ResultFontSize { get; private set; } = 32;
    public string ResultFontColor { get; private set; } = "#CDD6F4";
    public string ResultBackgroundColor { get; private set; } = "#00000000";

    private string _selectedFgColor = "#CDD6F4";
    private string _selectedBgColor = "#00000000";
    private Border? _activeFgSwatch;
    private Border? _activeBgSwatch;

    private static readonly string[] FontOptions =
    {
        "Segoe UI", "Consolas", "Arial", "Georgia",
        "Times New Roman", "Trebuchet MS", "Verdana", "Impact"
    };

    // Fg palette — text/foreground colors
    private static readonly (string Hex, string Label)[] FgColors =
    {
        ("#CDD6F4", "White"),
        ("#11111B", "Black"),
        ("#E24B4A", "Red"),
        ("#EF9F27", "Orange"),
        ("#F9E2AF", "Yellow"),
        ("#1D9E75", "Green"),
        ("#378ADD", "Blue"),
        ("#89B4FA", "Light blue"),
        ("#CBA6F7", "Purple"),
        ("#F38BA8", "Pink"),
        ("#F5C2E7", "Light pink"),
        ("#A6ADC8", "Gray"),
        ("#585B70", "Dark gray"),
    };

    // Bg palette — background colors (includes transparent)
    private static readonly (string Hex, string Label)[] BgColors =
    {
        ("#00000000", "Transparent"),
        ("#11111B",   "Near black"),
        ("#1E1E2E",   "Dark"),
        ("#313244",   "Surface"),
        ("#45475A",   "Mid"),
        ("#585B70",   "Light mid"),
        ("#E24B4A",   "Red"),
        ("#EF9F27",   "Orange"),
        ("#1D9E75",   "Green"),
        ("#378ADD",   "Blue"),
        ("#CBA6F7",   "Purple"),
        ("#F38BA8",   "Pink"),
        ("#181825",   "Mantle"),
        ("#CDD6F4",   "Light"),
    };

    public TitleCardDialog(
        string text = "",
        string fontFamily = "Segoe UI",
        double fontSize = 32,
        string fontColor = "#CDD6F4",
        string backgroundColor = "#00000000")
    {
        _selectedFgColor = fontColor;
        _selectedBgColor = backgroundColor;

        Title = "Title Card";
        Width = 600;
        MinWidth = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
        FontFamily = new FontFamily("Segoe UI");

        // ── Root layout ──────────────────────────────────
        var root = new StackPanel { Margin = new Thickness(16), };

        // ── Text input ───────────────────────────────────
        root.Children.Add(MakeLabel("Text (max 200 chars)"));
        _textBox = new TextBox
        {
            Text = text,
            MaxLength = 200,
            FontSize = 13,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 4, 0, 12),
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            AcceptsReturn = false
        };
        _textBox.TextChanged += (_, _) => UpdatePreview();
        root.Children.Add(_textBox);

        // ── Font + size row ──────────────────────────────
        var fontRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        fontRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fontRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        fontRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fontStack = new StackPanel();
        fontStack.Children.Add(MakeLabel("Font"));

        _fontFamilyBox = new ListBox
        {
            Height = 90,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1),
        };
        foreach (var f in FontOptions) _fontFamilyBox.Items.Add(f);
        _fontFamilyBox.SelectedItem = FontOptions.Contains(fontFamily) ? fontFamily : "Segoe UI";
        _fontFamilyBox.SelectionChanged += (_, _) => UpdatePreview();

        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(ListBoxItem.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(49, 50, 68))));
        itemStyle.Setters.Add(new Setter(ListBoxItem.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(205, 214, 244))));
        itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty,
            new Thickness(8, 4, 8, 4)));

        var hoverTrigger = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(69, 71, 90))));
        itemStyle.Triggers.Add(hoverTrigger);

        var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(88, 91, 112))));
        selectedTrigger.Setters.Add(new Setter(ListBoxItem.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(205, 214, 244))));
        itemStyle.Triggers.Add(selectedTrigger);

        _fontFamilyBox.ItemContainerStyle = itemStyle;
        _fontFamilyBox.Resources.Add(SystemColors.HighlightBrushKey,
            new SolidColorBrush(Color.FromRgb(88, 91, 112)));
        _fontFamilyBox.Resources.Add(SystemColors.HighlightTextBrushKey,
            new SolidColorBrush(Color.FromRgb(205, 214, 244)));
        _fontFamilyBox.Resources.Add(SystemColors.InactiveSelectionHighlightBrushKey,
            new SolidColorBrush(Color.FromRgb(69, 71, 90)));
        _fontFamilyBox.Resources.Add(SystemColors.InactiveSelectionHighlightTextBrushKey,
            new SolidColorBrush(Color.FromRgb(205, 214, 244)));

        fontStack.Children.Add(_fontFamilyBox);




        Grid.SetColumn(fontStack, 0);
        fontRow.Children.Add(fontStack);

        _fontSizeLabel = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 5)
        };
        var sizeStack = new StackPanel();
        var sizeLabelRow = new Grid();
        sizeLabelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sizeLabelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sizeLabel = MakeLabel("Font size");
        Grid.SetColumn(sizeLabel, 0);
        sizeLabelRow.Children.Add(sizeLabel);
        Grid.SetColumn(_fontSizeLabel, 1);
        sizeLabelRow.Children.Add(_fontSizeLabel);
        sizeStack.Children.Add(sizeLabelRow);

        _fontSizeSlider = new Slider
        {
            Minimum = 10,
            Maximum = 192,
            Value = Math.Clamp(fontSize, 10, 192),
            TickFrequency = 2,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _fontSizeSlider.ValueChanged += (_, _) =>
        {
            _fontSizeLabel.Text = ((int)_fontSizeSlider.Value) + "px";
            UpdatePreview();
        };
        _fontSizeLabel.Text = ((int)_fontSizeSlider.Value) + "px";
        sizeStack.Children.Add(_fontSizeSlider);
        Grid.SetColumn(sizeStack, 2);
        fontRow.Children.Add(sizeStack);
        root.Children.Add(fontRow);

        // ── Color palettes row ───────────────────────────
        var colorRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Fg swatches
        var fgStack = new StackPanel();
        fgStack.Children.Add(MakeSectionLabel("Font color"));
        var fgWrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (hex, label) in FgColors)
        {
            var sw = MakeSwatch(hex, label, isFg: true);
            if (string.Equals(hex, _selectedFgColor, StringComparison.OrdinalIgnoreCase))
            {
                MarkSwatchSelected(sw, true);
                _activeFgSwatch = sw;
            }
            fgWrap.Children.Add(sw);
        }
        fgStack.Children.Add(fgWrap);
        Grid.SetColumn(fgStack, 0);
        colorRow.Children.Add(fgStack);

        // Bg swatches
        var bgStack = new StackPanel();
        bgStack.Children.Add(MakeSectionLabel("Background color"));
        var bgWrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (hex, label) in BgColors)
        {
            var sw = MakeSwatch(hex, label, isFg: false);
            if (string.Equals(hex, _selectedBgColor, StringComparison.OrdinalIgnoreCase))
            {
                MarkSwatchSelected(sw, true);
                _activeBgSwatch = sw;
            }
            bgWrap.Children.Add(sw);
        }
        bgStack.Children.Add(bgWrap);
        Grid.SetColumn(bgStack, 2);
        colorRow.Children.Add(bgStack);

        root.Children.Add(colorRow);

        // ── Preview ──────────────────────────────────────
        root.Children.Add(MakeSectionLabel("Preview"));
        _previewBox = new Border
        {
            Height = 80,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 6, 0, 16),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };
        _previewText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(12, 6, 12, 6),
            Text = string.IsNullOrEmpty(text) ? "Preview" : text
        };
        _previewBox.Child = _previewText;
        root.Children.Add(_previewBox);

        // ── Buttons ──────────────────────────────────────
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var okBtn = MakeButton("OK", Color.FromRgb(29, 158, 117), Brushes.White);
        okBtn.Click += (_, _) => Confirm();
        var cancelBtn = MakeButton("Cancel",
            Color.FromRgb(49, 50, 68),
            new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            hasBorder: true);
        cancelBtn.Click += (_, _) => { DialogResult = false; };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        root.Children.Add(btnRow);

        Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        // Keyboard shortcuts
        _textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Confirm();
            if (e.Key == Key.Escape) DialogResult = false;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DialogResult = false;
        };

        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
            UpdatePreview();
        };
    }

    // ──────────────────────────────────────────────
    //  Swatch helpers
    // ──────────────────────────────────────────────

    private Border MakeSwatch(string hex, string label, bool isFg)
    {
        bool isTransparent = string.Equals(hex, "#00000000", StringComparison.OrdinalIgnoreCase);

        var sw = new Border
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.Hand,
            Tag = hex,
            ToolTip = label,
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            ClipToBounds = true
        };

        if (isTransparent)
        {
            // Checkerboard pattern for transparent
            sw.Background = new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 6, 6),
                ViewportUnits = BrushMappingMode.Absolute,
                Drawing = new DrawingGroup
                {
                    Children =
                    {
                        new GeometryDrawing(
                            new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                            null,
                            new RectangleGeometry(new Rect(0, 0, 6, 6))),
                        new GeometryDrawing(
                            new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                            null,
                            new GeometryGroup
                            {
                                Children =
                                {
                                    new RectangleGeometry(new Rect(0, 0, 3, 3)),
                                    new RectangleGeometry(new Rect(3, 3, 3, 3))
                                }
                            })
                    }
                }
            };
        }
        else
        {
            sw.Background = ParseHexBrush(hex);
        }

        sw.MouseLeftButtonUp += (_, _) =>
        {
            if (isFg)
            {
                if (_activeFgSwatch != null) MarkSwatchSelected(_activeFgSwatch, false);
                _activeFgSwatch = sw;
                _selectedFgColor = hex;
            }
            else
            {
                if (_activeBgSwatch != null) MarkSwatchSelected(_activeBgSwatch, false);
                _activeBgSwatch = sw;
                _selectedBgColor = hex;
            }
            MarkSwatchSelected(sw, true);
            UpdatePreview();
        };

        return sw;
    }

    private static void MarkSwatchSelected(Border sw, bool selected)
    {
        sw.BorderBrush = selected
            ? new SolidColorBrush(Colors.White)
            : Brushes.Transparent;

        // Show/hide checkmark child
        if (selected)
        {
            sw.Child = new TextBlock
            {
                Text = "✓",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            sw.Child = null;
        }
    }

    // ──────────────────────────────────────────────
    //  Preview
    // ──────────────────────────────────────────────

    private void UpdatePreview()
    {
        if (_previewText == null || _previewBox == null) return;

        // Text
        _previewText.Text = string.IsNullOrWhiteSpace(_textBox?.Text)
            ? "Preview"
            : _textBox.Text;

        // Font
        try { _previewText.FontFamily = new FontFamily(_fontFamilyBox?.SelectedItem?.ToString() ?? "Segoe UI"); }
        catch { _previewText.FontFamily = new FontFamily("Segoe UI"); }

        // Font size — clamp display to 64 so it fits in the preview box
        double fs = _fontSizeSlider?.Value ?? 32;
        _previewText.FontSize = Math.Clamp(fs, 10, 64);

        // Colors — exactly what will appear on the card
        _previewText.Foreground = ParseHexBrush(_selectedFgColor);

        bool transparent = string.Equals(_selectedBgColor, "#00000000", StringComparison.OrdinalIgnoreCase);
        _previewBox.Background = transparent
            ? new DrawingBrush    // same checkerboard as swatch
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 8, 8),
                ViewportUnits = BrushMappingMode.Absolute,
                Drawing = new DrawingGroup
                {
                    Children =
                    {
                        new GeometryDrawing(new SolidColorBrush(Color.FromRgb(69, 71, 90)), null, new RectangleGeometry(new Rect(0,0,8,8))),
                        new GeometryDrawing(new SolidColorBrush(Color.FromRgb(49, 50, 68)), null,
                            new GeometryGroup { Children = { new RectangleGeometry(new Rect(0,0,4,4)), new RectangleGeometry(new Rect(4,4,4,4)) }})
                    }
                }
            }
            : ParseHexBrush(_selectedBgColor);
    }

    // ──────────────────────────────────────────────
    //  Confirm
    // ──────────────────────────────────────────────

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(_textBox.Text)) return;

        ResultText = _textBox.Text.Trim();
        ResultFontFamily = _fontFamilyBox.SelectedItem?.ToString() ?? "Segoe UI";
        ResultFontSize = (int)_fontSizeSlider.Value;
        ResultFontColor = _selectedFgColor.StartsWith('#')
            ? _selectedFgColor.ToUpperInvariant()
            : "#" + _selectedFgColor.ToUpperInvariant();
        ResultBackgroundColor = _selectedBgColor.StartsWith('#')
            ? _selectedBgColor.ToUpperInvariant()
            : "#" + _selectedBgColor.ToUpperInvariant();
        DialogResult = true;
    }

    // ──────────────────────────────────────────────
    //  UI helpers
    // ──────────────────────────────────────────────

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
        Margin = new Thickness(0, 0, 0, 0)
    };

    private static TextBlock MakeSectionLabel(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
        Margin = new Thickness(0, 0, 0, 0)
    };

    private static Button MakeButton(string content, Color bg, Brush fg, bool hasBorder = false) => new()
    {
        Content = content,
        Width = 80,
        Height = 32,
        Margin = new Thickness(4, 0, 0, 0),
        Background = new SolidColorBrush(bg),
        Foreground = fg,
        BorderThickness = hasBorder ? new Thickness(1) : new Thickness(0),
        BorderBrush = hasBorder
            ? new SolidColorBrush(Color.FromRgb(69, 71, 90))
            : Brushes.Transparent,
        Cursor = Cursors.Hand
    };

    private static Brush ParseHexBrush(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex[..2], 16);
                byte r = Convert.ToByte(hex[2..4], 16);
                byte g = Convert.ToByte(hex[4..6], 16);
                byte b = Convert.ToByte(hex[6..8], 16);
                return new SolidColorBrush(Color.FromArgb(a, r, g, b));
            }
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex[..2], 16);
                byte g = Convert.ToByte(hex[2..4], 16);
                byte b = Convert.ToByte(hex[4..6], 16);
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }
        }
        catch { }
        return new SolidColorBrush(Color.FromRgb(136, 136, 136));
    }
}