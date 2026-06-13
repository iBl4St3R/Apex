Dobry pomysł. To sporo kodu — podzielmy na etapy żeby nie pogubić się w implementacji.

Najpierw **model** — `NoteCard` nie nadaje się dla obrazów, potrzebujemy nowego modelu. Dodaj `ImageCard.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Apex.Models;

public class ImageCard
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("relativePath")]  // względem rootFolder, np. ".images/logo.png"
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("boardX")]
    public double BoardX { get; set; }

    [JsonPropertyName("boardY")]
    public double BoardY { get; set; }

    [JsonPropertyName("categoryId")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("locked")]
    public bool Locked { get; set; } = false;

    [JsonPropertyName("customWidth")]
    public double? CustomWidth { get; set; }

    [JsonPropertyName("customHeight")]
    public double? CustomHeight { get; set; }

    public ImageCard() { }

    public ImageCard(string id, string relativePath, double boardX, double boardY)
    {
        Id = id;
        RelativePath = relativePath;
        BoardX = boardX;
        BoardY = boardY;
    }
}
```

Dodaj do `ApexProject.cs`:

```csharp
[JsonPropertyName("imageCards")]
public List<ImageCard> ImageCards { get; set; } = new();
```

Teraz **skalowanie** — dwa osobne boole w `BoardView.xaml.cs` obok istniejących:

```csharp
public static bool ScaleImageCardsDown = true;
public static bool ScaleImageCardsUp = false;  // domyślnie nie rozciągamy w górę
```

Teraz **tworzenie elementu ImageCard** — dodaj metodę `CreateImageCardElement` w `BoardView.xaml.cs`:

```csharp
private Border CreateImageCardElement(ImageCard imageCard)
{
    string fullPath = Project != null
        ? FileService.GetFullPath(Project.RootFolder, imageCard.RelativePath)
        : string.Empty;

    double defaultWidth = imageCard.CustomWidth ?? 300;
    double defaultHeight = imageCard.CustomHeight ?? 200;

    var cardBorder = new Border
    {
        Width = defaultWidth,
        Height = defaultHeight,
        ClipToBounds = true,
        Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
        BorderBrush = imageCard.Locked
            ? new SolidColorBrush(Color.FromRgb(98, 79, 120))
            : new SolidColorBrush(Color.FromRgb(49, 50, 68)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Cursor = imageCard.Locked ? Cursors.Arrow : Cursors.Hand,
        Tag = imageCard,
        Focusable = false
    };

    var rootGrid = new Grid();

    // Obraz jako tło
    if (File.Exists(fullPath))
    {
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            double imgW = bitmap.PixelWidth;
            double renderWidth = defaultWidth;

            if (imgW > defaultWidth && ScaleImageCardsDown)
                renderWidth = defaultWidth;
            else if (imgW < defaultWidth && ScaleImageCardsUp)
                renderWidth = defaultWidth;
            else
                renderWidth = imgW;

            var image = new System.Windows.Controls.Image
            {
                Source = bitmap,
                Width = renderWidth,
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            rootGrid.Children.Add(image);
        }
        catch { }
    }

    // Overlay — kategoria (góra-prawo) i kłódka+data (dół-prawo)
    var overlay = new Grid();

    // Kategoria badge
    string? catColor = null;
    string? catName = null;
    if (!string.IsNullOrEmpty(imageCard.CategoryId) && Project != null)
    {
        var cat = Project.Categories.FirstOrDefault(c => c.Id == imageCard.CategoryId);
        if (cat != null) { catColor = cat.Color; catName = cat.Name; }
    }

    if (catName != null)
    {
        var catBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(180,
                Convert.ToByte(catColor!.TrimStart('#')[..2], 16),
                Convert.ToByte(catColor.TrimStart('#')[2..4], 16),
                Convert.ToByte(catColor.TrimStart('#')[4..6], 16))),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 6, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = catName,
                FontSize = 11,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            }
        };
        overlay.Children.Add(catBadge);
    }

    // Dół-prawo: kłódka + data
    string modifiedDateTime = "";
    if (File.Exists(fullPath))
    {
        try { modifiedDateTime = new FileInfo(fullPath).LastWriteTime.ToString("yyyy-MM-dd"); }
        catch { }
    }

    var bottomPanel = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 6, 6)
    };

    var bottomBg = new Border
    {
        Background = new SolidColorBrush(Color.FromArgb(160, 17, 17, 27)),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 2, 6, 2),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 6, 6)
    };

    var bottomContent = new StackPanel { Orientation = Orientation.Horizontal };

    var lockIcon = new TextBlock
    {
        Text = imageCard.Locked ? "🔒" : "🔓",
        FontSize = 11,
        Cursor = Cursors.Hand,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 4, 0),
        ToolTip = imageCard.Locked ? "Unlock" : "Lock"
    };
    lockIcon.MouseLeftButtonDown += (_, e) => e.Handled = true;
    lockIcon.MouseLeftButtonUp += (_, e) =>
    {
        imageCard.Locked = !imageCard.Locked;
        var current = BoardCanvas.Children.OfType<Border>()
            .FirstOrDefault(b => b.Tag == imageCard);
        if (current != null) ReplaceImageCardElement(imageCard, current);
        if (Project != null) FileService.SaveProject(Project);
        e.Handled = true;
    };
    bottomContent.Children.Add(lockIcon);

    if (!string.IsNullOrEmpty(modifiedDateTime))
        bottomContent.Children.Add(new TextBlock
        {
            Text = modifiedDateTime,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
            VerticalAlignment = VerticalAlignment.Center
        });

    bottomBg.Child = bottomContent;
    overlay.Children.Add(bottomBg);
    rootGrid.Children.Add(overlay);
    cardBorder.Child = rootGrid;

    // Events — drag, resize, context menu
    cardBorder.MouseLeftButtonDown += ImageCard_MouseLeftButtonDown;
    cardBorder.MouseMove += Card_MouseMove;
    cardBorder.MouseLeftButtonUp += Card_MouseLeftButtonUp;
    cardBorder.MouseLeave += Card_ResizeMouseLeave;
    cardBorder.ContextMenu = BuildImageCardContextMenu(imageCard, cardBorder);

    return cardBorder;
}

private void ReplaceImageCardElement(ImageCard imageCard, Border oldElement)
{
    int idx = BoardCanvas.Children.IndexOf(oldElement);
    if (idx >= 0)
    {
        var newElement = CreateImageCardElement(imageCard);
        Canvas.SetLeft(newElement, imageCard.BoardX);
        Canvas.SetTop(newElement, imageCard.BoardY);
        BoardCanvas.Children.RemoveAt(idx);
        BoardCanvas.Children.Insert(idx, newElement);
    }
}
```

