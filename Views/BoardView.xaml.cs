using Apex.Models;
using Apex.Services;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Apex.Views
{
    /// <summary>
    /// Interaction logic for BoardView.xaml
    /// Renders note cards on an infinite canvas with pan, zoom, drag, and context menus.
    /// Pan: left-click + drag on empty background, or middle-mouse drag.
    /// Zoom: mouse wheel toward cursor position (0.5x–2.5x).
    /// </summary>
    public partial class BoardView : UserControl
    {
        public ApexProject? Project { get; private set; }

        /// <summary>
        /// Fires when a card is clicked (not dragged). Passes the NoteCard.
        /// </summary>
        public event Action<NoteCard>? CardSelected;
        public event Action<NoteCard>? CardEditRequested;
        public event Action<NoteCard>? PreviewRequested;

        // ── Pan state ──
        private bool _isPanning;
        private Point _panStartMouse;
        private double _panStartTranslateX, _panStartTranslateY;

        // ── Card drag state ──
        private bool _isDraggingCard;
        private Border? _dragElement;
        private NoteCard? _dragCard;
        private Point _dragStartMouse;
        private double _dragStartLeft, _dragStartTop;
        private bool _didDrag;

        // ── Card preview state ──
        private Border? _previewCard;

        // ── Zoom ──
        private double _zoomLevel = 1.0;
        private const double ZoomMin = 0.5;
        private const double ZoomMax = 2.5;

        private readonly PreviewCache _previewCache = new();

        // ── Resize state ──
        private bool _isResizing;
        private Border? _resizeElement;
        private NoteCard? _resizeCard;
        private Point _resizeStartMouse;
        private double _resizeStartWidth, _resizeStartHeight;
        private ResizeEdge _resizeEdge;

        private enum ResizeEdge { None, Right, Bottom, BottomRight }

        private const double ResizeHitZone = 8.0; // px od krawędzi
        private const double CardMinWidth = 220;
        private const double CardMinHeight = 120;
        private const double CardMaxWidth = 880 * 4;  // 4× large
        private const double CardMaxHeight = 320 * 4;

        private bool _connectionsVisible = false;
        private readonly List<UIElement> _connectionLines = new();


        // ── Image scaling settings (future: connect to Settings panel) ──
        public static bool ScaleImagesDown = true;  // skaluj w dół jeśli szersze niż karta
        public static bool ScaleImagesUp = true;    // skaluj w górę jeśli węższe niż karta
        public static bool ScaleImageCardsDown = true;
        public static bool ScaleImageCardsUp = false;  // domyślnie nie rozciągamy w górę

        // ── Relation drawing state ──
        private bool _isDrawingRelation = false;
        private string? _relationSourceType;
        private string? _relationSourceRef;
        private System.Windows.Shapes.Line? _relationDragLine;
        private System.Windows.Shapes.Polygon? _relationDragArrow;

        // ── Relation drag (bend handle) state ──
        private bool _isDraggingBendHandle = false;
        private Relation? _draggingRelation;
        private Point _bendDragStartMouse;
        private double _bendDragStartX, _bendDragStartY;

        private readonly List<UIElement> _relationElements = new();


        public BoardView()
        {
            InitializeComponent();
            Loaded += (_, _) => CanvasContainer.Focus();
        }

        // ──────────────────────────────────────────────
        //  Project loading
        // ──────────────────────────────────────────────

        public void LoadProject(ApexProject project)
        {
            Project = project;
            _previewCache.StartWatching(project.RootFolder);
            RenderCards();
            Dispatcher.BeginInvoke(new Action(FitAllCards),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public void RefreshCards()
        {
            RenderCards();
        }

        // ──────────────────────────────────────────────
        //  FitAllCards — reset view to show all cards
        // ──────────────────────────────────────────────

        public void FitAllCards()
        {
            if (Project == null || Project.Cards.Count == 0 || BoardCanvas.Children.Count == 0)
            {
                ZoomTransform.ScaleX = ZoomTransform.ScaleY = 1.0;
                PanTransform.X = PanTransform.Y = 0;
                _zoomLevel = 1.0;
                return;
            }

            double viewportWidth = CanvasContainer.ActualWidth;
            double viewportHeight = CanvasContainer.ActualHeight;
            if (viewportWidth < 1 || viewportHeight < 1)
            {
                // Layout not ready yet — defer to Loaded priority
                Dispatcher.BeginInvoke(new Action(FitAllCards), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            // Calculate bounding box from actual card element positions and sizes
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var child in BoardCanvas.Children)
            {
                if (child is FrameworkElement fe)
                {
                    double left = Canvas.GetLeft(fe);
                    double top = Canvas.GetTop(fe);
                    double w = fe.ActualWidth > 0 ? fe.ActualWidth : 220;
                    double h = fe.ActualHeight > 0 ? fe.ActualHeight : 80;

                    if (left < minX) minX = left;
                    if (top < minY) minY = top;
                    if (left + w > maxX) maxX = left + w;
                    if (top + h > maxY) maxY = top + h;
                }
            }

            if (minX == double.MaxValue)
            {
                ZoomTransform.ScaleX = ZoomTransform.ScaleY = 1.0;
                PanTransform.X = PanTransform.Y = 0;
                _zoomLevel = 1.0;
                return;
            }

            double boundingWidth = maxX - minX;
            double boundingHeight = maxY - minY;

            // Zoom to fit with 40px padding on each side
            double zoomX = viewportWidth / (boundingWidth + 80);
            double zoomY = viewportHeight / (boundingHeight + 80);
            double zoom = Math.Min(zoomX, zoomY);
            zoom = Math.Clamp(zoom, ZoomMin, ZoomMax);

            _zoomLevel = zoom;
            ZoomTransform.ScaleX = zoom;
            ZoomTransform.ScaleY = zoom;

            // Center the bounding box in the viewport at this zoom
            PanTransform.X = (viewportWidth - boundingWidth * zoom) / 2 - minX * zoom;
            PanTransform.Y = (viewportHeight - boundingHeight * zoom) / 2 - minY * zoom;
        }

        // ──────────────────────────────────────────────
        //  FocusCard — zoom to a specific card
        // ──────────────────────────────────────────────

        public void FocusCard(string relativePath)
        {
            if (Project == null) return;

            var card = Project.Cards.FirstOrDefault(c =>
                string.Equals(c.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
            if (card == null) return;

            Border? cardElement = null;
            double cardWidth = 220, cardHeight = 80;
            foreach (var child in BoardCanvas.Children)
            {
                if (child is Border b && b.Tag is NoteCard nc &&
                    string.Equals(nc.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                {
                    cardElement = b;
                    if (b.ActualWidth > 0) cardWidth = b.ActualWidth;
                    if (b.ActualHeight > 0) cardHeight = b.ActualHeight;
                    break;
                }
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                double vpw = CanvasContainer.ActualWidth;
                double vph = CanvasContainer.ActualHeight;
                if (vpw < 1 || vph < 1) return;

                double targetZoom = 1.5;
                _zoomLevel = targetZoom;
                ZoomTransform.ScaleX = _zoomLevel;
                ZoomTransform.ScaleY = _zoomLevel;

                PanTransform.X = vpw / 2 - (card.BoardX + cardWidth / 2) * _zoomLevel;
                PanTransform.Y = vph / 2 - (card.BoardY + cardHeight / 2) * _zoomLevel;

                // Flash the card border white
                // Flash the card border white
                if (cardElement != null)
                {
                    // Zapamiętaj oryginalny kolor zależny od stanu locked
                    var originalBrush = card.Locked
                        ? new SolidColorBrush(Color.FromRgb(98, 79, 120))
                        : new SolidColorBrush(Color.FromRgb(49, 50, 68));
                    var originalThickness = new Thickness(1);

                    cardElement.BorderBrush = new SolidColorBrush(Colors.White);
                    cardElement.BorderThickness = new Thickness(2);

                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(800)
                    };
                    timer.Tick += (_, _) =>
                    {
                        cardElement.BorderBrush = originalBrush;
                        cardElement.BorderThickness = originalThickness;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ──────────────────────────────────────────────
        //  Card rendering
        // ──────────────────────────────────────────────

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

            // TitleCards
            foreach (var titleCard in Project.TitleCards)
            {
                var element = CreateTitleCardElement(titleCard);
                BoardCanvas.Children.Add(element);
                Canvas.SetLeft(element, titleCard.BoardX);
                Canvas.SetTop(element, titleCard.BoardY);
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                RenderConnections();
                RenderRelations();
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            // Relations render after layout pass (need ActualWidth/Height of cards)
            Dispatcher.BeginInvoke(new Action(RenderRelations),
                System.Windows.Threading.DispatcherPriority.Loaded);

        }

        // ──────────────────────────────────────────────
        //  Card element creation
        // ──────────────────────────────────────────────




        private Border CreateCardElement(NoteCard card)
        {
            string title = Path.GetFileNameWithoutExtension(card.RelativePath);

            string? catColor = null;
            string? catName = null;
            if (!string.IsNullOrEmpty(card.CategoryId) && Project != null)
            {
                var cat = Project.Categories.FirstOrDefault(c => c.Id == card.CategoryId);
                if (cat != null) { catColor = cat.Color; catName = cat.Name; }
            }

            var stripBrush = catColor != null
                ? ParseHexBrush(catColor)
                : new SolidColorBrush(Color.FromRgb(69, 71, 90));

            string fullPath = Project != null
                ? FileService.GetFullPath(Project.RootFolder, card.RelativePath)
                : string.Empty;

            string modifiedDateTime = "";
            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                try { modifiedDateTime = new FileInfo(fullPath).LastWriteTime.ToString("yyyy-MM-dd HH:mm"); }
                catch { }
            }

            double effectiveHeight = card.CustomHeight ?? card.CardSize switch
            {
                "medium" => 160,
                "large" => 320,
                _ => 120
            };
            double effectiveWidth = card.CustomWidth ?? card.CardSize switch
            {
                "medium" => 440,
                "large" => 880,
                _ => 220
            };

            int previewMaxChars = card.CustomWidth.HasValue || card.CustomHeight.HasValue
                ? (int)((effectiveHeight - 60) / 16.0 * (effectiveWidth / 10.0))
                : card.CardSize switch { "medium" => 300, "large" => 600, _ => 100 };

            previewMaxChars = Math.Clamp(previewMaxChars, 50, 8000);

            string previewText = !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath)
                                ? _previewCache.GetPreview(fullPath, previewMaxChars)
                                : "";

            // Outer card border
            var cardBorder = new Border
            {
                Width = effectiveWidth,  
                Height = effectiveHeight, 
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
                BorderBrush = card.Locked
        ? new SolidColorBrush(Color.FromRgb(98, 79, 120))
        : new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Cursor = card.Locked ? Cursors.Arrow : Cursors.Hand,
                Tag = card,
                Focusable = false
            };

            // Outer grid: color strip | content
            var outerGrid = new Grid();
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Color strip
            outerGrid.Children.Add(new Border
            {
                Background = stripBrush,
                CornerRadius = new CornerRadius(8, 0, 0, 8),
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Left
            });

            // Inner grid: 3 rows — title row | preview | date
            var innerGrid = new Grid { Margin = new Thickness(8, 6, 8, 6) };
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // title
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // preview
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // date

            // — Row 0: title + category badge —
            var titleRow = new Grid();
            titleRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // tytuł + badge
            titleRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // ścieżka folderu

            // Wiersz 0,0: tytuł + badge obok siebie
            var titleAndBadgeRow = new Grid();
            titleAndBadgeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleAndBadgeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 0);
            titleAndBadgeRow.Children.Add(titleBlock);

            if (catName != null)
            {
                var badge = new Border
                {
                    Background = stripBrush,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = catName,
                        FontSize = 11,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.SemiBold
                    }
                };
                Grid.SetColumn(badge, 1);
                titleAndBadgeRow.Children.Add(badge);
            }

            Grid.SetRow(titleAndBadgeRow, 0);
            titleRow.Children.Add(titleAndBadgeRow);

            // Wiersz 0,1: ścieżka folderu (tylko jeśli plik jest w podfolderze)
            string? folderPath = Path.GetDirectoryName(card.RelativePath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folderPath))
            {
                var folderLabel = new TextBlock
                {
                    Text = folderPath + "/",
                    FontSize = 9,                    // mniejsza
                    Foreground = new SolidColorBrush(Color.FromRgb(69, 71, 90)),  // ciemniejszy szary
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 1, 0, 0)
                };
                Grid.SetRow(folderLabel, 1);
                titleRow.Children.Add(folderLabel);
            }

            Grid.SetRow(titleRow, 0);
            innerGrid.Children.Add(titleRow);

            double previewMaxHeight = (card.CustomHeight.HasValue || card.CardSize is "large" or "medium")
                                    ? double.PositiveInfinity
                                    : 64;
            var previewBlock = BuildPreviewElement(
    previewText,
    effectiveWidth,
    !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath) ? fullPath : null,
    linkTarget =>
    {
        if (Project == null) return;
        var target = Project.Cards.FirstOrDefault(c =>
            string.Equals(
                Path.GetFileNameWithoutExtension(c.RelativePath),
                linkTarget,
                StringComparison.OrdinalIgnoreCase));
        if (target != null)
            FocusCard(target.RelativePath);
    },
    linkTarget => Project?.Cards.Any(c =>
        string.Equals(
            Path.GetFileNameWithoutExtension(c.RelativePath),
            linkTarget,
            StringComparison.OrdinalIgnoreCase)) == true
);


            Grid.SetRow(previewBlock, 1);
            innerGrid.Children.Add(previewBlock);



            // — Row 2: date + lock button —
            var bottomRow = new Grid();
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // pusty spacer
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // kłódka
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // data

            var lockIcon = new TextBlock
            {
                Text = card.Locked ? "🔒" : "🔓",
                FontSize = 12,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = card.Locked ? "Unlock card" : "Lock card"
            };

            lockIcon.MouseLeftButtonDown += (_, e) => e.Handled = true;
            lockIcon.MouseLeftButtonUp += (_, e) =>
            {
                card.Locked = !card.Locked;
                Border? current = BoardCanvas.Children.OfType<Border>().FirstOrDefault(b => b.Tag == card);
                if (current != null) ReplaceCardElement(card, current);
                if (Project != null) FileService.SaveProject(Project);
                Dispatcher.BeginInvoke(new Action(RenderRelations), System.Windows.Threading.DispatcherPriority.Loaded);
                e.Handled = true;
            };

            Grid.SetColumn(lockIcon, 1);
            bottomRow.Children.Add(lockIcon);

            if (!string.IsNullOrEmpty(modifiedDateTime))
            {
                var dateBlock = new TextBlock
                {
                    Text = modifiedDateTime,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                Grid.SetColumn(dateBlock, 2);
                bottomRow.Children.Add(dateBlock);
            }

            Grid.SetRow(bottomRow, 2);
            innerGrid.Children.Add(bottomRow);

            Grid.SetColumn(innerGrid, 1);
            outerGrid.Children.Add(innerGrid);
            cardBorder.Child = outerGrid;

            // Events
            cardBorder.MouseLeftButtonDown += Card_MouseLeftButtonDown;
            cardBorder.MouseMove += Card_MouseMove;
            cardBorder.MouseLeftButtonUp += Card_MouseLeftButtonUp;
            cardBorder.MouseLeave += Card_ResizeMouseLeave;
            cardBorder.ContextMenu = BuildCardContextMenu(card, cardBorder);




            return cardBorder;
        }




        // ──────────────────────────────────────────────
        //  Card drag
        // ──────────────────────────────────────────────

        private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is NoteCard card)
            {
                if (card.Locked) return;   // ← dodaj

                Point local = e.GetPosition(border);
                if (GetResizeEdge(border, local) != ResizeEdge.None)
                {
                    Card_ResizeMouseLeftButtonDown(sender, e);
                    return;
                }

                _isDraggingCard = true;
                _dragElement = border;
                _dragCard = card;
                _dragStartMouse = e.GetPosition(BoardCanvas);
                _dragStartLeft = Canvas.GetLeft(border);
                _dragStartTop = Canvas.GetTop(border);
                _didDrag = false;
                border.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Card_MouseMove(object sender, MouseEventArgs e)
        {
            // Resize ma pierwszeństwo
            if (_isResizing && _resizeElement != null
                && e.LeftButton == MouseButtonState.Pressed)
            {
                Point current = e.GetPosition(BoardCanvas);
                double dx = current.X - _resizeStartMouse.X;
                double dy = current.Y - _resizeStartMouse.Y;

                double newW = _resizeStartWidth;
                double newH = _resizeStartHeight;

                if (_resizeEdge == ResizeEdge.Right || _resizeEdge == ResizeEdge.BottomRight)
                    newW = Math.Clamp(_resizeStartWidth + dx, CardMinWidth, CardMaxWidth);
                if (_resizeEdge == ResizeEdge.Bottom || _resizeEdge == ResizeEdge.BottomRight)
                    newH = Math.Clamp(_resizeStartHeight + dy, CardMinHeight, CardMaxHeight);

                _resizeElement.Width = newW;
                _resizeElement.Height = newH;
                e.Handled = true;
                return;
            }

            // Hover cursor na krawędziach (gdy nie dragujemy)
            if (!_isDraggingCard && !_isResizing && sender is Border b)
                Card_ResizeMouseMove(sender, e);

            // Normalny drag
            if (_isDraggingCard && _dragElement != null
                && e.LeftButton == MouseButtonState.Pressed)
            {
                Point current = e.GetPosition(BoardCanvas);
                double dx = current.X - _dragStartMouse.X;
                double dy = current.Y - _dragStartMouse.Y;

                if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
                    _didDrag = true;

                if (_didDrag)
                {
                    double cardW = _dragElement.ActualWidth > 0 ? _dragElement.ActualWidth : 220;
                    double cardH = _dragElement.ActualHeight > 0 ? _dragElement.ActualHeight : 120;

                    double newLeft = Math.Max(0, Math.Min(_dragStartLeft + dx, 10000 - cardW));
                    double newTop = Math.Max(0, Math.Min(_dragStartTop + dy, 10000 - cardH));

                    Canvas.SetLeft(_dragElement, newLeft);
                    Canvas.SetTop(_dragElement, newTop);
                }
                e.Handled = true;
            }
        }

        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Zakończ resize
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
                else if (_resizeElement.Tag is TitleCard titleCard)
                {
                    titleCard.CustomWidth = _resizeElement.Width;
                    titleCard.CustomHeight = _resizeElement.Height;
                    ReplaceTitleCardElement(titleCard, _resizeElement);
                }

                if (Project != null) FileService.SaveProject(Project);

                _isResizing = false;
                _resizeElement = null;
                _resizeCard = null;
                e.Handled = true;
                return;
            }

            // Normalny drag end
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
                    else if (_dragElement.Tag is TitleCard titleCard)
                    {
                        titleCard.BoardX = newX;
                        titleCard.BoardY = newY;
                    }

                    if (Project != null) FileService.SaveProject(Project);

                    RenderRelations();
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
        }

        private FrameworkElement BuildPreviewElement(
    string text,
    double cardWidth,
    string? fullPath,
    Action<string>? onLinkClicked = null,
    Func<string, bool>? linkExists = null)
        {
            string? noteFolder = fullPath != null ? Path.GetDirectoryName(fullPath) : null;

            var stack = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };

            var htmlImgRegex = new Regex(@"<img[^>]+src\s*=\s*[""']([^""']+)[""'][^>]*>", RegexOptions.IgnoreCase);
            var mdImgRegex = new Regex(@"!\[[^\]]*\]\(([^)]+)\)");
            var htmlTagRegex = new Regex(@"<[^>]+>");
            var alignCenterRegex = new Regex(@"align\s*=\s*[""']center[""']", RegexOptions.IgnoreCase);

            var pendingLines = new List<string>();
            bool pendingCenter = false;

            bool inCodeBlock = false;
            var codeLines = new List<string>();

            void FlushPendingText(bool centered = false)
            {
                if (pendingLines.Count == 0) return;
                string joined = string.Join("\n", pendingLines);
                if (!string.IsNullOrWhiteSpace(joined))
                {
                    var tb = BuildPreviewTextBlock(joined, double.PositiveInfinity, onLinkClicked, linkExists);
                    if (centered)
                        tb.TextAlignment = TextAlignment.Center;
                    stack.Children.Add(tb);
                }
                pendingLines.Clear();
            }

            var lines = text.Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');

                if (line.StartsWith("```"))
                {
                    if (!inCodeBlock)
                    {
                        FlushPendingText(pendingCenter);
                        inCodeBlock = true;
                        codeLines.Clear();
                    }
                    else
                    {
                        inCodeBlock = false;
                        var codeBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(4),
                            Padding = new Thickness(8, 6, 8, 6),
                            Margin = new Thickness(0, 2, 0, 2)
                        };
                        var codeText = new TextBlock
                        {
                            Text = string.Join("\n", codeLines),
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 10,
                            Foreground = new SolidColorBrush(Color.FromRgb(186, 194, 222)),
                            TextWrapping = TextWrapping.Wrap
                        };
                        codeBorder.Child = codeText;
                        stack.Children.Add(codeBorder);
                        codeLines.Clear();
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeLines.Add(line);
                    continue;
                }


                var htmlMatch = htmlImgRegex.Match(line);
                var mdMatch = mdImgRegex.Match(line);

                if (htmlMatch.Success)
                {
                    FlushPendingText(pendingCenter);
                    pendingCenter = false;

                    // Sprawdź czy <img> ma align="center" lub jest wewnątrz tagu z center
                    bool imgCentered = alignCenterRegex.IsMatch(line);
                    var img = BuildCardImage(htmlMatch.Groups[1].Value, noteFolder, cardWidth);
                    if (img != null)
                    {
                        img.HorizontalAlignment = imgCentered
                            ? HorizontalAlignment.Center
                            : HorizontalAlignment.Left;
                        stack.Children.Add(img);
                    }

                    string rest = htmlTagRegex.Replace(line, "").Trim();
                    if (!string.IsNullOrWhiteSpace(rest))
                        pendingLines.Add(rest);
                }
                else if (mdMatch.Success)
                {
                    FlushPendingText(pendingCenter);
                    pendingCenter = false;

                    var img = BuildCardImage(mdMatch.Groups[1].Value, noteFolder, cardWidth);
                    if (img != null) stack.Children.Add(img);

                    string rest = mdImgRegex.Replace(line, "").Trim();
                    if (!string.IsNullOrWhiteSpace(rest))
                        pendingLines.Add(rest);
                }
                else if (line == "---" || line == "***" || line == "___")
                {
                    FlushPendingText(pendingCenter);
                    var separator = new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                        BorderThickness = new Thickness(0, 1, 0, 0),
                        Margin = new Thickness(0, 4, 0, 4),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    stack.Children.Add(separator);
                }
                else
                {
                    // Sprawdź czy linia zawiera tag z align="center"
                    bool hasTag = htmlTagRegex.IsMatch(line);
                    if (hasTag && alignCenterRegex.IsMatch(line))
                    {
                        // Flush poprzedniego tekstu bez centrowania
                        FlushPendingText(pendingCenter);
                        pendingCenter = true;
                    }
                    else if (hasTag && line.TrimStart().StartsWith("</"))
                    {
                        // Tag zamykający — flush z aktualnym center i reset
                        string cleaned = htmlTagRegex.Replace(line, "").Trim();
                        if (!string.IsNullOrWhiteSpace(cleaned))
                            pendingLines.Add(cleaned);
                        FlushPendingText(pendingCenter);
                        pendingCenter = false;
                        continue;
                    }

                    string cleanedLine = htmlTagRegex.Replace(line, "").Trim();
                    pendingLines.Add(cleanedLine);
                }
            }

            FlushPendingText(pendingCenter);
            return stack;
        }

        private static (string type, string refId)? GetElementTypeRef(Border b)
        {
            if (b.Tag is NoteCard nc) return ("note", nc.RelativePath);
            if (b.Tag is TitleCard tc) return ("title", tc.Id);
            if (b.Tag is ImageCard ic) return ("image", ic.Id);
            return null;
        }

        private Point GetElementCenter(string type, string refId)
        {
            foreach (var child in BoardCanvas.Children.OfType<Border>())
            {
                bool match = type switch
                {
                    "note" => child.Tag is NoteCard nc && string.Equals(nc.RelativePath, refId, StringComparison.OrdinalIgnoreCase),
                    "title" => child.Tag is TitleCard tc && string.Equals(tc.Id, refId, StringComparison.OrdinalIgnoreCase),
                    "image" => child.Tag is ImageCard ic && string.Equals(ic.Id, refId, StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
                if (match)
                {
                    double l = Canvas.GetLeft(child);
                    double t = Canvas.GetTop(child);
                    double w = child.ActualWidth > 0 ? child.ActualWidth : 220;
                    double h = child.ActualHeight > 0 ? child.ActualHeight : 80;
                    return new Point(l + w / 2, t + h / 2);
                }
            }
            return new Point(0, 0);
        }


        // ──────────────────────────────────────────────
        //  Relation drawing
        // ──────────────────────────────────────────────

        private void BeginDrawRelation(string sourceType, string sourceRef)
        {
            _isDrawingRelation = true;
            _relationSourceType = sourceType;
            _relationSourceRef = sourceRef;

            // Temporary drag line
            _relationDragLine = new System.Windows.Shapes.Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(180, 203, 166, 247)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            _relationDragArrow = new System.Windows.Shapes.Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(180, 203, 166, 247)),
                IsHitTestVisible = false
            };

            BoardCanvas.Children.Add(_relationDragLine);
            BoardCanvas.Children.Add(_relationDragArrow);

            // Start coords = center of source element
            Point src = GetElementCenter(sourceType, sourceRef);
            _relationDragLine.X1 = src.X;
            _relationDragLine.Y1 = src.Y;
            _relationDragLine.X2 = src.X;
            _relationDragLine.Y2 = src.Y;

            // Capture all mouse moves at canvas level
            CanvasTransformHost.MouseMove += DrawRelation_MouseMove;
            CanvasTransformHost.PreviewMouseLeftButtonUp += DrawRelation_MouseUp;

            // ESC cancels
            KeyDown += DrawRelation_KeyDown;

            Cursor = System.Windows.Input.Cursors.Cross;
        }

        private void DrawRelation_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawingRelation || _relationDragLine == null) return;

            Point pos = e.GetPosition(BoardCanvas);
            _relationDragLine.X2 = pos.X;
            _relationDragLine.Y2 = pos.Y;

            // Update temp arrowhead
            if (_relationDragArrow != null)
                UpdateArrowHead(_relationDragArrow,
                    _relationDragLine.X1, _relationDragLine.Y1,
                    pos.X, pos.Y);
        }

        private void DrawRelation_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawingRelation) return;

            // Find which element was clicked
            var hit = e.OriginalSource as DependencyObject;
            var targetBorder = FindAncestor<Border>(hit,
                b => b.Tag is NoteCard || b.Tag is TitleCard || b.Tag is ImageCard);

            if (targetBorder != null)
            {
                var tr = GetElementTypeRef(targetBorder);
                if (tr.HasValue)
                {
                    string tType = tr.Value.type;
                    string tRef = tr.Value.refId;

                    // No self-relations
                    bool isSelf = string.Equals(tType, _relationSourceType,
                                      StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(tRef, _relationSourceRef,
                                      StringComparison.OrdinalIgnoreCase);

                    if (!isSelf && Project != null)
                    {
                        var candidate = new Relation(
                            Guid.NewGuid().ToString("N")[..8],
                            _relationSourceType!, _relationSourceRef!,
                            tType, tRef);

                        // No duplicate relations
                        bool duplicate = Project.Relations.Any(r => r.IsSameAs(candidate));
                        if (!duplicate)
                        {
                            Project.Relations.Add(candidate);
                            FileService.SaveProject(Project);
                            RenderRelations();
                        }
                    }
                }
            }

            CancelDrawRelation();
            e.Handled = true;
        }

        private void DrawRelation_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                CancelDrawRelation();
        }

        private void CancelDrawRelation()
        {
            _isDrawingRelation = false;
            _relationSourceType = null;
            _relationSourceRef = null;

            if (_relationDragLine != null) { BoardCanvas.Children.Remove(_relationDragLine); _relationDragLine = null; }
            if (_relationDragArrow != null) { BoardCanvas.Children.Remove(_relationDragArrow); _relationDragArrow = null; }

            CanvasTransformHost.MouseMove -= DrawRelation_MouseMove;
            CanvasTransformHost.PreviewMouseLeftButtonUp -= DrawRelation_MouseUp;

            KeyDown -= DrawRelation_KeyDown;

            Cursor = System.Windows.Input.Cursors.Arrow;
        }




        private FrameworkElement? BuildCardImage(string src, string? noteFolder, double cardWidth)
        {
            try
            {
                Uri uri;
                if (Uri.IsWellFormedUriString(src, UriKind.Absolute))
                {
                    uri = new Uri(src, UriKind.Absolute);
                }
                else if (noteFolder != null)
                {
                    string fullImgPath = Path.GetFullPath(Path.Combine(noteFolder, src));
                    if (!File.Exists(fullImgPath)) return null;
                    uri = new Uri(fullImgPath, UriKind.Absolute);
                }
                else return null;

                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                double imgW = bitmap.PixelWidth;
                double availableW = cardWidth - 20; // margines 10px z każdej strony

                double renderWidth;
                if (imgW > availableW && ScaleImagesDown)
                    renderWidth = availableW;
                else if (imgW < availableW && ScaleImagesUp)
                    renderWidth = availableW;
                else
                    renderWidth = imgW;

                return new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    Width = renderWidth,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 4)
                };
            }
            catch { return null; }
        }





        // ──────────────────────────────────────────────
        //  Relation rendering
        // ──────────────────────────────────────────────

        public void RenderRelations()
        {
            // Remove old relation elements
            foreach (var el in _relationElements)
                BoardCanvas.Children.Remove(el);
            _relationElements.Clear();

            if (Project == null) return;

            // Znajdź indeks pierwszej karty (Border z Tag NoteCard/ImageCard/TitleCard)
            // Linie wstawiamy PRZED kartami, handlery PO kartach
            int firstCardIndex = 0;
            for (int i = 0; i < BoardCanvas.Children.Count; i++)
            {
                if (BoardCanvas.Children[i] is Border b &&
                    (b.Tag is NoteCard || b.Tag is ImageCard || b.Tag is TitleCard))
                {
                    firstCardIndex = i;
                    break;
                }
            }

            int lineInsertIndex = firstCardIndex; // linie i strzałki przed kartami
            int handleInsertIndex = BoardCanvas.Children.Count; // handlery na samym wierzchu

            foreach (var rel in Project.Relations)
            {
                Point src = GetElementCenter(rel.SourceType, rel.SourceRef);
                Point tgtCenter = GetElementCenter(rel.TargetType, rel.TargetRef);

                if (src == tgtCenter && src == new Point(0, 0)) continue;

                double midX = (src.X + tgtCenter.X) / 2 + rel.BendX;
                double midY = (src.Y + tgtCenter.Y) / 2 + rel.BendY;
                Point tgt = GetElementEdgePoint(rel.TargetType, rel.TargetRef, new Point(midX, midY));

                var path = BuildRelationPath(src, new Point(midX, midY), tgt, rel.LineColor, rel.LineThickness);

                var arrow = new System.Windows.Shapes.Polygon
                {
                    Fill = new SolidColorBrush(ParseHexColor(rel.LineColor, 200)),
                    IsHitTestVisible = false
                };
                UpdateArrowHead(arrow, midX, midY, tgt.X, tgt.Y, rel.LineThickness);

                BoardCanvas.Children.Insert(lineInsertIndex, path);
                lineInsertIndex++;
                BoardCanvas.Children.Insert(lineInsertIndex, arrow);
                lineInsertIndex++;

                // Sprawdź czy oba elementy są locked — jeśli tak, nie renderuj handle'a
                bool bothLocked = IsElementLocked(rel.SourceType, rel.SourceRef) && IsElementLocked(rel.TargetType, rel.TargetRef);

                _relationElements.Add(path);
                _relationElements.Add(arrow);

                if (!bothLocked)
                {
                    double handleX = 0.25 * src.X + 0.5 * midX + 0.25 * tgt.X;
                    double handleY = 0.25 * src.Y + 0.5 * midY + 0.25 * tgt.Y;
                    var handle = BuildBendHandle(rel, handleX, handleY);
                    BoardCanvas.Children.Add(handle);
                    _relationElements.Add(handle);
                }
                else
                {
                    // Placeholder null żeby indeksy path/arrow/handle były spójne w UpdateRelationVisuals
                    // Nie dodajemy nic — ale UpdateRelationVisuals i tak nie będzie wołane bo drag nie działa
                }
            }
        }

        private bool IsElementLocked(string type, string refId)
        {
            foreach (var child in BoardCanvas.Children.OfType<Border>())
            {
                bool match = type switch
                {
                    "note" => child.Tag is NoteCard nc && string.Equals(nc.RelativePath, refId, StringComparison.OrdinalIgnoreCase),
                    "title" => child.Tag is TitleCard tc && string.Equals(tc.Id, refId, StringComparison.OrdinalIgnoreCase),
                    "image" => child.Tag is ImageCard ic && string.Equals(ic.Id, refId, StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
                if (match)
                {
                    return child.Tag switch
                    {
                        NoteCard nc => nc.Locked,
                        TitleCard tc => tc.Locked,
                        ImageCard ic => ic.Locked,
                        _ => false
                    };
                }
            }
            return false;
        }

        private System.Windows.Shapes.Path BuildRelationPath(Point src, Point ctrl, Point tgt, string color = "#CBA6F7", double thickness = 1.5)
        {
            var geo = new System.Windows.Media.PathGeometry();
            var fig = new System.Windows.Media.PathFigure { StartPoint = src };
            fig.Segments.Add(new System.Windows.Media.QuadraticBezierSegment(ctrl, tgt, true));
            geo.Figures.Add(fig);

            return new System.Windows.Shapes.Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(ParseHexColor(color, 160)),
                StrokeThickness = thickness,
                Fill = System.Windows.Media.Brushes.Transparent,
                IsHitTestVisible = false
            };
        }

        private static Color ParseHexColor(string hex, byte alpha = 255)
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = Convert.ToByte(hex[..2], 16);
                    byte g = Convert.ToByte(hex[2..4], 16);
                    byte b = Convert.ToByte(hex[4..6], 16);
                    return Color.FromArgb(alpha, r, g, b);
                }
            }
            catch { }
            return Color.FromArgb(alpha, 203, 166, 247);
        }

        private System.Windows.Shapes.Path BuildRelationPath(Point src, Point ctrl, Point tgt)
        {
            var geo = new System.Windows.Media.PathGeometry();
            var fig = new System.Windows.Media.PathFigure { StartPoint = src };
            fig.Segments.Add(new System.Windows.Media.QuadraticBezierSegment(ctrl, tgt, true));
            geo.Figures.Add(fig);

            return new System.Windows.Shapes.Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(Color.FromArgb(160, 203, 166, 247)),
                StrokeThickness = 1.5,
                Fill = System.Windows.Media.Brushes.Transparent,
                IsHitTestVisible = false
            };
        }

        private System.Windows.Shapes.Ellipse BuildBendHandle(Relation rel, double cx, double cy)
        {
            const double R = 10;
            var handleColor = ParseHexColor(rel.LineColor, 255);
            var handle = new System.Windows.Shapes.Ellipse
            {
                Width = R * 2,
                Height = R * 2,
                Fill = new SolidColorBrush(handleColor),
                Stroke = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
                StrokeThickness = 2,
                Cursor = Cursors.SizeAll,
                Tag = rel,
                IsHitTestVisible = true
            };
            Canvas.SetLeft(handle, cx - R);
            Canvas.SetTop(handle, cy - R);

            handle.MouseLeftButtonDown += BendHandle_MouseDown;
            handle.MouseMove += BendHandle_MouseMove;
            handle.MouseLeftButtonUp += BendHandle_MouseUp;
            handle.MouseRightButtonUp += BendHandle_MouseRightButtonUp;

            return handle;
        }

        private void BendHandle_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Ellipse handle) return;
            if (handle.Tag is not Relation rel) return;

            var menu = new ContextMenu();
            menu.PlacementTarget = handle;

            var settingsItem = new MenuItem { Header = "Relation settings" };
            settingsItem.Click += (_, _) =>
            {
                var dialog = new RelationSettingsDialog(rel.LineColor, rel.LineThickness) { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() == true)
                {
                    rel.LineColor = dialog.ResultColor;
                    rel.LineThickness = dialog.ResultThickness;
                    if (Project != null) FileService.SaveProject(Project);
                    RenderRelations();
                }
            };
            menu.Items.Add(settingsItem);

            var deleteItem = new MenuItem { Header = "Delete relation" };
            deleteItem.Click += (_, _) =>
            {
                Project?.Relations.Remove(rel);
                if (Project != null) FileService.SaveProject(Project);
                RenderRelations();
            };
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
            e.Handled = true;
        }

        private static void UpdateArrowHead(System.Windows.Shapes.Polygon arrow, double x1, double y1, double x2, double y2, double thickness = 1.5)
        {
            double scale = Math.Clamp(thickness / 1.5, 0.7, 3.0);
            double aLen = 11 * scale;
            double aWidth = 5 * scale;

            double angle = Math.Atan2(y2 - y1, x2 - x1);
            double ax = x2 - aLen * Math.Cos(angle);
            double ay = y2 - aLen * Math.Sin(angle);

            arrow.Points = new PointCollection
    {
        new Point(x2, y2),
        new Point(ax - aWidth * Math.Sin(angle), ay + aWidth * Math.Cos(angle)),
        new Point(ax + aWidth * Math.Sin(angle), ay - aWidth * Math.Cos(angle))
    };
        }


        // ──────────────────────────────────────────────
        //  Bend handle drag
        // ──────────────────────────────────────────────

        private void BendHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Ellipse handle) return;
            if (handle.Tag is not Relation rel) return;

            _isDraggingBendHandle = true;
            _draggingRelation = rel;
            _bendDragStartMouse = e.GetPosition(BoardCanvas);
            _bendDragStartX = rel.BendX;
            _bendDragStartY = rel.BendY;

            handle.CaptureMouse();

            e.Handled = true;
        }

        



        private void BendHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingBendHandle || _draggingRelation == null) return;
            if (sender is not System.Windows.Shapes.Ellipse handle) return;

            Point cur = e.GetPosition(BoardCanvas);
            double dx = cur.X - _bendDragStartMouse.X;
            double dy = cur.Y - _bendDragStartMouse.Y;

            _draggingRelation.BendX = _bendDragStartX + dx;
            _draggingRelation.BendY = _bendDragStartY + dy;

            // Zaktualizuj wizualnie BEZ pełnego RenderRelations() — żeby nie tracić mouse capture
            UpdateRelationVisuals(_draggingRelation, handle);

            e.Handled = true;
        }

        private void BendHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingBendHandle) return;

            if (sender is System.Windows.Shapes.Ellipse handle)
                handle.ReleaseMouseCapture();

            

            _isDraggingBendHandle = false;

            if (Project != null) FileService.SaveProject(Project);

            // Pełny rerender dopiero po puszczeniu przycisku
            RenderRelations();

            _draggingRelation = null;
            e.Handled = true;
        }

        private void UpdateRelationVisuals(Relation rel, System.Windows.Shapes.Ellipse handle)
        {
            Point src = GetElementCenter(rel.SourceType, rel.SourceRef);
            Point tgtCenter = GetElementCenter(rel.TargetType, rel.TargetRef);

            double midX = (src.X + tgtCenter.X) / 2 + rel.BendX;
            double midY = (src.Y + tgtCenter.Y) / 2 + rel.BendY;

            // Użyj midX/midY jako punktu "skąd przychodzimy" żeby edge point był spójny z RenderRelations
            Point tgt = GetElementEdgePoint(rel.TargetType, rel.TargetRef, new Point(midX, midY));

            // Handle na krzywej Beziera liczonej do tgt (krawędź) — tak samo jak RenderRelations
            double handleX = 0.25 * src.X + 0.5 * midX + 0.25 * tgt.X;
            double handleY = 0.25 * src.Y + 0.5 * midY + 0.25 * tgt.Y;

            Canvas.SetLeft(handle, handleX - handle.Width / 2);
            Canvas.SetTop(handle, handleY - handle.Height / 2);

            int handleIdx = _relationElements.IndexOf(handle);
            if (handleIdx < 2) return;

            if (_relationElements[handleIdx - 2] is System.Windows.Shapes.Path path)
            {
                var geo = new System.Windows.Media.PathGeometry();
                var fig = new System.Windows.Media.PathFigure { StartPoint = src };
                fig.Segments.Add(new System.Windows.Media.QuadraticBezierSegment(new Point(midX, midY), tgt, true));
                geo.Figures.Add(fig);
                path.Data = geo;
            }

            if (_relationElements[handleIdx - 1] is System.Windows.Shapes.Polygon arrow)
                UpdateArrowHead(arrow, midX, midY, tgt.X, tgt.Y, rel.LineThickness);
        }

        private Point GetElementEdgePoint(string type, string refId, Point from)
        {
            foreach (var child in BoardCanvas.Children.OfType<Border>())
            {
                bool match = type switch
                {
                    "note" => child.Tag is NoteCard nc && string.Equals(nc.RelativePath, refId, StringComparison.OrdinalIgnoreCase),
                    "title" => child.Tag is TitleCard tc && string.Equals(tc.Id, refId, StringComparison.OrdinalIgnoreCase),
                    "image" => child.Tag is ImageCard ic && string.Equals(ic.Id, refId, StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
                if (!match) continue;

                double l = Canvas.GetLeft(child);
                double t = Canvas.GetTop(child);
                double w = child.ActualWidth > 0 ? child.ActualWidth : 220;
                double h = child.ActualHeight > 0 ? child.ActualHeight : 80;

                double cx = l + w / 2;
                double cy = t + h / 2;

                double dx = cx - from.X;
                double dy = cy - from.Y;
                if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001) return new Point(cx, cy);

                // Oblicz przecięcie z każdą krawędzią prostokąta i weź najbliższe od from
                double tMin = double.MaxValue;

                // lewa: x = l
                if (Math.Abs(dx) > 0.001) { double tl = (l - from.X) / dx; if (tl > 0) { double iy = from.Y + tl * dy; if (iy >= t && iy <= t + h) tMin = Math.Min(tMin, tl); } }
                // prawa: x = l+w
                if (Math.Abs(dx) > 0.001) { double tr = (l + w - from.X) / dx; if (tr > 0) { double iy = from.Y + tr * dy; if (iy >= t && iy <= t + h) tMin = Math.Min(tMin, tr); } }
                // góra: y = t
                if (Math.Abs(dy) > 0.001) { double tt = (t - from.Y) / dy; if (tt > 0) { double ix = from.X + tt * dx; if (ix >= l && ix <= l + w) tMin = Math.Min(tMin, tt); } }
                // dół: y = t+h
                if (Math.Abs(dy) > 0.001) { double tb = (t + h - from.Y) / dy; if (tb > 0) { double ix = from.X + tb * dx; if (ix >= l && ix <= l + w) tMin = Math.Min(tMin, tb); } }

                if (tMin < double.MaxValue)
                    return new Point(from.X + tMin * dx, from.Y + tMin * dy);

                return new Point(cx, cy);
            }
            return new Point(0, 0);
        }


        private ContextMenu BuildRelationContextMenu(Relation rel)
        {
            var menu = new ContextMenu();

            var deleteItem = new MenuItem { Header = "Delete relation" };
            deleteItem.Click += (_, _) =>
            {
                Project?.Relations.Remove(rel);
                if (Project != null) FileService.SaveProject(Project);
                RenderRelations();
            };
            menu.Items.Add(deleteItem);

            var settingsItem = new MenuItem
            {
                Header = "Relation settings",
                IsEnabled = false   // placeholder
            };
            menu.Items.Add(settingsItem);

            return menu;
        }

        private static TextBlock BuildPreviewTextBlock(
    string text,
    double maxHeight,
    Action<string>? onLinkClicked = null,
    Func<string, bool>? linkExists = null)
        {
            var tb = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = maxHeight,
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                LineHeight = 16,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };

            var lines = text.Split('\n');
            bool firstLine = true;

            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');

                if (!firstLine)
                    tb.Inlines.Add(new System.Windows.Documents.LineBreak());
                firstLine = false;

                var headingMatch = System.Text.RegularExpressions.Regex.Match(line, @"^(#{1,3})\s+(.*)");
                if (headingMatch.Success)
                {
                    double fs = headingMatch.Groups[1].Value.Length switch { 1 => 14, 2 => 13, _ => 12 };
                    tb.Inlines.Add(new System.Windows.Documents.Run(headingMatch.Groups[2].Value)
                    {
                        FontWeight = FontWeights.Bold,
                        FontSize = fs,
                        Foreground = new SolidColorBrush(Color.FromRgb(180, 194, 222))
                    });
                    continue;
                }

               

                if (string.IsNullOrWhiteSpace(line))
                {
                    tb.Inlines.Add(new System.Windows.Documents.Run(" "));
                    continue;
                }

                AddInlineFormattedRuns(tb.Inlines, line, onLinkClicked, linkExists);
            }

            return tb;
        }

        private static void AddInlineFormattedRuns(
    System.Windows.Documents.InlineCollection inlines,
    string line,
    Action<string>? onLinkClicked = null,
    Func<string, bool>? linkExists = null)  
        {
            // Najpierw rozbij linię na segmenty po markerach wiki-linków
            var markerPattern = new System.Text.RegularExpressions.Regex(@"apex_link§([^§]+)§");
            int lastIndex = 0;

            foreach (System.Text.RegularExpressions.Match wm in markerPattern.Matches(line))
            {
                // Tekst przed markerem — parsuj bold/italic normalnie
                if (wm.Index > lastIndex)
                    AddFormattedSegment(inlines, line[lastIndex..wm.Index]);

                // Wiki-link jako klikalny Hyperlink
                string linkTarget = wm.Groups[1].Value;
                bool exists = linkExists == null || linkExists(linkTarget);

                if (exists)
                {
                    var hyperlink = new System.Windows.Documents.Hyperlink
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
                        TextDecorations = System.Windows.TextDecorations.Underline,
                        Cursor = Cursors.Hand,
                        ToolTip = $"Przejdź do: \"{linkTarget}\""
                    };
                    hyperlink.Inlines.Add(new System.Windows.Documents.Run(linkTarget)
                    {
                        FontSize = 11
                    });
                    if (onLinkClicked != null)
                        hyperlink.Click += (_, e) => { onLinkClicked(linkTarget); e.Handled = true; };
                    inlines.Add(hyperlink);
                }
                else
                {
                    // Karta nie istnieje — renderuj jako zwykły tekst, bez hiperlinku
                    inlines.Add(new System.Windows.Documents.Run(linkTarget)
                    {
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134))
                    });
                }

                lastIndex = wm.Index + wm.Length;
            }

            if (lastIndex < line.Length)
                AddFormattedSegment(inlines, line[lastIndex..]);
        }

        private static void AddFormattedSegment(
    System.Windows.Documents.InlineCollection inlines, string text)
        {
            var pattern = new System.Text.RegularExpressions.Regex(
                @"(\*\*(.+?)\*\*)|(\*(.+?)\*)|(`(.+?)`)");

            int lastIndex = 0;
            foreach (System.Text.RegularExpressions.Match m in pattern.Matches(text))
            {
                if (m.Index > lastIndex)
                    inlines.Add(new System.Windows.Documents.Run(text[lastIndex..m.Index])
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134))
                    });

                if (m.Groups[1].Success)
                    inlines.Add(new System.Windows.Documents.Run(m.Groups[2].Value)
                    {
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(180, 194, 222))
                    });
                else if (m.Groups[3].Success)
                    inlines.Add(new System.Windows.Documents.Run(m.Groups[4].Value)
                    {
                        FontStyle = FontStyles.Italic,
                        Foreground = new SolidColorBrush(Color.FromRgb(147, 163, 200))
                    });
                else if (m.Groups[5].Success)
                    inlines.Add(new System.Windows.Documents.Run(m.Groups[6].Value)
                    {
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168))
                    });

                lastIndex = m.Index + m.Length;
            }

            if (lastIndex < text.Length)
                inlines.Add(new System.Windows.Documents.Run(text[lastIndex..])
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134))
                });
        }



        private static ResizeEdge GetResizeEdge(Border card, Point localPoint)
        {
            double w = card.ActualWidth;
            double h = card.ActualHeight;
            double z = ResizeHitZone;

            bool onRight = localPoint.X >= w - z;
            bool onBottom = localPoint.Y >= h - z;

            if (onRight && onBottom) return ResizeEdge.BottomRight;
            if (onRight) return ResizeEdge.Right;
            if (onBottom) return ResizeEdge.Bottom;
            return ResizeEdge.None;
        }

        private void Card_ResizeMouseMove(object sender, MouseEventArgs e)
        {
            if (_isResizing) return; // podczas resize nie zmieniamy kursora

            if (sender is Border border)
            {
                Point local = e.GetPosition(border);
                var edge = GetResizeEdge(border, local);
                border.Cursor = edge switch
                {
                    ResizeEdge.BottomRight => Cursors.SizeNWSE,
                    ResizeEdge.Right => Cursors.SizeWE,
                    ResizeEdge.Bottom => Cursors.SizeNS,
                    _ => Cursors.Hand
                };
            }
        }

        private void Card_ResizeMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isResizing && sender is Border border)
                border.Cursor = Cursors.Hand;
        }

        private void Card_ResizeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border) return;

            // Sprawdź locked dla obu typów
            if (border.Tag is NoteCard nc && nc.Locked) return;
            if (border.Tag is ImageCard ic && ic.Locked) return;
            if (border.Tag is TitleCard tc && tc.Locked) return;

            Point local = e.GetPosition(border);
            var edge = GetResizeEdge(border, local);
            if (edge == ResizeEdge.None) return;

            _isResizing = true;
            _resizeElement = border;
            _resizeCard = border.Tag is NoteCard noteCard ? noteCard : null;
            _resizeEdge = edge;
            _resizeStartMouse = e.GetPosition(BoardCanvas);
            _resizeStartWidth = border.ActualWidth;
            _resizeStartHeight = border.ActualHeight;

            border.CaptureMouse();
            e.Handled = true;
        }



        // ──────────────────────────────────────────────
        //  Card hover preview
        // ──────────────────────────────────────────────

        //private void Card_MouseEnter(object sender, MouseEventArgs e)
        //{
        //    if (sender is Border border && border.Tag is NoteCard card && Project != null)
        //    {
        //        _previewCard = border;
        //        string fullPath = FileService.GetFullPath(Project.RootFolder, card.RelativePath);
        //        if (File.Exists(fullPath))
        //        {
        //            string previewText = GetPreviewText(fullPath, 300);
        //            var previewBlock = FindPreviewBlock(border);
        //            if (previewBlock != null)
        //            {
        //                previewBlock.Text = previewText;
        //                previewBlock.Visibility = Visibility.Visible;
        //            }
        //        }
        //    }
        //}

        //private void Card_MouseLeave(object sender, MouseEventArgs e)
        //{
        //    if (sender is Border border)
        //    {
        //        var previewBlock = FindPreviewBlock(border);
        //        if (previewBlock != null)
        //            previewBlock.Visibility = Visibility.Collapsed;
        //        _previewCard = null;
        //    }
        //}

        //private static TextBlock? FindPreviewBlock(Border cardBorder)
        //{
        //    if (cardBorder.Child is Grid grid && grid.Children.Count > 1 && grid.Children[1] is StackPanel stack)
        //    {
        //        foreach (var child in stack.Children)
        //        {
        //            if (child is TextBlock tb && tb.Tag is string s && s == "PreviewBlock")
        //                return tb;
        //        }
        //    }
        //    return null;
        //}




        // ──────────────────────────────────────────────
        //  Card context menu
        // ──────────────────────────────────────────────

        private ContextMenu BuildCardContextMenu(NoteCard card, Border cardElement)
        {
            var menu = new ContextMenu();

            var previewItem = new MenuItem { Header = "Preview" };
            previewItem.Click += (_, _) => PreviewRequested?.Invoke(card);
            menu.Items.Add(previewItem);
            menu.Items.Add(new Separator());

            var addRelationItem = new MenuItem { Header = "Add relation" };
            addRelationItem.Click += (_, _) => BeginDrawRelation("note", card.RelativePath);
            menu.Items.Add(addRelationItem);

            var sizeItem = new MenuItem { Header = "Set size" };
            var sizeOptions = new (string Label, string Value)[]
            {
    ("Minimum", "minimum"),
    ("Medium", "medium"),
    ("Large", "large")
            };
            foreach (var (label, value) in sizeOptions)
            {
                var currentSize = string.IsNullOrEmpty(card.CardSize) ? "minimum" : card.CardSize;
                var sizeOption = new MenuItem
                {
                    Header = label,
                    IsChecked = string.Equals(currentSize, value, StringComparison.OrdinalIgnoreCase)
                };
                sizeOption.Click += (_, _) =>
                {
                    card.CardSize = value;
                    card.CustomWidth = null;  // reset ręcznego resizu
                    card.CustomHeight = null;
                    ReplaceCardElement(card, cardElement);
                    FileService.SaveProject(Project!);
                };
                sizeItem.Items.Add(sizeOption);
            }
            menu.Items.Add(sizeItem);

            var editItem = new MenuItem { Header = "Edit" };
            editItem.Click += (_, _) => CardEditRequested?.Invoke(card);
            menu.Items.Add(editItem);

            var catItem = new MenuItem { Header = "Set category" };
            if (Project != null)
            {
                foreach (var cat in Project.Categories)
                {
                    var subItem = new MenuItem
                    {
                        Header = $"{cat.Name}",
                        Tag = cat.Id,
                        IsChecked = string.Equals(card.CategoryId, cat.Id, StringComparison.OrdinalIgnoreCase)
                    };
                    subItem.Click += (_, _) =>
                    {
                        card.CategoryId = cat.Id;
                        ReplaceCardElement(card, cardElement);
                        FileService.SaveProject(Project!);
                    };
                    catItem.Items.Add(subItem);
                }
            }
            var noCatItem = new MenuItem { Header = "(None)" };
            noCatItem.Click += (_, _) =>
            {
                card.CategoryId = null;
                ReplaceCardElement(card, cardElement);
                FileService.SaveProject(Project!);
            };
            catItem.Items.Add(noCatItem);
            menu.Items.Add(catItem);

            var openExternalItem = new MenuItem { Header = "Open in system editor" };
            openExternalItem.Click += (_, _) =>
            {
                string fullPath = FileService.GetFullPath(Project!.RootFolder, card.RelativePath);
                if (File.Exists(fullPath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fullPath,
                        UseShellExecute = true
                    });
            };
            menu.Items.Add(openExternalItem);


            var copyAsTemplateItem = new MenuItem { Header = "Copy as template" };
            copyAsTemplateItem.Click += (_, _) =>
            {
                if (Project == null) return;
                string fullPath = FileService.GetFullPath(Project.RootFolder, card.RelativePath);
                if (!File.Exists(fullPath)) return;

                string defaultName = Path.GetFileNameWithoutExtension(card.RelativePath);
                var nameDialog = new InputDialog("Save as Template",
                    "Template name:")
                {
                    Owner = Window.GetWindow(this)
                };
                nameDialog.Answer = defaultName;
                if (nameDialog.ShowDialog() != true ||
                    string.IsNullOrWhiteSpace(nameDialog.Answer)) return;

                var template = TemplateService.CreateFromNote(
                    Project.RootFolder, fullPath, nameDialog.Answer.Trim());
                if (template != null)
                    System.Windows.MessageBox.Show(
                        $"Template \"{template.TemplateName}\" saved.",
                        "Template Saved", MessageBoxButton.OK,
                        MessageBoxImage.Information);
            };
            menu.Items.Add(copyAsTemplateItem);




            var deleteItem = new MenuItem { Header = "Delete" };
            deleteItem.Click += (_, _) =>
            {
                var result = System.Windows.MessageBox.Show(
                    $"Delete \"{Path.GetFileNameWithoutExtension(card.RelativePath)}\"?\n" +
                    "The file will be moved to the Recycle Bin.",
                    "Delete Note",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        string fullPath = FileService.GetFullPath(Project!.RootFolder, card.RelativePath);
                        if (File.Exists(fullPath))
                            FileService.DeleteNoteFile(Project, card.RelativePath);
                        Project.Cards.Remove(card);
                        BoardCanvas.Children.Remove(cardElement);
                        FileService.SaveProject(Project);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show(
                            $"Failed to delete:\n{ex.Message}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            };
            menu.Items.Add(deleteItem);
            return menu;
        }

        private void ReplaceCardElement(NoteCard card, Border oldElement)
        {
            int idx = BoardCanvas.Children.IndexOf(oldElement);
            if (idx >= 0)
            {
                var newElement = CreateCardElement(card);
                Canvas.SetLeft(newElement, card.BoardX);
                Canvas.SetTop(newElement, card.BoardY);
                BoardCanvas.Children.RemoveAt(idx);
                BoardCanvas.Children.Insert(idx, newElement);
            }
        }

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

            cardBorder.SizeChanged += (_, args) =>
            {
                cardBorder.Clip = new System.Windows.Media.RectangleGeometry
                {
                    RadiusX = 8,
                    RadiusY = 8,
                    Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height)
                };
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
                var current = BoardCanvas.Children.OfType<Border>().FirstOrDefault(b => b.Tag == imageCard);
                if (current != null) ReplaceImageCardElement(imageCard, current);
                if (Project != null) FileService.SaveProject(Project);
                Dispatcher.BeginInvoke(new Action(RenderRelations), System.Windows.Threading.DispatcherPriority.Loaded);
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

        private Border CreateTitleCardElement(TitleCard titleCard)
        {
            var bg = ParseHexBrush(titleCard.BackgroundColor);

            double w = titleCard.CustomWidth ?? 300;
            double h = titleCard.CustomHeight ?? 80;

            var cardBorder = new Border
            {
                Width = w,
                Height = h,
                Background = bg,
                BorderBrush = titleCard.Locked
                    ? new SolidColorBrush(Color.FromRgb(98, 79, 120))
                    : new SolidColorBrush(Color.FromRgb(69, 71, 90)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Cursor = titleCard.Locked ? Cursors.Arrow : Cursors.Hand,
                Tag = titleCard,
                Focusable = false,
                ClipToBounds = true
            };

            var grid = new Grid();

            var textBlock = new TextBlock
            {
                Text = titleCard.Text,
                FontFamily = new FontFamily(titleCard.FontFamily),
                FontSize = titleCard.FontSize,
                Foreground = ParseHexBrush(titleCard.FontColor),
                FontWeight = titleCard.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = titleCard.Italic ? FontStyles.Italic : FontStyles.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(12, 8, 12, 8)
            };
            grid.Children.Add(textBlock);

            // Kłódka w rogu
            var lockIcon = new TextBlock
            {
                Text = titleCard.Locked ? "🔒" : "🔓",
                FontSize = 11,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 4),
                ToolTip = titleCard.Locked ? "Unlock" : "Lock"
            };
            lockIcon.MouseLeftButtonDown += (_, e) => e.Handled = true;
            lockIcon.MouseLeftButtonUp += (_, e) =>
            {
                titleCard.Locked = !titleCard.Locked;
                var current = BoardCanvas.Children.OfType<Border>().FirstOrDefault(b => b.Tag == titleCard);
                if (current != null) ReplaceTitleCardElement(titleCard, current);
                if (Project != null) FileService.SaveProject(Project);
                Dispatcher.BeginInvoke(new Action(RenderRelations), System.Windows.Threading.DispatcherPriority.Loaded);
                e.Handled = true;
            };
            grid.Children.Add(lockIcon);

            cardBorder.Child = grid;

            cardBorder.MouseLeftButtonDown += TitleCard_MouseLeftButtonDown;
            cardBorder.MouseMove += Card_MouseMove;
            cardBorder.MouseLeftButtonUp += Card_MouseLeftButtonUp;
            cardBorder.MouseLeave += Card_ResizeMouseLeave;
            cardBorder.ContextMenu = BuildTitleCardContextMenu(titleCard, cardBorder);

            return cardBorder;
        }

        private void ReplaceTitleCardElement(TitleCard titleCard, Border oldElement)
        {
            int idx = BoardCanvas.Children.IndexOf(oldElement);
            if (idx >= 0)
            {
                var newElement = CreateTitleCardElement(titleCard);
                Canvas.SetLeft(newElement, titleCard.BoardX);
                Canvas.SetTop(newElement, titleCard.BoardY);
                BoardCanvas.Children.RemoveAt(idx);
                BoardCanvas.Children.Insert(idx, newElement);
            }
        }

        private ContextMenu BuildTitleCardContextMenu(TitleCard titleCard, Border cardElement)
        {
            var menu = new ContextMenu();

            var editItem = new MenuItem { Header = "Edit" };
            editItem.Click += (_, _) =>
            {
                var dialog = new TitleCardDialog(
                    titleCard.Text, titleCard.FontFamily,
                    titleCard.FontSize, titleCard.FontColor, titleCard.BackgroundColor,
                    titleCard.Bold, titleCard.Italic);
                dialog.Owner = Window.GetWindow(this);
                if (dialog.ShowDialog() == true)
                {
                    titleCard.Text = dialog.ResultText;
                    titleCard.FontFamily = dialog.ResultFontFamily;
                    titleCard.FontSize = dialog.ResultFontSize;
                    titleCard.FontColor = dialog.ResultFontColor;
                    titleCard.BackgroundColor = dialog.ResultBackgroundColor;
                    titleCard.Bold = dialog.ResultBold;
                    titleCard.Italic = dialog.ResultItalic;
                    ReplaceTitleCardElement(titleCard, cardElement);
                    if (Project != null) FileService.SaveProject(Project);
                }
            };
            menu.Items.Add(editItem);

            var addRelationItem = new MenuItem { Header = "Add relation" };
            addRelationItem.Click += (_, _) => BeginDrawRelation("title", titleCard.Id);
            menu.Items.Add(addRelationItem);



            var deleteItem = new MenuItem { Header = "Delete" };
            deleteItem.Click += (_, _) =>
            {
                Project?.TitleCards.Remove(titleCard);
                BoardCanvas.Children.Remove(cardElement);
                if (Project != null) FileService.SaveProject(Project);
            };
            menu.Items.Add(deleteItem);

            return menu;
        }

        private void TitleCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is TitleCard titleCard)
            {
                if (titleCard.Locked) return;

                Point local = e.GetPosition(border);
                if (GetResizeEdge(border, local) != ResizeEdge.None)
                {
                    Card_ResizeMouseLeftButtonDown(sender, e);
                    return;
                }

                _isDraggingCard = true;
                _dragElement = border;
                _dragCard = null;
                _dragStartMouse = e.GetPosition(BoardCanvas);
                _dragStartLeft = Canvas.GetLeft(border);
                _dragStartTop = Canvas.GetTop(border);
                _didDrag = false;
                border.CaptureMouse();
                e.Handled = true;
            }
        }

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

            var addRelationItem = new MenuItem { Header = "Add relation" };
            addRelationItem.Click += (_, _) => BeginDrawRelation("image", imageCard.Id);
            menu.Items.Add(addRelationItem);

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

        // ──────────────────────────────────────────────
        //  Pan — left or middle mouse on empty background
        //  Handled at UserControl level to intercept before
        //  card events consume the mouse.
        // ──────────────────────────────────────────────

        private void BoardView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Nie startuj pan podczas rysowania relacji
            if (_isDrawingRelation) return;

            if (!IsClickOnCard(e.OriginalSource as DependencyObject))
            {
                StartPan(e);
            }
        }

        private void BoardView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning && (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed))
            {
                ContinuePan(e);
            }
        }

        private void BoardView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndPan();
        }

        private void CanvasTransformHost_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                StartPan(e);
                e.Handled = true;
            }
        }

        private void CanvasTransformHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning && (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed))
            {
                ContinuePan(e);
            }
        }

        private void CanvasTransformHost_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
                EndPan();
        }

        private bool IsClickOnCard(DependencyObject? source)
        {
            if (FindAncestor<System.Windows.Shapes.Ellipse>(source) != null)
                return true;

            return FindAncestor<Border>(source,
                b => b.Tag is NoteCard || b.Tag is ImageCard || b.Tag is TitleCard) != null;
        }

        private void StartPan(MouseEventArgs e)
        {
            _isPanning = true;
            _panStartMouse = e.GetPosition(this);
            _panStartTranslateX = PanTransform.X;
            _panStartTranslateY = PanTransform.Y;
            Cursor = Cursors.SizeAll;
            CanvasTransformHost.CaptureMouse();
            e.Handled = true;
        }

        private void ContinuePan(MouseEventArgs e)
        {
            Point current = e.GetPosition(this);
            double dx = current.X - _panStartMouse.X;
            double dy = current.Y - _panStartMouse.Y;

            double newX = _panStartTranslateX + dx;
            double newY = _panStartTranslateY + dy;

            // Clamp: nie pozwól odskoczyć dalej niż 200px od krawędzi kanwy
            const double margin = 200.0;
            double vpw = CanvasContainer.ActualWidth;
            double vph = CanvasContainer.ActualHeight;
            double canvasScaled = 10000.0 * _zoomLevel;

            // Max X: canvas lewa krawędź nie może być dalej niż +200px od lewej viewportu
            double maxX = margin;
            // Min X: canvas prawa krawędź nie może być dalej niż -200px od prawej viewportu
            double minX = vpw - canvasScaled - margin;

            double maxY = margin;
            double minY = vph - canvasScaled - margin;

            PanTransform.X = Math.Clamp(newX, minX, maxX);
            PanTransform.Y = Math.Clamp(newY, minY, maxY);

            e.Handled = true;
        }

        private void EndPan()
        {
            if (_isPanning)
            {
                _isPanning = false;
                Cursor = Cursors.Arrow;
                CanvasTransformHost.ReleaseMouseCapture();
            }
        }



        // ── Zoom (mouse wheel) — toward cursor ──────────────────
        private void BoardView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double delta = e.Delta > 0 ? 0.1 : -0.1;
            double newZoom = Math.Round(Math.Clamp(_zoomLevel + delta, ZoomMin, ZoomMax), 1);
            if (Math.Abs(newZoom - _zoomLevel) < 0.01)
                return;

            Point mouseInViewport = e.GetPosition(CanvasContainer);
            double canvasMouseX = (mouseInViewport.X - PanTransform.X) / _zoomLevel;
            double canvasMouseY = (mouseInViewport.Y - PanTransform.Y) / _zoomLevel;

            _zoomLevel = newZoom;
            ZoomTransform.ScaleX = _zoomLevel;
            ZoomTransform.ScaleY = _zoomLevel;

            double rawX = mouseInViewport.X - canvasMouseX * _zoomLevel;
            double rawY = mouseInViewport.Y - canvasMouseY * _zoomLevel;

            // Clamp po zoom
            const double margin = 200.0;
            double vpw = CanvasContainer.ActualWidth;
            double vph = CanvasContainer.ActualHeight;
            double canvasScaled = 10000.0 * _zoomLevel;

            PanTransform.X = Math.Clamp(rawX, vpw - canvasScaled - margin, margin);
            PanTransform.Y = Math.Clamp(rawY, vph - canvasScaled - margin, margin);

            e.Handled = true;
        }

        // ── Right-click context menu — new card at correct position ──
        private void CanvasTransformHost_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<Border>(e.OriginalSource as DependencyObject,
                b => b.Tag is NoteCard || b.Tag is ImageCard || b.Tag is TitleCard) != null)
                return;

            var menu = new ContextMenu();

            // Mouse in viewport space → convert to canvas space
            Point mouseInViewport = e.GetPosition(CanvasContainer);
            Point canvasPos = new Point(
                (mouseInViewport.X - PanTransform.X) / _zoomLevel,
                (mouseInViewport.Y - PanTransform.Y) / _zoomLevel
            );

            var newCardItem = new MenuItem { Header = "New card" };
            newCardItem.Click += (_, _) => CreateNewNoteAt(canvasPos);
            menu.Items.Add(newCardItem);

            var newImageItem = new MenuItem { Header = "New image" };
            newImageItem.Click += (_, _) => CreateNewImageAt(canvasPos);
            menu.Items.Add(newImageItem);

            var newTitleItem = new MenuItem { Header = "New title" };
            newTitleItem.Click += (_, _) => CreateNewTitleAt(canvasPos);
            menu.Items.Add(newTitleItem);

            var resetViewItem = new MenuItem { Header = "Reset view" };
            resetViewItem.Click += (_, _) => FitAllCards();
            menu.Items.Add(resetViewItem);

            menu.PlacementTarget = CanvasTransformHost;
            menu.IsOpen = true;
            e.Handled = true;
        }


        // ── Ctrl+N — center of viewport in canvas space ──────────
        private void BoardView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                double vpw = CanvasContainer.ActualWidth;
                double vph = CanvasContainer.ActualHeight;

                // Viewport center → canvas coordinates
                double canvasX = (vpw / 2 - PanTransform.X) / _zoomLevel;
                double canvasY = (vph / 2 - PanTransform.Y) / _zoomLevel;

                CreateNewNoteAt(new Point(canvasX, canvasY));
                e.Handled = true;
            }
            else if (e.Key == Key.Home && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                FitAllCards();
                e.Handled = true;
            }
        }

        // ──────────────────────────────────────────────
        //  Create new note
        // ──────────────────────────────────────────────

        private void CreateNewNoteAt(Point position)
        {
            if (Project == null) return;

            var templates = TemplateService.LoadAll(Project.RootFolder);
            var dialog = new NewNoteDialog(Project.RootFolder, templates)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() != true) return;

            string title = dialog.NoteTitle;

            foreach (char c in Path.GetInvalidFileNameChars())
                title = title.Replace(c.ToString(), "");

            if (string.IsNullOrWhiteSpace(title))
            {
                System.Windows.MessageBox.Show(
                    "Title contains invalid characters.",
                    "Invalid Title", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Determine target folder — create it now if needed (only on actual note creation)
            string targetFolder = Project.RootFolder;
            string? autoCategory = null;

            if (dialog.SelectedTemplate != null)
            {
                string desired = string.IsNullOrWhiteSpace(dialog.SelectedTemplate.DefaultFolder)
                    ? Project.RootFolder
                    : Path.GetFullPath(Path.Combine(
                        Project.RootFolder,
                        dialog.SelectedTemplate.DefaultFolder.Replace('/', Path.DirectorySeparatorChar)));

                if (!Directory.Exists(desired))
                    Directory.CreateDirectory(desired);

                targetFolder = desired;
                autoCategory = dialog.SelectedTemplate.DefaultCategoryId;
            }

            string relativePath = FileService.GetRelativePath(
                Project.RootFolder,
                Path.Combine(targetFolder, title + ".md"));

            string fullPath = FileService.GetFullPath(Project.RootFolder, relativePath);

            if (File.Exists(fullPath))
            {
                System.Windows.MessageBox.Show(
                    $"A note named \"{title}.md\" already exists in that folder.\n" +
                    "Please choose a different name.",
                    "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string content = dialog.SelectedTemplate != null
                    ? TemplateService.ReadContent(Project.RootFolder, dialog.SelectedTemplate)
                    : $"# {title}\n\n";

                File.WriteAllText(fullPath, content);

                var card = new NoteCard(relativePath, position.X, position.Y)
                {
                    CategoryId = autoCategory
                };
                Project.Cards.Add(card);

                var element = CreateCardElement(card);
                Canvas.SetLeft(element, card.BoardX);
                Canvas.SetTop(element, card.BoardY);
                BoardCanvas.Children.Add(element);

                FileService.SaveProject(Project);

                // Navigate to edit mode in Structure
                PreviewRequested?.Invoke(card);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CardEditRequested?.Invoke(card);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to create note:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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

        private void CreateNewTitleAt(Point position)
        {
            if (Project == null) return;

            var dialog = new TitleCardDialog();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() != true) return;

            string id = Guid.NewGuid().ToString("N")[..8];
            var titleCard = new TitleCard(id, dialog.ResultText, position.X, position.Y)
            {
                FontFamily = dialog.ResultFontFamily,
                FontSize = dialog.ResultFontSize,
                FontColor = dialog.ResultFontColor,
                BackgroundColor = dialog.ResultBackgroundColor,
                Bold = dialog.ResultBold,
                Italic = dialog.ResultItalic
            };

            Project.TitleCards.Add(titleCard);

            var element = CreateTitleCardElement(titleCard);
            Canvas.SetLeft(element, position.X);
            Canvas.SetTop(element, position.Y);
            BoardCanvas.Children.Add(element);

            FileService.SaveProject(Project);
        }

        // ──────────────────────────────────────────────
        //  Utility
        // ──────────────────────────────────────────────

        public void FocusBoard()
        {
            Dispatcher.BeginInvoke(new Action(() => CanvasContainer.Focus()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static T? FindAncestor<T>(DependencyObject? child, Func<T, bool>? predicate = null)
    where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T t && (predicate == null || predicate(t)))
                    return t;

                // VisualTreeHelper.GetParent rzuca na non-Visual (np. Run, Inline)
                // Użyj LogicalTreeHelper jako fallback
                child = child is Visual or Visual3D
                    ? VisualTreeHelper.GetParent(child)
                    : LogicalTreeHelper.GetParent(child);
            }
            return null;
        }

        private static Brush ParseHexBrush(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
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


        private void RenderConnections()
        {
            // Usuń stare linie
            foreach (var line in _connectionLines)
                BoardCanvas.Children.Remove(line);
            _connectionLines.Clear();

            if (!_connectionsVisible || Project == null) return;

            var connections = ConnectionResolver.ResolveAll(Project);

            // Mapa card → element na canvas
            var cardElements = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in BoardCanvas.Children.OfType<Border>())
            {
                if (child.Tag is NoteCard nc)
                    cardElements[nc.RelativePath] = child;
            }

            foreach (var (source, target) in connections)
            {
                if (!cardElements.TryGetValue(source.RelativePath, out var srcEl)) continue;
                if (!cardElements.TryGetValue(target.RelativePath, out var tgtEl)) continue;

                double srcW = srcEl.ActualWidth > 0 ? srcEl.ActualWidth : (source.CustomWidth ?? 220);
                double srcH = srcEl.ActualHeight > 0 ? srcEl.ActualHeight : (source.CustomHeight ?? 120);
                double tgtW = tgtEl.ActualWidth > 0 ? tgtEl.ActualWidth : (target.CustomWidth ?? 220);
                double tgtH = tgtEl.ActualHeight > 0 ? tgtEl.ActualHeight : (target.CustomHeight ?? 120);

                // Środek kart
                double x1 = source.BoardX + srcW / 2;
                double y1 = source.BoardY + srcH / 2;
                double x2 = target.BoardX + tgtW / 2;
                double y2 = target.BoardY + tgtH / 2;

                var line = new System.Windows.Shapes.Line
                {
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2,
                    Stroke = new SolidColorBrush(Color.FromArgb(120, 137, 180, 250)),
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false,
                    // Linia za kartami — wstaw na początku
                };

                // Strzałka na końcu (trójkąt)
                var arrowHead = BuildArrowHead(x1, y1, x2, y2);

                // Wstaw linie NA SPÓD (przed kartami)
                BoardCanvas.Children.Insert(0, line);
                BoardCanvas.Children.Insert(1, arrowHead);
                _connectionLines.Add(line);
                _connectionLines.Add(arrowHead);
            }
        }

        private System.Windows.Shapes.Polygon BuildArrowHead(
    double x1, double y1, double x2, double y2)
        {
            double angle = Math.Atan2(y2 - y1, x2 - x1);
            double arrowLength = 10;
            double arrowWidth = 5;

            double ax = x2 - arrowLength * Math.Cos(angle);
            double ay = y2 - arrowLength * Math.Sin(angle);

            var arrow = new System.Windows.Shapes.Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(160, 137, 180, 250)),
                IsHitTestVisible = false,
                Points = new PointCollection
        {
            new Point(x2, y2),
            new Point(ax - arrowWidth * Math.Sin(angle),
                      ay + arrowWidth * Math.Cos(angle)),
            new Point(ax + arrowWidth * Math.Sin(angle),
                      ay - arrowWidth * Math.Cos(angle))
        }
            };
            return arrow;
        }

        public void SetConnectionsVisible(bool visible)
        {
            _connectionsVisible = visible;
            RenderConnections();
        }


        ~BoardView() => _previewCache.Dispose();
    }
}