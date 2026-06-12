using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Apex.Models;
using Apex.Services;

namespace Apex.Views
{
    /// <summary>
    /// Interaction logic for NoteViewer.xaml
    /// Displays the content of a .md note file in read or edit mode.
    /// </summary>
    public partial class NoteViewer : UserControl
    {
        private ApexProject? _project;
        private string? _currentFilePath;
        private string? _currentRelativePath;
        private string? _savedContent;
        private FileSystemWatcher? _watcher;

        /// <summary>
        /// Fires when the user clicks a wiki [[link]] inside the note.
        /// Argument is the link target (e.g., "Character" from [[Character]]).
        /// </summary>
        public event Action<string>? LinkClicked;

        public NoteViewer()
        {
            InitializeComponent();
        }

        // ──────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────

        /// <summary>
        /// Loads a .md file into the viewer, renders it in read mode.
        /// </summary>
        public void LoadNote(string fullPath, ApexProject project)
        {
            _project = project;
            _currentFilePath = fullPath;
            _currentRelativePath = FileService.GetRelativePath(project.RootFolder, fullPath);

            StopWatcher();
            SwitchToReadMode();

            if (!File.Exists(fullPath))
            {
                ReadContent.Document = new FlowDocument(
                    new Paragraph(new Run($"File not found: {fullPath}") { Foreground = Brushes.Red }));
                return;
            }

            string markdown = File.ReadAllText(fullPath);

            // Store for edit-mode revert
            _savedContent = markdown;

            // Render the markdown
            RenderMarkdown(markdown);

            // Update toolbar
            UpdateToolbar();

            // Start watching the file for external changes
            StartWatcher();
        }

        /// <summary>
        /// Switches to edit mode, showing the raw markdown in a text editor.
        /// </summary>
        public void EnterEditMode()
        {
            if (_currentFilePath == null || _savedContent == null)
                return;

            // Hide external banner when entering edit mode
            ExternalChangeBanner.Visibility = Visibility.Collapsed;

            SwitchToEditMode();
        }

        /// <summary>
        /// Switches to read mode, rendering the current content.
        /// </summary>
        public void ExitEditMode()
        {
            SwitchToReadMode();

            if (_savedContent != null)
                RenderMarkdown(_savedContent);
        }

        // ──────────────────────────────────────────────
        //  Mode switching
        // ──────────────────────────────────────────────

        private void SwitchToReadMode()
        {
            // Toolbar
            ReadToolbar.Visibility = Visibility.Visible;
            EditToolbar.Visibility = Visibility.Collapsed;

            // Content
            ReadContent.Visibility = Visibility.Visible;
            EditContent.Visibility = Visibility.Collapsed;

            // Focus the viewer (not the editor)
            ReadContent.Focus();
        }

        private void SwitchToEditMode()
        {
            // Toolbar
            ReadToolbar.Visibility = Visibility.Collapsed;
            EditToolbar.Visibility = Visibility.Visible;

            // Content
            ReadContent.Visibility = Visibility.Collapsed;
            EditContent.Visibility = Visibility.Visible;

            // Load markdown into the text editor
            EditContent.Text = _savedContent ?? string.Empty;
            EditTitleBox.Text = GetFileNameWithoutExtension();

            // Focus the editor
            EditContent.Focus();
            EditContent.CaretIndex = EditContent.Text.Length;
        }

        // ──────────────────────────────────────────────
        //  Markdown rendering
        // ──────────────────────────────────────────────

        private void RenderMarkdown(string markdown)
        {
            ReadContent.Document = MarkdownRenderer.RenderToFlowDocument(markdown, target =>
            {
                LinkClicked?.Invoke(target);
            });
        }

        // ──────────────────────────────────────────────
        //  Toolbar
        // ──────────────────────────────────────────────