Context menu dla ImageCard:

```csharp
private ContextMenu BuildImageCardContextMenu(ImageCard imageCard, Border cardElement)
{
    var menu = new ContextMenu();

    var catItem = new MenuItem { Header = "Set category" };
    if (Project != null)
    {
        foreach (var cat in Project.Categories)
        {
            var subItem = new MenuItem
            {
                Header = cat.Name,
                IsChecked = string.Equals(imageCard.CategoryId, cat.Id, StringComparison.OrdinalIgnoreCase)
            };
            subItem.Click += (_, _) =>
            {
                imageCard.CategoryId = cat.Id;
                ReplaceImageCardElement(imageCard, cardElement);
                FileService.SaveProject(Project!);
            };
            catItem.Items.Add(subItem);
        }
    }
    var noCatItem = new MenuItem { Header = "(None)" };
    noCatItem.Click += (_, _) =>
    {
        imageCard.CategoryId = null;
        ReplaceImageCardElement(imageCard, cardElement);
        FileService.SaveProject(Project!);
    };
    catItem.Items.Add(noCatItem);
    menu.Items.Add(catItem);

    var deleteItem = new MenuItem { Header = "Delete" };
    deleteItem.Click += (_, _) =>
    {
        var result = System.Windows.MessageBox.Show(
            "Remove this image from the board?\n(File will NOT be deleted from disk.)",
            "Remove Image",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            Project?.ImageCards.Remove(imageCard);
            BoardCanvas.Children.Remove(cardElement);
            if (Project != null) FileService.SaveProject(Project);
        }
    };
    menu.Items.Add(deleteItem);
    return menu;
}
```

Mouse down dla ImageCard (osobny handler bo `Tag` jest `ImageCard` nie `NoteCard`):

```csharp
private void ImageCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    if (sender is Border border && border.Tag is ImageCard imageCard)
    {
        if (imageCard.Locked) return;

        Point local = e.GetPosition(border);
        if (GetResizeEdge(border, local) != ResizeEdge.None)
        {
            Card_ResizeMouseLeftButtonDown(sender, e);
            return;
        }

        _isDraggingCard = true;
        _dragElement = border;
        _dragCard = null; // ImageCard nie jest NoteCard
        _dragStartMouse = e.GetPosition(BoardCanvas);
        _dragStartLeft = Canvas.GetLeft(border);
        _dragStartTop = Canvas.GetTop(border);
        _didDrag = false;
        border.CaptureMouse();
        e.Handled = true;
    }
}
```

