
---

## 1. `NoteTemplate.cs` — nowy plik w `Models/`

```csharp
using System.Text.Json.Serialization;

namespace Apex.Models;

/// <summary>
/// Metadata for a note template stored in .templates/ folder.
/// The actual content lives in the .md file; this is the sidecar JSON.
/// </summary>
public class NoteTemplate
{
    [JsonPropertyName("templateName")]
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Relative path of the .md file inside .templates/</summary>
    [JsonPropertyName("mdFileName")]
    public string MdFileName { get; set; } = string.Empty;

    /// <summary>Category ID to auto-assign to notes created from this template.</summary>
    [JsonPropertyName("defaultCategoryId")]
    public string? DefaultCategoryId { get; set; }

    /// <summary>
    /// Project-relative folder where new notes will be placed.
    /// Empty = root folder. If folder doesn't exist, root is used.
    /// </summary>
    [JsonPropertyName("defaultFolder")]
    public string DefaultFolder { get; set; } = string.Empty;

    public NoteTemplate() { }

    public NoteTemplate(string templateName, string mdFileName)
    {
        TemplateName = templateName;
        MdFileName = mdFileName;
    }
}
```

---

## 2. `TemplateService.cs` — nowy plik w `Services/`

```csharp
using System.IO;
using System.Text.Json;
using Apex.Models;

namespace Apex.Services;

/// <summary>
/// Manages note templates stored in {rootFolder}/.templates/
/// Each template = one .md file + one .json sidecar (same base name).
/// </summary>
public static class TemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string GetTemplatesFolder(string rootFolder) =>
        Path.Combine(rootFolder, ".templates");

    private static string GetSidecarPath(string rootFolder, string mdFileName) =>
        Path.Combine(GetTemplatesFolder(rootFolder),
            Path.GetFileNameWithoutExtension(mdFileName) + ".json");

    // ──────────────────────────────────────────────
    //  Load all templates
    // ──────────────────────────────────────────────

    public static List<NoteTemplate> LoadAll(string rootFolder)
    {
        string folder = GetTemplatesFolder(rootFolder);
        if (!Directory.Exists(folder)) return new();

        var result = new List<NoteTemplate>();
        foreach (string mdPath in Directory.EnumerateFiles(folder, "*.md")
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(mdPath);
            var template = LoadMeta(rootFolder, fileName)
                           ?? new NoteTemplate(
                               Path.GetFileNameWithoutExtension(fileName),
                               fileName);
            result.Add(template);
        }
        return result;
    }

    // ──────────────────────────────────────────────
    //  Load / Save metadata sidecar
    // ──────────────────────────────────────────────

    public static NoteTemplate? LoadMeta(string rootFolder, string mdFileName)
    {
        string path = GetSidecarPath(rootFolder, mdFileName);
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<NoteTemplate>(json, JsonOptions);
        }
        catch { return null; }
    }

    public static void SaveMeta(string rootFolder, NoteTemplate template)
    {
        EnsureFolder(rootFolder);
        string path = GetSidecarPath(rootFolder, template.MdFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(template, JsonOptions));
    }

    // ──────────────────────────────────────────────
    //  Create template from existing note
    // ──────────────────────────────────────────────

    /// <summary>
    /// Copies a note's .md content into .templates/ and creates an empty sidecar.
    /// Returns the new NoteTemplate, or null on failure.
    /// </summary>
    public static NoteTemplate? CreateFromNote(
        string rootFolder,
        string sourceFullPath,
        string templateName)
    {
        EnsureFolder(rootFolder);
        string folder = GetTemplatesFolder(rootFolder);

        // Sanitize file name
        string safe = string.Concat(templateName.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(safe)) safe = "template";

        string mdFileName = safe + ".md";
        string destPath = Path.Combine(folder, mdFileName);

        // Avoid overwriting — append number
        int n = 1;
        while (File.Exists(destPath))
        {
            mdFileName = safe + n + ".md";
            destPath = Path.Combine(folder, mdFileName);
            n++;
        }

        File.Copy(sourceFullPath, destPath);

        var template = new NoteTemplate(templateName, mdFileName);
        SaveMeta(rootFolder, template);
        return template;
    }

    // ──────────────────────────────────────────────
    //  Get full path of template .md
    // ──────────────────────────────────────────────

    public static string GetMdFullPath(string rootFolder, string mdFileName) =>
        Path.Combine(GetTemplatesFolder(rootFolder), mdFileName);

    // ──────────────────────────────────────────────
    //  Read content
    // ──────────────────────────────────────────────

    public static string ReadContent(string rootFolder, NoteTemplate template)
    {
        string path = GetMdFullPath(rootFolder, template.MdFileName);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    // ──────────────────────────────────────────────
    //  Resolve target folder
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns the absolute folder path where a note from this template should land.
    /// Falls back to rootFolder if the configured folder doesn't exist.
    /// </summary>
    public static string ResolveTargetFolder(string rootFolder, NoteTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.DefaultFolder))
            return rootFolder;

        string full = Path.GetFullPath(
            Path.Combine(rootFolder, template.DefaultFolder.Replace('/', Path.DirectorySeparatorChar)));

        return Directory.Exists(full) ? full : rootFolder;
    }

    // ──────────────────────────────────────────────
    //  Delete template
    // ──────────────────────────────────────────────

    public static void Delete(string rootFolder, NoteTemplate template)
    {
        string mdPath = GetMdFullPath(rootFolder, template.MdFileName);
        string sidecar = GetSidecarPath(rootFolder, template.MdFileName);
        if (File.Exists(mdPath)) File.Delete(mdPath);
        if (File.Exists(sidecar)) File.Delete(sidecar);
    }

    // ──────────────────────────────────────────────
    //  Helper
    // ──────────────────────────────────────────────

    private static void EnsureFolder(string rootFolder)
    {
        string folder = GetTemplatesFolder(rootFolder);
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
    }
}
```

