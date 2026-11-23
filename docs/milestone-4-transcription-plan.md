# Milestone 4 plan: transcribe a selected section

**Goal.** Select a section of a video, get a timestamped transcript of just that section, offline, and be able to turn
lines of it into notes. The same section selection and the transcript become the input for milestone 5 (local LLM).

## Facts this plan rests on

| Fact | Source / status |
|---|---|
| Whisper.net 1.9.1 is current; the core package ships no runtime, you add `Whisper.net.Runtime` (CPU) and optionally `.Vulkan` (Windows x64) / `.NoAvx` / CUDA. Runtime order defaults to CUDA, Vulkan, CPU and can be set with `RuntimeLibraryOrder`. | NuGet pages (checked) |
| English models, quantized: tiny.en q5_1 31 MiB, base.en q5_1 57 MiB, small.en q5_1 181 MiB, medium.en q5_0 514 MiB. Old README RAM figures: tiny ≈ 390 MB, base ≈ 500 MB; none found for small/medium. | whisper.cpp model listing (checked; RAM for small+ **unverified**) |
| Whisper wants 16 kHz mono PCM. | Whisper convention (checked) |
| Pulling that audio out with a second libmpv instance (`ao=pcm`, `start`/`end`) | **Verified (spike 4b).** See results below. |
| This dev machine: i5-7200U (2 cores / 4 threads), 16 GB RAM, AMD R5 M330 (2 GB) + Intel HD 620, Vulkan loader present. | Measured here |
| Speed of Whisper on this CPU/GPU | **Unknown.** Needs a benchmark. |

The CPU is weak, so the design assumes transcription may be *slower than real time* and makes that comfortable
(streaming results, cancel, caching) instead of hoping it is fast.

## Spike results so far

**Audio extraction (second libmpv instance): works, and is fast. Decision: option A, no ffmpeg needed.**

| Input | Section | Result |
|---|---|---|
| AVI with PCM audio (22.05 kHz) | 30 s | exactly 30.00 s of 16 kHz mono 16-bit WAV in 0.68 s (first call) |
| Same AVI, whole file | 224 s | 223.97 s in 0.22 s (30 of 30 repeat runs succeeded) |
| Plain WAV | 30 s | 0.07 s |
| Section running past the end | 200–400 s of a 224 s file | returns the 24 s that exist |
| Video without an audio track | any | clear error: "This video has no audio track to transcribe." |

- Extraction costs well under 1% of real time, so it is a non-issue next to the speech recognition itself.
- One run out of the first ~35 finished with an empty output file and could not be reproduced. `MpvAudioExtractor`
  retries once automatically and only then reports "no audio in the selected section".
- Not yet tested: MKV/MP4 with AAC, files with several audio tracks (the first/default track is used), very long files.
- Package sizes (measured): `Whisper.net.Runtime` (CPU) native files ≈ 2 MB; `Whisper.net.Runtime.Vulkan` adds ≈ 55 MB
  (`ggml-vulkan-whisper.dll`). Shipping Vulkan is therefore a 55 MB decision.

**Whisper speed and memory** on this machine (i5-7200U, 2 cores / 4 threads; Whisper.net 1.9.1; quantized q5_1 English
models; the same 60 s section of clean synthetic speech each time):

| Model | Runtime | Time for 60 s | × real time | First text after | Peak memory |
|---|---|---|---|---|---|
| base.en q5_1 | CPU | 15.8 s | 0.26 (3.8× faster than playing it) | 4.8 s | 373 MB |
| base.en q5_1 | Vulkan | 14.3 s | 0.24 | 8.8 s | 508 MB |
| small.en q5_1 | CPU | 56.4 s | 0.94 (about real time) | 17.8 s | 614 MB |
| small.en q5_1 | Vulkan | 27.3 s | 0.46 (2.2× faster than playing it) | 9.6 s | 739 MB |

Long run, base.en CPU, whole 224 s: 59.3 s (0.26×), peak 411 MB, no drift or repeated text at the end.
Model load takes 0.2–0.4 s on CPU (0.7–2.3 s on Vulkan).

