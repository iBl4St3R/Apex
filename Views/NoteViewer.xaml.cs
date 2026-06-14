using Apex.Models;
using Apex.Services;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

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
            BuildMdToolbar();
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

            MdToolbar.Visibility = Visibility.Collapsed;
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

            MdToolbar.Visibility = Visibility.Visible;
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

        private void BuildMdToolbar()
        {
            MdToolbarPanel.Children.Clear();

            // ── Nagłówki ──
            AddToolbarGroup("HEADINGS", new[]
            {
        MakeToolBtn("H1", "Heading 1", () => InsertLinePrefix("# ")),
        MakeToolBtn("H2", "Heading 2", () => InsertLinePrefix("## ")),
        MakeToolBtn("H3", "Heading 3", () => InsertLinePrefix("### ")),
        MakeToolBtn("H4", "Heading 4", () => InsertLinePrefix("#### ")),
    });

            AddToolbarSeparator();

            // ── Inline formatting ──
            AddToolbarGroup("FORMAT", new[]
            {
        MakeToolBtn("B", "Bold", () => WrapSelection("**", "**"), bold: true),
        MakeToolBtn("I", "Italic", () => WrapSelection("*", "*"), italic: true),
        MakeToolBtn("~~", "Strikethrough", () => WrapSelection("~~", "~~")),
        MakeToolBtn("`", "Inline code", () => WrapSelection("`", "`")),
    });

            AddToolbarSeparator();

            // ── Bloki ──
            AddToolbarGroup("BLOCKS", new[]
            {
        MakeToolBtn("{ }", "Code block", () => InsertBlock("```\n", "\n```")),
        MakeToolBtn("❝", "Blockquote", () => InsertLinePrefix("> ")),
        MakeToolBtn("─", "Horizontal rule", () => InsertRaw("\n---\n")),
    });

            AddToolbarSeparator();

            // ── Listy ──
            AddToolbarGroup("LISTS", new[]
            {
        MakeToolBtn("• —", "Bullet list", () => InsertLinePrefix("- ")),
        MakeToolBtn("1.", "Numbered list", () => InsertLinePrefix("1. ")),
        MakeToolBtn("☐", "Task item", () => InsertLinePrefix("- [ ] ")),
    });

            AddToolbarSeparator();

            // ── Linki i media ──
            AddToolbarGroup("INSERT", new[]
            {
        MakeToolBtn("🔗", "Link", () => InsertLink()),
        MakeToolBtn("🖼", "Image (MD)", () => InsertImageMd()),
        MakeToolBtn("⊞ img", "Image centered (HTML)", () => InsertImageHtml()),
        MakeToolBtn("[[", "Wiki-link", () => WrapSelection("[[", "]]")),
    });

            AddToolbarSeparator();

            // ── Tabela ──
            AddToolbarGroup("TABLE", new[]
            {
        MakeToolBtn("⊞ 2×2", "Table 2 columns", () => InsertTable(2, 3)),
        MakeToolBtn("⊞ 3×3", "Table 3 columns", () => InsertTable(3, 3)),
    });
        }

        // ── Helpers do budowania UI ──────────────────────────────────────

        private void AddToolbarGroup(string label, Button[] buttons)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0) };

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            group.Children.Add(lbl);

            foreach (var btn in buttons)
                group.Children.Add(btn);

            MdToolbarPanel.Children.Add(group);
        }

        private void AddToolbarSeparator()
        {
            MdToolbarPanel.Children.Add(new Border
            {
                Width = 1,
                Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                Margin = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Stretch
            });
        }

        private static Button MakeToolBtn(string content, string tooltip, Action action, bool bold = false, bool italic = false)
        {
            var btn = new Button
            {
                Content = content,
                ToolTip = tooltip,
                MinWidth = 28,
                Height = 24,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(1, 0, 1, 0),
                FontSize = 11,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
                Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
                Cursor = Cursors.Hand
            };

            // Hover effect
            btn.MouseEnter += (_, _) => btn.Background = new SolidColorBrush(Color.FromRgb(69, 71, 90));
            btn.MouseLeave += (_, _) => btn.Background = new SolidColorBrush(Color.FromRgb(49, 50, 68));

            btn.Click += (_, _) => action();
            return btn;
        }

        // ── Operacje na tekście ──────────────────────────────────────────

        /// <summary>Owija zaznaczenie w prefix+suffix. Jeśli brak zaznaczenia — wstawia placeholder.</summary>
        private void WrapSelection(string prefix, string suffix, string placeholder = "text")
        {
            int selStart = EditContent.SelectionStart;
            int selLen = EditContent.SelectionLength;
            string selected = selLen > 0 ? EditContent.SelectedText : placeholder;

            string replacement = prefix + selected + suffix;
            int caretAfter = selStart + prefix.Length + selected.Length + suffix.Length;

            EditContent.Text = EditContent.Text[..selStart] + replacement + EditContent.Text[(selStart + selLen)..];

            // Zaznacz wstawiony tekst (bez prefix/suffix) jeśli był placeholder
            if (selLen == 0)
            {
                EditContent.SelectionStart = selStart + prefix.Length;
                EditContent.SelectionLength = placeholder.Length;
            }
            else
            {
                EditContent.CaretIndex = caretAfter;
            }

            EditContent.Focus();
        }

        /// <summary>Dodaje prefix na początku każdej zaznaczonej linii (lub bieżącej).</summary>
        private void InsertLinePrefix(string prefix)
        {
            int selStart = EditContent.SelectionStart;
            int selEnd = selStart + EditContent.SelectionLength;
            string text = EditContent.Text;

            // Znajdź początek pierwszej zaznaczonej linii
            int lineStart = selStart;
            while (lineStart > 0 && text[lineStart - 1] != '\n')
                lineStart--;

            // Znajdź koniec ostatniej zaznaczonej linii
            int lineEnd = selEnd;
            while (lineEnd < text.Length && text[lineEnd] != '\n')
                lineEnd++;

            string block = text[lineStart..lineEnd];
            var lines = block.Split('\n');
            var modified = lines.Select(l => prefix + l);
            string replacement = string.Join("\n", modified);

            EditContent.Text = text[..lineStart] + replacement + text[lineEnd..];
            EditContent.CaretIndex = lineStart + replacement.Length;
            EditContent.Focus();
        }

        /// <summary>Wstawia blok przed i po zaznaczeniu (np. code block).</summary>
        private void InsertBlock(string before, string after, string placeholder = "code")
        {
            int selStart = EditContent.SelectionStart;
            int selLen = EditContent.SelectionLength;
            string selected = selLen > 0 ? EditContent.SelectedText : placeholder;

            string replacement = before + selected + after;
            EditContent.Text = EditContent.Text[..selStart] + replacement + EditContent.Text[(selStart + selLen)..];

            if (selLen == 0)
            {
                EditContent.SelectionStart = selStart + before.Length;
                EditContent.SelectionLength = placeholder.Length;
            }
            else
            {
                EditContent.CaretIndex = selStart + replacement.Length;
            }

            EditContent.Focus();
        }

        /// <summary>Wstawia surowy tekst w miejscu karetki.</summary>
        private void InsertRaw(string text)
        {
            int caret = EditContent.SelectionStart;
            int selLen = EditContent.SelectionLength;
            EditContent.Text = EditContent.Text[..caret] + text + EditContent.Text[(caret + selLen)..];
            EditContent.CaretIndex = caret + text.Length;
            EditContent.Focus();
        }

        /// <summary>Wstawia link MD — jeśli zaznaczono tekst, używa go jako label.</summary>
        private void InsertLink()
        {
            int selStart = EditContent.SelectionStart;
            int selLen = EditContent.SelectionLength;
            string label = selLen > 0 ? EditContent.SelectedText : "link text";
            string snippet = $"[{label}](url)";

            EditContent.Text = EditContent.Text[..selStart] + snippet + EditContent.Text[(selStart + selLen)..];

            // Zaznacz "url" żeby użytkownik mógł od razu wpisać
            int urlStart = selStart + label.Length + 3; // [label](  ← 3 znaki
            EditContent.SelectionStart = urlStart;
            EditContent.SelectionLength = 3; // "url"
            EditContent.Focus();
        }

        /// <summary>Wstawia obrazek MD inline.</summary>
        private void InsertImageMd()
        {
            int caret = EditContent.SelectionStart;
            int selLen = EditContent.SelectionLength;
            string snippet = "![alt text](filename.png)";
            EditContent.Text = EditContent.Text[..caret] + snippet + EditContent.Text[(caret + selLen)..];

            // Zaznacz "filename.png"
            EditContent.SelectionStart = caret + 13; // ![alt text](  ← 13 znaków
            EditContent.SelectionLength = 12;         // "filename.png"
            EditContent.Focus();
        }

        /// <summary>Wstawia obrazek HTML z wyrównaniem do środka.</summary>
        private void InsertImageHtml()
        {
            int caret = EditContent.SelectionStart;
            int selLen = EditContent.SelectionLength;
            string snippet = "<p align=\"center\">\n  <img src=\"filename.png\" width=\"400\" alt=\"\">\n</p>";
            EditContent.Text = EditContent.Text[..caret] + "\n" + snippet + "\n" + EditContent.Text[(caret + selLen)..];

            // Zaznacz "filename.png"
            int fnStart = caret + 1 + 19 + 11; // \n + <p align="center">\n  <img src="  ← count
                                               // Prostsze — znajdź "filename.png" w wstawionym tekście
            int snippetStart = caret + 1;
            int fnIdx = snippet.IndexOf("filename.png", StringComparison.Ordinal);
            EditContent.SelectionStart = snippetStart + fnIdx;
            EditContent.SelectionLength = 12;
            EditContent.Focus();
        }

        /// <summary>Wstawia tabelę MD z podaną liczbą kolumn i wierszy.</summary>
        private void InsertTable(int cols, int rows)
        {
            int caret = EditContent.SelectionStart;
            int selLen = EditContent.SelectionLength;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine();

            // Header
            sb.Append('|');
            for (int c = 0; c < cols; c++)
                sb.Append($" Header {c + 1} |");
            sb.AppendLine();

            // Separator
            sb.Append('|');
            for (int c = 0; c < cols; c++)
                sb.Append(" --- |");
            sb.AppendLine();

            // Rows
            for (int r = 0; r < rows - 1; r++)
            {
                sb.Append('|');
                for (int c = 0; c < cols; c++)
                    sb.Append(" Cell |");
                sb.AppendLine();
            }

            string snippet = sb.ToString();
            EditContent.Text = EditContent.Text[..caret] + snippet + EditContent.Text[(caret + selLen)..];
            EditContent.CaretIndex = caret + snippet.Length;
            EditContent.Focus();
        }

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