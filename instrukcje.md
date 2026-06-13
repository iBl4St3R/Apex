edytuj plik \Apex\Views\TitleCardDialog.cs

**1. Dodaj pola do klasy** (obok innych `private readonly`):

```csharp
    private readonly CheckBox _boldCheck;
    private readonly CheckBox _italicCheck;
```

**2. Znajdź:**

```csharp
        sizeStack.Children.Add(_fontSizeSlider);
        Grid.SetColumn(sizeStack, 2);
```

**Wstaw przed `Grid.SetColumn`:**

```csharp
        var styleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0)
        };

        _boldCheck = new CheckBox
        {
            Content = "Bold",
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            Margin = new Thickness(0, 0, 12, 0),
            IsChecked = false
        };
        _boldCheck.Checked += (_, _) => UpdatePreview();
        _boldCheck.Unchecked += (_, _) => UpdatePreview();

        _italicCheck = new CheckBox
        {
            Content = "Italic",
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            IsChecked = false
        };
        _italicCheck.Checked += (_, _) => UpdatePreview();
        _italicCheck.Unchecked += (_, _) => UpdatePreview();

        styleRow.Children.Add(_boldCheck);
        styleRow.Children.Add(_italicCheck);
        sizeStack.Children.Add(styleRow);
```

**3. W `UpdatePreview` znajdź:**

```csharp
        // Colors — exactly what will appear on the card
        _previewText.Foreground = ParseHexBrush(_selectedFgColor);
```

**Wstaw przed tą linią:**

```csharp
        _previewText.FontWeight = _boldCheck?.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
        _previewText.FontStyle = _italicCheck?.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
```

**4. W `Confirm` znajdź:**

```csharp
        ResultFontFamily = _fontFamilyBox.SelectedItem?.ToString() ?? "Segoe UI";
        ResultFontSize = (int)_fontSizeSlider.Value;
```

**Dopisz dwa nowe property wynikowe** — najpierw dodaj je do klasy obok innych `public string Result...`:

```csharp
    public bool ResultBold { get; private set; }
    public bool ResultItalic { get; private set; }
```

**Następnie w `Confirm` po `ResultFontSize`:**

```csharp
        ResultBold = _boldCheck.IsChecked == true;
        ResultItalic = _italicCheck.IsChecked == true;
```