What this means:
- **base.en on the CPU is comfortable**: a 10-minute section transcribes in about 2.6 minutes, in under 0.5 GB.
- **small.en on the CPU takes about as long as the section itself.** Usable for "better accuracy, run it and wait", not
  as a default. With Vulkan it is about twice as fast.
- **Vulkan is a small win for base.en (about 10%) and a 2× win for small.en**, but costs 55 MB of download, about 125–135 MB
  more memory, and a slower first result (shader warm-up). Which of the two GPUs it used is unknown.
- Results arrive **in batches, one per 30-second window of audio** (about 5–6 s apart for base.en on CPU), not line by
  line. Progress in the UI therefore updates per window.
- Segment times are **relative to the start of the extracted audio**; the section's start must be added to get video time.
- **Caveat:** the test audio is clean, computer-generated speech, so accuracy was perfect. Real lectures (accents, room
  noise, jargon) will be worse, and that is where small.en earns its place. Speed does not depend much on the audio.

### Decisions from the spike
1. **Audio extraction: second libmpv instance.** No ffmpeg download.
2. **Default model: base.en q5_1 on the CPU.** Offer small.en q5_1 as "Better accuracy (slower)".
3. **Ship the CPU runtime only for now (about 2 MB).** Vulkan is deferred as an optional add-on: it only pays off for
   small.en, and it adds 55 MB. Revisit if small.en turns out to be what students need on real lecture audio.
4. **Free the model after each run.** Loading is fast, and releasing it keeps memory free for the LLM in milestone 5.
5. **Progress per 30-second window**, plus Cancel between windows.

## What 4c built (all in `SpectraPlayer.Core/Transcription`)

