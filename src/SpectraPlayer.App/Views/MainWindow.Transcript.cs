using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using SpectraPlayer.App.ViewModels;
using SpectraPlayer.Core;
using SpectraPlayer.Core.Library;
using SpectraPlayer.Core.Playback.Mpv;
using SpectraPlayer.Core.Transcription;

namespace SpectraPlayer.App.Views;

// Transcript tab: transcribe the marked section on this computer, read along while the video plays, turn lines into notes.
public partial class MainWindow
{
    private readonly ModelManager _models = new(Path.Combine(AppPaths.DataDirectory, "models"));
    private readonly ObservableCollection<TranscriptRow> _transcript = [];

    private ModelInfo _model = WhisperModels.Default;
    private TranscriptRow? _currentRow;
    private CancellationTokenSource? _transcribeCts;
    private bool _transcribing;

    // Text can come from a subtitle file next to the video (instant, exact) or from a speech model.
    private string? _subtitleFile;
    private bool _preferSubtitles = true;

    private bool UsingSubtitles => _preferSubtitles && _subtitleFile is not null;
    private string SourceId => UsingSubtitles ? TranscriptSources.SubtitlesId : _model.Id;
    private string SourceLabel => UsingSubtitles ? $"subtitle file {Path.GetFileName(_subtitleFile)}" : _model.DisplayName;

    private void InitializeTranscript()
    {
        TranscriptList.ItemsSource = _transcript;
        _transcript.CollectionChanged += (_, _) => RefreshTranscriptUi();
        _preferSubtitles = _library.GetSetting("preferSubtitles") != "0";

        var saved = _library.GetSetting("transcriptModel");
        _model = KnownModels().FirstOrDefault(m => m.Id == saved) ?? WhisperModels.Default;
        RefreshTranscriptUi();
    }

    private IEnumerable<ModelInfo> KnownModels() => WhisperModels.All.Concat(_models.Installed().Where(m => m.IsCustom));

    /// <summary>Shows what is already stored for this video and the chosen model.</summary>
    private void LoadTranscript(string videoPath)
    {
        try { _subtitleFile = SubtitleFinder.Find(videoPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _subtitleFile = null; }

        _transcript.Clear();
        _currentRow = null;
        try
        {
            foreach (var segment in _library.GetTranscript(videoPath, SourceId, TimeSpan.Zero, TimeSpan.FromDays(7)))
                _transcript.Add(new TranscriptRow(segment));
        }
        catch (SqliteException ex)
        {
            ShowOsd($"Could not read the transcript: {ex.Message}", 4);
        }
        RefreshTranscriptUi();
    }

    private void RefreshTranscriptUi()
    {
        var hasRows = _transcript.Count > 0;

        TranscriptEmpty.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
        var action = UsingSubtitles ? "Get text from subtitles" : "Transcribe section";
        TranscriptEmpty.Text = _transcribing ? "The text appears here as it is recognised."
            : _currentPath is null ? $"Open a video, mark a section, then press {action}."
            : _selection.IsComplete
                ? UsingSubtitles ? "Press Get text from subtitles to read the subtitle lines of the marked section."
                                 : "Press Transcribe section to turn the speech in the marked section into text."
            : $"Mark a section first (I and O, or Shift+drag on the seek bar), then press {action}.";

        TranscribeButton.Content = action;
        ModelButton.Content = UsingSubtitles ? "Source: Subtitle file  ▾" : $"Source: {_model.DisplayName}  ▾";
        ModelButton.IsEnabled = !_transcribing;
        TranscribeButton.IsEnabled = _currentPath is not null && _selection.IsComplete && !_transcribing;
        TranscriptNoteButton.IsEnabled = hasRows;
        TranscriptCopyButton.IsEnabled = hasRows;
        TranscriptExportButton.IsEnabled = hasRows;

        if (_subtitleFile is null)
        {
            SourceInfo.Visibility = Visibility.Collapsed;
        }
        else
        {
            var name = Path.GetFileName(_subtitleFile);
            SourceInfo.Text = UsingSubtitles
                ? $"Using {name}: instant, no speech model needed."
                : $"{name} is next to this video. Choose it under Source for instant text.";
            SourceInfo.Visibility = Visibility.Visible;
        }
    }

    // ---- transcribing ----------------------------------------------------------------------------------------

    private void OnTranscribeClick(object sender, RoutedEventArgs e) => Forget(TranscribeSelectionAsync());

    private void OnTranscribeCancelClick(object sender, RoutedEventArgs e)
    {
        _transcribeCts?.Cancel();
        TranscribeStage.Text = "Cancelling…";
    }

    private async Task TranscribeSelectionAsync()
    {
        if (_transcribing) return;
        if (_currentPath is not { } path || !_selection.IsComplete)
        {
            ShowOsd("Mark a section first");
            return;
        }

        var section = new TimeRange(_selection.Start!.Value, _selection.End!.Value);

        if (UsingSubtitles)
        {
            await ImportSubtitlesAsync(path, section);
            return;
        }

        var model = _model;

        // Never download without asking: say what, how big, from where and where it goes.
        if (!_models.IsInstalled(model) && !ConfirmModelDownload(model)) return;

        _transcribing = true;
        _transcribeCts = new CancellationTokenSource();
        var ct = _transcribeCts.Token;
        SetTranscribeStatus(null);
        SetTranscribeBusy(true);

        try
        {
            if (!_models.IsInstalled(model))
            {
                ShowTranscribeProgress("Downloading speech model…", 0);
                await _models.DownloadAsync(model,
                    new Progress<DownloadProgress>(p => ShowTranscribeProgress($"Downloading speech model…  {p.Fraction:P0}", p.Fraction)), ct);
            }

            ShowTranscribeProgress("Starting…", 0);
            var progress = new Progress<TranscriptionProgress>(p => ShowTranscribeProgress(
                p.Stage switch
                {
                    "Transcribing" => $"Transcribing…  {p.Fraction:P0}",
                    "Reading audio" => "Reading audio…",
                    _ => p.Stage,
                }, p.Fraction));

            var service = new TranscriptionService(new MpvAudioExtractor(), new WhisperTranscriber(), _library);
            await service.TranscribeAsync(path, section, model, _models.PathFor(model), progress,
                segment => Dispatcher.BeginInvoke(() => OnLiveSegment(path, segment)), ct);

            ShowOsd(string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase)
                ? "Transcript ready"
                : $"Transcript ready for {Path.GetFileNameWithoutExtension(path)}", 2.5);
        }
        catch (OperationCanceledException)
        {
            SetTranscribeStatus("Cancelled. The text found so far is kept; run it again to carry on.", isError: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException
                                       or InvalidDataException or MpvException or MpvNotFoundException or SqliteException)
        {
            SetTranscribeStatus(FriendlyError(ex), isError: true);
        }
        finally
        {
            _transcribing = false;
            _transcribeCts?.Dispose();
            _transcribeCts = null;
            SetTranscribeBusy(false);

            // Show exactly what is stored (live lines had no ids yet), also after a cancel or an error.
            if (string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase)) LoadTranscript(path);
            RefreshTranscriptUi();
        }
    }

