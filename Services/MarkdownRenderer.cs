using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Markdig;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Apex.Services;

public static class MarkdownRenderer
{
    private static readonly Regex WikiLinkPattern =
        new(@"\[\[([^\]]+)\]\]", RegexOptions.Compiled);

    private static readonly Regex MarkerPattern =
        new(@"apex_link§([^§]+)§", RegexOptions.Compiled);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseGridTables()
        .UseTaskLists()
        .UsePipeTables()
        .Build();

    public static FlowDocument RenderToFlowDocument(
    string markdown,
    Action<string>? onLinkClicked = null,
    Func<string, bool>? linkExists = null,
    string? noteFolder = null)  // ← dodaj
    {
        try
        {
            string preprocessed = WikiLinkPattern.Replace(markdown, m =>
                $"apex_link§{m.Groups[1].Value}§");

            var document = Markdown.Parse(preprocessed, Pipeline);

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
                var blockElement = RenderBlock(block, onLinkClicked, linkExists, noteFolder);
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

    // ── Block rendering ───────────────────────────────────────────────────────

    private static Block? RenderBlock(
    Markdig.Syntax.MarkdownObject block,
    Action<string>? onLinkClicked,
    Func<string, bool>? linkExists,
    string? noteFolder = null)
    {
        return block switch
        {
            Markdig.Syntax.ParagraphBlock para => RenderParagraph(para, onLinkClicked, linkExists, noteFolder),
            Markdig.Syntax.HeadingBlock heading => RenderHeading(heading, onLinkClicked, linkExists, noteFolder),
            Markdig.Syntax.FencedCodeBlock code => RenderCodeBlock(code),
            Markdig.Syntax.CodeBlock indented => RenderIndentedCodeBlock(indented),
            Markdig.Syntax.ListBlock list => RenderListBlock(list, onLinkClicked, linkExists, noteFolder),
            Markdig.Syntax.ThematicBreakBlock => RenderThematicBreak(),
            Markdig.Syntax.HtmlBlock html => RenderHtmlFallback(html, noteFolder),
            _ => null
        };
    }

    private static Paragraph RenderParagraph(
    Markdig.Syntax.ParagraphBlock para,
    Action<string>? onLinkClicked,
    Func<string, bool>? linkExists,
    string? noteFolder = null)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
        RenderInlines(para.Inline, paragraph.Inlines, onLinkClicked, linkExists, noteFolder);
        return paragraph;
    }

    private static Paragraph RenderHeading(
    Markdig.Syntax.HeadingBlock heading,
    Action<string>? onLinkClicked,
    Func<string, bool>? linkExists,
    string? noteFolder = null)
    {
        double fontSize = heading.Level switch { 1 => 28, 2 => 22, 3 => 18, _ => 16 };
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, heading.Level == 1 ? 16 : 12, 0, 8),
            FontSize = fontSize,
            FontWeight = heading.Level <= 2 ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244))
        };
        RenderInlines(heading.Inline, paragraph.Inlines, onLinkClicked, linkExists, noteFolder);
        return paragraph;
    }

    private static Block RenderHtmlFallback(Markdig.Syntax.HtmlBlock html, string? noteFolder = null)
    {
        string rawHtml = string.Join("\n",
            html.Lines.Lines.Select(l => l.ToString()).Where(l => l != null));

        var htmlTagRegex = new Regex(@"<[^>]+>", RegexOptions.IgnoreCase);
        var imgRegex = new Regex(@"<img[^>]+src\s*=\s*[""']([^""']+)[""'][^>]*>", RegexOptions.IgnoreCase);

        var section = new Section();

        bool pendingCenter = false;

        foreach (string rawLine in rawHtml.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');

            var imgMatch = imgRegex.Match(line);
            if (imgMatch.Success)
            {
                var para = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
                AddImageInline(para.Inlines, imgMatch.Groups[1].Value, noteFolder);
                section.Blocks.Add(para);
                pendingCenter = false;
            }
            else
            {
                // Sprawdź czy ta linia zawiera tag z align="center"
                if (htmlTagRegex.IsMatch(line))
                {
                    pendingCenter = Regex.IsMatch(line,
                        @"align\s*=\s*[""']center[""']",
                        RegexOptions.IgnoreCase);
                }

                string cleaned = htmlTagRegex.Replace(line, "").Trim();
                if (!string.IsNullOrEmpty(cleaned))
                {
                    var para = new Paragraph
                    {
                        Margin = new Thickness(0, 0, 0, 4),
                        TextAlignment = pendingCenter ? TextAlignment.Center : TextAlignment.Left
                    };
                    para.Inlines.Add(new Run(cleaned)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244))
                    });
                    section.Blocks.Add(para);
                    pendingCenter = false; // reset po użyciu
                }
            }
        }

        // Jeśli section jest pusty — zwróć pusty paragraf
        if (!section.Blocks.Any())
            return new Paragraph();

        return section;
    }

    private static void AddImageInline(InlineCollection target, string src, string? noteFolder)
    {
        try
        {
            Uri uri;
            if (Uri.IsWellFormedUriString(src, UriKind.Absolute))
            {
                // http:// lub absolutna ścieżka
                uri = new Uri(src, UriKind.Absolute);
            }
            else if (noteFolder != null)
            {
                // Ścieżka względna — rozwiąż względem folderu notatki
                string fullPath = Path.GetFullPath(Path.Combine(noteFolder, src));
                if (!File.Exists(fullPath))
                {
                    target.Add(new Run($"[Image not found: {src}]")
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
                        FontStyle = FontStyles.Italic
                    });
                    return;
                }
                uri = new Uri(fullPath, UriKind.Absolute);
            }
            else return;

            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            var image = new System.Windows.Controls.Image
            {
                Source = bitmap,
                MaxWidth = 600,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(0, 4, 0, 4)
            };

            target.Add(new InlineUIContainer(image));
        }
        catch
        {
            target.Add(new Run($"[Image: {src}]")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
                FontStyle = FontStyles.Italic
            });
        }
    }

    private static Paragraph RenderCodeBlock(Markdig.Syntax.FencedCodeBlock code)
    {
        var lines = code.Lines.Lines.Select(l => l.ToString()).Where(l => l != null).ToList();
        string codeText = string.Join("\n", lines).TrimEnd('\n');
        string language = code.Info?.Trim().ToLowerInvariant() ?? "";

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            BorderBrush = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            BorderThickness = new Thickness(1),
        };

        // Label języka w prawym górnym rogu — przez InlineUIContainer
        if (!string.IsNullOrEmpty(language))
        {
            var langLabel = new TextBlock
            {
                Text = language,
                FontSize = 10,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var labelContainer = new InlineUIContainer(langLabel);
            paragraph.Inlines.Add(labelContainer);
            paragraph.Inlines.Add(new LineBreak());
        }

        // Syntax highlighting
        var highlightedRuns = GetHighlightedRuns(codeText, language);
        foreach (var inline in highlightedRuns)
            paragraph.Inlines.Add(inline);

        return paragraph;
    }

    private static Paragraph RenderIndentedCodeBlock(Markdig.Syntax.CodeBlock code)
    {
        var lines = code.Lines.Lines.Select(l => l.ToString()).Where(l => l != null).ToList();
        string codeText = string.Join("\n", lines).TrimEnd('\n');

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            BorderBrush = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            BorderThickness = new Thickness(1),
        };

        foreach (var inline in GetHighlightedRuns(codeText, ""))
            paragraph.Inlines.Add(inline);

        return paragraph;
    }

    private static List<Inline> GetHighlightedRuns(string code, string language)
    {
        var result = new List<Inline>();

        // Spróbuj znaleźć definicję highlighting dla danego języka
        IHighlightingDefinition? highlighting = null;
        if (!string.IsNullOrEmpty(language))
        {
            // AvalonEdit mapuje nazwy — próbuj kilka wariantów
            highlighting = language switch
            {
                "c#" or "csharp" or "cs" => HighlightingManager.Instance.GetDefinition("C#"),
                "c++" or "cpp" => HighlightingManager.Instance.GetDefinition("C++"),
                "c" => HighlightingManager.Instance.GetDefinition("C++"), // fallback
                "java" => HighlightingManager.Instance.GetDefinition("Java"),
                "js" or "javascript" => HighlightingManager.Instance.GetDefinition("JavaScript"),
                "ts" or "typescript" => HighlightingManager.Instance.GetDefinition("JavaScript"),
                "python" or "py" => HighlightingManager.Instance.GetDefinition("Python"),
                "xml" or "xaml" or "html" => HighlightingManager.Instance.GetDefinition("XML"),
                "css" => HighlightingManager.Instance.GetDefinition("CSS"),
                "sql" => HighlightingManager.Instance.GetDefinition("SQL"),
                "php" => HighlightingManager.Instance.GetDefinition("PHP"),
                "powershell" or "ps1" => HighlightingManager.Instance.GetDefinition("PowerShell"),
                "bash" or "sh" or "shell" => HighlightingManager.Instance.GetDefinition("BAT"),
                "json" => HighlightingManager.Instance.GetDefinition("Json"),
                "markdown" or "md" => null,
                _ => HighlightingManager.Instance.GetDefinitionByExtension("." + language)
            };
        }

        if (highlighting == null)
        {
            // Brak highlighting — zwróć plain text z podziałem na linie
            bool first = true;
            foreach (string line in code.Split('\n'))
            {
                if (!first) result.Add(new LineBreak());
                first = false;
                result.Add(new Run(line) { Foreground = new SolidColorBrush(Color.FromRgb(186, 194, 222)) });
            }
            return result;
        }

        // Użyj AvalonEdit DocumentHighlighter do podkolorowania
        try
        {
            var document = new ICSharpCode.AvalonEdit.Document.TextDocument(code);
            var highlighter = new ICSharpCode.AvalonEdit.Highlighting.DocumentHighlighter(document, highlighting);

            int lineCount = document.LineCount;
            for (int lineNum = 1; lineNum <= lineCount; lineNum++)
            {
                if (lineNum > 1) result.Add(new LineBreak());

                var docLine = document.GetLineByNumber(lineNum);
                var highlightedLine = highlighter.HighlightLine(lineNum);
                string lineText = document.GetText(docLine.Offset, docLine.Length);

                if (highlightedLine.Sections.Count == 0)
                {
                    result.Add(new Run(lineText) { Foreground = new SolidColorBrush(Color.FromRgb(186, 194, 222)) });
                    continue;
                }

                int pos = 0;
                foreach (var section in highlightedLine.Sections)
                {
                    // Tekst przed sekcją (bez formatowania)
                    if (section.Offset > pos)
                    {
                        string plain = lineText[pos..section.Offset];
                        result.Add(new Run(plain) { Foreground = new SolidColorBrush(Color.FromRgb(186, 194, 222)) });
                    }

                    // Tekst sekcji z kolorem
                    int end = Math.Min(section.Offset + section.Length, lineText.Length);
                    if (section.Offset < end)
                    {
                        string sectionText = lineText[section.Offset..end];
                        var run = new Run(sectionText);
                        run.Foreground = ConvertAvalonColor(section.Color);
                        if (section.Color.FontWeight.HasValue)
                            run.FontWeight = section.Color.FontWeight.Value == FontWeights.Bold ? FontWeights.Bold : FontWeights.Normal;
                        if (section.Color.FontStyle.HasValue)
                            run.FontStyle = section.Color.FontStyle.Value == FontStyles.Italic ? FontStyles.Italic : FontStyles.Normal;
                        result.Add(run);
                    }

                    pos = section.Offset + section.Length;
                }

                // Tekst po ostatniej sekcji
                if (pos < lineText.Length)
                    result.Add(new Run(lineText[pos..]) { Foreground = new SolidColorBrush(Color.FromRgb(186, 194, 222)) });
            }
        }
        catch
        {
            // Fallback jeśli highlighting się wysypie
            result.Clear();
            bool first = true;
            foreach (string line in code.Split('\n'))
            {
                if (!first) result.Add(new LineBreak());
                first = false;
                result.Add(new Run(line) { Foreground = new SolidColorBrush(Color.FromRgb(186, 194, 222)) });
            }
        }

        return result;
    }



    private static Section RenderListBlock(
    Markdig.Syntax.ListBlock list,
    Action<string>? onLinkClicked,
    Func<string, bool>? linkExists,
    string? noteFolder = null)
    {
        var section = new Section();
        bool isOrdered = list.IsOrdered;
        int index = list.OrderedStart != null ? int.Parse(list.OrderedStart) : 1;

        foreach (var item in list)
        {
            if (item is Markdig.Syntax.ListItemBlock listItem)
            {
                var para = new Paragraph { Margin = new Thickness(16, 0, 0, 4), TextIndent = 0 };
                string prefix = isOrdered ? $"{index}. " : "• ";
                para.Inlines.Add(new Run(prefix)
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200))
                });
                var listPara = listItem.OfType<Markdig.Syntax.ParagraphBlock>().FirstOrDefault();
                if (listPara != null)
                    RenderInlines(listPara.Inline, para.Inlines, onLinkClicked, linkExists, noteFolder);
                section.Blocks.Add(para);
                index++;
            }
        }
        return section;
    }

    private static Block RenderThematicBreak()
    {
        return new Paragraph
        {
            Margin = new Thickness(0, 6, 0, 6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0)
        };
    }

    // ── Inline rendering ──────────────────────────────────────────────────────

    private static void RenderInlines(
    Markdig.Syntax.Inlines.ContainerInline? inlineContainer,
    InlineCollection target,
    Action<string>? onLinkClicked,
    Func<string, bool>? linkExists = null,
    string? noteFolder = null)  // ← dodaj
    {
        if (inlineContainer == null) return;

        foreach (var inline in inlineContainer)
        {
            switch (inline)
            {
                case Markdig.Syntax.Inlines.LiteralInline literal:
                    RenderLiteralWithMarkers(literal.ToString(), target, onLinkClicked, linkExists);
                    break;

                case Markdig.Syntax.Inlines.EmphasisInline emphasis:
                    RenderEmphasis(emphasis, target, onLinkClicked, linkExists, noteFolder);
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

                case Markdig.Syntax.Inlines.LinkInline img when img.IsImage:
                    AddImageInline(target, img.Url ?? "", noteFolder);
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

    private static Brush ConvertAvalonColor(HighlightingColor color)
    {
        if (color.Foreground == null)
            return new SolidColorBrush(Color.FromRgb(205, 214, 244));

        var brush = color.Foreground.GetBrush(null);
        if (brush is not SolidColorBrush scb)
            return new SolidColorBrush(Color.FromRgb(205, 214, 244));

        var c = scb.Color;

        // AvalonEdit domyślne kolory są na jasne tło — remapuj na Catppuccin Mocha
        // Sprawdzamy przybliżone kolory bo AvalonEdit nie daje nazw tokenów

        // Ciemny niebieski → słowa kluczowe — mapuj na jasny niebieski/lawendowy
        if (c.R < 80 && c.G < 80 && c.B > 100)
            return new SolidColorBrush(Color.FromRgb(203, 166, 247)); // purple — keywords

        // Ciemna zieleń → komentarze — mapuj na szary-zielony
        if (c.R < 80 && c.G > 80 && c.B < 80)
            return new SolidColorBrush(Color.FromRgb(108, 112, 134)); // gray — comments

        // Ciemna czerwień/brąz → stringi — mapuj na zielony
        if (c.R > 100 && c.G < 60 && c.B < 60)
            return new SolidColorBrush(Color.FromRgb(166, 227, 161)); // green — strings

        // Ciemny teal/morski → typy — mapuj na niebieski
        if (c.R < 80 && c.G > 80 && c.B > 80)
            return new SolidColorBrush(Color.FromRgb(137, 180, 250)); // blue — types

        // Ciemny fiolet → mapuj na różowy
        if (c.R > 80 && c.G < 60 && c.B > 80)
            return new SolidColorBrush(Color.FromRgb(243, 139, 168)); // pink — special

        // Kolor który jest jasny (>180 każdy kanał) — zostaw prawie bez zmian, tylko rozjaśnij lekko
        if (c.R > 180 && c.G > 180 && c.B > 180)
            return new SolidColorBrush(Color.FromRgb(205, 214, 244)); // default text

        // Wszystko inne — zwróć domyślny kolor tekstu
        return new SolidColorBrush(Color.FromRgb(205, 214, 244));
    }

    private static void RenderLiteralWithMarkers(
        string text,
        InlineCollection target,
        Action<string>? onLinkClicked,
        Func<string, bool>? linkExists = null)
    {
        int lastIndex = 0;

        foreach (Match m in MarkerPattern.Matches(text))
        {
            if (m.Index > lastIndex)
                target.Add(new Run(text[lastIndex..m.Index])
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244))
                });

            string linkTarget = m.Groups[1].Value;
            target.Add(BuildWikiHyperlink(linkTarget, onLinkClicked, linkExists));
            lastIndex = m.Index + m.Length;
        }

        if (lastIndex < text.Length)
            target.Add(new Run(text[lastIndex..])
            {
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244))
            });
    }

    private static Hyperlink BuildWikiHyperlink(
        string linkTarget,
        Action<string>? onLinkClicked,
        Func<string, bool>? linkExists)
    {
        bool exists = linkExists == null || linkExists(linkTarget);

        var hyperlink = new Hyperlink
        {
            TextDecorations = exists ? TextDecorations.Underline : null,
            Cursor = exists ? Cursors.Hand : Cursors.Arrow,
            ToolTip = exists
                ? $"Przejdź do: \"{linkTarget}\""
                : $"Nie znaleziono: \"{linkTarget}\""
        };
        hyperlink.Foreground = new SolidColorBrush(exists
            ? Color.FromRgb(137, 180, 250)
            : Color.FromRgb(108, 112, 134));
        hyperlink.Inlines.Add(new Run(linkTarget) { Foreground = hyperlink.Foreground });

        if (exists && onLinkClicked != null)
            hyperlink.Click += (_, e) => { onLinkClicked(linkTarget); e.Handled = true; };

        return hyperlink;
    }

    private static void RenderEmphasis(
    Markdig.Syntax.Inlines.EmphasisInline emphasis,
    InlineCollection target,
    Action<string>? onLinkClicked,
    Func<string, bool>? linkExists = null,
    string? noteFolder = null)  // ← dodaj
    {
        bool isBold = emphasis.DelimiterCount >= 2;
        bool isItalic = emphasis.DelimiterChar == '*' || emphasis.DelimiterChar == '_';
        bool isStrikethrough = emphasis.DelimiterChar == '~';

        var temp = new Span();
        RenderInlines(emphasis, temp.Inlines, onLinkClicked, linkExists, noteFolder);  // ← przekaż

        foreach (var child in temp.Inlines.ToList())
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

    private static void RenderLink(
        Markdig.Syntax.Inlines.LinkInline link,
        InlineCollection target,
        Action<string>? onLinkClicked)
    {
        string? url = link.Url;

        if (url != null)
        {
            var hyperlink = new Hyperlink
            {
                Foreground = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
                ToolTip = url
            };
            try { hyperlink.NavigateUri = new Uri(url); } catch { }
            hyperlink.Inlines.Add(new Run(link.FirstChild?.ToString() ?? url));
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
            target.Add(new Run(link.FirstChild?.ToString() ?? "")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(137, 180, 250))
            });
        }
    }
}