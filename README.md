<p align="center"><img src="docs/logo.png" width="112" alt="Spectraplayer logo"></p>

# Spectraplayer

A simple, PotPlayer-style video player for Windows, built for studying: **one tab per subject**, each tab a folder shown
as a playlist, with resume positions, PotPlayer-compatible bookmarks, timestamped notes, and transcripts of any section of
a video made on your own computer. Open source (GPL-3.0 recommended; the libmpv builds in common use are GPL).

**Status: first version.** Player, tabs and playlists, bookmarks, notes, section marking, offline transcription with
subtitle-file support and export, and a finished look (logo, title bar, welcome screen). A language model for asking
questions about a section was designed but is not part of this version (`docs/milestone-5-llm-plan.md`).

## Look and feel

- **Dark, one window.** The title bar is drawn by the app (Windows would otherwise colour it with your accent colour) and holds
  the logo, the tabs and the window buttons, like a browser. Dialogs share the same look.
- **Colours mean something.** Blue is progress and actions, **amber** marks bookmarks, **teal** notes, **violet** the section you
  marked, coral a problem. The same colours are used on the seek bar and in the panels.
- **Made by NeuSpectra:** the company name and logo appear on the welcome screen, in About, in the setup program and in the
  executable's properties.
- **Welcome screen** when nothing is open: open a video, add a folder as a tab, or continue one of your subjects; the
  main shortcuts (play/pause, bookmark, speed, notes, sections, tabs) are listed there and **F1** shows all of them. You can drop a
  video or a folder onto the window instead (a folder becomes a tab).
- The current video and its subject are shown in the control bar. Click the logo for **About**.
- The logo is drawn once in `src/SpectraPlayer.App/Assets/Logo.xaml` and used by the window; `tools/IconGen` renders the same
  drawing into the `.ico` (run `dotnet run --project tools/IconGen` after changing it).

## Installer

`installer\Build-Installer.ps1` builds **`artifacts\installer\Spectraplayer-Setup-<version>.exe`**, one file (about 133 MB) that
carries everything the app needs on 64-bit Windows: the .NET runtime, the video engine (libmpv), the speech engine, SQLite and,
optionally, the speech model so transcripts work offline straight away. Nothing else is meant to be installed first.
(So far it has been tested only on the development machine, Windows 10 with the .NET SDK present; try it on a computer
without .NET before relying on that.)

- **For the user:** double-click, press Next. It installs for the current user only (no administrator prompt) into
  `%LOCALAPPDATA%\Programs\Spectraplayer`, adds a Start Menu entry, and offers a desktop shortcut, an "Open with" entry for video
  files, and the speech model (ticked). The wizard shows the NeuSpectra and Spectraplayer logos; the publisher is NeuSpectra.
- **Uninstall** (Settings > Apps, or the Start Menu) removes the program. It then asks whether to delete your library (tabs, notes,
  transcripts, watch progress, the speech model); No keeps it for a reinstall. Bookmark files next to your videos are never touched.
