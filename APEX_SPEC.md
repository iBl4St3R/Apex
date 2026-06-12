# APEX — Project Notes Manager
## Specification v1.0

---

## 1. Overview

Apex is a standalone WPF desktop application for Windows 11.
It is a project notes manager that stores all data as plain Markdown (.md) files
on disk, organized in a folder structure that mirrors the logical hierarchy
created by the user inside the application.

Apex is not tied to any specific project. Any folder can become an Apex project.
The application is designed for solo developers managing personal projects.

---

## 2. Project File (.apex)

Every Apex project consists of:
- A root folder containing all Markdown files and subfolders
- A single `.apex` file (JSON format) located in the root folder

The `.apex` file stores:
- Project name (string)
- Absolute path to root folder (string)
- Last opened view: "board" or "structure" (string)
- Last zoom level for board view (float)
- User-defined categories (array, see Section 5)
- Card positions on the board (array, see Section 6)
- Application theme override: "system" | "light" | "dark" (string)

The `.apex` file does NOT store note content. Content lives exclusively in .md files.
The `.apex` file is safe to commit to Git alongside the .md files.

Example .apex structure:
```json
{
  "projectName": "Prism of Dominion",
  "rootFolder": "C:/Projects/MyGame/Notes",
  "lastView": "board",
  "lastZoom": 1.0,
  "themeOverride": "system",
  "categories": [
    { "id": "cat_bugs", "name": "BUG", "color": "#E24B4A" },
    { "id": "cat_ideas", "name": "IDEA", "color": "#378ADD" },
    { "id": "cat_tasks", "name": "TASK", "color": "#1D9E75" }
  ],
  "cards": [
    {
      "relativePath": "Bugs/Bot-not-playing.md",
      "boardX": 120.0,
      "boardY": 340.0,
      "categoryId": "cat_bugs"
    }
  ]
}
```

---

## 3. Startup Screen

When Apex is launched with no argument, it displays a startup screen with two options:
- "Create new project" — opens a folder picker dialog, user selects or creates
  an empty folder, enters a project name, Apex creates the .apex file there.
- "Open existing project" — opens a file picker filtered to *.apex files.

Apex can also be launched by double-clicking a .apex file in Windows Explorer
(file association registered on install). In that case the startup screen is
skipped and the project opens directly.

The startup screen shows a list of recently opened projects (path + name),
clicking any of them opens the project directly.

---

## 4. Application Layout

The application window is divided into two main areas:

```
+--------------------------------------------------+
|  [Toolbar]                                       |
+------------------+-------------------------------+
|                  |                               |
|  Left Panel      |  Right Panel                  |
|  (switchable)    |  (content view)               |
|                  |                               |
+------------------+-------------------------------+
|  [Status Bar]                                    |
+--------------------------------------------------+
```

The left panel has two modes toggled by buttons in the toolbar:
- BOARD view (Section 6)
- STRUCTURE view (Section 7)

The right panel displays note content (Section 8).

The divider between left and right panels is draggable.

---

## 5. Categories

Categories are user-defined labels that can be assigned to any note.
Each category has:
- Unique ID (generated internally, not visible to user)
- Name (short string, e.g. "BUG", "IDEA", "TASK", "DEVLOG")
- Color (hex color chosen by user from a color picker)

Categories are managed in Settings (Section 10).
A note can have exactly one category, or no category.
The category badge is displayed in the top-right corner of every card on the board
and next to the filename in the structure tree.

---

## 6. Board View (Left Panel — Board Mode)

The board is an infinite 2D canvas. The user can pan by clicking and dragging
the background, and zoom with the mouse wheel.

Each note is represented as a rectangular card on the canvas.
Cards can be freely dragged to any position.
Card positions are saved to the .apex file immediately on drag end.

### 6.1 Card Appearance

Each card displays:
- Title (filename without .md extension), top-left, bold
- Category badge, top-right corner, colored background matching category color,
  white text, small font
- Creation date, bottom-left, small muted font
- Last modified date, bottom-right, small muted font
- A thin left border strip in the category color (or neutral gray if no category)

Card width: fixed 220px
Card height: auto, minimum 80px

On hover, the card expands vertically to show the first 300 characters
of the note's Markdown content as plain text (Markdown symbols stripped).
The hover preview disappears when the mouse leaves the card.

