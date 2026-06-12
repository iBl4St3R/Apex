using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Apex.Models;
using Apex.Services;

namespace Apex.Views
{
    /// <summary>
    /// Window for managing project categories (add, edit, delete).
    /// </summary>
    public partial class CategoryManagerWindow : Window
    {
        private readonly ApexProject _project;
        private Category? _editingCategory; // non-null when editing an existing category

        private static readonly string[] PresetColors =
        {
            "E24B4A", "EF9F27", "F9E2AF", "1D9E75",
            "179483", "378ADD", "89B4FA", "CBA6F7",
            "F38BA8", "F5C2E7", "CDD6F4", "BAC2DE",
            "A6ADC8", "6C7086", "585B70", "45475A"
        };

        private Border? _selectedColorSwatch;

        public CategoryManagerWindow(ApexProject project)
        {
            _project = project;
            InitializeComponent();
            Owner = Application.Current.MainWindow;

            BuildColorPicker();
            RefreshCategoryList();
        }

        // ──────────────────────────────────────────────
        //  Color picker
        // ──────────────────────────────────────────────

        private void BuildColorPicker()
        {
            ColorPicker.Children.Clear();

            foreach (string hex in PresetColors)
            {
                var swatch = new Border
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(4),
                    Background = ParseHexBrush(hex),
                    Cursor = Cursors.Hand,
                    Tag = hex,
                    ToolTip = $"#{hex}"
                };

                swatch.MouseLeftButtonDown += (_, _) => SelectColor(swatch, hex);

                ColorPicker.Children.Add(swatch);
            }

            // Select the first color by default
            if (ColorPicker.Children.Count > 0 && ColorPicker.Children[0] is Border first)
                SelectColor(first, PresetColors[0]);
        }

        private void SelectColor(Border swatch, string hex)
        {
            // Remove checkmark from previous selection
            if (_selectedColorSwatch != null)
            {
                _selectedColorSwatch.Child = null;
            }

            _selectedColorSwatch = swatch;
            HexColorBox.Text = hex;

            // Add checkmark overlay
            swatch.Child = new TextBlock
            {
                Text = "✓",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private string GetSelectedColor()
        {
            string hex = HexColorBox.Text.Trim();
            if (hex.Length == 6 && hex.All(c => Uri.IsHexDigit(c)))
                return "#" + hex.ToUpperInvariant();

            // Fallback to selected swatch
            if (_selectedColorSwatch?.Tag is string tagHex)
                return "#" + tagHex.ToUpperInvariant();

            return "#CDD6F4";
        }

        // ──────────────────────────────────────────────
        //  Category list
        // ──────────────────────────────────────────────

        private void RefreshCategoryList()
        {
            CategoryList.ItemsSource = null;
            CategoryList.ItemsSource = _project.Categories.Select(c => new CategoryViewModel
            {
                Name = c.Name,
                Color = ParseHexBrush(c.Color),
                Id = c.Id
            }).ToList();
        }

        private class CategoryViewModel
        {
            public string Name { get; set; } = "";
            public string Id { get; set; } = "";
            public Brush Color { get; set; } = Brushes.Gray;
        }

        // ──────────────────────────────────────────────
        //  Add / Save
        // ──────────────────────────────────────────────

        private void CategoryNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            AddSaveButton.IsEnabled = !string.IsNullOrWhiteSpace(CategoryNameBox.Text);
        }

        private void HexColorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Highlight matching swatch if hex matches a preset
            string text = HexColorBox.Text.Trim().ToUpperInvariant();
            foreach (var child in ColorPicker.Children)
            {
                if (child is Border swatch && swatch.Tag is string hex && hex == text)
                {
                    SelectColor(swatch, hex);
                    return;
                }
            }
        }

        private void AddSaveButton_Click(object sender, RoutedEventArgs e)
        {
            string name = CategoryNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            string color = GetSelectedColor();

            if (_editingCategory != null)
            {
                // Update existing category
                _editingCategory.Name = name;
                _editingCategory.Color = color;
                _editingCategory = null;
                AddSaveButton.Content = "Add";
                CancelEditButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Add new category
                string id = Guid.NewGuid().ToString("N")[..8];
                _project.Categories.Add(new Category(id, name, color));
            }

            CategoryNameBox.Clear();
            HexColorBox.Text = PresetColors[0];
            if (ColorPicker.Children.Count > 0 && ColorPicker.Children[0] is Border first)
                SelectColor(first, PresetColors[0]);

            RefreshCategoryList();
            FileService.SaveProject(_project);
        }

        // ──────────────────────────────────────────────
        //  Edit
        // ──────────────────────────────────────────────

        private void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is CategoryViewModel vm)
            {
                var category = _project.Categories.FirstOrDefault(c => c.Id == vm.Id);
                if (category == null) return;

                _editingCategory = category;
                CategoryNameBox.Text = category.Name;

                // Strip # for display
                string hex = category.Color.TrimStart('#');
                HexColorBox.Text = hex;

                // Select matching swatch
                string upperHex = hex.ToUpperInvariant();
                foreach (var child in ColorPicker.Children)
                {
                    if (child is Border swatch && swatch.Tag is string sh && sh == upperHex)
                    {
                        SelectColor(swatch, hex);
                        break;
                    }
                }

                AddSaveButton.Content = "Save";
                CancelEditButton.Visibility = Visibility.Visible;
                CategoryNameBox.Focus();
            }
        }

        private void CancelEditButton_Click(object sender, RoutedEventArgs e)
        {
            CancelEdit();
        }

        private void CancelEdit()
        {
            _editingCategory = null;
            AddSaveButton.Content = "Add";
            CancelEditButton.Visibility = Visibility.Collapsed;
            CategoryNameBox.Clear();
            HexColorBox.Text = PresetColors[0];
            if (ColorPicker.Children.Count > 0 && ColorPicker.Children[0] is Border first)
                SelectColor(first, PresetColors[0]);
        }

        // ──────────────────────────────────────────────
        //  Delete
        // ──────────────────────────────────────────────

        private void DeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is CategoryViewModel vm)
            {
                var category = _project.Categories.FirstOrDefault(c => c.Id == vm.Id);
                if (category == null) return;

                // Check if any card uses this category
                var cardsInUse = _project.Cards.Where(c => c.CategoryId == category.Id).ToList();
                if (cardsInUse.Count > 0)
                {
                    var result = System.Windows.MessageBox.Show(
                        $"{cardsInUse.Count} note(s) use this category.\n" +
                        "Delete anyway? Their category will be removed.",
                        "Category In Use",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                        return;

                    // Remove category from all cards
                    foreach (var card in cardsInUse)
                        card.CategoryId = null;
                }

                _project.Categories.Remove(category);

                // If we were editing this category, cancel edit
                if (_editingCategory?.Id == category.Id)
                    CancelEdit();

                RefreshCategoryList();
                FileService.SaveProject(_project);
            }
        }

        // ──────────────────────────────────────────────
        //  Helper
        // ──────────────────────────────────────────────

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