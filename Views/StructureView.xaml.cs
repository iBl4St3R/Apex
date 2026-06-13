using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Apex.Models;
using Apex.Services;

namespace Apex.Views
{
    /// <summary>
    /// Interaction logic for StructureView.xaml
    /// Displays the project's file tree (.md files and folders).
    /// Supports multi-select via Ctrl+click and Shift+click.
    /// </summary>
    public partial class StructureView : UserControl
    {
        private ApexProject _project = new();
        private readonly List<string> _selectedPaths = new();
        private string? _lastClickedPath;
        private List<FileTreeItem> _allItems = new(); // flat DFS order for range selection

        /// <summary>
        /// Fires when the user selects one or more .md files in the tree.
        /// Argument is the list of relative paths of selected files.
        /// </summary>
        public event Action<List<string>>? MultipleFilesSelected;

        /// <summary>
        /// Fires when the user right-clicks a file and selects "Find on board".
        /// Argument is the relative path of the file to find.
        /// </summary>
        public event Action<string>? FindOnBoard;

        /// <summary>
        /// Fires when the user creates a new file in the structure view.
        /// Used to notify BoardView to refresh.
        /// </summary>
        public event Action? NoteCreated;

        public StructureView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Loads the file tree from disk based on the given project.
        /// </summary>
        public void LoadProject(ApexProject project)
        {
            _project = project;
            _selectedPaths.Clear();
            _lastClickedPath = null;
            _allItems.Clear();
            BuildTree();
        }

        private void BuildTree()
        {
            FileTree.Items.Clear();
            _allItems.Clear();

            if (string.IsNullOrEmpty(_project.RootFolder) || !Directory.Exists(_project.RootFolder))
                return;

            var rootDir = new DirectoryInfo(_project.RootFolder);
            var rootNode = BuildFolderNode(rootDir, 0);
            if (rootNode != null)
            {
                foreach (var child in rootNode.Children)
                    FileTree.Items.Add(child);
            }
        }

        private FileTreeItem? BuildFolderNode(DirectoryInfo dir, int depth)
        {
            var folderNode = new FileTreeItem
            {
                DisplayName = dir.Name,
                RelativePath = FileService.GetRelativePath(_project.RootFolder, dir.FullName),
                IsFolder = true,
                Depth = depth,
                Children = new System.Collections.ObjectModel.ObservableCollection<FileTreeItem>()
            };

            foreach (var subDir in dir.EnumerateDirectories()
                         .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                var child = BuildFolderNode(subDir, depth + 1);
                if (child != null)
                    folderNode.Children.Add(child);
            }

            foreach (var file in dir.EnumerateFiles("*.md")
                         .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                string relativePath = FileService.GetRelativePath(_project.RootFolder, file.FullName);
                var fileNode = new FileTreeItem
                {
                    DisplayName = Path.GetFileNameWithoutExtension(file.Name),
                    RelativePath = relativePath,
                    IsFolder = false,
                    Depth = depth + 1,
                    FullPath = file.FullName,
                    Children = new System.Collections.ObjectModel.ObservableCollection<FileTreeItem>()
                };

                // Look up category from card data
                var card = _project.Cards.FirstOrDefault(c =>
                    string.Equals(c.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));


                fileNode.HasCategory = false;
                fileNode.MissingCard = card == null; // nowa właściwość

                if (card != null && !string.IsNullOrEmpty(card.CategoryId))
                {
                    var category = _project.Categories.FirstOrDefault(cat =>
                        string.Equals(cat.Id, card.CategoryId, StringComparison.OrdinalIgnoreCase));
                    if (category != null)
                    {
                        fileNode.HasCategory = true;
                        fileNode.CategoryName = category.Name;
                        fileNode.CategoryColor = ParseHexColor(category.Color);
                    }
                }

                folderNode.Children.Add(fileNode);
            }

            // Collect all items in DFS order for range selection
            CollectItems(folderNode);

            if (depth == 0)
                return folderNode;

            return folderNode.Children.Count > 0 ? folderNode : null;
        }

        private void CollectItems(FileTreeItem item)
        {
            _allItems.Add(item);
            foreach (var child in item.Children)
                CollectItems(child);
        }

        // ──────────────────────────────────────────────
        //  Multi-select click handling
        // ──────────────────────────────────────────────

        private void FileTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Find the clicked TreeViewItem
            var treeViewItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (treeViewItem == null)
                return;

            var clickedItem = treeViewItem.DataContext as FileTreeItem;
            if (clickedItem == null)
                return;

