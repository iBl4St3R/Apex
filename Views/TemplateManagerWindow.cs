using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Apex.Models;
using Apex.Services;

namespace Apex.Views;

/// <summary>
/// Small window listing all templates for the current project.
/// Allows creating a new blank template or deleting existing ones.
/// </summary>
public class TemplateManagerWindow : Window
{
    private readonly ApexProject _project;
    private readonly ListBox _list;

    /// <summary>
    /// Raised when user wants to open/edit a template in the main NoteViewer.
    /// Argument is the full path of the template .md file.
    /// </summary>
    public event Action<string>? OpenTemplateRequested;

    public TemplateManagerWindow(ApexProject project)
    {
        _project = project;

        Title = "Templates";
        Width = 380;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // list
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // buttons

        // Header
        var header = new TextBlock
        {
            Text = "PROJECT TEMPLATES",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // List
        _list = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _list.MouseDoubleClick += (_, _) => OpenSelected();
        Grid.SetRow(_list, 1);
        root.Children.Add(_list);

        // Bottom buttons
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var newBtn = MakeButton("New template",
            Color.FromRgb(29, 158, 117), System.Windows.Media.Brushes.White);
        newBtn.Click += (_, _) => CreateBlankTemplate();

        var openBtn = MakeButton("Open",
            Color.FromRgb(55, 138, 221), System.Windows.Media.Brushes.White);
        openBtn.Click += (_, _) => OpenSelected();

        var deleteBtn = MakeButton("Delete",
            Color.FromRgb(226, 75, 74), System.Windows.Media.Brushes.White);
        deleteBtn.Click += (_, _) => DeleteSelected();

        btnRow.Children.Add(newBtn);
        btnRow.Children.Add(openBtn);
        btnRow.Children.Add(deleteBtn);
        Grid.SetRow(btnRow, 2);
        root.Children.Add(btnRow);

        Content = root;
        Refresh();
    }

    // ──────────────────────────────────────────────
    //  List
    // ──────────────────────────────────────────────

    private void Refresh()
    {
        _list.Items.Clear();
        var templates = TemplateService.LoadAll(_project.RootFolder);
        foreach (var t in templates)
        {
            var item = new ListBoxItem
            {
                Tag = t,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                Padding = new Thickness(10, 6, 10, 6)
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = t.TemplateName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            });

            // Sub-line: category + folder info
            string catName = "(no category)";
            if (!string.IsNullOrEmpty(t.DefaultCategoryId))
            {
                var cat = _project.Categories.FirstOrDefault(c =>
                    string.Equals(c.Id, t.DefaultCategoryId, StringComparison.OrdinalIgnoreCase));
                if (cat != null) catName = cat.Name;
            }

            string folderLabel = string.IsNullOrWhiteSpace(t.DefaultFolder)
                ? "root folder"
                : t.DefaultFolder;

            panel.Children.Add(new TextBlock
            {
                Text = $"{catName}  ·  {folderLabel}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                Margin = new Thickness(0, 2, 0, 0)
            });

            item.Content = panel;
            _list.Items.Add(item);
        }
    }

    // ──────────────────────────────────────────────
    //  Actions
    // ──────────────────────────────────────────────

    private void OpenSelected()
    {
        if (_list.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not NoteTemplate template) return;

        string fullPath = TemplateService.GetMdFullPath(
            _project.RootFolder, template.MdFileName);

        OpenTemplateRequested?.Invoke(fullPath);
        Close();
    }

    private void CreateBlankTemplate()
    {
        var dialog = new InputDialog("New Template", "Template name:");
        dialog.Owner = this;
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Answer))
            return;

        string name = dialog.Answer.Trim();
        string safe = string.Concat(name.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        TemplateService.EnsureFolder(_project.RootFolder);

        string folder = TemplateService.GetTemplatesFolder(_project.RootFolder);
        string mdFileName = safe + ".md";
        string destPath = Path.Combine(folder, mdFileName);

        int n = 1;
        while (File.Exists(destPath))
        {
            mdFileName = safe + n + ".md";
            destPath = Path.Combine(folder, mdFileName);
            n++;
        }

        File.WriteAllText(destPath, $"# {name}\n\n");

        var template = new NoteTemplate(name, mdFileName);
        TemplateService.SaveMeta(_project.RootFolder, template);

        Refresh();

        // Auto-open the new template for editing
        OpenTemplateRequested?.Invoke(destPath);
        Close();
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not NoteTemplate template) return;

        var result = MessageBox.Show(
            $"Delete template \"{template.TemplateName}\"?",
            "Delete Template",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        TemplateService.Delete(_project.RootFolder, template);
        Refresh();
    }

    // ──────────────────────────────────────────────
    //  Helper
    // ──────────────────────────────────────────────

    private static Button MakeButton(string content, Color bg, System.Windows.Media.Brush fg) => new()
    {
        Content = content,
        Height = 32,
        Padding = new Thickness(14, 0, 14, 0),
        Margin = new Thickness(6, 0, 0, 0),
        Background = new SolidColorBrush(bg),
        Foreground = fg,
        BorderThickness = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand
    };
}