---

## 3. `NewNoteDialog.cs` — nowy plik w `Views/`

Zastępuje stary `InputDialog` przy tworzeniu karty. Zawiera pole tytułu + przyciski templatek.

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Apex.Models;
using Apex.Services;

namespace Apex.Views;

/// <summary>
/// Dialog shown when creating a new note card on the board.
/// Lets the user pick a title and optionally a template.
/// </summary>
public class NewNoteDialog : Window
{
    private readonly TextBox _titleBox;
    private NoteTemplate? _selectedTemplate;

    public string NoteTitle => _titleBox.Text.Trim();
    public NoteTemplate? SelectedTemplate => _selectedTemplate;

    public NewNoteDialog(string rootFolder, List<NoteTemplate> templates)
    {
        Title = "New Note";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(20) };

        // Title label
        root.Children.Add(new TextBlock
        {
            Text = "Note title",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Title input
        _titleBox = new TextBox
        {
            FontSize = 14,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 0, 16),
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Color.FromRgb(205, 214, 244))
        };
        _titleBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) TryApply();
            if (e.Key == System.Windows.Input.Key.Escape) DialogResult = false;
        };
        root.Children.Add(_titleBox);

        // Template section label
        root.Children.Add(new TextBlock
        {
            Text = "TEMPLATE",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Template buttons wrap panel
        var wrap = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 20),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // "Blank" button — always first, selected by default
        var blankBtn = MakeTemplateButton("Blank", null);
        SetTemplateButtonActive(blankBtn, true);
        wrap.Children.Add(blankBtn);

        // One button per template
        foreach (var t in templates)
        {
            var btn = MakeTemplateButton(t.TemplateName, t);
            wrap.Children.Add(btn);
        }

        root.Children.Add(wrap);

        // Bottom buttons
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelBtn = MakeActionButton("Cancel",
            Color.FromRgb(49, 50, 68),
            new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            hasBorder: true);
        cancelBtn.Click += (_, _) => { DialogResult = false; };

        var applyBtn = MakeActionButton("Apply",
            Color.FromRgb(29, 158, 117),
            System.Windows.Media.Brushes.White);
        applyBtn.Click += (_, _) => TryApply();

        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(applyBtn);
        root.Children.Add(btnRow);

        Content = root;

        Loaded += (_, _) =>
        {
            _titleBox.Focus();
        };

        // store wrap for toggle logic
        _wrap = wrap;
    }

    // ── Template button helpers ──

    private readonly WrapPanel _wrap;
    private Button? _activeTemplateButton;

    private Button MakeTemplateButton(string label, NoteTemplate? template)
    {
        var btn = new Button
        {
            Content = label,
            Tag = template,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(12, 5, 12, 5),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1)
        };

        btn.Click += (_, _) =>
        {
            // Deactivate old
            if (_activeTemplateButton != null)
                SetTemplateButtonActive(_activeTemplateButton, false);

            _selectedTemplate = template;
            _activeTemplateButton = btn;
            SetTemplateButtonActive(btn, true);
        };

        // Track the first (Blank) button as default active
        if (template == null)
            _activeTemplateButton = btn;

        return btn;
    }

    private static void SetTemplateButtonActive(Button btn, bool active)
    {
        btn.Background = new SolidColorBrush(active
            ? Color.FromRgb(29, 158, 117)
            : Color.FromRgb(49, 50, 68));
        btn.Foreground = active
            ? System.Windows.Media.Brushes.White
            : new SolidColorBrush(Color.FromRgb(205, 214, 244));
        btn.BorderBrush = active
            ? new SolidColorBrush(Color.FromRgb(29, 158, 117))
            : new SolidColorBrush(Color.FromRgb(69, 71, 90));
    }

    private static Button MakeActionButton(string content, Color bg, System.Windows.Media.Brush fg,
        bool hasBorder = false) => new()
    {
        Content = content,
        Width = 80,
        Height = 32,
        Margin = new Thickness(6, 0, 0, 0),
        Background = new SolidColorBrush(bg),
        Foreground = fg,
        BorderThickness = hasBorder ? new Thickness(1) : new Thickness(0),
        BorderBrush = hasBorder
            ? new SolidColorBrush(Color.FromRgb(69, 71, 90))
            : System.Windows.Media.Brushes.Transparent,
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private void TryApply()
    {
        if (string.IsNullOrWhiteSpace(_titleBox.Text))
        {
            _titleBox.Focus();
            return;
        }
        DialogResult = true;
    }
}
```

---

## 4. `NoteViewer.xaml` — zmodyfikowany

Dodaj pasek na dole (widoczny tylko gdy plik jest w `.templates/`). Wstaw przed zamknięciem `</Grid>`:

```xml
<!-- ════════════════════════════════════════
     TEMPLATE METADATA BAR (visible only for .templates/ files)
     ════════════════════════════════════════ -->