- **To build it:** you need the .NET 8 SDK, [Inno Setup 6](https://jrsoftware.org/isinfo.php) and `libs\libmpv\libmpv-2.dll`
  (`tools\Get-LibMpv.ps1`):

  ```powershell
  .\installer\Build-Installer.ps1 -DownloadModel          # publishes, fetches the model (checksum-verified), builds the setup
  .\installer\Build-Installer.ps1 -ModelPath D:\ggml-base.en-q5_1.bin   # use a copy you already have (it is only read)
  .\installer\Build-Installer.ps1                         # without the model: the app asks before downloading it on first use
  ```

- **Licences:** the setup includes mpv/libmpv, which is GPL, and installs `THIRD-PARTY-NOTICES.txt` next to the program. Shipping a
  GPL component brings obligations for how Spectraplayer itself is licensed and distributed (a libmpv build under the LGPL would
  leave more freedom). Decide the project's own licence, and have it checked if needed, before publishing the setup.
- For testing, `setup.exe /DIR=<folder> /DATADIR=<folder>` installs somewhere else (set the `SPECTRAPLAYER_DATA` environment
  variable to the same data folder when running the app), and `/VERYSILENT` installs without any windows.

## Build and run

Requires the .NET 8 SDK and a 64-bit `libmpv-2.dll` (mpv 0.38 or newer).

```powershell
# 1. Get libmpv (needs 7-Zip), or drop libmpv-2.dll into libs\libmpv yourself
.\tools\Get-LibMpv.ps1

# 2. Build, test, run
dotnet build SpectraPlayer.sln
dotnet test tests\SpectraPlayer.Core.Tests
dotnet run --project src\SpectraPlayer.App
```

Passing a video path opens it (inside a tab's folder it plays as part of that tab). Without libmpv the app still starts,
shows your tabs and playlists, and tells you what is missing.

## Layout

| Path | What |
|---|---|
| `src/SpectraPlayer.Core` | UI-free library: `IPlayerEngine`, libmpv engine (`Playback/Mpv`), `.pbf` bookmarks, folder scanner, natural sort, SQLite library with notes and Markdown export (`Library/`), transcription pipeline (`Transcription/`) |
| `spikes/TranscribeSpike` | Command-line benchmark and end-to-end check for transcription (not part of the solution) |
| `tools/IconGen` | Renders the logos into `spectraplayer.ico`, `docs/logo.png`, the NeuSpectra variant used in the app and the installer's pictures |
| `installer/` | The setup program: Inno Setup script, build script, notices, wizard pictures (see *Installer*) |
| `branding/` | The NeuSpectra company logo (source for the generated images) |
| `src/SpectraPlayer.App` | WPF app: theme, seek bar with bookmark and note markers, tab strip, playlist/notes/bookmarks panel, video host, fullscreen overlay |
| `tests/SpectraPlayer.Core.Tests` | xUnit tests for bookmarks, scanner, sorting, the library database (including schema upgrade) and Markdown export |
| `tools/Get-LibMpv.ps1` | Fetches libmpv into `libs/libmpv` |

The UI only talks to `IPlayerEngine`, so the libmpv backend can be replaced (e.g. LibVLC) without touching it.

## Tabs and playlists

- **+** (or `Ctrl+T`) adds folders as tabs; select several folders to add several subjects at once. Tabs are remembered
  between launches, as are the active tab and each tab's last video.
- A tab's playlist is every video under its folder (subfolders included), in natural order (`Lecture 2` before
  `Lecture 10`). It is read from disk whenever the tab is opened, so new files appear on their own; *Rescan folder* in
  the tab menu refreshes it manually.
- Switching tabs saves where you were and resumes the other subject's last video at its saved position. At launch the
  last session is restored **paused**.
- Each video's position is saved every ~5 s and on exit. The playlist shows a thin progress bar for videos in progress
  and a check mark once 90% has been watched. A video that ends moves on to the next in the tab. *Mark as watched /
  unwatched* is in the playlist right-click menu.
- Every video in the playlist shows its length (`12:34`, or `1:02:03`), and the header shows the length of the whole
  playlist as a clock (`08:42:15`, with a `+` while it is still being worked out; hover for the time still to watch).
  Lengths are read in the background by a second, headless libmpv, a few per second, and remembered in the library, so a
  folder is measured once (again only for files that change).
- Tab right-click menu: rename, rescan, show in Explorer, move left/right, close. Middle-click closes a tab. Closing a
  tab never touches the files; its progress stays in the library.
- Data lives in `%LOCALAPPDATA%\Spectraplayer\library.db` (SQLite). Set the `SPECTRAPLAYER_DATA` environment variable to
  use another folder.

## Notes

- Press **N** while watching: the Notes panel opens with the cursor in the note box. Type, then **Ctrl+Enter** (or *Add
  note*) to save. **Esc** hands the keyboard back to the player shortcuts.
- The timestamp shows *now 12:34* and follows playback while the box is empty; it locks (*at 12:34*) when you start
  typing, so a note is stamped with the moment the thought began. The ⟳ button re-locks it to the current time. If the
  next lecture starts while you are still typing, the note stays with the video it was started on.
- Notes appear as teal dots under the seek bar (hover for a preview) and in the panel. Double-click a note to jump to it;
  right-click or `F2` / `Del` to edit or delete it. Multi-line notes and Markdown in the text are kept as typed.
- The **⋯** button exports or copies notes as Markdown: one video (`- **[12:34]** text` bullets), or the whole active
  tab as one document with a section per video, in playlist order. Pastes cleanly into Obsidian or Notion.
- Notes are stored in the library database, keyed by the video's full path, so moving or renaming a file detaches its
  notes. Each note also has an optional end time and a `source` field; both are reserved for the section-based and
  AI-generated notes planned next.

## Marking a section

The **Transcript** tab in the side panel is where you pick the stretch of a video to work on and turn its speech into text.

- Press **I** to mark the start and **O** to mark the end while watching, or hold **Shift** and drag on the seek bar.
  Marking in either order works; the earlier point becomes the start.
- The section shows as a band with `[ ]` brackets on the seek bar. Presets: *Last 2 min*, *Last 5 min*,
  *Whole video*, *Clear*. *Loop this section* plays only the marked part so you can check it.
- The section belongs to the current video and is cleared when you open another.

## Transcribing a section

Everything runs on your computer; nothing from your videos is sent anywhere.

1. Mark a section (above), then press **Transcribe section**.
2. The first time, you are asked before a speech model is downloaded (the standard one is 57 MB, a more accurate one is
   181 MB; choose with the **Model** button). Models live in `%LOCALAPPDATA%\Spectraplayer\models`; you can also drop your
   own `ggml-*.bin` file there. After that, transcribing works offline.
3. Lines appear as they are recognised, with a progress bar and a **Cancel** button. Cancelling keeps the text found so far;
   running it again carries on from there. Text is saved, so asking for the same section again is instant.
4. While the video plays, the line being spoken is highlighted. **Double-click** a line to jump there. Select lines (Ctrl or
   Shift for several) and press **Make note from selected lines** to turn them into a note with a start and end time, or
   **Copy** them.

**Subtitle files.** If a `.srt` or `.vtt` file sits next to the video (`Lecture 1.srt`, `Lecture 1.en.srt`…), the button
becomes **Get text from subtitles**: the lines of the marked section appear instantly and exactly, with no speech model.
Choose between the subtitle file and a speech model under **Source**.

**Export.** **Export ▾** saves the selected lines (or all of them) as a Markdown document or as a `.srt` subtitle file.
The suggested name `<video>.transcript.srt` sits next to the video, so players show it as subtitles; this gives any video
subtitles made by Whisper.

On a modest laptop (2-core i5, no usable GPU) the standard model transcribes about four times faster than the video plays;
the accurate one takes about as long as the video. Details and measurements: `docs/milestone-4-transcription-plan.md`.
Set `SPECTRAPLAYER_MODEL_SOURCE` to a folder-style URL to download models from your own mirror (checksums still apply).

## Start-up

Spectraplayer opens on its welcome screen for about three and a half seconds, then fades into the playlist that was open (the
last video, paused where you left it). A click or key press skips the wait. With nothing to resume, the welcome screen stays.
Opening a video directly (double-click, *Open with*) skips it altogether and just plays.

## Focus timer

Study in timed sessions with breaks. Open the **Focus** tab in the side panel (press `T`, or click the timer in the control
bar). Four sessions make a round; the ring shows what is left of the current part.

- **Start / pause** with the big button or `Shift+T` (which also says what it did on the video, so the panel needn't be open);
  the round button resets the part, the arrow skips to the next one. Skipping a session does not count it.
- **Length**: 25 / 5, 50 / 10, 90 / 15 or 40 / 8 minutes (focus / break).
- When a part ends there is a soft chime and a message on the video. With *Pause the video during breaks* (on by default)
  the video pauses for the break and plays on when it is over, unless you started it again yourself.
  *Count only while the video plays* stops the clock whenever the video is paused (breaks always run).
- While a timer is going, a small ring with the time sits in the control bar. In fullscreen it also shows in the top-right
  corner whenever the control bar is up (and for a few seconds after a part ends), then fades out so it never gets in the way.
- Today's focused time and session count are kept; so are the chosen length and the three options.

## Keyboard and mouse

| Input | Action |
|---|---|
| Space / left click | Play / pause |
| ← / → , Ctrl+← / → | Seek 5 s / 30 s |
| ↑ / ↓ , mouse wheel | Volume |
| M | Mute |
| Enter / F11 / double-click, Esc | Fullscreen / leave fullscreen |
| PgUp / PgDn | Previous / next video in the tab |
| N | Take a note at the current time (Ctrl+Enter saves, Esc leaves the box) |
| I / O, Shift+drag on seek bar | Mark section start / end |
| P / H | Show or hide the playlist / bookmarks panel (also the hide button at the top of the panel) |
| B | Add bookmark at the current time |
| [ / ] | Previous / next bookmark |
| Ctrl+T / Ctrl+W | Add folder as tab / close tab |
| Ctrl+Tab, Ctrl+Shift+Tab, Ctrl+1…9 | Switch tabs |
| C / X, or > / < | Speed up / slow down by 0.25× (the speed button in the control bar shows it and offers presets) |
| Z | Normal speed |
| F1 | All keyboard shortcuts |
| F / D | Frame step forward / back |
| Ctrl+O | Open file |
| Right click | Menu |
| Ctrl+, | Settings |
| T / Shift+T | Show the focus timer / start or pause it |

The panel slides in and out. While it is hidden, and in fullscreen, moving the mouse to the right edge of the screen slides it
in over the video, and it slides away again when the mouse leaves. Playlist, Notes, Transcript and Bookmarks all work that way
in fullscreen too (N opens Notes there and keeps the panel up while you type).

### Changing the shortcuts

These are the defaults. Open **Settings** (the gear in the title bar, Ctrl+, or the right-click menu; the F1 list has a
*Change shortcuts…* button too) to change any of them:

- click a key, then press the new one; Esc cancels;
- **+** gives an action a second key, **×** removes a key, the arrow next to a row puts that action back to its default;
- a key belongs to one action: if the one you press is taken, it moves over and Settings says which action lost it;
- changes apply straight away and are remembered (in the library database, so they survive updates; a key added to the
  defaults in a later version still reaches actions you never changed);
- the tooltips, the right-click menu, the F1 list and the welcome screen always show your keys.

Not every combination is allowed: Alt+F4 and Alt+Space belong to Windows, and Ctrl/Alt+Shift with a number or a symbol key
is refused because what that types depends on the keyboard layout (the video surface couldn't recognise it).

The key table lives in `Core/Shortcuts` (plain text, unit-tested) and `Input/KeyMap.cs`; it feeds both WPF and mpv,
because the video surface can take keyboard focus for itself.

## Bookmarks and PotPlayer `.pbf`

- Bookmarks are saved automatically as `<video name>.pbf` next to the video, which PotPlayer also picks up as chapters.
- When a video is opened, Spectraplayer looks for an existing `.pbf` next to it, then in PotPlayer's own bookmark folders
  (`%APPDATA%\PotPlayerMini64\Bookmark`, `...\PotPlayerMini\Bookmark`). Bookmarks found there are copied to the sidecar
  file on the next change; PotPlayer's copy is never modified.
- Right-click → Import / Export bookmarks works with any `.pbf` file.
- Format handled: UTF-16 text, `[Bookmark]` header, `index=milliseconds*name*opaque` lines, closing `count=` line.
  The opaque field (PotPlayer's thumbnail data) is preserved on round trips. This format comes from community
  documentation, not an official spec; it has been tested here against the documented layout only.

## Roadmap

1. ~~Player shell, bookmarks~~
2. ~~Tabs: one folder per tab as a playlist, remembered between launches, resume positions (SQLite)~~
3. ~~Notes panel: timestamped notes, Markdown export~~
4. ~~Transcription of a selected range (Whisper), subtitle files, export~~
5. Local LLM (llama.cpp sidecar) for questions and summaries on the selected range
6. OCR of on-screen slides (Windows built-in OCR)
