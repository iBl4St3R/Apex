using Apex.Models;
using Apex.Services;
using System.IO;
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
                if (cardElement != null)
                {
                    var originalBrush = cardElement.BorderBrush;
                    cardElement.BorderBrush = new SolidColorBrush(Colors.White);
                    cardElement.BorderThickness = new Thickness(2);

                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(800)
                    };
                    timer.Tick += (_, _) =>
                    {
                        cardElement.BorderBrush = originalBrush;
                        cardElement.BorderThickness = new Thickness(1);
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
            if (Project == null) return;

            foreach (var card in Project.Cards)
            {
                var element = CreateCardElement(card);
                BoardCanvas.Children.Add(element);
                Canvas.SetLeft(element, card.BoardX);
                Canvas.SetTop(element, card.BoardY);
            }
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

            // Rozmiary i preview
            double cardWidth = card.CustomWidth ?? card.CardSize switch
            {
                "medium" => 440,
                "large" => 880,
                _ => 220
            };
            double cardHeight = card.CustomHeight ?? card.CardSize switch
            {
                "medium" => 160,
                "large" => 320,
                _ => 120
            };
            int previewMaxChars = card.CardSize switch { "medium" => 300, "large" => 600, _ => 200 };

            string previewText = !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath)
                                ? _previewCache.GetPreview(fullPath, previewMaxChars)
                                : "";

            // Outer card border
            var cardBorder = new Border
            {
                Width = cardWidth,   
                Height = cardHeight,
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
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
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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
            titleRow.Children.Add(titleBlock);

            if (catName != null)
            {
                var badge = new Border
                {
                    Background = stripBrush,
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = catName,
                        FontSize = 10,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.SemiBold
                    }
                };
                Grid.SetColumn(badge, 1);
                titleRow.Children.Add(badge);
            }

            Grid.SetRow(titleRow, 0);
            innerGrid.Children.Add(titleRow);

            double previewMaxHeight = (card.CustomHeight.HasValue || card.CardSize is "large" or "medium")
                                    ? double.PositiveInfinity
                                    : 64;
            var previewBlock = BuildPreviewTextBlock(previewText, previewMaxHeight);
            Grid.SetRow(previewBlock, 1);
            innerGrid.Children.Add(previewBlock);

            

            // — Row 2: date (przyciśnięta do dołu) —
            if (!string.IsNullOrEmpty(modifiedDateTime))
            {
                var dateBlock = new TextBlock
                {
                    Text = modifiedDateTime,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                Grid.SetRow(dateBlock, 2);
                innerGrid.Children.Add(dateBlock);
            }

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
                // Sprawdź czy to resize
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
            if (_isResizing && _resizeElement != null && _resizeCard != null
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
            if (_isDraggingCard && _dragElement != null && _dragCard != null
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
            if (_isResizing && _resizeElement != null && _resizeCard != null)
            {
                _resizeElement.ReleaseMouseCapture();

                // Zapisz wymiary do modelu
                _resizeCard.CustomWidth = _resizeElement.Width;
                _resizeCard.CustomHeight = _resizeElement.Height;
                if (Project != null)
                    FileService.SaveProject(Project);

                _isResizing = false;
                _resizeElement = null;
                _resizeCard = null;
                e.Handled = true;
                return;
            }

            // Normalny drag end
            if (_isDraggingCard && _dragElement != null && _dragCard != null)
            {
                _dragElement.ReleaseMouseCapture();

                if (_didDrag)
                {
                    _dragCard.BoardX = Canvas.GetLeft(_dragElement);
                    _dragCard.BoardY = Canvas.GetTop(_dragElement);
                    if (Project != null)
                        FileService.SaveProject(Project);
                }
                else
                {
                    CardSelected?.Invoke(_dragCard);
                }

                _isDraggingCard = false;
                _dragElement = null;
                _dragCard = null;
                _didDrag = false;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Renders simple markdown into a TextBlock with Inlines.
        /// Supports: headings (bold+slightly larger), **bold**, *italic*, plain lines.
        /// No FlowDocument overhead — safe for 20+ cards.
        /// </summary>
        private static TextBlock BuildPreviewTextBlock(string text, double maxHeight)
        {
            var tb = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = maxHeight,
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                LineHeight = 16,          // stały odstęp — eliminuje "skoki" między liniami
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

                // Nagłówek (#, ##, ###)
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

                // Pusta linia — mały spacer zamiast pełnej przerwy
                if (string.IsNullOrWhiteSpace(line))
                {
                    tb.Inlines.Add(new System.Windows.Documents.Run(" "));
                    continue;
                }

                // Linia z bold/italic — parsuj inline
                AddInlineFormattedRuns(tb.Inlines, line);
            }

            return tb;
        }

        private static void AddInlineFormattedRuns(
    System.Windows.Documents.InlineCollection inlines, string line)
        {
            // Prosta maszyna stanów dla **bold** i *italic*
            var pattern = new System.Text.RegularExpressions.Regex(
                @"(\*\*(.+?)\*\*)|(\*(.+?)\*)|(`(.+?)`)");

            int lastIndex = 0;
            foreach (System.Text.RegularExpressions.Match m in pattern.Matches(line))
            {
                // Tekst przed dopasowaniem
                if (m.Index > lastIndex)
                    inlines.Add(new System.Windows.Documents.Run(line[lastIndex..m.Index])
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134))
                    });

                if (m.Groups[1].Success) // **bold**
                    inlines.Add(new System.Windows.Documents.Run(m.Groups[2].Value)
                    {
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(180, 194, 222))
                    });
                else if (m.Groups[3].Success) // *italic*
                    inlines.Add(new System.Windows.Documents.Run(m.Groups[4].Value)
                    {
                        FontStyle = FontStyles.Italic,
                        Foreground = new SolidColorBrush(Color.FromRgb(147, 163, 200))
                    });
                else if (m.Groups[5].Success) // `code`
                    inlines.Add(new System.Windows.Documents.Run(m.Groups[6].Value)
                    {
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168))
                    });

                lastIndex = m.Index + m.Length;
            }

            // Reszta linii po ostatnim dopasowaniu
            if (lastIndex < line.Length)
                inlines.Add(new System.Windows.Documents.Run(line[lastIndex..])
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
            if (sender is not Border border || border.Tag is not NoteCard card) return;

            Point local = e.GetPosition(border);
            var edge = GetResizeEdge(border, local);
            if (edge == ResizeEdge.None) return;

            // Rozpocznij resize zamiast drag
            _isResizing = true;
            _resizeElement = border;
            _resizeCard = card;
            _resizeEdge = edge;
            _resizeStartMouse = e.GetPosition(BoardCanvas);
            _resizeStartWidth = border.ActualWidth;
            _resizeStartHeight = border.ActualHeight;

            border.CaptureMouse();
            e.Handled = true; // zapobiega uruchomieniu Card_MouseLeftButtonDown
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

        // ──────────────────────────────────────────────
        //  Pan — left or middle mouse on empty background
        //  Handled at UserControl level to intercept before
        //  card events consume the mouse.
        // ──────────────────────────────────────────────

        private void BoardView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Check that the click is NOT on a card
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
            return FindAncestor<Border>(source, b => b.Tag is NoteCard) != null;
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
            if (FindAncestor<Border>(e.OriginalSource as DependencyObject, b => b.Tag is NoteCard) != null)
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

            var dialog = new InputDialog("New Note", "Enter a title for the new note:");
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Answer))
                return;

            string title = dialog.Answer.Trim();
            string relativePath = title + ".md";

            try
            {
                FileService.CreateNoteFile(Project, relativePath);
                var card = new NoteCard(relativePath, position.X, position.Y);
                Project.Cards.Add(card);

                var element = CreateCardElement(card);
                Canvas.SetLeft(element, card.BoardX);
                Canvas.SetTop(element, card.BoardY);
                BoardCanvas.Children.Add(element);

                FileService.SaveProject(Project);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to create note:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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


        ~BoardView() => _previewCache.Dispose();
    }
}