Teraz aktualizuj `RenderCards` żeby renderował też ImageCards:

```csharp
private void RenderCards()
{
    BoardCanvas.Children.Clear();
    _connectionLines.Clear();
    if (Project == null) return;

    foreach (var card in Project.Cards)
    {
        var element = CreateCardElement(card);
        BoardCanvas.Children.Add(element);
        Canvas.SetLeft(element, card.BoardX);
        Canvas.SetTop(element, card.BoardY);
    }

    // ImageCards
    foreach (var imageCard in Project.ImageCards)
    {
        var element = CreateImageCardElement(imageCard);
        BoardCanvas.Children.Add(element);
        Canvas.SetLeft(element, imageCard.BoardX);
        Canvas.SetTop(element, imageCard.BoardY);
    }

    Dispatcher.BeginInvoke(new Action(RenderConnections),
        System.Windows.Threading.DispatcherPriority.Loaded);
}
```

I obsługa drag end dla ImageCard — w `Card_MouseLeftButtonUp` dodaj case gdy `_dragCard == null` ale `_dragElement?.Tag is ImageCard`:

```csharp
if (_isDraggingCard && _dragElement != null)
{
    _dragElement.ReleaseMouseCapture();

    if (_didDrag)
    {
        double newX = Canvas.GetLeft(_dragElement);
        double newY = Canvas.GetTop(_dragElement);

        if (_dragCard != null)
        {
            _dragCard.BoardX = newX;
            _dragCard.BoardY = newY;
        }
        else if (_dragElement.Tag is ImageCard imgCard)
        {
            imgCard.BoardX = newX;
            imgCard.BoardY = newY;
        }

        if (Project != null) FileService.SaveProject(Project);
    }
    else
    {
        if (_dragCard != null)
            CardSelected?.Invoke(_dragCard);
        // ImageCard — klik bez drag nie robi nic
    }

    _isDraggingCard = false;
    _dragElement = null;
    _dragCard = null;
    _didDrag = false;
    e.Handled = true;
}
```

Na końcu — "New Image" w context menu prawego kliku na boardzie, w `CanvasTransformHost_MouseRightButtonUp`:

```csharp
var newImageItem = new MenuItem { Header = "New image" };
newImageItem.Click += (_, _) => CreateNewImageAt(canvasPos);
menu.Items.Add(newImageItem);
```

I metoda:

```csharp
private void CreateNewImageAt(Point position)
{
    if (Project == null) return;

    var dialog = new Microsoft.Win32.OpenFileDialog
    {
        Title = "Select an image",
        Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
        Multiselect = false
    };

    if (dialog.ShowDialog() != true) return;

    string sourcePath = dialog.FileName;
    string imagesFolder = Path.Combine(Project.RootFolder, ".images");
    if (!Directory.Exists(imagesFolder))
        Directory.CreateDirectory(imagesFolder);

    string fileName = Path.GetFileName(sourcePath);
    string destPath = Path.Combine(imagesFolder, fileName);

    // Jeśli plik już istnieje w .images — użyj go bez kopiowania
    if (!File.Exists(destPath))
        File.Copy(sourcePath, destPath);

    string relativePath = ".images/" + fileName;
    string id = Guid.NewGuid().ToString("N")[..8];

    var imageCard = new ImageCard(id, relativePath, position.X, position.Y);
    Project.ImageCards.Add(imageCard);

    var element = CreateImageCardElement(imageCard);
    Canvas.SetLeft(element, position.X);
    Canvas.SetTop(element, position.Y);
    BoardCanvas.Children.Add(element);

    FileService.SaveProject(Project);
}
```

Resize ImageCard — w `Card_MouseLeftButtonUp` w bloku resize dodaj obsługę `ImageCard` analogicznie do `NoteCard`:

```csharp
if (_isResizing && _resizeElement != null)
{
    _resizeElement.ReleaseMouseCapture();

    if (_resizeCard != null)
    {
        _resizeCard.CustomWidth = _resizeElement.Width;
        _resizeCard.CustomHeight = _resizeElement.Height;
        ReplaceCardElement(_resizeCard, _resizeElement);
    }
    else if (_resizeElement.Tag is ImageCard imgCard)
    {
        imgCard.CustomWidth = _resizeElement.Width;
        imgCard.CustomHeight = _resizeElement.Height;
        ReplaceImageCardElement(imgCard, _resizeElement);
    }

    if (Project != null) FileService.SaveProject(Project);

    _isResizing = false;
    _resizeElement = null;
    _resizeCard = null;
    e.Handled = true;
    return;
}
```