using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Apex.Models;
using Apex.Services;

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
            RenderCards();
            Dispatcher.BeginInvoke(new Action(FitAllCards), System.Windows.Threading.DispatcherPriority.Loaded);
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
                if (cat != null)
                {
                    catColor = cat.Color;
                    catName = cat.Name;
                }
            }

            var stripColor = catColor != null ? ParseHexBrush(catColor) : new SolidColorBrush(Color.FromRgb(69, 71, 90));

            string fullPath = Project != null
                ? FileService.GetFullPath(Project.RootFolder, card.RelativePath)
                : string.Empty;
            string createdDate = "";
            string modifiedDate = "";
            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                try
                {
                    var fi = new FileInfo(fullPath);
                    createdDate = fi.CreationTime.ToString("yyyy-MM-dd");
                    modifiedDate = fi.LastWriteTime.ToString("yyyy-MM-dd");
                }
                catch { }
            }

            var cardBorder = new Border
            {
                Width = 220,
                MinHeight = 80,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
                Tag = card,
                Focusable = false
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(new Border
            {
                Background = stripColor,
                CornerRadius = new CornerRadius(8, 0, 0, 8),
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Left
            });

            var contentStack = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 0);
            topRow.Children.Add(titleBlock);

            if (catName != null)
            {
                var badge = new Border
                {
                    Background = stripColor,
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
                topRow.Children.Add(badge);
            }

            contentStack.Children.Add(topRow);

            var previewBlock = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 120,
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed,
                Tag = "PreviewBlock"
            };
            contentStack.Children.Add(previewBlock);

            var bottomRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0)
            };
            if (!string.IsNullOrEmpty(createdDate))
            {
                bottomRow.Children.Add(new TextBlock
                {
                    Text = createdDate,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                    Margin = new Thickness(0, 0, 8, 0)
                });
            }
            if (!string.IsNullOrEmpty(modifiedDate))
            {
                bottomRow.Children.Add(new TextBlock
                {
                    Text = modifiedDate,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134))
                });
            }
            contentStack.Children.Add(bottomRow);

            Grid.SetColumn(contentStack, 1);
            grid.Children.Add(contentStack);
            cardBorder.Child = grid;

            // Card drag events
            cardBorder.MouseLeftButtonDown += Card_MouseLeftButtonDown;
            cardBorder.MouseMove += Card_MouseMove;
            cardBorder.MouseLeftButtonUp += Card_MouseLeftButtonUp;

            // Card hover preview
            cardBorder.MouseEnter += Card_MouseEnter;
            cardBorder.MouseLeave += Card_MouseLeave;

            // Card context menu
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
            if (_isDraggingCard && _dragElement != null && _dragCard != null && e.LeftButton == MouseButtonState.Pressed)
            {
                Point current = e.GetPosition(BoardCanvas);
                double dx = current.X - _dragStartMouse.X;
                double dy = current.Y - _dragStartMouse.Y;

                if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
                    _didDrag = true;

                if (_didDrag)
                {
                    double newLeft = _dragStartLeft + dx;
                    double newTop = _dragStartTop + dy;
                    newLeft = Math.Max(0, Math.Min(newLeft, BoardCanvas.Width - 220));
                    newTop = Math.Max(0, Math.Min(newTop, BoardCanvas.Height - 80));
                    Canvas.SetLeft(_dragElement, newLeft);
                    Canvas.SetTop(_dragElement, newTop);
                }
                e.Handled = true;
            }
        }

        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
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

        // ──────────────────────────────────────────────
        //  Card hover preview
        // ──────────────────────────────────────────────

        private void Card_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.Tag is NoteCard card && Project != null)
            {
                _previewCard = border;
                string fullPath = FileService.GetFullPath(Project.RootFolder, card.RelativePath);
                if (File.Exists(fullPath))
                {
                    string previewText = GetPreviewText(fullPath, 300);
                    var previewBlock = FindPreviewBlock(border);
                    if (previewBlock != null)
                    {
                        previewBlock.Text = previewText;
                        previewBlock.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private void Card_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                var previewBlock = FindPreviewBlock(border);
                if (previewBlock != null)
                    previewBlock.Visibility = Visibility.Collapsed;
                _previewCard = null;
            }
        }

        private static TextBlock? FindPreviewBlock(Border cardBorder)
        {
            if (cardBorder.Child is Grid grid && grid.Children.Count > 1 && grid.Children[1] is StackPanel stack)
            {
                foreach (var child in stack.Children)
                {
                    if (child is TextBlock tb && tb.Tag is string s && s == "PreviewBlock")
                        return tb;
                }
            }
            return null;
        }

        private static string GetPreviewText(string fullPath, int maxChars)
        {
            try
            {
                string content = File.ReadAllText(fullPath);
                string plain = System.Text.RegularExpressions.Regex.Replace(content,
                    @"[#*_~`>\-\[\]()!|]", " ");
                plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ");
                plain = plain.Trim();
                return plain.Length <= maxChars ? plain : plain[..maxChars] + "…";
            }
            catch
            {
                return "";
            }
        }

        // ──────────────────────────────────────────────
        //  Card context menu
        // ──────────────────────────────────────────────

        private ContextMenu BuildCardContextMenu(NoteCard card, Border cardElement)
        {
            var menu = new ContextMenu();

            var editItem = new MenuItem { Header = "Edit" };
            editItem.Click += (_, _) => CardSelected?.Invoke(card);
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
            PanTransform.X = _panStartTranslateX + dx;
            PanTransform.Y = _panStartTranslateY + dy;
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

        // ──────────────────────────────────────────────
        //  Zoom (mouse wheel) — toward cursor
        // ──────────────────────────────────────────────

        private void BoardView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double delta = e.Delta > 0 ? 0.1 : -0.1;
            double newZoom = Math.Round(Math.Clamp(_zoomLevel + delta, ZoomMin, ZoomMax), 1);
            if (Math.Abs(newZoom - _zoomLevel) < 0.01)
                return;

            // Mouse position relative to the transform host
            Point mouse = e.GetPosition(CanvasTransformHost);

            double scale = newZoom / _zoomLevel;
            _zoomLevel = newZoom;
            ZoomTransform.ScaleX = _zoomLevel;
            ZoomTransform.ScaleY = _zoomLevel;

            // Keep the point under the cursor fixed
            PanTransform.X = mouse.X - scale * (mouse.X - PanTransform.X);
            PanTransform.Y = mouse.Y - scale * (mouse.Y - PanTransform.Y);

            e.Handled = true;
        }

        // ──────────────────────────────────────────────
        //  Right-click context menu on empty canvas
        // ──────────────────────────────────────────────

        private void CanvasContainer_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                if (FindAncestor<Border>(source, b => b.Tag is NoteCard) != null)
                    return;
            }

            var menu = new ContextMenu();
            var newNoteItem = new MenuItem { Header = "New note" };
            newNoteItem.Click += (_, _) =>
            {
                Point clickPos = Mouse.GetPosition(BoardCanvas);
                clickPos = new Point(clickPos.X / _zoomLevel, clickPos.Y / _zoomLevel);
                CreateNewNoteAt(clickPos);
            };
            menu.Items.Add(newNoteItem);
            CanvasTransformHost.ContextMenu = menu;
        }

        // ──────────────────────────────────────────────
        //  Reset view button
        // ──────────────────────────────────────────────

        private void ResetViewButton_Click(object sender, RoutedEventArgs e)
        {
            FitAllCards();
        }

        // ──────────────────────────────────────────────
        //  Ctrl+N / Ctrl+Home
        // ──────────────────────────────────────────────

        private void BoardView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Viewport center in canvas coordinates (accounting for zoom + pan)
                double vpw = CanvasContainer.ActualWidth;
                double vph = CanvasContainer.ActualHeight;
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

        private static T? FindAncestor<T>(DependencyObject? child, Func<T, bool>? predicate = null)
            where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T t && (predicate == null || predicate(t)))
                    return t;
                child = VisualTreeHelper.GetParent(child);
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
    }
}