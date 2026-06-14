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
    public partial class NoteViewer : UserControl
    {
        private ApexProject? _project;
        private string? _currentFilePath;
        private string? _currentRelativePath;
        private string? _savedContent;
        private FileSystemWatcher? _watcher;
        private int _wikiTriggerIndex = -1;

        // Template state
        private NoteTemplate? _currentTemplate;
        private bool _templateBarChanging = false;

        public event Action<string>? LinkClicked;

        public NoteViewer()
        {
            InitializeComponent();
        }

        // ──────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────

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
            _savedContent = markdown;
            RenderMarkdown(markdown);
            UpdateToolbar();
            UpdateTemplateBar();
            StartWatcher();
        }

        public void EnterEditMode()
        {
            if (_currentFilePath == null || _savedContent == null) return;
            ExternalChangeBanner.Visibility = Visibility.Collapsed;
            SwitchToEditMode();
        }

        public void ExitEditMode()
        {
            SwitchToReadMode();
            if (_savedContent != null)
                RenderMarkdown(_savedContent);
        }

        // ──────────────────────────────────────────────
        //  Template bar
        // ──────────────────────────────────────────────

        private void UpdateTemplateBar()
        {
            if (_project == null || _currentFilePath == null)
            {
                TemplateBar.Visibility = Visibility.Collapsed;
                _currentTemplate = null;
                return;
            }

            string templatesFolder = TemplateService.GetTemplatesFolder(_project.RootFolder);
            bool isTemplate = _currentFilePath.StartsWith(
                templatesFolder, StringComparison.OrdinalIgnoreCase);

            if (!isTemplate)
            {
                TemplateBar.Visibility = Visibility.Collapsed;
                _currentTemplate = null;
                return;
            }

            TemplateBar.Visibility = Visibility.Visible;

            // Load or create template meta
            string mdFileName = Path.GetFileName(_currentFilePath);
            _currentTemplate = TemplateService.LoadMeta(_project.RootFolder, mdFileName)
                               ?? new NoteTemplate(
                                   Path.GetFileNameWithoutExtension(mdFileName),
                                   mdFileName);

            // Populate category dropdown
            _templateBarChanging = true;
            TemplateCategory.Items.Clear();
            TemplateCategory.Items.Add(new ComboBoxItem
            {
                Content = "(None)",
                Tag = (string?)null
            });
            foreach (var cat in _project.Categories)
            {
                TemplateCategory.Items.Add(new ComboBoxItem
                {
                    Content = cat.Name,
                    Tag = cat.Id,
                    Foreground = new SolidColorBrush(
                        ParseHexColor(cat.Color))
                });
            }

            // Select current
            var selected = TemplateCategory.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(
                    i.Tag as string,
                    _currentTemplate.DefaultCategoryId,
                    StringComparison.OrdinalIgnoreCase));
            TemplateCategory.SelectedItem = selected ?? TemplateCategory.Items[0];

            // Folder
            TemplateFolderBox.Text = _currentTemplate.DefaultFolder;
            _templateBarChanging = false;
        }

        private void SaveTemplateMeta()
        {
            if (_project == null || _currentTemplate == null) return;
            TemplateService.SaveMeta(_project.RootFolder, _currentTemplate);
        }

        private void TemplateCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_templateBarChanging || _currentTemplate == null) return;
            if (TemplateCategory.SelectedItem is ComboBoxItem item)
            {
                _currentTemplate.DefaultCategoryId = item.Tag as string;
                SaveTemplateMeta();
            }
        }

        private void TemplateFolderBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_templateBarChanging || _currentTemplate == null) return;
            _currentTemplate.DefaultFolder = TemplateFolderBox.Text.Trim();
            SaveTemplateMeta();
        }

        // ──────────────────────────────────────────────
        //  Mode switching
        // ──────────────────────────────────────────────

        private void SwitchToReadMode()
        {
            ReadToolbar.Visibility = Visibility.Visible;
            EditToolbar.Visibility = Visibility.Collapsed;
            ReadContent.Visibility = Visibility.Visible;
            EditContent.Visibility = Visibility.Collapsed;

            EditContent.TextChanged -= EditContent_WikiTextChanged;
            EditContent.PreviewKeyDown -= EditContent_WikiKeyDown;
            WikiLinkPopup.Visibility = Visibility.Collapsed;

            ReadContent.Focus();
        }

        private void SwitchToEditMode()
        {
            ReadToolbar.Visibility = Visibility.Collapsed;
            EditToolbar.Visibility = Visibility.Visible;
            ReadContent.Visibility = Visibility.Collapsed;
            EditContent.Visibility = Visibility.Visible;

            EditContent.Text = _savedContent ?? string.Empty;
            EditTitleBox.Text = GetFileNameWithoutExtension();

            EditContent.Focus();
            EditContent.CaretIndex = EditContent.Text.Length;

            EditContent.TextChanged += EditContent_WikiTextChanged;
            EditContent.PreviewKeyDown += EditContent_WikiKeyDown;
        }

        // ──────────────────────────────────────────────
        //  Markdown rendering
        // ──────────────────────────────────────────────

        private void RenderMarkdown(string markdown)
        {
            string? noteFolder = _currentFilePath != null
                ? Path.GetDirectoryName(_currentFilePath)
                : null;

            ReadContent.Document = MarkdownRenderer.RenderToFlowDocument(
                markdown,
                target => LinkClicked?.Invoke(target),
                linkTarget =>
                {
                    if (_project == null) return false;
                    return _project.Cards.Any(c =>
                        string.Equals(
                            Path.GetFileNameWithoutExtension(c.RelativePath),
                            linkTarget,
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            c.RelativePath.Replace('\\', '/').Replace(".md", ""),
                            linkTarget,
                            StringComparison.OrdinalIgnoreCase));
                },
                noteFolder);
        }

        // ──────────────────────────────────────────────
        //  Toolbar
        // ──────────────────────────────────────────────

        private void UpdateToolbar()
        {
            if (_currentFilePath == null) return;

            NoteTitle.Text = GetFileNameWithoutExtension();

            if (_project != null && _currentRelativePath != null)
            {
                var card = _project.Cards.FirstOrDefault(c =>
                    string.Equals(c.RelativePath, _currentRelativePath,
                        StringComparison.OrdinalIgnoreCase));
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
                    else CategoryBadge.Visibility = Visibility.Collapsed;
                }
                else CategoryBadge.Visibility = Visibility.Collapsed;
            }

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
        //  Button handlers
        // ──────────────────────────────────────────────

        private void EditButton_Click(object sender, RoutedEventArgs e) => EnterEditMode();
        private void SaveButton_Click(object sender, RoutedEventArgs e) => SaveNote();
        private void CancelButton_Click(object sender, RoutedEventArgs e) => CancelEdit();

        // ──────────────────────────────────────────────
        //  Wiki-link autocomplete (unchanged)
        // ──────────────────────────────────────────────

        private void EditContent_WikiTextChanged(object sender, TextChangedEventArgs e)
        {
            int caret = EditContent.CaretIndex;
            string text = EditContent.Text;

            int searchFrom = Math.Max(0, caret - 1);
            int triggerIdx = -1;

            for (int i = searchFrom; i >= 1; i--)
            {
                if (text[i] == '[' && text[i - 1] == '[')
                {
                    string between = text.Substring(i + 1, caret - i - 1);
                    if (!between.Contains("]]"))
                    {
                        triggerIdx = i - 1;
                        break;
                    }
                }
            }

            if (triggerIdx < 0)
            {
                WikiLinkPopup.Visibility = Visibility.Collapsed;
                _wikiTriggerIndex = -1;
                return;
            }

            _wikiTriggerIndex = triggerIdx;
            string filter = text.Substring(triggerIdx + 2, caret - triggerIdx - 2);

            if (_project == null) return;

            var matches = _project.Cards
                .Where(c => Path.GetFileNameWithoutExtension(c.RelativePath)
                    .Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.RelativePath)
                .Take(10)
                .Select(c => c.RelativePath.Replace('\\', '/').Replace(".md", ""))
                .ToList();

            if (matches.Count == 0)
            {
                WikiLinkPopup.Visibility = Visibility.Collapsed;
                return;
            }

            WikiLinkList.ItemsSource = matches;
            WikiLinkList.SelectedIndex = 0;

            var rect = EditContent.GetRectFromCharacterIndex(caret);
            WikiLinkPopup.Margin = new Thickness(rect.Left + 16, rect.Bottom + 12, 0, 0);
            WikiLinkPopup.Visibility = Visibility.Visible;
        }

        private void EditContent_WikiKeyDown(object sender, KeyEventArgs e)
        {
            if (WikiLinkPopup.Visibility != Visibility.Visible) return;

            switch (e.Key)
            {
                case Key.Down:
                    if (WikiLinkList.SelectedIndex < WikiLinkList.Items.Count - 1)
                        WikiLinkList.SelectedIndex++;
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (WikiLinkList.SelectedIndex > 0)
                        WikiLinkList.SelectedIndex--;
                    e.Handled = true;
                    break;
                case Key.Enter:
                    CommitWikiLink();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    WikiLinkPopup.Visibility = Visibility.Collapsed;
                    _wikiTriggerIndex = -1;
                    e.Handled = true;
                    break;
            }
        }

        private void CommitWikiLink()
        {
            if (WikiLinkList.SelectedItem is not string selected) return;
            string text = EditContent.Text;
            int caret = EditContent.CaretIndex;
            string before = text[.._wikiTriggerIndex];
            string after = text[caret..];
            string inserted = $"[[{selected}]]";
            EditContent.Text = before + inserted + after;
            EditContent.CaretIndex = before.Length + inserted.Length;
            WikiLinkPopup.Visibility = Visibility.Collapsed;
            _wikiTriggerIndex = -1;
        }

        private void WikiLinkList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
            => CommitWikiLink();

        // ──────────────────────────────────────────────
        //  Save / Cancel
        // ──────────────────────────────────────────────

        private void SaveNote()
        {
            if (_currentFilePath == null) return;

            try
            {
                string newContent = EditContent.Text;
                string newTitle = EditTitleBox.Text.Trim();
                string oldTitle = GetFileNameWithoutExtension();

                if (!string.IsNullOrEmpty(newTitle) &&
                    !string.Equals(newTitle, oldTitle, StringComparison.OrdinalIgnoreCase))
                {
                    string? dir = Path.GetDirectoryName(_currentFilePath);
                    string newPath = Path.Combine(dir ?? string.Empty, newTitle + ".md");

                    if (!string.Equals(newPath, _currentFilePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(newPath))
                        {
                            System.Windows.MessageBox.Show(
                                $"A file named \"{newTitle}.md\" already exists.",
                                "Cannot Rename", MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }

                        if (_project != null && _currentRelativePath != null)
                        {
                            string oldRel = _currentRelativePath;
                            string newRel = FileService.GetRelativePath(
                                _project.RootFolder, newPath);
                            FileService.RenameNoteFile(_project, oldRel, newRel);
                            _currentFilePath = newPath;
                            _currentRelativePath = newRel;
                        }
                        else
                        {
                            File.Move(_currentFilePath, newPath);
                            _currentFilePath = newPath;
                        }

                        // If renaming a template file, update sidecar
                        if (_currentTemplate != null && _project != null)
                        {
                            string oldSidecar = TemplateService.GetTemplatesFolder(
                                _project.RootFolder) + "/" +
                                Path.GetFileNameWithoutExtension(
                                    _currentTemplate.MdFileName) + ".json";
                            _currentTemplate.MdFileName = Path.GetFileName(_currentFilePath);
                            _currentTemplate.TemplateName = newTitle;
                            TemplateService.SaveMeta(_project.RootFolder, _currentTemplate);
                            if (File.Exists(oldSidecar)) File.Delete(oldSidecar);
                        }
                    }
                }

                // Create folder on first save if it doesn't exist yet
                string? saveDir = Path.GetDirectoryName(_currentFilePath);
                if (!string.IsNullOrEmpty(saveDir) && !Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);

                File.WriteAllText(_currentFilePath, newContent);
                _savedContent = newContent;
                UpdateToolbar();
                UpdateTemplateBar();
                RenderMarkdown(newContent);
                SwitchToReadMode();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to save:\n{ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelEdit()
        {
            if (_savedContent != null) RenderMarkdown(_savedContent);
            UpdateToolbar();
            SwitchToReadMode();
        }

        // ──────────────────────────────────────────────
        //  File watcher
        // ──────────────────────────────────────────────

        private void StartWatcher()
        {
            if (_currentFilePath == null) return;
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
            catch { }
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
            Dispatcher.Invoke(() =>
            {
                if (EditContent.Visibility == Visibility.Visible)
                    ExternalChangeBanner.Visibility = Visibility.Visible;
                else
                    ReloadExternalChanges();
            });
        }

        private void ReloadExternalChanges()
        {
            if (_currentFilePath == null || !File.Exists(_currentFilePath)) return;
            try
            {
                string content = File.ReadAllText(_currentFilePath);
                _savedContent = content;
                if (EditContent.Visibility == Visibility.Visible)
                    EditContent.Text = content;
                else
                    RenderMarkdown(content);
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
            => ExternalChangeBanner.Visibility = Visibility.Collapsed;

        // ──────────────────────────────────────────────
        //  Keyboard shortcuts
        // ──────────────────────────────────────────────

        private void UserControl_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            bool inEditMode = EditContent.Visibility == Visibility.Visible;

            if (!inEditMode && e.Key == Key.F2)
            {
                EnterEditMode();
                e.Handled = true;
            }
            else if (inEditMode && e.Key == Key.S &&
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SaveNote();
                e.Handled = true;
            }
            else if (inEditMode && e.Key == Key.Escape)
            {
                CancelEdit();
                e.Handled = true;
            }
        }

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

        private string GetFileNameWithoutExtension() =>
            _currentFilePath != null
                ? Path.GetFileNameWithoutExtension(_currentFilePath)
                : string.Empty;

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

        private static Color ParseHexColor(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                    return Color.FromRgb(
                        Convert.ToByte(hex[..2], 16),
                        Convert.ToByte(hex[2..4], 16),
                        Convert.ToByte(hex[4..6], 16));
            }
            catch { }
            return Color.FromRgb(136, 136, 136);
        }
    }
}