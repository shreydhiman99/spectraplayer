using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using SpectraPlayer.App.Controls;
using SpectraPlayer.App.ViewModels;
using SpectraPlayer.Core;
using SpectraPlayer.Core.Library;

namespace SpectraPlayer.App.Views;

// Notes: timestamped text notes on the current video, stored in the library, exportable as Markdown.
public partial class MainWindow
{
    private readonly ObservableCollection<Note> _notes = [];

    // While the note box is empty the timestamp follows playback ("now"); once you start typing it locks to that moment.
    private bool _stampLocked;
    private TimeSpan _noteStamp;
    private string? _stampPath; // the video the locked timestamp belongs to (playback may move on while you type)

    private void InitializeNotes()
    {
        NoteList.ItemsSource = _notes;
        _notes.CollectionChanged += (_, _) => RefreshNotesUi();
        RefreshNotesUi();
        UpdateStampLabel();
    }

    private void LoadNotes(string videoPath)
    {
        _notes.Clear();
        try
        {
            foreach (var note in _library.GetNotes(videoPath)) _notes.Add(note);
        }
        catch (SqliteException ex)
        {
            ShowOsd($"Could not read notes: {ex.Message}", 4);
        }
    }

    private void RefreshNotesUi()
    {
        NoteCount.Text = _notes.Count == 0 ? string.Empty : _notes.Count.ToString();
        NoteEmpty.Visibility = _notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Seek.NoteMarkers = _notes.Select(n => new SeekMarker(n.Start.TotalSeconds, n.Preview)).ToList();
    }

    // ---- taking notes ----------------------------------------------------------------------------------------

    /// <summary>N: open the notes panel with the cursor in the note box, ready to type.</summary>
    private void BeginNote()
    {
        if (_fullscreen) SetFullscreen(false); // the note box lives in the side panel
        ShowPanel(PanelView.Notes);
        // The panel may only just have become visible, so focus once layout has caught up.
        Dispatcher.BeginInvoke(() =>
        {
            NoteInput.Focus();
            Keyboard.Focus(NoteInput);
        }, DispatcherPriority.Input);
    }

    private TimeSpan PlaybackStamp() =>
        _engine.HasMedia ? TimeSpan.FromMilliseconds(Math.Round(_engine.Position.TotalMilliseconds)) : TimeSpan.Zero;

    private TimeSpan CurrentStamp() => _stampLocked ? _noteStamp : PlaybackStamp();

    private void UpdateStampLabel() =>
        NoteStamp.Text = (_stampLocked ? "at " : "now ") + TimeFormat.Format(CurrentStamp());

    private void TickNoteStamp()
    {
        if (_stampLocked || SidePanel.Visibility != Visibility.Visible || _panelView != PanelView.Notes) return;
        UpdateStampLabel();
    }

    private void OnNoteInputChanged(object sender, TextChangedEventArgs e)
    {
        var hasText = NoteInput.Text.Length > 0;
        NotePlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;

        if (hasText && !_stampLocked)
        {
            _stampLocked = true;
            _noteStamp = PlaybackStamp(); // the moment you began the thought...
            _stampPath = _currentPath;    // ...on this video
        }
        else if (!hasText)
        {
            _stampLocked = false;
        }
        UpdateStampLabel();
    }

    private void OnNoteStampReset(object sender, RoutedEventArgs e)
    {
        _noteStamp = PlaybackStamp();
        _stampPath = _currentPath;
        _stampLocked = NoteInput.Text.Length > 0;
        UpdateStampLabel();
    }

