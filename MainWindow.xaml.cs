using Apex.Models;
using Apex.Services;
using Apex.Views;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace Apex
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// Hosts the toolbar, left panel (board/structure), right panel (content), and status bar.
    /// </summary>
    public partial class MainWindow : Window
    {
        public ApexProject Project { get; }

        private ViewMode _currentMode;

        private readonly BoardView _boardView;
        private readonly StructureView _structureView;
        private readonly NoteViewer _noteViewer;
        private readonly MergeViewer _mergeViewer;


        private enum ViewMode
        {
            Board,
            Structure
        }

        public MainWindow(ApexProject project)
        {
            Project = project;
            DataContext = project;

            InitializeComponent();

            Title = $"{project.ProjectName} — Apex";

            // Show root folder in status bar
            StatusRootPath.Text = project.RootFolder;

            // Create view instances
            _boardView = new BoardView();


            _boardView.CardSelected += OnBoardCardSelected;

            _boardView.CardEditRequested += OnBoardCardEditRequested;
            _boardView.PreviewRequested += OnBoardPreviewRequested;

            ConnectionsToggle.Checked += (_, _) => _boardView.SetConnectionsVisible(true);
            ConnectionsToggle.Unchecked += (_, _) => _boardView.SetConnectionsVisible(false);

            _structureView = new StructureView();
            _structureView.MultipleFilesSelected += OnStructureFilesSelected;
            _structureView.FindOnBoard += OnStructureFindOnBoard;

            _noteViewer = new NoteViewer();
            _noteViewer.LinkClicked += OnLinkClicked;
            NoteViewerHost.Content = _noteViewer;

            _mergeViewer = new MergeViewer();

            // Restore last view mode from project settings
            ViewMode initialMode = string.Equals(project.LastView, "structure", StringComparison.OrdinalIgnoreCase)
                ? ViewMode.Structure
                : ViewMode.Board;

            SetViewMode(initialMode);

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 44,
                ResizeBorderThickness = new Thickness(4),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0)
            });
        }


        /// <summary>
        /// Switches the left panel between Board and Structure view.
        /// </summary>
        private void SetViewMode(ViewMode mode)
        {
            _currentMode = mode;

            BoardToggle.IsChecked = mode == ViewMode.Board;
            StructureToggle.IsChecked = mode == ViewMode.Structure;

            UpdateToggleButtonStyles(mode);

            switch (mode)
            {
                case ViewMode.Board:
                    LeftPanelContent.Content = _boardView;
                    _boardView.LoadProject(Project);
                    _boardView.FocusBoard();
                    Project.LastView = "board";

                    MainSplitter.Visibility = Visibility.Collapsed;
                    RightPanelHost.Visibility = Visibility.Collapsed;
                    LeftPanelColumn.Width = new GridLength(1, GridUnitType.Star);
                    SplitterColumn.Width = new GridLength(0);
                    RightPanelColumn.Width = new GridLength(0);
                    break;

                case ViewMode.Structure:
                    LeftPanelContent.Content = _structureView;
                    _structureView.LoadProject(Project);
                    Project.LastView = "structure";

                    MainSplitter.Visibility = Visibility.Visible;
                    RightPanelHost.Visibility = Visibility.Visible;
                    LeftPanelColumn.Width = new GridLength(300);
                    SplitterColumn.Width = GridLength.Auto;
                    RightPanelColumn.Width = new GridLength(1, GridUnitType.Star);
                    break;
            }
        }

       


        private void UpdateToggleButtonStyles(ViewMode mode)
        {
            var activeBg = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(49, 50, 68));
            var activeFg = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(205, 214, 244)); // jasny tekst na aktywnym
            var inactiveBg = System.Windows.Media.Brushes.Transparent;
            var inactiveFg = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(88, 91, 112)); // ciemniejszy tekst na nieaktywnym

            BoardToggle.Background = mode == ViewMode.Board ? activeBg : inactiveBg;
            BoardToggle.Foreground = mode == ViewMode.Board ? activeFg : inactiveFg;
            StructureToggle.Background = mode == ViewMode.Structure ? activeBg : inactiveBg;
            StructureToggle.Foreground = mode == ViewMode.Structure ? activeFg : inactiveFg;
        }



        // ──────────────────────────────────────────────
        //  Toolbar event handlers
        // ──────────────────────────────────────────────

        private void OnBoardPreviewRequested(NoteCard card)
        {
            SetViewMode(ViewMode.Structure);
            _structureView.SelectFile(card.RelativePath);
        }

        private void BoardToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_currentMode != ViewMode.Board)
                SetViewMode(ViewMode.Board);
        }

        private void BoardToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (StructureToggle.IsChecked != true)
                BoardToggle.IsChecked = true;
        }

        private void StructureToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_currentMode != ViewMode.Structure)
                SetViewMode(ViewMode.Structure);
        }

        private void StructureToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (BoardToggle.IsChecked != true)
                StructureToggle.IsChecked = true;
        }

        // ──────────────────────────────────────────────
        //  Splitter
        // ──────────────────────────────────────────────

        // ──────────────────────────────────────────────
        //  Settings (category manager)
        // ──────────────────────────────────────────────

        private void OnBoardCardEditRequested(NoteCard card)
        {
            SetViewMode(ViewMode.Structure);
            _structureView.SelectFile(card.RelativePath);
            _noteViewer.EnterEditMode();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var catWindow = new CategoryManagerWindow(Project);
            catWindow.Owner = this;
            catWindow.ShowDialog();

            // Refresh board and structure after categories change
            _boardView.LoadProject(Project);
            _structureView.LoadProject(Project);
        }

        // ──────────────────────────────────────────────
        //  Splitter
        // ──────────────────────────────────────────────

        private void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            // Remember the left panel width (unused currently, available for future persistence)
        }

        // ──────────────────────────────────────────────
        //  File selection from StructureView
        // ──────────────────────────────────────────────

        private void OnStructureFilesSelected(List<string> relativePaths)
        {
            if (relativePaths.Count == 0) return;

            if (relativePaths.Count == 1)
            {
                // Single file → NoteViewer
                string fullPath = Path.Combine(Project.RootFolder, relativePaths[0]);
                if (!File.Exists(fullPath)) return;

                ShowRightPanelContent(_noteViewer);
                _noteViewer.LoadNote(fullPath, Project);
            }
            else
            {
                // Multiple files → MergeViewer
                var fullPaths = relativePaths
                    .Select(p => Path.Combine(Project.RootFolder, p))
                    .Where(File.Exists)
                    .ToList();

                if (fullPaths.Count == 0) return;

                ShowRightPanelContent(_mergeViewer);
                _mergeViewer.LoadFiles(fullPaths);
            }
        }

        // ──────────────────────────────────────────────
        //  Card selection from BoardView
        // ──────────────────────────────────────────────

        private void OnBoardCardSelected(NoteCard card)
        {
            string fullPath = Path.Combine(Project.RootFolder, card.RelativePath);
            if (!File.Exists(fullPath)) return;

            ShowRightPanelContent(_noteViewer);
            _noteViewer.LoadNote(fullPath, Project);
        }

        /// <summary>
        /// Replaces the right panel placeholder with the given content control.
        /// </summary>
        private void ShowRightPanelContent(UIElement content)
        {
            RightPanelPlaceholder.Visibility = Visibility.Collapsed;
            NoteViewerHost.Visibility = Visibility.Visible;
            NoteViewerHost.Content = content;
        }

        // ──────────────────────────────────────────────
        //  [[link]] navigation
        // ──────────────────────────────────────────────

        // ──────────────────────────────────────────────
        //  Structure view: Find on board
        // ──────────────────────────────────────────────

        private void OnStructureFindOnBoard(string relativePath)
        {
            SetViewMode(ViewMode.Board);
            _boardView.FocusBoard();
            _boardView.FocusCard(relativePath);
        }

        // ──────────────────────────────────────────────
        //  [[link]] navigation
        // ──────────────────────────────────────────────

        private void OnLinkClicked(string linkTarget)
        {
            if (string.IsNullOrEmpty(Project?.RootFolder))
                return;

            string? foundPath = FindNoteByTitle(linkTarget);
            if (foundPath == null) return;

            string relativePath = FileService.GetRelativePath(Project.RootFolder, foundPath);

            SetViewMode(ViewMode.Board);
            _boardView.FocusBoard();
            _boardView.FocusCard(relativePath);
        }

        private string? FindNoteByTitle(string title)
        {
            try
            {
                var root = new DirectoryInfo(Project.RootFolder);
                if (!root.Exists) return null;

                // Jeśli title zawiera / to jest to pełna ścieżka względna
                if (title.Contains('/') || title.Contains('\\'))
                {
                    string fullPath = FileService.GetFullPath(Project.RootFolder, title + ".md");
                    return File.Exists(fullPath) ? fullPath : null;
                }

                // Tylko nazwa — szukaj po nazwie pliku
                return root.EnumerateFiles("*.md", SearchOption.AllDirectories)
                    .FirstOrDefault(f =>
                        string.Equals(
                            Path.GetFileNameWithoutExtension(f.Name),
                            title,
                            StringComparison.OrdinalIgnoreCase))
                    ?.FullName;
            }
            catch
            {
                return null;
            }
        }

        // ──────────────────────────────────────────────
        //  Keyboard shortcuts
        // ──────────────────────────────────────────────

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == System.Windows.Input.Key.F &&
    (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                if (_searchOpen) CloseSearch();
                else OpenSearch();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.OemComma &&
                     (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                // Ctrl+, — Settings (TODO)
                e.Handled = true;
            }
        }





        // ──────────────────────────────────────────────
        //  Search
        // ──────────────────────────────────────────────

        private bool _searchOpen = false;

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_searchOpen)
                CloseSearch();
            else
                OpenSearch();
        }

        private void OpenSearch()
        {
            _searchOpen = true;
            SearchBoxContainer.Visibility = Visibility.Visible;
            SearchBox.Text = "";
            SearchBox.Focus();
            SearchResultsList.ItemsSource = null;
            SearchPopup.IsOpen = false;
        }

        private void CloseSearch()
        {
            _searchOpen = false;
            SearchBoxContainer.Visibility = Visibility.Collapsed;
            SearchPopup.IsOpen = false;
            SearchBox.Text = "";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                SearchPopup.IsOpen = false;
                return;
            }

            var results = Project.Cards
                .Where(c =>
                {
                    string name = Path.GetFileNameWithoutExtension(c.RelativePath);
                    string path = c.RelativePath.Replace('\\', '/');
                    return name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || path.Contains(query, StringComparison.OrdinalIgnoreCase);
                })
                .Select(c => new SearchResult
                {
                    DisplayName = Path.GetFileNameWithoutExtension(c.RelativePath),
                    RelativePath = c.RelativePath.Replace('\\', '/'),
                    Card = c
                })
                .OrderBy(r => r.DisplayName)
                .Take(20)
                .ToList();

            if (results.Count == 0)
            {
                results.Add(new SearchResult
                {
                    DisplayName = "No results",
                    RelativePath = "",
                    Card = null
                });
            }

            SearchResultsList.ItemsSource = results;
            SearchPopup.IsOpen = true;
        }

        private void TemplatesButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new TemplateManagerWindow(Project);
            window.Owner = this;
            window.OpenTemplateRequested += OnOpenTemplateRequested;
            window.ShowDialog();
        }

        private void OnOpenTemplateRequested(string fullPath)
        {
            // Switch to Structure view and open the template file in NoteViewer
            SetViewMode(ViewMode.Structure);
            _structureView.SelectFile(
                FileService.GetRelativePath(Project.RootFolder, fullPath));

            // The file is in .templates/ so not in the tree — open directly in NoteViewer
            ShowRightPanelContent(_noteViewer);
            _noteViewer.LoadNote(fullPath, Project);
            _noteViewer.EnterEditMode();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaxRestoreButton.Content = "□";
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaxRestoreButton.Content = "❐";
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Podwójne kliknięcie = maximize/restore
                if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                    MaxRestoreButton.Content = "□";
                }
                else
                {
                    WindowState = WindowState.Maximized;
                    MaxRestoreButton.Content = "❐";
                }
            }
            else
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Close();

        private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                CloseSearch();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Down)
            {
                if (SearchResultsList.Items.Count > 0)
                {
                    SearchResultsList.SelectedIndex = Math.Max(0,
                        SearchResultsList.SelectedIndex < 0 ? 0 : SearchResultsList.SelectedIndex + 1);
                }
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (SearchResultsList.SelectedItem is SearchResult r && r.Card != null)
                    NavigateToResult(r);
                else if (SearchResultsList.Items.Count > 0
                         && SearchResultsList.Items[0] is SearchResult first && first.Card != null)
                    NavigateToResult(first);
                e.Handled = true;
            }
        }

        private void SearchResultsList_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SearchResultsList.SelectedItem is SearchResult r && r.Card != null)
                NavigateToResult(r);
        }

        private void NavigateToResult(SearchResult result)
        {
            CloseSearch();

            if (_currentMode == ViewMode.Board)
            {
                _boardView.FocusCard(result.Card!.RelativePath);
            }
            else
            {
                _structureView.SelectFile(result.Card!.RelativePath);
            }
        }

        private void SearchPopup_Closed(object sender, EventArgs e)
        {
            // Popup zamknięty przez kliknięcie poza nim — zamknij też searchbox
            // ale tylko jeśli focus nie wrócił do SearchBox
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!SearchBox.IsFocused)
                    CloseSearch();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var source = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);

            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var wa = screen.WorkingArea;
            var ma = screen.Bounds;

            mmi.ptMaxPosition.x = wa.Left - ma.Left;
            mmi.ptMaxPosition.y = wa.Top - ma.Top;
            mmi.ptMaxSize.x = wa.Width;
            mmi.ptMaxSize.y = wa.Height;
            mmi.ptMaxTrackSize.x = wa.Width;
            mmi.ptMaxTrackSize.y = wa.Height;

            System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
        }

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState == WindowState.Maximized)
                MaxRestoreButton.Content = "❐";
            else if (WindowState == WindowState.Normal)
                MaxRestoreButton.Content = "□";
        }

        private class SearchResult
        {
            public string DisplayName { get; set; } = "";
            public string RelativePath { get; set; } = "";
            public NoteCard? Card { get; set; }
        }


        private void ExportHtmlButton_Click(object sender, RoutedEventArgs e)
        {
            if (Project == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export project to HTML",
                Filter = "HTML file (*.html)|*.html",
                FileName = Project.ProjectName + ".html",
                DefaultExt = ".html"
            };

            if (dialog.ShowDialog() != true) return;

            string outputPath = dialog.FileName;

            // Pokaż progress przez zmianę tytułu okna
            string oldTitle = Title;
            Title = "Exporting…";
            IsEnabled = false;

            try
            {
                ExportService.Export(Project, outputPath);

                string folderName = Project.ProjectName;
                System.Windows.MessageBox.Show(
                    $"Export complete!\n\n" +
                    $"• {System.IO.Path.GetFileName(outputPath)}\n" +
                    $"• {folderName}/ (images folder)\n\n" +
                    $"Keep both files in the same directory.",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Otwórz folder wynikowy
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.IO.Path.GetDirectoryName(outputPath)!,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Export failed:\n{ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
                Title = oldTitle;
            }
        }
    }
}