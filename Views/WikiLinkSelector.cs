using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Apex.Models;

namespace Apex.Views;

/// <summary>
/// Popup selector for ambiguous [[wiki-links]].
/// Shows matching notes with fuzzy filtering, keyboard navigation.
/// </summary>
public class WikiLinkSelector : Window
{
    private readonly List<NoteCard> _allCandidates;
    private readonly TextBox _searchBox;
    private readonly ListBox _listBox;

    public NoteCard? SelectedCard { get; private set; }

    public WikiLinkSelector(string linkText, List<NoteCard> candidates)
    {
        _allCandidates = candidates;

        Title = $"Select target for [[{linkText}]]";
        Width = 420;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");

        var stack = new StackPanel { Margin = new Thickness(16) };

        var label = new TextBlock
        {
            Text = $"Multiple notes match \"[[{linkText}]]\". Select one:",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
            Margin = new Thickness(0, 0, 0, 10)
        };
        stack.Children.Add(label);

        _searchBox = new TextBox
        {
            FontSize = 13,
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        _searchBox.TextChanged += (_, _) => FilterList(_searchBox.Text);
        _searchBox.KeyDown += SearchBox_KeyDown;
        stack.Children.Add(_searchBox);

        _listBox = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            FontSize = 13,
            Height = 160
        };
        _listBox.MouseDoubleClick += (_, _) => ConfirmSelection();
        _listBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) ConfirmSelection();
            if (e.Key == Key.Escape) { DialogResult = false; }
        };
        stack.Children.Add(_listBox);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var okBtn = new Button
        {
            Content = "Select",
            Width = 80,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(29, 158, 117)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        okBtn.Click += (_, _) => ConfirmSelection();
        btnRow.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 30,
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; };
        btnRow.Children.Add(cancelBtn);
        stack.Children.Add(btnRow);

        Content = stack;
        FilterList("");

        Loaded += (_, _) =>
        {
            _searchBox.Focus();
            if (_listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;
        };
    }

    private void FilterList(string filter)
    {
        _listBox.Items.Clear();
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? _allCandidates
            : _allCandidates.Where(c =>
                c.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var card in filtered)
        {
            _listBox.Items.Add(new ListBoxItem
            {
                Content = card.RelativePath,
                Tag = card,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                Background = Brushes.Transparent
            });
        }

        if (_listBox.Items.Count > 0)
            _listBox.SelectedIndex = 0;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (_listBox.Items.Count > 0)
            {
                _listBox.SelectedIndex = Math.Min(
                    (_listBox.SelectedIndex < 0 ? 0 : _listBox.SelectedIndex + 1),
                    _listBox.Items.Count - 1);
                _listBox.Focus();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter) ConfirmSelection();
        else if (e.Key == Key.Escape) { DialogResult = false; }
    }

    private void ConfirmSelection()
    {
        if (_listBox.SelectedItem is ListBoxItem item && item.Tag is NoteCard card)
        {
            SelectedCard = card;
            DialogResult = true;
        }
    }
}