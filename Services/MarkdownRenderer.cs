using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Markdig;

namespace Apex.Services;

/// <summary>
/// Static helper that renders Markdown text (with [[wiki-link]] support)
/// into a WPF FlowDocument for display in a FlowDocumentScrollViewer.
/// Shared by NoteViewer and MergeViewer.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex WikiLinkPattern = new(@"\[\[([^\]]+)\]\]", RegexOptions.Compiled);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseGridTables()
        .UseTaskLists()
        .UsePipeTables()
        .Build();

    /// <summary>
    /// Parses <paramref name="markdown"/> and returns a FlowDocument ready
    /// to be placed in a FlowDocumentScrollViewer.
    /// When the user clicks a [[wiki-link]], <paramref name="onLinkClicked"/>
    /// is invoked with the link target name.
    /// </summary>
    public static FlowDocument RenderToFlowDocument(string markdown, Action<string>? onLinkClicked = null)
    {
        try
        {
            // Preprocess [[wiki links]] into markdown links with a custom scheme
            string processed = WikiLinkPattern.Replace(markdown, m =>
            {
                string target = m.Groups[1].Value;
                string escaped = target.Replace("(", "%28").Replace(")", "%29");
                return $"[{target}](apex://link/{escaped})";
            });

            // Parse with Markdig
            var document = Markdown.Parse(processed, Pipeline);

            // Build FlowDocument
            var flowDoc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI, sans-serif"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
                LineHeight = 1.4,
                PagePadding = new Thickness(0),
            };

            foreach (var block in document)
            {
                var blockElement = RenderBlock(block, onLinkClicked);
                if (blockElement != null)
                    flowDoc.Blocks.Add(blockElement);
            }

            return flowDoc;
        }
        catch (Exception ex)
        {
            return new FlowDocument(
                new Paragraph(
                    new Run($"Rendering error: {ex.Message}")
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(226, 75, 74))
                    }));
        }
    }

    // ──────────────────────────────────────────────
    //  Block-level rendering
    // ──────────────────────────────────────────────

    private static Block? RenderBlock(Markdig.Syntax.MarkdownObject block, Action<string>? onLinkClicked)
    {
        return block switch
        {
            Markdig.Syntax.ParagraphBlock para => RenderParagraph(para, onLinkClicked),
            Markdig.Syntax.HeadingBlock heading => RenderHeading(heading, onLinkClicked),
            Markdig.Syntax.FencedCodeBlock code => RenderCodeBlock(code),
            Markdig.Syntax.CodeBlock indentedCode => RenderIndentedCodeBlock(indentedCode),
            Markdig.Syntax.ListBlock list => RenderListBlock(list, onLinkClicked),
            Markdig.Syntax.ThematicBreakBlock => RenderThematicBreak(),
            _ => null
        };
    }

    private static Paragraph RenderParagraph(Markdig.Syntax.ParagraphBlock para, Action<string>? onLinkClicked)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
        RenderInlines(para.Inline, paragraph.Inlines, onLinkClicked);
        return paragraph;
    }

    private static Paragraph RenderHeading(Markdig.Syntax.HeadingBlock heading, Action<string>? onLinkClicked)
    {
        double fontSize = heading.Level switch
        {
            1 => 28,
            2 => 22,
            3 => 18,
            _ => 16
        };

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, heading.Level == 1 ? 16 : 12, 0, 8),
            FontSize = fontSize,
            FontWeight = heading.Level <= 2 ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244))
        };

        RenderInlines(heading.Inline, paragraph.Inlines, onLinkClicked);
        return paragraph;
    }

    private static Paragraph RenderCodeBlock(Markdig.Syntax.FencedCodeBlock code)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(186, 194, 222)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            BorderThickness = new Thickness(1),
        };

        foreach (var slice in code.Lines.Lines)
        {
            string text = slice.ToString();
            if (text == null) continue;
            paragraph.Inlines.Add(new Run(text)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(186, 194, 222))
            });
            paragraph.Inlines.Add(new LineBreak());
        }

        return paragraph;
    }

    private static Paragraph RenderIndentedCodeBlock(Markdig.Syntax.CodeBlock code)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(186, 194, 222)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            BorderThickness = new Thickness(1),
        };

        foreach (var slice in code.Lines.Lines)
        {
            string text = slice.ToString();
            if (text == null) continue;
            paragraph.Inlines.Add(new Run(text));
            paragraph.Inlines.Add(new LineBreak());
        }

        return paragraph;
    }

    private static Section RenderListBlock(Markdig.Syntax.ListBlock list, Action<string>? onLinkClicked)
    {
        var section = new Section();
        bool isOrdered = list.IsOrdered;
        int index = list.OrderedStart != null ? int.Parse(list.OrderedStart) : 1;

        foreach (var item in list)
        {
            if (item is Markdig.Syntax.ListItemBlock listItem)
            {
                var para = new Paragraph
                {
                    Margin = new Thickness(16, 0, 0, 4),
                    TextIndent = 0,
                };

                string prefix = isOrdered ? $"{index}. " : "• ";
                para.Inlines.Add(new Run(prefix)
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200))
                });

                var listPara = listItem.OfType<Markdig.Syntax.ParagraphBlock>().FirstOrDefault();
                if (listPara != null)
                    RenderInlines(listPara.Inline, para.Inlines, onLinkClicked);
                section.Blocks.Add(para);
                index++;
            }
        }

        return section;
    }

    private static Block RenderThematicBreak()
    {
        return new Paragraph(new Run(new string('─', 80)))
        {
            Margin = new Thickness(0, 4, 0, 8),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
            TextAlignment = TextAlignment.Center
        };
    }

    // ──────────────────────────────────────────────
    //  Inline-level rendering
    // ──────────────────────────────────────────────

    private static void RenderInlines(Markdig.Syntax.Inlines.ContainerInline? inlineContainer, InlineCollection target, Action<string>? onLinkClicked)
    {
        if (inlineContainer == null) return;

        foreach (var inline in inlineContainer)
        {
            switch (inline)
            {
                case Markdig.Syntax.Inlines.LiteralInline literal:
                    target.Add(new Run(literal.ToString())
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244))
                    });
                    break;

                case Markdig.Syntax.Inlines.EmphasisInline emphasis:
                    RenderEmphasis(emphasis, target, onLinkClicked);
                    break;

                case Markdig.Syntax.Inlines.CodeInline codeInline:
                    target.Add(new Run(codeInline.Content)
                    {
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
                        Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168))
                    });
                    break;

                case Markdig.Syntax.Inlines.LinkInline link:
                    RenderLink(link, target, onLinkClicked);
                    break;

                case Markdig.Syntax.Inlines.LineBreakInline:
                    target.Add(new LineBreak());
                    break;
            }
        }
    }

    private static void RenderEmphasis(Markdig.Syntax.Inlines.EmphasisInline emphasis, InlineCollection target, Action<string>? onLinkClicked)
    {
        bool isBold = emphasis.DelimiterCount >= 2;
        bool isItalic = emphasis.DelimiterChar == '*' || emphasis.DelimiterChar == '_';
        bool isStrikethrough = emphasis.DelimiterChar == '~';

        var temp = new Span();
        RenderInlines(emphasis, temp.Inlines, onLinkClicked);

        foreach (var child in temp.Inlines)
        {
            if (child is Run run)
            {
                if (isBold) run.FontWeight = FontWeights.Bold;
                if (isItalic) run.FontStyle = FontStyles.Italic;
                if (isStrikethrough) run.TextDecorations = TextDecorations.Strikethrough;
                target.Add(run);
            }
            else
            {
                target.Add(child);
            }
        }
    }

    private static void RenderLink(Markdig.Syntax.Inlines.LinkInline link, InlineCollection target, Action<string>? onLinkClicked)
    {
        string? url = link.Url;

        if (url != null && url.StartsWith("apex://link/"))
        {
            // [[wiki-link]]
            string linkTarget = Uri.UnescapeDataString(url["apex://link/".Length..]);
            var hyperlink = new Hyperlink
            {
                Foreground = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand,
                Tag = linkTarget,
                ToolTip = $"Open \"{linkTarget}\""
            };
            hyperlink.Inlines.Add(new Run(linkTarget));
            if (onLinkClicked != null)
            {
                hyperlink.Click += (_, e) =>
                {
                    onLinkClicked(linkTarget);
                    e.Handled = true;
                };
            }
            target.Add(hyperlink);
        }
        else if (url != null)
        {
            // External link
            var hyperlink = new Hyperlink
            {
                Foreground = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
                NavigateUri = new Uri(url),
                ToolTip = url
            };
            hyperlink.Inlines.Add(new Run(url));
            hyperlink.RequestNavigate += (_, e) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.ToString(),
                    UseShellExecute = true
                });
            };
            target.Add(hyperlink);
        }
        else
        {
            var run = new Run(link.FirstChild?.ToString() ?? url ?? "")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(137, 180, 250))
            };
            target.Add(run);
        }
    }
}