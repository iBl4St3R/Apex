

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