    /// <summary>Takes the keyboard out of the note box so shortcuts (Space, arrows, N…) work on the player again.</summary>
    private void ReleaseNoteFocus()
    {
        // Clearing only keyboard focus isn't enough: the window would hand focus straight back to its last focused element.
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(NoteInput), null);
        Keyboard.Focus(this);
    }

    private void OnNoteInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            AddNoteFromComposer();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ReleaseNoteFocus(); // give the keyboard back to the player shortcuts
            e.Handled = true;
        }
    }

    private void OnAddNoteClick(object sender, RoutedEventArgs e) => AddNoteFromComposer();
    private void OnTakeNoteClick(object sender, RoutedEventArgs e) => BeginNote();
    private void OnPanelNotesClick(object sender, RoutedEventArgs e) => ShowPanel(PanelView.Notes);

    private void AddNoteFromComposer()
    {
        var text = NoteInput.Text.Trim();
        if (text.Length == 0) return;
        // The note belongs to the video the timestamp was taken on, even if playback has moved on since (e.g. the next lecture started).
        var target = _stampLocked ? _stampPath ?? _currentPath : _currentPath;
        if (target is null)
        {
            ShowOsd("Open a video first");
            return;
        }

        Note note;
        try { note = _library.AddNote(target, CurrentStamp(), text); }
        catch (SqliteException ex)
        {
            ShowOsd($"Could not save note: {ex.Message}", 4);
            return;
        }

        var onCurrentVideo = string.Equals(target, _currentPath, StringComparison.OrdinalIgnoreCase);
        if (onCurrentVideo)
        {
            var index = 0;
            while (index < _notes.Count && _notes[index].Start <= note.Start) index++;
            _notes.Insert(index, note);
            NoteList.ScrollIntoView(note);
        }

        NoteInput.Clear(); // also unlocks the timestamp so it follows playback again
        ShowOsd(onCurrentVideo
            ? $"Note added  {note.TimeLabel}"
            : $"Note added to {Path.GetFileNameWithoutExtension(target)}  {note.TimeLabel}", onCurrentVideo ? 1.5 : 3);
    }

    // ---- the list --------------------------------------------------------------------------------------------

    private void GoToNote(Note note)
    {
        if (!_engine.HasMedia) return;
        _engine.Seek(note.Start);
        Seek.Position = note.Start.TotalSeconds;
        _holdPositionUntil = Environment.TickCount64 + 400;
        ShowOsd($"{note.TimeLabel}  {note.Preview}", 2.5);
    }

    private void EditNote(Note note)
    {
        var text = InputDialog.AskMultiline(this, "Edit note", $"Note at {note.TimeLabel}", note.Text);
        if (string.IsNullOrWhiteSpace(text) || text == note.Text) return;

        try
        {
            if (_library.UpdateNoteText(note.Id, text) is not { } updated) return;
            var index = _notes.IndexOf(note);
            if (index >= 0) _notes[index] = updated;
        }
        catch (SqliteException ex)
        {
            ShowOsd($"Could not save note: {ex.Message}", 4);
        }
    }

    private void DeleteNote(Note note)
    {
        try { _library.DeleteNote(note.Id); }
        catch (SqliteException ex)
        {
            ShowOsd($"Could not delete note: {ex.Message}", 4);
            return;
        }
        _notes.Remove(note);
    }

    private void OnNoteDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var onItem = e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(NoteList, d) is ListBoxItem;
        if (onItem && NoteList.SelectedItem is Note note) GoToNote(note);
    }

    private void OnNoteListKeyDown(object sender, KeyEventArgs e)
    {
        if (NoteList.SelectedItem is not Note note) return;
        if (e.Key == Key.F2) { EditNote(note); e.Handled = true; }
        else if (e.Key == Key.Delete) { DeleteNote(note); e.Handled = true; }
    }

    private void OnNoteRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem container) container.IsSelected = true;
    }

    private void OnNoteGoTo(object sender, RoutedEventArgs e)
    {
        if (NoteList.SelectedItem is Note note) GoToNote(note);
    }

    private void OnNoteEdit(object sender, RoutedEventArgs e)
    {
        if (NoteList.SelectedItem is Note note) EditNote(note);
    }

    private void OnNoteDelete(object sender, RoutedEventArgs e)
    {
        if (NoteList.SelectedItem is Note note) DeleteNote(note);
    }

    private void OnNoteCopy(object sender, RoutedEventArgs e)
    {
        if (NoteList.SelectedItem is Note note) CopyToClipboard(note.Text, "Note copied");
    }

    // ---- export ----------------------------------------------------------------------------------------------

    private void OnNotesMenuClick(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)FindResource("NotesMenu");
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private string VideoNotesMarkdown() =>
        NotesMarkdown.ForVideo(Path.GetFileNameWithoutExtension(_currentPath!), _notes.ToList());

    private void OnExportVideoNotes(object sender, RoutedEventArgs e)
    {
        if (_currentPath is null || _notes.Count == 0)
        {
            ShowOsd("No notes to export on this video");
            return;
        }

        var name = Path.GetFileNameWithoutExtension(_currentPath);
        SaveMarkdown($"{name} - notes.md", Path.GetDirectoryName(_currentPath), VideoNotesMarkdown());
    }

    private void OnExportTabNotes(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            ShowOsd("Open a tab first");
            return;
        }

        var sections = BuildTabSections(_activeTab);
        if (sections.Count == 0)
        {
            ShowOsd("This tab has no notes yet");
            return;
        }

        SaveMarkdown($"{_activeTab.Title} - notes.md", _activeTab.Folder, NotesMarkdown.ForTab(_activeTab.Title, sections));
    }

    private void OnCopyVideoNotes(object sender, RoutedEventArgs e)
    {
        if (_currentPath is null || _notes.Count == 0)
        {
            ShowOsd("No notes to copy on this video");
            return;
        }
        CopyToClipboard(VideoNotesMarkdown(), "Notes copied as Markdown");
    }

    /// <summary>One section per video with notes: playlist order first, then notes of videos no longer in the folder.</summary>
    private List<NotesSection> BuildTabSections(TabViewModel tab)
    {
        var byPath = _library.GetNotesUnder(tab.Folder);
        var sections = new List<NotesSection>();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in tab.Items)
        {
            if (!byPath.TryGetValue(item.Path, out var notes)) continue;
            sections.Add(new NotesSection(item.Name, item.HasSubfolder ? item.Subfolder : null, notes));
            listed.Add(item.Path);
        }

        foreach (var (path, notes) in byPath.Where(kv => !listed.Contains(kv.Key)).OrderBy(kv => kv.Key, NaturalComparer.Instance))
        {
            var folder = Path.GetDirectoryName(Path.GetRelativePath(tab.Folder, path));
            sections.Add(new NotesSection(Path.GetFileNameWithoutExtension(path), string.IsNullOrEmpty(folder) ? null : folder, notes));
        }
        return sections;
    }

    private void SaveMarkdown(string suggestedName, string? initialDirectory, string markdown) =>
        SaveTextFile("Export notes", "Markdown (*.md)|*.md|All files|*.*", ".md", suggestedName, initialDirectory, markdown, "Notes exported");

    /// <summary>Asks where to save and writes UTF-8 text (no byte-order mark).</summary>
    private void SaveTextFile(string title, string filter, string defaultExt, string suggestedName, string? initialDirectory,
        string content, string confirmation)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExt,
            FileName = SafeFileName(suggestedName),
            InitialDirectory = initialDirectory is not null && Directory.Exists(initialDirectory) ? initialDirectory : string.Empty,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            ShowOsd(confirmation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private void CopyToClipboard(string text, string confirmation)
    {
        try
        {
            Clipboard.SetText(text);
            ShowOsd(confirmation);
        }
        catch (ExternalException)
        {
            ShowOsd("Clipboard is busy, try again", 3); // another app is holding it
        }
    }
}