            // Only handle file items (not folders) for selection
            if (clickedItem.IsFolder)
            {
                // Allow folder expand/collapse — clear selection for cleanliness
                ClearSelection();
                _selectedPaths.Clear();
                _lastClickedPath = null;
                return;
            }

            e.Handled = true; // Prevent TreeView from handling selection natively

            bool ctrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (ctrlHeld)
            {
                // Ctrl+click: toggle this item in/out of selection
                if (_selectedPaths.Contains(clickedItem.RelativePath))
                {
                    _selectedPaths.Remove(clickedItem.RelativePath);
                    clickedItem.IsSelected = false;
                }
                else
                {
                    _selectedPaths.Add(clickedItem.RelativePath);
                    clickedItem.IsSelected = true;
                }
                _lastClickedPath = clickedItem.RelativePath;
            }
            else if (shiftHeld && _lastClickedPath != null)
            {
                // Shift+click: select range from last clicked to current
                SelectRange(_lastClickedPath, clickedItem.RelativePath);
                _lastClickedPath = clickedItem.RelativePath;
            }
            else
            {
                // Plain click: clear all, select only this one
                ClearSelection();
                _selectedPaths.Clear();
                _selectedPaths.Add(clickedItem.RelativePath);
                clickedItem.IsSelected = true;
                _lastClickedPath = clickedItem.RelativePath;
            }

            // Fire the event
            if (_selectedPaths.Count > 0)
            {
                MultipleFilesSelected?.Invoke(new List<string>(_selectedPaths));
            }
        }

        private void ClearSelection()
        {
            foreach (var path in _selectedPaths)
            {
                var item = FindItemByPath(path);
                if (item != null)
                    item.IsSelected = false;
            }
        }

        private void SelectRange(string fromPath, string toPath)
        {
            // Clear selection first (incremental: only select the range, don't add to existing)
            ClearSelection();
            _selectedPaths.Clear();

            // Find indices in flat list
            int fromIdx = -1, toIdx = -1;
            for (int i = 0; i < _allItems.Count; i++)
            {
                if (!_allItems[i].IsFolder)
                {
                    if (_allItems[i].RelativePath == fromPath) fromIdx = i;
                    if (_allItems[i].RelativePath == toPath) toIdx = i;
                }
            }

            if (fromIdx < 0 || toIdx < 0)
            {
                // Fallback: just select the target
                var target = FindItemByPath(toPath);
                if (target != null)
                {
                    _selectedPaths.Add(toPath);
                    target.IsSelected = true;
                }
                return;
            }

            int start = Math.Min(fromIdx, toIdx);
            int end = Math.Max(fromIdx, toIdx);

            for (int i = start; i <= end; i++)
            {
                var item = _allItems[i];
                if (!item.IsFolder)
                {
                    item.IsSelected = true;
                    _selectedPaths.Add(item.RelativePath);
                }
            }
        }

        private FileTreeItem? FindItemByPath(string relativePath)
        {
            return _allItems.FirstOrDefault(i =>
                string.Equals(i.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        }

        // ──────────────────────────────────────────────
        //  Context menu handlers
        // ──────────────────────────────────────────────

        private FileTreeItem? GetContextItem(object sender)
        {
            var item = sender as MenuItem;
            if (item == null) return null;
            var ctx = item.DataContext as FileTreeItem;
            return ctx;
        }

        private void CtxOpen_Click(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item == null || item.IsFolder) return;
            MultipleFilesSelected?.Invoke(new List<string> { item.RelativePath });
        }

        private void CtxFindOnBoard_Click(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item == null || item.IsFolder) return;
            FindOnBoard?.Invoke(item.RelativePath);
        }

        private void CtxNewNoteHere_Click(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item == null) return;

            string folder = item.IsFolder
                ? item.RelativePath
                : Path.GetDirectoryName(item.RelativePath)?.Replace('\\', '/') ?? "";

