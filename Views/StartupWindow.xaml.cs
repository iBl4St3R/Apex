using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Apex.Services;
using Forms = System.Windows.Forms;

namespace Apex.Views;

/// <summary>
/// Interaction logic for StartupWindow.xaml.
/// Displays the startup screen with options to create, open, or reopen a project.
/// </summary>
public partial class StartupWindow : Window
{
    /// <summary>
    /// Raised when a project has been selected. Subscribers receive the .apex file path.
    /// </summary>
    public event Action<string>? ProjectSelected;

    public StartupWindow()
    {
        InitializeComponent();
        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        var recent = FileService.LoadRecentProjects();
        RecentProjectsList.ItemsSource = recent;
    }

    private void CreateNewButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select or create an empty folder for the new project:"
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            string folderPath = dialog.SelectedPath;

            // Check if folder already contains an .apex file
            if (FileService.FindApexFile(folderPath) != null)
            {
                System.Windows.MessageBox.Show(
                    "This folder already contains an Apex project. Please select a different folder.",
                    "Project Exists",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Ask for project name
            var nameDialog = new InputDialog("Project Name", "Enter a name for the project:")
            {
                Owner = this
            };
            if (nameDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(nameDialog.Answer))
            {
                try
                {
                    var project = FileService.CreateNewProject(folderPath, nameDialog.Answer.Trim());
                    string apexFilePath = FileService.GetApexFilePath(project.RootFolder);
                    OnProjectSelected(apexFilePath);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to create project:\n{ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
    }

    private void OpenExistingButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Forms.OpenFileDialog
        {
            Filter = "Apex Project (*.apex)|*.apex|All Files (*.*)|*.*",
            Title = "Open an Apex project (.apex file)"
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            string apexFilePath = dialog.FileName;

            // Validate that it's a valid project
            var project = FileService.LoadProject(apexFilePath);
            if (project == null)
            {
                System.Windows.MessageBox.Show(
                    "The selected file is not a valid Apex project file.",
                    "Invalid Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            OnProjectSelected(apexFilePath);
        }
    }

    private void RecentProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string apexFilePath)
        {
            // Validate the project still exists and can be loaded
            var project = FileService.LoadProject(apexFilePath);
            if (project == null)
            {
                System.Windows.MessageBox.Show(
                    "This project file could not be found or is no longer valid." +
                    "\nIt will be removed from the recent projects list.",
                    "Project Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                // Refresh the list to remove the stale entry
                LoadRecentProjects();
                return;
            }

            OnProjectSelected(apexFilePath);
        }
    }

    private void OnProjectSelected(string apexFilePath)
    {
        ProjectSelected?.Invoke(apexFilePath);
        // The App.xaml.cs event handler already sets DialogResult = true,
        // which closes this window as part of the ShowDialog() modal loop.
        // Only set it here if no handler was subscribed.
        if (DialogResult == null)
            DialogResult = true;
    }
}

/// <summary>
/// Simple input dialog for getting a single text value from the user.
/// </summary>
public class InputDialog : Window
{
    private readonly TextBox _inputBox;

    public string? Answer
    {
        get => _inputBox.Text;
        set => _inputBox.Text = value ?? "";
    }

    public InputDialog(string title, string prompt)
    {
        Title = title;
        Width = 400;
        Height = 165;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46));
        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
        FontFamily = new FontFamily("Segoe UI, sans-serif");

        var stack = new StackPanel { Margin = new Thickness(20) };

        var promptText = new TextBlock
        {
            Text = prompt,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 16),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244))
        };
        stack.Children.Add(promptText);

        _inputBox = new TextBox
        {
            FontSize = 14,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 16),
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1),
            CaretBrush = new SolidColorBrush(Color.FromRgb(205, 214, 244))
        };
        stack.Children.Add(_inputBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1)
        };
        okButton.Click += (s, e) => { DialogResult = true; };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 32,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            BorderThickness = new Thickness(1)
        };
        cancelButton.Click += (s, e) => { DialogResult = false; };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        stack.Children.Add(buttonPanel);

        Content = stack;

        // Allow Enter to submit
        _inputBox.KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                DialogResult = true;
            }
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                DialogResult = false;
            }
        };

        _inputBox.Focus();
        Loaded += (s, e) => _inputBox.Focus();
    }
}