    /// <summary>Takes the lines of the marked section from the subtitle file: no model, no waiting.</summary>
    private async Task ImportSubtitlesAsync(string videoPath, TimeRange section)
    {
        var file = _subtitleFile!;
        SetTranscribeStatus(null);
        try
        {
            var cues = await Task.Run(() => SubtitleParser.ReadFile(file));
            if (cues.Count == 0)
            {
                SetTranscribeStatus($"{Path.GetFileName(file)} has no readable subtitle text.", isError: true);
                return;
            }

            var added = SubtitleImporter.Import(_library, videoPath, section, cues);
            if (string.Equals(videoPath, _currentPath, StringComparison.OrdinalIgnoreCase)) LoadTranscript(videoPath);

            if (added > 0) ShowOsd($"{added} subtitle line{(added == 1 ? "" : "s")} added", 2.5);
            else SetTranscribeStatus("Nothing new: the subtitle file has no more text in this section, or it was already added.", isError: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            SetTranscribeStatus(ex.Message, isError: true);
        }
    }

    private bool ConfirmModelDownload(ModelInfo model) =>
        ConfirmDialog.Ask(this, "Download speech model",
            "To turn speech into text, Spectraplayer needs a speech model.\n\n" +
            $"{model.DisplayName}\n" +
            $"Size: {model.SizeText}\n" +
            $"From: {model.Url?.Host}\n" +
            $"Saved in: {_models.ModelsDirectory}\n\n" +
            "It is downloaded once and kept on your computer. Transcribing then works offline, and nothing from your " +
            "videos is sent anywhere.",
            $"Download ({model.SizeText})");

    private static string FriendlyError(Exception ex) => ex switch
    {
        MpvNotFoundException => "libmpv could not be loaded, so the audio can't be read. Run tools\\Get-LibMpv.ps1.",
        HttpRequestException => "The model could not be downloaded. Check your internet connection and try again.",
        _ => ex.Message,
    };

    private void SetTranscribeBusy(bool busy)
    {
        TranscribeBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        TranscribeButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        ModelButton.IsEnabled = !busy;
        TranscribeBar.Value = 0;
        if (!busy) TranscribeStage.Text = string.Empty;
    }

    private void ShowTranscribeProgress(string stage, double fraction)
    {
        TranscribeStage.Text = stage;
        TranscribeBar.Value = Math.Clamp(fraction, 0, 1);
    }

    private void SetTranscribeStatus(string? text, bool isError = true)
    {
        TranscribeStatus.Text = text ?? string.Empty;
        TranscribeStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        TranscribeStatus.Foreground = (System.Windows.Media.Brush)FindResource(isError ? "DangerBrush" : "TextDimBrush");
    }

    /// <summary>A line recognised while the run is still going (cached lines are replayed first, so skip those already shown).</summary>
    private void OnLiveSegment(string videoPath, TranscriptSegment segment)
    {
        if (!string.Equals(videoPath, _currentPath, StringComparison.OrdinalIgnoreCase)) return; // you moved to another video
        if (_transcript.Any(r => r.Start == segment.Start && r.End == segment.End && r.Text == segment.Text)) return;

        var row = new TranscriptRow(segment);
        var index = 0;
        while (index < _transcript.Count && _transcript[index].Start <= row.Start) index++;
        _transcript.Insert(index, row);

        if (!TranscriptList.IsMouseOver) TranscriptList.ScrollIntoView(row);
    }

    // ---- model choice ----------------------------------------------------------------------------------------

    private void OnModelButtonClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ModelButton, Placement = PlacementMode.Bottom };