| Piece | Role |
|---|---|
| `MpvAudioExtractor` | Cuts a range out of any file as 16 kHz mono WAV via a second libmpv (see spike results) |
| `WhisperTranscriber` | Whisper.net on the CPU runtime; loads the model for one run and releases it; drops `[BLANK_AUDIO]`, `(music)` and similar non-speech markers |
| `TranscriptionService` | Works out which parts of the requested range are not yet transcribed, transcribes only those, stores the result, reports progress, can be cancelled without losing text |
| `ModelManager` + `WhisperModels` | Catalog of base.en and small.en (sizes and SHA-256 checked against Hugging Face's published values), resumable download, free-space check, checksum check, lists user-supplied `ggml-*.bin` files. It never downloads by itself: the UI must ask first |
| `LibraryStore` (schema v3) | `transcript_segments` and `transcribed_ranges`, cached per video and per model; upgrade from v2 tested |
| `TimeRanges`, `SegmentFilter` | Interval arithmetic and the non-speech filter |

`spikes/TranscribeSpike service <media> <model>` runs the real pipeline from the command line: fresh section, same
section again, a wider section, a cancelled run, and a resume.

### Verified end to end with real Whisper (base.en, CPU, 224 s speech video)
- Fresh 60 s section: 17–22 s, text at real video times.
- Same section again: instant, from the cache. Wider section: only the missing 0–20 s and 80–110 s were transcribed.
- Cancelled after the first window: the text found so far was kept; the next run continued from exactly where the
  stored text ended (111.8 s, then 115.3 s) and finished the stretch. Whole video ended up covered, no gaps.

### Quality problems found and fixed while testing
1. **Garbled text at the edges of a range.** Cutting audio mid-sentence made Whisper invent words ("…the net force
   acts as a force."). Now each run gets **2 s of extra audio on both sides** (`ContextPadding`); a segment is kept only if
   its centre lies inside the requested range, and keeps its real times even if it runs a little past the edge.
2. **Repeated sentences when resuming.** Two runs next to each other both recognised the sentence at the join. Fix: when
   a sentence runs past the end of a range, coverage extends to the end of that sentence, so the next run starts after
   it, and a run that starts at the end of a stored sentence gets no lead-in context.
3. **Lost words when the earlier cut was arbitrary** (my first fix for 2 overcorrected). Fixed by deciding the lead-in
   from the data: no lead-in only if a stored segment ends *exactly* where the new gap begins.

### Known limits
- A range that starts or ends in the middle of a word is still approximate at that very edge.
- Because of the rules above, coverage can be a second or two longer than the range you asked for.
- Cancelling takes effect between recognised segments, not instantly (a 30-second window of audio is processed in one go).
- The app build copies Whisper runtime files for win-x86 and win-arm64 as well as win-x64. Harmless but unneeded; trimming
  them (a `RuntimeIdentifier`) is a small packaging cleanup for later.
- Not run yet on real lecture audio, only on synthetic speech.

## What 4d built (the Transcript tab)

- **Section area** (compact): the marked section, Set start / Set end, presets, *Loop it*.
- **Transcribe section** button. While running it becomes a progress bar with the stage ("Reading audio…",
  "Transcribing… 83%") and a **Cancel** button. Lines appear in the list as each 30-second window is recognised.
- **Model: Standard (base, English) ▾** menu: base.en / small.en with "installed" or the download size, any `ggml-*.bin` you
  put in the models folder, and *Open the models folder*. The choice is remembered.
- **Consent before any download.** If the chosen model is missing, a dialog states what is downloaded, its size, the
  address it comes from, where it is saved, and that nothing from your videos leaves the computer. Nothing is fetched until you
  press Download. The download shows progress, can be cancelled, resumes next time, and is checksum-verified.
- **The list** shows everything stored for the current video and chosen model, not just the marked section. The line being
  spoken is highlighted and kept in view (not while the pointer is over the list). **Double-click** a line to seek there.
  Ctrl/Shift-click selects several lines.
- **Make note from selected lines**: one note from the selection, with start and end times (it shows as a range in the
  Notes tab and a dot on the seek bar), stored with `source = "transcript"`. **Copy** copies the selected lines (or all of
  them) as `[mm:ss] text`.
- Errors are shown in the panel in red: no audio track, libmpv missing, download failed, disk full, checksum failure.
- `SPECTRAPLAYER_MODEL_SOURCE` points downloads at a mirror (checksums still apply); this is how the download path was
  tested here, against a local web server.

### Deviations from the plan
- **Kept as a `MainWindow` partial, not a `TranscriptPanel` UserControl.** The panel shares the marked section, the
  engine and the library with the window; a UserControl would first need a view-model layer. Worth doing when `MainWindow`
  is next refactored (it is now about eight partial files).
- **Single click selects, double-click seeks** (the plan said click to seek) so that several lines can be selected for a note.

### Verified in the running app
Consent dialog (content checked, nothing downloaded before accepting) → download from the local mirror (57 MB, verified) →
transcription of a 30 s section with live lines and a progress bar → follow-along highlight → select two lines → note
stored as `transcript` with a 20.5–30.1 s range → double-click seek → Copy → cancel on the whole video (status message,
CPU idle at once, 12 lines and the covered range kept).

### Not verified yet
- Switching video or closing the app while a transcription is running (the code cancels on close and ignores live lines for
  another video, but this was not exercised).
- The small.en download and the model menu (same code path as base.en, not clicked), user-supplied models, the error
  messages shown in the panel.
- How much playback stutters while transcribing on a 2-core CPU. Whisper uses all cores; limiting it to leave one free
  is an option if it proves noticeable.
- Lists of thousands of lines (each live line is compared against the existing ones, which is fine for hundreds).

## What 4e built

**Subtitle files as a source** (`SubtitleParser`, `SubtitleFinder`, `SubtitleImporter` in `Core/Transcription`)
- A `.srt` or `.vtt` next to the video is found automatically: `Name.srt`, then English-tagged (`Name.en.srt`,
  `Name.eng.vtt`…), then any other `Name.*.srt`. Lookup is by exact name prefix, so `Lecture 10.srt` is never taken for
  `Lecture 1`. Files Spectraplayer exports itself (`*.transcript.srt`) are never treated as a source.
- The parser is tolerant: numbered or unnumbered cues, multi-line cues, WebVTT headers/notes/styles/settings, optional
  hours, comma or dot, `<i>`/`<font>` and `{\an8}` markup, HTML entities; `[Music]`, `(applause)` and `♪` lines are dropped.
  UTF-8/16 with or without a byte-order mark; anything that is not valid UTF-8 is read as Windows-1252.
- It is used by default whenever the video has one: the button reads **Get text from subtitles**, the source line says
  "Using speech.en.srt: instant, no speech model needed", and no model is needed or downloaded.
- The lines of the marked section are copied into the same transcript store (as source `subtitles`, kept apart from Whisper
  text), with the same rules as the speech pipeline: a line belongs to the section holding its centre, coverage extends to
  the end of a line that runs past the edge, so neighbouring sections never repeat a line. Everything else
  (list, follow-along, make note, copy, export) works unchanged.
- **Source** menu: tick/untick *Subtitle file (name) — instant*, *Read the subtitle file again* (forgets the imported
  lines, for when the file changed), the speech models, *Open the models folder*. Picking a model while a subtitle file
  is in use switches to the model. The preference is remembered.

**Export** (`TranscriptExport`; **Export ▾** button next to Copy)
- Exports the selected lines, or all lines when none are selected.
- *Markdown document*: `# Title — transcript`, line count and source, then `**[mm:ss]** text` paragraphs.
- *Subtitle file (.srt)*: proper SubRip with real video times; overlaps from context padding are trimmed so lines follow
  each other. The save dialog suggests `<video>.transcript.srt` in the video's folder, so mpv (and most players) show it as
  subtitles next time. This turns any video into a subtitled one using Whisper. UTF-8, no byte-order mark.

### Verified in the running app
Subtitle file found and used (label, button text and info line change; 4 lines added from a section; italic markup,
`&amp;` and "Café" handled; one `[Music]` cue dropped; a cue outside the section left out) → Export as Markdown and as SRT through
the real save dialog (default name and folder checked; files read back) → switching Source to the speech model and back
(list, button and info line follow; setting saved). mpv also displays the subtitle file on the video by itself.

### Not verified in the app (covered by unit tests)
`.vtt` files, non-UTF-8 files, *Read the subtitle file again*, exporting only selected lines. **Not built:** subtitle
tracks embedded inside the video file (MKV and similar); they would need extracting through mpv or ffmpeg.

## Design

### 1. Section selection (no AI; useful on its own, reused by milestone 5)
- `I` marks the start, `O` marks the end at the current time; both are shown as a translucent band on the seek bar.
  Also Shift+drag on the seek bar. Presets in the panel: *Last 2 min*, *Whole video*, *Clear*.
- A selection chip in the panel shows `12:34 – 15:10 (2:36)` with Set start / Set end / Clear.
- Session-only state (not saved), one selection at a time.

### 2. Where the text comes from
1. **Sidecar subtitles** (`.srt` / `.vtt` next to the video): parse and slice by range. Instant, exact, no model.
   Embedded subtitle tracks are a later stretch (they need extraction through mpv or ffmpeg).
2. **Whisper** on the extracted audio of the selected range only.

### 3. Audio extraction (decision gate in the spike)
- `IAudioExtractor` with two implementations:
  - **A. Second libmpv instance** writing 16 kHz mono WAV for `[start, end]`. No extra download. Unverified.
  - **B. `ffmpeg.exe`** fetched by the setup script (`tools/Get-FFmpeg.ps1`, same pattern as libmpv). Certain, costs an
    extra download of tens of MB.
- Pick A if the spike shows it is accurate and fast enough; otherwise B. The rest of the code does not care.

### 4. Whisper
- `Whisper.net` + `Whisper.net.Runtime` + `Whisper.net.Runtime.Vulkan`; Vulkan is used automatically when a working GPU
  is found, with CPU fallback. Benchmark both on this machine before deciding which to prefer by default.
  No CUDA runtime (very large, NVIDIA only).
- English-only `.en` models first. Default is chosen by the benchmark; expected to be **base.en q5_1**, with
  **small.en q5_1** as the "better accuracy, slower" option. (`tiny.en` if even base is too slow.)
- Run off the UI thread; stream segments to the UI as they are produced; progress bar with elapsed/estimated time;
  **Cancel** at any moment.
- Try whisper.cpp's voice-activity detection if Whisper.net exposes it (skips silence, reduces made-up text on music or
  silence); otherwise filter by the no-speech probability.
- One heavy model at a time: dispose the Whisper model before milestone 5 loads the LLM (8 GB machines).

### 5. Models: first-run download, never bundled
- Settings-free: the first time you press *Transcribe*, a dialog names the model, size and source
  (`ggerganov/whisper.cpp` on Hugging Face) and asks before downloading. Resumable, progress, SHA-256 check.
- Stored in `%LOCALAPPDATA%\Spectraplayer\models`. `SPECTRAPLAYER_DATA` overrides it, like the database.
- `ModelManager` lists installed models and lets a power user drop in their own `.bin` file.

### 6. Storage (schema v3, using the existing step-by-step migration)
- `transcript_segments(id, video_path, start_ms, end_ms, text, source, model, created_utc)`, path keyed like notes.
- `transcribed_ranges(video_path, start_ms, end_ms, model)`, merged when they overlap, so asking for a section that is
  already covered returns instantly and a new overlapping request only transcribes the gap.
- Segment times are absolute video times, not relative to the selection.

### 7. UI: a *Transcript* view in the side panel
- Panel switcher becomes `Playlist | Notes | Transcript | Bookmarks`; at 320 px that is tight, so the labels shorten or
  the panel gets a little wider.
- Contents: selection chip, **Transcribe section** button with progress and Cancel, the segment list.
- Click a line to seek; the line playing right now is highlighted and scrolls into view (follow-along).
- Select lines, then **Make note**: creates a note with a start and an end (the `End` field already exists) and
  `source = "transcript"`. Also copy, and export the section as Markdown or `.srt`.

### 8. Code layout
- `SpectraPlayer.Core/Transcription/`: `ITranscriber`, `WhisperTranscriber`, `IAudioExtractor` (+ implementations),
  `SubtitleParser`, `ModelManager`, `TranscriptStore` (inside `LibraryStore`).
- App: a `TranscriptPanel` **UserControl** with its own view model rather than one more `MainWindow` partial; the main
  window is already large. The seek bar gets a `Selection` property.

## Steps and what "done" means

| # | Step | Done when |
|---|---|---|
| 4a | Section selection UI | **Done.** I/O keys, Shift+drag and presets work; band on the seek bar; loop preview; 14 tests for the selection logic |
| 4b | **Spike and benchmark** | **Done.** Audio extraction verified and Whisper benchmarked on CPU and Vulkan (results and decisions above). The throw-away tool is `spikes/TranscribeSpike` |
| 4c | Core pipeline | **Done.** Range transcription with progress, cancel and cache; model download (resume, free-space check, checksum); schema v3 with upgrade test; verified end to end with real Whisper (notes below). 53 new tests |
| 4d | Transcript panel | **Done.** Transcribe from the UI with consent and download, live lines, cancel, follow-along, seek, make note, copy (details below) |
| 4e | Subtitles and export | **Done.** Sidecar `.srt`/`.vtt` used as an instant source; Markdown and `.srt` export (details below). **Milestone 4 is complete** |

## Testing approach
- **Unit:** subtitle parser, range merge/gap logic, model manager against a fake HTTP server, store and migration.
- **Real speech without downloads:** generate a WAV of a known sentence with Windows' built-in speech synthesizer
  (`System.Speech`) and check the transcript contains it. The generated test videos have no audio, so the extractor is
  tested on that WAV and on an audio file with a video-like container if one can be produced.
- **App:** drive the running app as in earlier milestones (screenshots, saved database state).

## Risks and open questions
- **Speed.** On a 2-core i5-7200U, small.en may run well below real time. The benchmark decides the default; the UI is
  built so a slow run is still usable (partial results, cancel, cache). Possible later extra: transcribe the next
  video in the background while idle.
- **Vulkan on old GPUs** may be slower than the CPU, or crash on some drivers; the runtime order must be easy to force.
- **Hallucinated text** on silence and music; mitigated by VAD / no-speech filtering, not eliminated.
- **Package size:** native runtimes add to the download; size unverified.
- **Non-English audio:** English models only for now; a multilingual model option can follow.
- **Permissions during development:** the benchmark needs the Whisper.net NuGet packages (normal restore) and one or two
  model files (about 57 and 181 MiB); I will ask before downloading the models.