<Border x:Name="TemplateBar"
        Grid.Row="2"
        Background="#11111B"
        BorderBrush="#313244"
        BorderThickness="0,1,0,0"
        Padding="12,8"
        VerticalAlignment="Bottom"
        Visibility="Collapsed">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="16"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>

        <!-- Category -->
        <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
            <TextBlock Text="AUTO-CATEGORY"
                       FontSize="10" FontWeight="SemiBold"
                       Foreground="#6C7086"
                       VerticalAlignment="Center"
                       Margin="0,0,8,0"/>
            <ComboBox x:Name="TemplateCategory"
                      Width="140"
                      FontSize="12"
                      Background="#313244"
                      Foreground="#CDD6F4"
                      BorderBrush="#45475A"
                      SelectionChanged="TemplateCategory_SelectionChanged"/>
        </StackPanel>

        <!-- Target folder -->
        <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
            <TextBlock Text="DEFAULT FOLDER"
                       FontSize="10" FontWeight="SemiBold"
                       Foreground="#6C7086"
                       VerticalAlignment="Center"
                       Margin="0,0,8,0"/>
            <TextBox x:Name="TemplateFolderBox"
                     Width="180"
                     FontSize="12"
                     Padding="6,3"
                     Background="#313244"
                     Foreground="#CDD6F4"
                     BorderBrush="#45475A"
                     BorderThickness="1"
                     CaretBrush="#CDD6F4"
                     ToolTip="Project-relative folder path, e.g. Tasks/Daily"
                     TextChanged="TemplateFolderBox_TextChanged"/>
        </StackPanel>

        <!-- Placeholder for future MD editor toolbar -->
        <TextBlock Grid.Column="3"
                   Text="Template options"
                   FontSize="10"
                   Foreground="#45475A"
                   VerticalAlignment="Center"
                   HorizontalAlignment="Right"
                   Margin="0,0,8,0"/>
    </Grid>