        if (_subtitleFile is not null)
        {
            var useSubtitles = new MenuItem
            {
                Header = $"{(_preferSubtitles ? "✓  " : "     ")}Subtitle file ({Path.GetFileName(_subtitleFile)})  —  instant",
                ToolTip = "Use the subtitles that come with this video instead of a speech model",
            };
            useSubtitles.Click += (_, _) => SetPreferSubtitles(!_preferSubtitles);
            menu.Items.Add(useSubtitles);

            var reread = new MenuItem { Header = "     Read the subtitle file again", ToolTip = "Forget the lines taken from it so far (use after changing the file)" };
            reread.Click += (_, _) => RereadSubtitles();
            menu.Items.Add(reread);
            menu.Items.Add(new Separator());
        }

        foreach (var model in KnownModels())
        {
            var state = _models.IsInstalled(model) ? "installed" : $"{model.SizeText} download";
            var item = new MenuItem
            {
                Header = $"{(model.Id == _model.Id && !UsingSubtitles ? "✓  " : "     ")}{model.DisplayName}  —  {state}",
                ToolTip = model.Description,
            };
            item.Click += (_, _) => ChooseModel(model);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var open = new MenuItem { Header = "     Open the models folder" };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(_models.ModelsDirectory);
            OpenInExplorer($"\"{_models.ModelsDirectory}\"");
        };
        menu.Items.Add(open);

