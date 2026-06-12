using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Apex.Models;
using Apex.Services;
using Apex.Views;

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
        }

        /// <summary>
        /// Switches the left panel between Board and Structure view.
        /// </summary>
        private void SetViewMode(ViewMode mode)
        {
            _currentMode = mode;

            // Toggle button states
            BoardToggle.IsChecked = mode == ViewMode.Board;
            StructureToggle.IsChecked = mode == ViewMode.Structure;

            // Toggle button visual — highlight the active one
            UpdateToggleButtonStyles(mode);

            // Inject the active view
            switch (mode)
            {
                case ViewMode.Board:
                    LeftPanelContent.Content = _boardView;
                    _boardView.LoadProject(Project);
                    Project.LastView = "board";
                    break;
                case ViewMode.Structure:
                    LeftPanelContent.Content = _structureView;
                    _structureView.LoadProject(Project);
                    Project.LastView = "structure";
                    break;
            }
        }

        private void UpdateToggleButtonStyles(ViewMode mode)
        {
            var activeBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(49, 50, 68));
            var activeFg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(205, 214, 244));
            var inactiveBg = System.Windows.Media.Brushes.Transparent;
            var inactiveFg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 173, 200));

            BoardToggle.Background = mode == ViewMode.Board ? activeBg : inactiveBg;
            BoardToggle.Foreground = mode == ViewMode.Board ? activeFg : inactiveFg;
            StructureToggle.Background = mode == ViewMode.Structure ? activeBg : inactiveBg;
            StructureToggle.Foreground = mode == ViewMode.Structure ? activeFg : inactiveFg;
        }

        // ──────────────────────────────────────────────
        //  Toolbar event handlers
        // ──────────────────────────────────────────────

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
            // Switch to board view
            SetViewMode(ViewMode.Board);

            // Focus the card
            _boardView.FocusCard(relativePath);
        }

        // ──────────────────────────────────────────────
        //  [[link]] navigation
        // ──────────────────────────────────────────────

        private void OnLinkClicked(string linkTarget)
        {
            if (string.IsNullOrEmpty(Project.RootFolder))
                return;

            string? foundPath = FindNoteByTitle(linkTarget);
            if (foundPath != null)
            {
                ShowRightPanelContent(_noteViewer);
                _noteViewer.LoadNote(foundPath, Project);
            }
        }

        private string? FindNoteByTitle(string title)
        {
            try
            {
                var root = new DirectoryInfo(Project.RootFolder);
                if (!root.Exists) return null;

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
                // Ctrl+F — Search (TODO)
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.OemComma &&
                     (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                // Ctrl+, — Settings (TODO)
                e.Handled = true;
            }
        }
    }
}