using System.IO;
using System.Windows;
using Apex.Services;
using Apex.Views;

namespace Apex
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Check if launched with an .apex file path (e.g., from file association)
            if (e.Args.Length > 0)
            {
                string arg = e.Args[0].Trim('"');
                if (arg.EndsWith(".apex", StringComparison.OrdinalIgnoreCase) && File.Exists(arg))
                {
                    OpenProject(arg);
                    return;
                }
            }

            // No valid command-line argument — show the startup screen
            ShowStartupWindow();
        }

        private void ShowStartupWindow()
        {
            var startupWindow = new StartupWindow();

            startupWindow.ProjectSelected += apexFilePath =>
            {
                startupWindow.DialogResult = true;
                OpenProject(apexFilePath);
            };

            startupWindow.ShowDialog();
        }

        private void OpenProject(string apexFilePath)
        {
            var project = FileService.LoadProject(apexFilePath);
            if (project == null)
            {
                System.Windows.MessageBox.Show(
                    "Could not load the Apex project file.\n" + apexFilePath,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow(project);
            mainWindow.Show();
        }
    }
}