### 6.2 Card Interactions

Left click: selects the card and opens the note in the right panel.
Double click: opens the note in edit mode in the right panel.
Right click: opens context menu with options:
  - New note (Ctrl+N)
  - Edit
  - Delete (moves .md file to Windows Recycle Bin after confirmation prompt)
  - Set category (submenu listing all user categories)
  - Make container (see Section 6.4)
  - View connections (see Section 6.5)

Ctrl+N anywhere on the board (or right click → New note):
  Opens a small inline dialog asking for note title and category.
  After confirmation, creates the .md file and adds the card to the board
  at the position where right-click occurred, or at center of current viewport
  if using keyboard shortcut.

### 6.3 Free Mode (default board mode)

In free mode, cards can be placed anywhere on the canvas manually.
Cards can be grouped into containers.

A container is a card that contains other cards visually nested inside it.
Creating a container: right click on a card → "Make container".
Dragging another card on top of a container card snaps it inside.
The container card expands to fit its children.

Container hierarchy mirrors the folder structure on disk:
- Container card "Character" = folder Character/ on disk
- Child card "Stats" inside "Character" = file Character/Stats.md on disk
- If "Stats" also becomes a container, it creates Character/Stats/ folder

Moving a card out of a container moves the .md file out of the subfolder.
Moving a card into a container moves the .md file into the subfolder.
All file system operations happen immediately and silently.

### 6.4 Connections

Connections between cards are drawn automatically based on [[link]] syntax
found in the Markdown content of notes.

If note A contains [[B]] anywhere in its text, a line is drawn from card A to card B.
Lines are thin (1px), colored with a muted gray.
Lines are always visible in free mode (they can be toggled off via toolbar button).
Arrowhead on the target end of the line indicates direction.

### 6.5 Connections View Mode

Right click on any card → "View connections" switches the board to
Connections View Mode for that card.

In this mode:
- The selected card is placed in the center of the canvas
- All cards that have a [[link]] to or from the selected card are arranged
  in a circle around it, evenly spaced
- Lines are drawn from the center card to each connected card
- Cards with no connection to the selected card are hidden
- A button "Exit connections view" appears in the toolbar to return to free mode
- Clicking a different card in connections view re-centers the view on that card

---

## 7. Structure View (Left Panel — Structure Mode)

The structure view shows a file tree of all .md files and folders
in the project root folder.

```
Structure tree example:
  > Bugs/
      Bot-not-playing.md        [BUG]
      Stamina-drain.md          [BUG]
  > Tasks/
      Add-pat-head.md           [TASK]
      Remove-grab-pussy.md      [TASK]
  > Ideas/
      Utility-card-left-hand.md [IDEA]
  Character.md
  INBOX.md
```

Folders are expandable/collapsible (click on folder name or arrow).
Files show their category badge next to the filename.
Single click on a file: selects it and opens content in right panel.
Ctrl+click or Shift+click: multi-selects files (see Section 8.3).

Right click on a file: context menu with:
  - Open
  - Rename
  - Delete (moves to Recycle Bin after confirmation)
  - Set category

Right click on a folder: context menu with:
  - New note here
  - New subfolder
  - Rename folder
  - Delete folder (recursive, confirmation required)

The tree auto-refreshes when files change on disk (see Section 9).

---

## 8. Right Panel — Content View

The right panel displays note content.
When no note is selected, it shows a placeholder: "Select a note to view it."

### 8.1 Single Note — Read Mode

The note is rendered as formatted Markdown:
- Headings rendered with size hierarchy
- Bold, italic, strikethrough rendered visually
- Code blocks rendered in monospace with background
- [[link]] syntax rendered as clickable inline links.
  Clicking a [[link]] selects and opens the linked note.
  If the linked note does not exist, the link is shown in red.
- Checkboxes rendered as actual checkboxes (clicking them toggles
  the [ ] / [x] state and saves immediately)

A toolbar above the content shows:
  - Note title (read-only in this mode)
  - "Edit" button (or F2 shortcut)
  - Category badge
  - Creation date / Last modified date

### 8.2 Single Note — Edit Mode

Activated by clicking "Edit" button, pressing F2, or double-clicking a card.