            // Show input dialog
            var dialog = new InputDialog("New Note", "Enter a title:");
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Answer))
                return;

            string title = dialog.Answer.Trim();
            string relativePath = string.IsNullOrEmpty(folder) ? title + ".md" : folder + "/" + title + ".md";



            try
            {
                string fullCheckPath = FileService.GetFullPath(_project.RootFolder, relativePath);
                if (File.Exists(fullCheckPath))
                {
                    System.Windows.MessageBox.Show(
                        $"A note named \"{title}.md\" already exists in this folder.",
                        "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }


                FileService.CreateNoteFile(_project, relativePath);

                // Add a card at position 0,0
                var card = new NoteCard(relativePath, 0, 0);
                _project.Cards.Add(card);
                FileService.SaveProject(_project);

                // Reload tree
                LoadProject(_project);

                NoteCreated?.Invoke();
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

        private void CtxNewSubfolder_Click(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item == null || !item.IsFolder) return;

            var dialog = new InputDialog("New Subfolder", "Enter folder name:");
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Answer))
                return;

            string name = dialog.Answer.Trim();
            string relativePath = item.RelativePath + "/" + name;

            try
            {
                FileService.CreateFolder(_project, relativePath);
                LoadProject(_project);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to create folder:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CtxRename_Click(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item == null) return;

            string currentName = item.IsFolder
                ? Path.GetFileName(item.RelativePath.TrimEnd('/'))
                : Path.GetFileNameWithoutExtension(item.RelativePath);

            var dialog = new InputDialog("Rename", $"Rename \"{currentName}\":");
            dialog.Owner = Window.GetWindow(this);
            dialog.Answer = currentName; // pre-fill
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Answer))
                return;

            string newName = dialog.Answer.Trim();
            if (string.Equals(newName, currentName, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                if (item.IsFolder)
                {
                    string oldFolderFullPath = FileService.GetFullPath(_project.RootFolder, item.RelativePath);
                    string? parentPath = Path.GetDirectoryName(item.RelativePath.TrimEnd('/'))?.Replace('\\', '/');
                    string newRel = string.IsNullOrEmpty(parentPath)
                        ? newName
                        : parentPath + "/" + newName;
                    string newFolderFullPath = FileService.GetFullPath(_project.RootFolder, newRel);

                    // Edge case: nowa nazwa to ta sama nazwa (case-insensitive ale inny case)
                    bool samePathDifferentCase = string.Equals(
                        oldFolderFullPath, newFolderFullPath, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(oldFolderFullPath, newFolderFullPath, StringComparison.Ordinal);

                    // Edge case: folder o tej nazwie już istnieje
                    if (!samePathDifferentCase && Directory.Exists(newFolderFullPath))
                    {
                        System.Windows.MessageBox.Show(
                            $"A folder named \"{newName}\" already exists in this location.",
                            "Cannot Rename",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    try
                    {
                        // Na Windows rename przez Move (obsługuje zmianę case przez temp)
                        if (samePathDifferentCase)
                        {
                            string tempPath = newFolderFullPath + "_apex_tmp_" + Guid.NewGuid().ToString("N")[..6];
                            Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(oldFolderFullPath, tempPath);
                            Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(tempPath, newFolderFullPath);
                        }
                        else
                        {
                            Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(oldFolderFullPath, newFolderFullPath);
                        }

                        // Zaktualizuj wszystkie karty których ścieżka zaczyna się od starego folderu
                        string oldPrefix = item.RelativePath.TrimEnd('/', '\\').Replace('\\', '/') + "/";
                        string newPrefix = newRel.TrimEnd('/', '\\').Replace('\\', '/') + "/";

                        foreach (var card in _project.Cards)
                        {
                            string normalizedPath = card.RelativePath.Replace('\\', '/');
                            if (normalizedPath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                                card.RelativePath = newPrefix + normalizedPath[oldPrefix.Length..];
                        }

                        FileService.SaveProject(_project);
                        LoadProject(_project);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show(
                            $"Failed to rename folder:\n{ex.Message}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    return; // nie wpadaj w blok file rename poniżej
                }

                string ext = ".md";
                string parent = Path.GetDirectoryName(item.RelativePath)?.Replace('\\', '/') ?? "";
                string newFileRel = string.IsNullOrEmpty(parent) ? newName + ext : parent + "/" + newName + ext;
                FileService.RenameNoteFile(_project, item.RelativePath, newFileRel);
                LoadProject(_project);
                FileService.SaveProject(_project);
            }
            catch (Exception ex)
            {
                string msg = ex.HResult == unchecked((int)0x80070005)
                    ? $"Cannot rename \"{currentName}\" — close File Explorer and try again."
                    : $"Failed to rename folder:\n{ex.Message}";
                System.Windows.MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CtxDelete_Click(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item == null) return;

            string display = item.IsFolder
                ? item.DisplayName
                : Path.GetFileNameWithoutExtension(item.RelativePath);

            var result = System.Windows.MessageBox.Show(
                $"Delete \"{display}\"?\n" +
                (item.IsFolder
                    ? "The folder and all its contents will be moved to the Recycle Bin."
                    : "The file will be moved to the Recycle Bin."),
                "Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (item.IsFolder)
                {
                    // Delete all files in the folder recursively
                    var filesToRemove = _project.Cards
                        .Where(c => c.RelativePath.StartsWith(item.RelativePath + "/", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    foreach (var card in filesToRemove)
                    {
                        string fullPath = FileService.GetFullPath(_project.RootFolder, card.RelativePath);
                        if (File.Exists(fullPath))
                            FileService.DeleteNoteFile(_project, card.RelativePath);
                        _project.Cards.Remove(card);
                    }

                    // Delete the folder
                    string folderFullPath = FileService.GetFullPath(_project.RootFolder, item.RelativePath);
                    if (Directory.Exists(folderFullPath))
                        Directory.Delete(folderFullPath, true);
                }
                else
                {
                    string fullPath = FileService.GetFullPath(_project.RootFolder, item.RelativePath);
                    if (File.Exists(fullPath))
                        FileService.DeleteNoteFile(_project, item.RelativePath);

                    _project.Cards.RemoveAll(c =>
                        string.Equals(c.RelativePath, item.RelativePath, StringComparison.OrdinalIgnoreCase));
                }

                LoadProject(_project);
                FileService.SaveProject(_project);
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


        public void SelectFile(string relativePath)
        {
            ClearSelection();
            _selectedPaths.Clear();

            var item = FindItemByPath(relativePath);
            if (item != null)
            {
                item.IsSelected = true;
                _selectedPaths.Add(relativePath);
                _lastClickedPath = relativePath;
                MultipleFilesSelected?.Invoke(new List<string> { relativePath });
            }
        }

        // ──────────────────────────────────────────────
        //  Utility
        // ──────────────────────────────────────────────
        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_project.RootFolder) ||
                !Directory.Exists(_project.RootFolder))
                return;

            // Sprawdź czy Explorer już ma otwarty ten folder
            var processes = System.Diagnostics.Process.GetProcessesByName("explorer");
            foreach (var proc in processes)
            {
                // Spróbuj wysunąć istniejące okno na wierzch przez shell
                // Explorer nie udostępnia łatwo który folder ma otwarty,
                // więc po prostu otwieramy — Windows sam wykryje duplikat i wysunie
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _project.RootFolder,
                UseShellExecute = true
            });
        }

        private void CtxOpenExternal_Click(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item == null || item.IsFolder) return;

            string fullPath = FileService.GetFullPath(_project.RootFolder, item.RelativePath);
            if (File.Exists(fullPath))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true
                });
        }

        private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T ancestor)
                    return ancestor;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private static Brush ParseHexColor(string hex)
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


        private void CtxCreateCard_Click(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item == null || item.IsFolder) return;

            // Znajdź wolną pozycję na boardzie (prosta siatka)
            double x = 100 + (_project.Cards.Count % 5) * 260;
            double y = 100 + (_project.Cards.Count / 5) * 160;

            var card = new NoteCard(item.RelativePath, x, y);
            _project.Cards.Add(card);
            FileService.SaveProject(_project);

            // Odśwież tree żeby warning zniknął
            LoadProject(_project);

            // Przenieś na board i pokaż kartę
            FindOnBoard?.Invoke(item.RelativePath);
        }


        private void CtxCopyAsTemplate_Click(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item == null || item.IsFolder) return;

            string fullPath = FileService.GetFullPath(_project.RootFolder, item.RelativePath);
            if (!File.Exists(fullPath)) return;

            string defaultName = Path.GetFileNameWithoutExtension(item.RelativePath);
            var dialog = new InputDialog("Save as Template", "Template name:");
            dialog.Owner = Window.GetWindow(this);
            dialog.Answer = defaultName;
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Answer))
                return;

            var template = TemplateService.CreateFromNote(
                _project.RootFolder, fullPath, dialog.Answer.Trim());
            if (template != null)
                System.Windows.MessageBox.Show(
                    $"Template \"{template.TemplateName}\" saved.\n" +
                    $"Open it via Structure view in the .templates folder to set category and folder.",
                    "Template Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }


    }



    /// <summary>
    /// Represents a single item in the file tree.
    /// Can be either a folder or a .md file.
    /// </summary>
    public class FileTreeItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;

        public string DisplayName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public int Depth { get; set; }
        public bool HasCategory { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public Brush CategoryColor { get; set; } = new SolidColorBrush(Color.FromRgb(136, 136, 136));
        public System.Collections.ObjectModel.ObservableCollection<FileTreeItem> Children { get; set; } = new();

        public bool MissingCard { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Converts tree item depth to an indentation width in pixels (depth * 16).
    /// </summary>
    public class IndentConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is int depth)
                return depth * 16.0;
            return 0.0;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}