</Border>
```

Ale `TemplateBar` musi być w Row="2" razem z `ReadContent` i `EditContent` — więc zmień Grid na trzy wiersze i przesuń content row na `*`. Aktualna `Grid.Row="2"` to content — dodajemy Row 3 dla template bara:

W `NoteViewer.xaml` w sekcji `Grid.RowDefinitions` dodaj czwarty wiersz:

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />  <!-- banner -->
    <RowDefinition Height="Auto" />  <!-- toolbar -->
    <RowDefinition Height="*" />     <!-- content -->
    <RowDefinition Height="Auto" />  <!-- template bar -->
</Grid.RowDefinitions>
```

I zmień `TemplateBar` na `Grid.Row="3"`.

---

## 5. `NoteViewer.xaml.cs` — zmodyfikowany (pełny plik)

```csharp
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
```

---

## 6. `BoardView.xaml.cs` — tylko zmienione metody

### 6a. Zamień `CreateNewNoteAt` na nową wersję z `NewNoteDialog`

```csharp
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

    // Sanitize
    foreach (char c in Path.GetInvalidFileNameChars())
        title = title.Replace(c.ToString(), "");

    if (string.IsNullOrWhiteSpace(title))
    {
        System.Windows.MessageBox.Show(
            "Title contains invalid characters.",
            "Invalid Title", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    // Determine target folder from template (or root)
    string targetFolder = Project.RootFolder;
    string? autoCategory = null;

    if (dialog.SelectedTemplate != null)
    {
        targetFolder = TemplateService.ResolveTargetFolder(
            Project.RootFolder, dialog.SelectedTemplate);
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
        // Ensure subfolder exists
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Write content — blank or from template
        string content;
        if (dialog.SelectedTemplate != null)
            content = TemplateService.ReadContent(Project.RootFolder, dialog.SelectedTemplate);
        else
            content = $"# {title}\n\n";

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

        // Navigate straight to edit mode in Structure
        PreviewRequested?.Invoke(card);
        // Small delay so the structure view has time to load
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
```

### 6b. Dodaj "Copy as template" do `BuildCardContextMenu`

Wstaw przed `deleteItem` w `BuildCardContextMenu`:

```csharp
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
```

---

## 7. `StructureView.xaml.cs` — zmiany

### 7a. Usuń "New note here" z menu kontekstowego

W `StructureView.xaml` usuń lub ukryj `CtxNewNoteHere` — najprościej ustaw `Visibility="Collapsed"` na elemencie w XAML:

```xml
<MenuItem x:Name="CtxNewNoteHere" Header="New note here"
          Click="CtxNewNoteHere_Click"
          Visibility="Collapsed"/>
```

### 7b. Dodaj "Copy as template" do menu kontekstowego struktury

W `StructureView.xaml` w sekcji `ContextMenu` dodaj nową pozycję (np. po `CtxFindOnBoard`):

```xml
<MenuItem x:Name="CtxCopyAsTemplate" Header="Copy as template"
          Click="CtxCopyAsTemplate_Click"/>
```

Ukryj ją dla folderów — w triggerach:

```xml
<DataTrigger Binding="{Binding IsFolder}" Value="True">
    <Setter TargetName="CtxCopyAsTemplate" Property="Visibility" Value="Collapsed"/>
</DataTrigger>
```

W `StructureView.xaml.cs` dodaj handler:

```csharp
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
```

---