        menu.IsOpen = true;
    }

    private void ChooseModel(ModelInfo model)
    {
        if (_transcribing) return;

        // Picking a model while the video has a subtitle file means "use the model instead".
        var switchingAwayFromSubtitles = UsingSubtitles;
        if (switchingAwayFromSubtitles)
        {
            _preferSubtitles = false;
            _library.SetSetting("preferSubtitles", "0");
        }
        if (model.Id == _model.Id && !switchingAwayFromSubtitles) return;

        _model = model;
        _library.SetSetting("transcriptModel", model.Id);
        if (_currentPath is not null) LoadTranscript(_currentPath);
        RefreshTranscriptUi();
    }

    private void SetPreferSubtitles(bool prefer)
    {
        if (_transcribing) return;
        _preferSubtitles = prefer;
        _library.SetSetting("preferSubtitles", prefer ? "1" : "0");
        if (_currentPath is not null) LoadTranscript(_currentPath);
        RefreshTranscriptUi();
    }

    private void RereadSubtitles()
    {
        if (_currentPath is null || _transcribing) return;
        _library.ClearTranscript(_currentPath, TranscriptSources.SubtitlesId);
        LoadTranscript(_currentPath);
        ShowOsd("Subtitle lines cleared. Press Get text from subtitles to read the file again.", 3.5);
    }

    // ---- export ----------------------------------------------------------------------------------------------

    private void OnTranscriptExportClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Top };

        var markdown = new MenuItem { Header = "Markdown document (.md)…", ToolTip = "Readable text with timestamps, for notes apps" };
        markdown.Click += (_, _) => ExportTranscript(asMarkdown: true);
        menu.Items.Add(markdown);

        var srt = new MenuItem
        {
            Header = "Subtitle file (.srt)…",
            ToolTip = "Saved next to the video by default, so players show it as subtitles",
        };
        srt.Click += (_, _) => ExportTranscript(asMarkdown: false);
        menu.Items.Add(srt);

        menu.IsOpen = true;
    }

    /// <summary>Exports the selected lines, or every line if none are selected.</summary>
    private void ExportTranscript(bool asMarkdown)
    {
        var rows = TranscriptList.SelectedItems.Count > 0 ? SelectedRows().ToList() : _transcript.ToList();
        if (rows.Count == 0 || _currentPath is null)
        {
            ShowOsd("No transcript to export");
            return;
        }

        var segments = rows.Select(r => r.Segment).ToList();
        var name = Path.GetFileNameWithoutExtension(_currentPath);
        var folder = Path.GetDirectoryName(_currentPath);

        if (asMarkdown)
            SaveTextFile("Export transcript", "Markdown (*.md)|*.md|All files|*.*", ".md",
                $"{name} - transcript.md", folder, TranscriptExport.ToMarkdown(name, SourceLabel, segments), "Transcript exported");
        else
            SaveTextFile("Export subtitles", "SubRip subtitles (*.srt)|*.srt|All files|*.*", ".srt",
                name + SubtitleFinder.ExportSuffix, folder, TranscriptExport.ToSrt(segments), "Subtitles exported");
    }

    // ---- reading along ---------------------------------------------------------------------------------------

    /// <summary>Highlights the line being spoken now, and keeps it in view unless you are using the list.</summary>
    private void TickTranscript()
    {
        if (_transcript.Count == 0 || SidePanel.Visibility != Visibility.Visible || _panelView != PanelView.Transcript) return;

        var position = _engine.Position;
        TranscriptRow? current = null;
        foreach (var row in _transcript)
        {
            if (row.Start > position) break;
            current = row;
        }
        // Between two lines, stay on the last one for a moment instead of flickering; further on, show nothing.
        if (current is not null && position >= current.End + TimeSpan.FromSeconds(1.5)) current = null;

        if (ReferenceEquals(current, _currentRow)) return;
        if (_currentRow is not null) _currentRow.IsCurrent = false;
        _currentRow = current;
        if (current is null) return;

        current.IsCurrent = true;
        if (!TranscriptList.IsMouseOver) TranscriptList.ScrollIntoView(current);
    }

    private void SeekTo(TimeSpan time)
    {
        if (!_engine.HasMedia) return;
        _engine.Seek(time);
        Seek.Position = time.TotalSeconds;
        _holdPositionUntil = Environment.TickCount64 + 400;
    }

    // ---- list actions ----------------------------------------------------------------------------------------

    private IEnumerable<TranscriptRow> SelectedRows() =>
        TranscriptList.SelectedItems.OfType<TranscriptRow>().OrderBy(r => r.Start);

    private void OnTranscriptDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var onItem = e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(TranscriptList, d) is ListBoxItem;
        if (onItem && SelectedRows().FirstOrDefault() is { } row) SeekTo(row.Start);
    }

    private void OnTranscriptRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem container || container.IsSelected) return; // keep a multi-selection you right-click into
        TranscriptList.SelectedItems.Clear();
        container.IsSelected = true;
    }

    private void OnTranscriptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopyTranscript();
            e.Handled = true;
        }
    }

    private void OnTranscriptGoTo(object sender, RoutedEventArgs e)
    {
        if (SelectedRows().FirstOrDefault() is { } row) SeekTo(row.Start);
    }

    private void OnTranscriptCopy(object sender, RoutedEventArgs e) => CopyTranscript();

    private void CopyTranscript()
    {
        var rows = TranscriptList.SelectedItems.Count > 0 ? SelectedRows().ToList() : _transcript.ToList();
        if (rows.Count == 0) return;
        CopyToClipboard(string.Join("\n", rows.Select(r => $"[{r.TimeLabel}] {r.Text}")),
            rows.Count == 1 ? "Line copied" : $"{rows.Count} lines copied");
    }

    private void OnTranscriptMakeNote(object sender, RoutedEventArgs e)
    {
        var rows = SelectedRows().ToList();
        if (rows.Count == 0)
        {
            ShowOsd("Select one or more lines first (Ctrl or Shift to pick several)", 3);
            return;
        }
        if (_currentPath is null) return;

        var start = rows.Min(r => r.Start);
        var end = rows.Max(r => r.End);
        var text = string.Join(" ", rows.Select(r => r.Text));

        Note note;
        try { note = _library.AddNote(_currentPath, start, text, end, source: "transcript"); }
        catch (SqliteException ex)
        {
            ShowOsd($"Could not save note: {ex.Message}", 4);
            return;
        }

        var index = 0;
        while (index < _notes.Count && _notes[index].Start <= note.Start) index++;
        _notes.Insert(index, note);
        ShowOsd($"Note added from transcript  {note.TimeLabel}", 2.5);
    }
}