The formatted view is replaced by a plain text editor showing raw Markdown.
The editor supports:
- Syntax highlighting for Markdown
- [[link]] autocomplete: typing [[ shows a dropdown with all note names in project
- Tab key inserts 2 spaces
- Standard text editing shortcuts (Ctrl+Z undo, Ctrl+Y redo, Ctrl+A select all)

The toolbar in edit mode shows:
  - Editable title field (renaming the title renames the .md file on disk)
  - "Save" button (Ctrl+S)
  - "Cancel" button (Esc) — reverts to last saved state
  - Category selector dropdown

Saving: Ctrl+S or clicking Save writes the file to disk immediately
and switches back to read mode.

### 8.3 Multi-select View (Merge View)

When multiple files are selected in the structure view (Ctrl+click or Shift+click),
the right panel displays the content of all selected files merged into a single
scrollable view.

The files are displayed one after another in the order they were selected.
There are no separators, no headers between files, no visual boundaries.
The merged content reads as if it were one continuous document.

Merge view is read-only. Editing is not available in merge view.
Clicking any single file in the structure tree exits merge view.

---

## 9. File System Watching

Apex watches the project root folder recursively for any file system changes.

If a .md file is modified externally (e.g. by VSCode or Git):
  - If the file is NOT currently open in edit mode: reload silently, no prompt.
  - If the file IS open in edit mode: show a non-blocking banner at the top
    of the editor: "This file was modified externally. Load external changes
    or keep your edits?" with two buttons: "Load external" and "Keep mine".
    The user's unsaved edits are not lost until they choose "Load external".

If a .md file is created externally: add it to the tree and board automatically.
If a .md file is deleted externally: remove it from the tree and board automatically.
If a folder is created/deleted externally: reflect in tree automatically.

Apex never locks .md files. Other applications can always read and write them.

---

## 10. Search

Search is accessible via Ctrl+F or a search icon in the toolbar.
Search opens an overlay panel or sidebar, not a separate window.

Search searches by:
  - Note filename (without .md extension)
  - [[link]] references: typing "Character" shows all notes that contain [[Character]]
    and all notes that are linked from Character.md

Search does NOT search inside note content (full text search is out of scope).

Results are shown as a list of matching filenames with their category badge.
Clicking a result opens the note in the right panel.
Pressing Escape closes search.

---

## 11. Settings

Settings are accessible via a gear icon in the toolbar or Ctrl+comma.
Settings open as an overlay panel (not a separate window).

Settings sections:

### 11.1 Theme
  - System (follows Windows light/dark mode setting) — default
  - Force Light
  - Force Dark

### 11.2 Categories
  - List of all user-defined categories with name and color swatch
  - "Add category" button: enter name, pick color from color picker
  - Each category has a delete button (categories in use show a warning before delete)
  - Each category has an edit button (rename or recolor)

### 11.3 About
  - Application version
  - Link to project repository (if any)

---

## 12. Keyboard Shortcuts Summary

  Ctrl+N          New note
  Ctrl+S          Save current note (in edit mode)
  Ctrl+F          Open search
  Ctrl+,          Open settings
  Ctrl+click      Multi-select in structure view
  Shift+click     Range select in structure view
  F2              Enter edit mode for selected note
  Esc             Cancel edit / close overlay
  Ctrl+Z          Undo (in edit mode)
  Ctrl+Y          Redo (in edit mode)
  Mouse wheel     Zoom in/out on board
  Middle click    Pan board (alternative to click+drag background)

---

## 13. Technical Stack

  Language:       C# (.NET 8)
  UI Framework:   WPF (Windows Presentation Foundation)
  Markdown render: Markdig (parsing) + custom WPF renderer
  Board canvas:   WPF Canvas with custom drag/drop and pan/zoom
  File watching:  System.IO.FileSystemWatcher
  JSON:           System.Text.Json
  Target OS:      Windows 10 / Windows 11 (64-bit)
  Distribution:   Single .exe or MSIX installer

No external database. No network requests. No telemetry.
All data stays on user's disk.

---

## 14. Out of Scope (v1.0)

The following features are explicitly NOT part of v1.0:
  - Automatic backup / Git integration
  - Full-text search inside note content
  - Multiple projects open simultaneously
  - Collaboration / sync
  - Mobile or web version
  - Image embedding in notes
  - Export to PDF or HTML
  - Plugin system