        private void UpdateToolbar()
        {
            if (_currentFilePath == null)
                return;

            // Title
            string title = GetFileNameWithoutExtension();
            NoteTitle.Text = title;

            // Category badge
            if (_project != null && _currentRelativePath != null)
            {
                var card = _project.Cards.FirstOrDefault(c =>
                    string.Equals(c.RelativePath, _currentRelativePath, StringComparison.OrdinalIgnoreCase));
                if (card != null && !string.IsNullOrEmpty(card.CategoryId))
                {
                    var category = _project.Categories.FirstOrDefault(cat =>
                        string.Equals(cat.Id, card.CategoryId, StringComparison.OrdinalIgnoreCase));
                    if (category != null)
                    {
                        CategoryBadge.Visibility = Visibility.Visible;
                        CategoryBadge.Background = ParseHexBrush(category.Color);
                        CategoryNameText.Text = category.Name;
                    }
                    else
                    {
                        CategoryBadge.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    CategoryBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Dates
            try
            {
                var fileInfo = new FileInfo(_currentFilePath);
                CreationDateText.Text = fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm");
                ModifiedDateText.Text = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                CreationDateText.Text = "—";
                ModifiedDateText.Text = "—";
            }
        }

        // ──────────────────────────────────────────────
        //  Button handlers (read mode)
        // ──────────────────────────────────────────────

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            EnterEditMode();
        }

        // ──────────────────────────────────────────────
        //  Button handlers (edit mode)
        // ──────────────────────────────────────────────

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveNote();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelEdit();
        }

        private void SaveNote()
        {
            if (_currentFilePath == null)
                return;

            try
            {
                string newContent = EditContent.Text;

                // Handle title rename
                string newTitle = EditTitleBox.Text.Trim();
                string oldTitle = GetFileNameWithoutExtension();

                if (!string.IsNullOrEmpty(newTitle) &&
                    !string.Equals(newTitle, oldTitle, StringComparison.OrdinalIgnoreCase))
                {
                    // Rename the file
                    string? dir = Path.GetDirectoryName(_currentFilePath);
                    string newPath = Path.Combine(dir ?? string.Empty, newTitle + ".md");

                    if (!string.Equals(newPath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(newPath))
                        {
                            System.Windows.MessageBox.Show(
                                $"A file named \"{newTitle}.md\" already exists.",
                                "Cannot Rename",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        if (_project != null && _currentRelativePath != null)
                        {
                            string oldRel = _currentRelativePath;
                            string newRel = FileService.GetRelativePath(_project.RootFolder, newPath);

                            FileService.RenameNoteFile(_project, oldRel, newRel);

                            _currentFilePath = newPath;
                            _currentRelativePath = newRel;
                        }
                        else
                        {
                            File.Move(_currentFilePath, newPath);
                            _currentFilePath = newPath;
                        }
                    }
                }

                // Write content to disk
                File.WriteAllText(_currentFilePath, newContent);
                _savedContent = newContent;

                // Check if file was renamed; update toolbar and re-render
                UpdateToolbar();
                RenderMarkdown(newContent);
                SwitchToReadMode();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to save:\n{ex.Message}",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CancelEdit()
        {
            // Revert to last saved content
            if (_savedContent != null)
                RenderMarkdown(_savedContent);

            UpdateToolbar();
            SwitchToReadMode();
        }

        // ──────────────────────────────────────────────
        //  External file watcher
        // ──────────────────────────────────────────────

        private void StartWatcher()
        {
            if (_currentFilePath == null)
                return;

            try
            {
                string? dir = Path.GetDirectoryName(_currentFilePath);
                string fileName = Path.GetFileName(_currentFilePath);

                if (dir == null) return;

                _watcher = new FileSystemWatcher(dir, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                _watcher.Changed += OnExternalFileChange;
            }
            catch
            {
                // File watcher is best-effort
            }
        }

        private void StopWatcher()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnExternalFileChange;
                _watcher.Dispose();
                _watcher = null;
            }
            ExternalChangeBanner.Visibility = Visibility.Collapsed;
        }

        private void OnExternalFileChange(object sender, FileSystemEventArgs e)
        {
            // Must dispatch to UI thread
            Dispatcher.Invoke(() =>
            {
                if (EditContent.Visibility == Visibility.Visible)
                {
                    // In edit mode — show the banner
                    ExternalChangeBanner.Visibility = Visibility.Visible;
                }
                else
                {
                    // In read mode — reload silently
                    ReloadExternalChanges();
                }
            });
        }

        private void ReloadExternalChanges()
        {
            if (_currentFilePath == null || !File.Exists(_currentFilePath))
                return;

            try
            {
                string content = File.ReadAllText(_currentFilePath);
                _savedContent = content;

                if (EditContent.Visibility == Visibility.Visible)
                {
                    // Update the editor text too
                    EditContent.Text = content;
                }
                else
                {
                    RenderMarkdown(content);
                }

                UpdateToolbar();
            }
            catch { }
        }

        private void LoadExternalButton_Click(object sender, RoutedEventArgs e)
        {
            ExternalChangeBanner.Visibility = Visibility.Collapsed;
            ReloadExternalChanges();
        }

        private void KeepMineButton_Click(object sender, RoutedEventArgs e)
        {
            ExternalChangeBanner.Visibility = Visibility.Collapsed;
        }

        // ──────────────────────────────────────────────
        //  Keyboard shortcuts
        // ──────────────────────────────────────────────

        private void UserControl_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            bool inEditMode = EditContent.Visibility == Visibility.Visible;

            if (!inEditMode && e.Key == Key.F2)
            {
                // F2 — Enter edit mode
                EnterEditMode();
                e.Handled = true;
            }
            else if (inEditMode && e.Key == Key.S &&
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // Ctrl+S — Save
                SaveNote();
                e.Handled = true;
            }
            else if (inEditMode && e.Key == Key.Escape)
            {
                // Esc — Cancel edit
                CancelEdit();
                e.Handled = true;
            }
        }

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

        private string GetFileNameWithoutExtension()
        {
            return _currentFilePath != null
                ? Path.GetFileNameWithoutExtension(_currentFilePath)
                : string.Empty;
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