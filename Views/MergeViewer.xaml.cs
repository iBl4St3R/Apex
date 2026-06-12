using System.IO;
using System.Text;
using System.Windows.Controls;
using Apex.Services;

namespace Apex.Views
{
    /// <summary>
    /// Displays the content of multiple .md files merged into a single
    /// continuous document. Read-only, no separators between files.
    /// </summary>
    public partial class MergeViewer : UserControl
    {
        public MergeViewer()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Loads the given .md files, concatenates their content, and renders them.
        /// <paramref name="fullPaths"/> are absolute file paths.
        /// </summary>
        public void LoadFiles(List<string> fullPaths)
        {
            var sb = new StringBuilder();
            foreach (string path in fullPaths)
            {
                if (!File.Exists(path)) continue;

                string content = File.ReadAllText(path);
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(content);
            }

            string merged = sb.ToString();
            ContentReader.Document = MarkdownRenderer.RenderToFlowDocument(merged);
        }
    }
}