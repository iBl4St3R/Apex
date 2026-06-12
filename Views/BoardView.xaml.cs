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
        private const double ZoomMin = 0.3;
        private const double ZoomMax = 3.0;

        public BoardView()
        {
            InitializeComponent();
            // Ensure container can receive focus for keyboard events
            Loaded += (_, _) => CanvasContainer.Focus();
        }

        // ──────────────────────────────────────────────
        //  Project loading
        // ──────────────────────────────────────────────

        public void LoadProject(ApexProject project)
        {
            Project = project;
            RenderCards();
            FitAllCards();
        }

        public void RefreshCards()
        {
            RenderCards();
        }

        /// <summary>
        /// Centers all cards in the viewport at zoom 1.0.
        /// </summary>
        public void FitAllCards()
        {
            if (Project == null || Project.Cards.Count == 0)
            {
                ZoomTransform.ScaleX = ZoomTransform.ScaleY = 1.0;
                PanTransform.X = PanTransform.Y = 0;
                _zoomLevel = 1.0;
                return;
            }

            double minX = Project.Cards.Min(c => c.BoardX);
            double minY = Project.Cards.Min(c => c.BoardY);
            double maxX = Project.Cards.Max(c => c.BoardX + 220);
            double maxY = Project.Cards.Max(c => c.BoardY + 80);

            double centerX = (minX + maxX) / 2;
            double centerY = (minY + maxY) / 2;

            ZoomTransform.ScaleX = ZoomTransform.ScaleY = 1.0;
            _zoomLevel = 1.0;

            double viewportCenterX = BoardScroll.ViewportWidth / 2;
            double viewportCenterY = BoardScroll.ViewportHeight / 2;

            PanTransform.X = viewportCenterX - centerX;
            PanTransform.Y = viewportCenterY - centerY;
        }

        /// <summary>
        /// Zooms to a specific card, centering it in the viewport at 1.5x zoom.
        /// Flashes the card border white for 0.8s as a visual cue.
        /// </summary>
        public void FocusCard(string relativePath)
        {
            if (Project == null) return;

            var card = Project.Cards.FirstOrDefault(c =>
                string.Equals(c.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
            if (card == null) return;

            // Find the card element on the canvas
            Border? cardElement = null;
            foreach (var child in BoardCanvas.Children)
            {
                if (child is Border b && b.Tag is NoteCard nc &&
                    string.Equals(nc.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                {
                    cardElement = b;
                    break;
                }
            }

            double cardCenterX = card.BoardX + 110; // half of card width
            double cardCenterY = card.BoardY + 40;  // half of min card height

            double targetZoom = 1.5;
            _zoomLevel = targetZoom;
            ZoomTransform.ScaleX = targetZoom;
            ZoomTransform.ScaleY = targetZoom;

            double viewportCenterX = BoardScroll.ViewportWidth / 2;
            double viewportCenterY = BoardScroll.ViewportHeight / 2;

            PanTransform.X = viewportCenterX - cardCenterX;
            PanTransform.Y = viewportCenterY - cardCenterY;

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
        }

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

            // Resolve category
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

            // Color strip on the left
            var stripColor = catColor != null ? ParseHexBrush(catColor) : new SolidColorBrush(Color.FromRgb(69, 71, 90));

            // Dates
            string fullPath = Project != null
                ? FileService.GetFullPath(Project.RootFolder, card.RelativePath)
                : string.Empty;
            string createdDate = "";
            string modifiedDate = "";
            if (fullPath != null && File.Exists(fullPath))
            {
                try
                {
                    var fi = new FileInfo(fullPath);
                    createdDate = fi.CreationTime.ToString("yyyy-MM-dd");
                    modifiedDate = fi.LastWriteTime.ToString("yyyy-MM-dd");
                }
                catch { }
            }

            // Card outer Border (the card itself)
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

            // Inner grid
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); // color strip
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Color strip
            grid.Children.Add(new Border
            {
                Background = stripColor,
                CornerRadius = new CornerRadius(8, 0, 0, 8),
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Left
            });

            // Right-side content
            var contentStack = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };

            // Top row: title (left) + category badge (right)
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

            // Preview text area (hidden by default, shown on hover)
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

            // Bottom row: dates
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

            // ── Events ──

            // Card drag
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

                // Consider it a drag if moved more than 3px
                if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
                    _didDrag = true;

                if (_didDrag)
                {
                    double newLeft = _dragStartLeft + dx;
                    double newTop = _dragStartTop + dy;

                    // Clamp to canvas bounds
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
                    // Save new position
                    _dragCard.BoardX = Canvas.GetLeft(_dragElement);
                    _dragCard.BoardY = Canvas.GetTop(_dragElement);
                    if (Project != null)
                        FileService.SaveProject(Project);
                }
                else
                {
                    // It was a click, not a drag — fire selection event
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
                {
                    previewBlock.Visibility = Visibility.Collapsed;
                }
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
                // Strip common Markdown symbols
                string plain = System.Text.RegularExpressions.Regex.Replace(content,
                    @"[#*_~`>\-\[\]()!|]", " ");
                // Collapse whitespace
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

            // Edit
            var editItem = new MenuItem { Header = "Edit" };
            editItem.Click += (_, _) =>
            {
                CardSelected?.Invoke(card);
                // MainWindow will switch NoteViewer to edit mode — handled by the subscriber
            };
            menu.Items.Add(editItem);

            // Set Category submenu
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
                        // Rebuild the card element
                        int idx = BoardCanvas.Children.IndexOf(cardElement);
                        if (idx >= 0)
                        {
                            var newElement = CreateCardElement(card);
                            Canvas.SetLeft(newElement, card.BoardX);
                            Canvas.SetTop(newElement, card.BoardY);
                            BoardCanvas.Children.RemoveAt(idx);
                            BoardCanvas.Children.Insert(idx, newElement);
                        }
                        FileService.SaveProject(Project!);
                    };
                    catItem.Items.Add(subItem);
                }
            }
            // "No category" option
            var noCatItem = new MenuItem { Header = "(None)" };
            noCatItem.Click += (_, _) =>
            {
                card.CategoryId = null;
                int idx = BoardCanvas.Children.IndexOf(cardElement);
                if (idx >= 0)
                {
                    var newElement = CreateCardElement(card);
                    Canvas.SetLeft(newElement, card.BoardX);
                    Canvas.SetTop(newElement, card.BoardY);
                    BoardCanvas.Children.RemoveAt(idx);
                    BoardCanvas.Children.Insert(idx, newElement);
                }
                FileService.SaveProject(Project!);
            };
            catItem.Items.Add(noCatItem);
            menu.Items.Add(catItem);

            // Delete
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
                        {
                            FileService.DeleteNoteFile(Project, card.RelativePath);
                        }
                        // Remove from cards list and canvas
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

        // ──────────────────────────────────────────────
        //  Canvas pan (click-drag on empty background)
        // ──────────────────────────────────────────────

        private void CanvasContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Only start pan if we clicked on the container itself, not a card
            if (e.OriginalSource == BoardCanvas || e.OriginalSource == CanvasContainer)
            {
                _isPanning = true;
                _panStartMouse = e.GetPosition(this);
                _panStartTranslateX = PanTransform.X;
                _panStartTranslateY = PanTransform.Y;
                CanvasContainer.CaptureMouse();
                e.Handled = true;
            }
        }

        private void CanvasContainer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
            {
                Point current = e.GetPosition(this);
                double dx = current.X - _panStartMouse.X;
                double dy = current.Y - _panStartMouse.Y;

                PanTransform.X = _panStartTranslateX + dx;
                PanTransform.Y = _panStartTranslateY + dy;
                e.Handled = true;
            }
        }

        private void CanvasContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                CanvasContainer.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        // ──────────────────────────────────────────────
        //  Zoom (mouse wheel)
        // ──────────────────────────────────────────────

        private void BoardScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double delta = e.Delta > 0 ? 0.1 : -0.1;
            double newZoom = Math.Round(Math.Clamp(_zoomLevel + delta, ZoomMin, ZoomMax), 1);

            if (Math.Abs(newZoom - _zoomLevel) < 0.01)
                return;

            // Zoom centered on mouse position
            Point mousePos = e.GetPosition(CanvasContainer);

            double oldScale = _zoomLevel;
            _zoomLevel = newZoom;
            ZoomTransform.ScaleX = _zoomLevel;
            ZoomTransform.ScaleY = _zoomLevel;

            // Adjust pan to keep the point under the mouse stable
            double offsetX = mousePos.X * oldScale - mousePos.X * _zoomLevel;
            double offsetY = mousePos.Y * oldScale - mousePos.Y * _zoomLevel;
            PanTransform.X += offsetX;
            PanTransform.Y += offsetY;

            e.Handled = true;
        }

        // ──────────────────────────────────────────────
        //  Right-click context menu on empty canvas
        // ──────────────────────────────────────────────

        private void CanvasContainer_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Only show if clicking on empty canvas (not a card)
            if (e.OriginalSource is DependencyObject source)
            {
                // If the click was on a card element or its child, let the card handle it
                if (FindAncestor<Border>(source, b => b.Tag is NoteCard) != null)
                    return;
            }

            var menu = new ContextMenu();
            var newNoteItem = new MenuItem { Header = "New note" };
            newNoteItem.Click += (_, _) =>
            {
                Point clickPos = Mouse.GetPosition(BoardCanvas);
                // Adjust for zoom
                clickPos = new Point(clickPos.X / _zoomLevel, clickPos.Y / _zoomLevel);
                CreateNewNoteAt(clickPos);
            };
            menu.Items.Add(newNoteItem);

            // Set the context menu on the container
            CanvasContainer.ContextMenu = menu;
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
                // Create note at center of viewport
                double viewCenterX = BoardScroll.HorizontalOffset + BoardScroll.ViewportWidth / 2;
                double viewCenterY = BoardScroll.VerticalOffset + BoardScroll.ViewportHeight / 2;
                // Adjust for zoom/pan
                viewCenterX = (viewCenterX - PanTransform.X) / _zoomLevel;
                viewCenterY = (viewCenterY - PanTransform.Y) / _zoomLevel;

                CreateNewNoteAt(new Point(viewCenterX, viewCenterY));
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

            // Use the InputDialog from StartupWindow
            var dialog = new InputDialog("New Note", "Enter a title for the new note:");
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Answer))
                return;

            string title = dialog.Answer.Trim();
            string relativePath = title + ".md";

            try
            {
                // Create the .md file
                FileService.CreateNoteFile(Project, relativePath);

                // Add a card at the clicked position
                var card = new NoteCard(relativePath, position.X, position.Y);
                Project.Cards.Add(card);

                // Render the new card
                var element = CreateCardElement(card);
                Canvas.SetLeft(element, card.BoardX);
                Canvas.SetTop(element, card.BoardY);
                BoardCanvas.Children.Add(element);

                // Save project
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