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