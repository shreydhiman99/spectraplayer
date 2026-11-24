package com.neuspectra.spectraplayer

import android.app.Application
import android.content.Intent
import android.media.RingtoneManager
import android.net.Uri
import android.os.SystemClock
import android.provider.OpenableColumns
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.neuspectra.spectraplayer.core.FocusPhase
import com.neuspectra.spectraplayer.core.FocusTimer
import com.neuspectra.spectraplayer.core.MsRange
import com.neuspectra.spectraplayer.core.Note
import com.neuspectra.spectraplayer.core.TranscriptExport
import com.neuspectra.spectraplayer.core.TranscriptSegment
import com.neuspectra.spectraplayer.transcribe.ModelInfo
import com.neuspectra.spectraplayer.transcribe.ModelManager
import com.neuspectra.spectraplayer.transcribe.NoAudioException
import com.neuspectra.spectraplayer.transcribe.TranscriptionService
import com.neuspectra.spectraplayer.transcribe.WhisperModels
import kotlinx.coroutines.CancellationException
import java.io.IOException
import com.neuspectra.spectraplayer.core.NotesMarkdown
import com.neuspectra.spectraplayer.core.NotesSection
import com.neuspectra.spectraplayer.core.PbfBookmark
import com.neuspectra.spectraplayer.core.PbfFile
import com.neuspectra.spectraplayer.core.TimeFormat
import com.neuspectra.spectraplayer.data.BookmarkRecord
import com.neuspectra.spectraplayer.data.DurationService
import com.neuspectra.spectraplayer.data.FolderScanner
import com.neuspectra.spectraplayer.data.Library
import com.neuspectra.spectraplayer.data.MediaEntry
import com.neuspectra.spectraplayer.data.ProgressRecord
import com.neuspectra.spectraplayer.data.TabRecord
import com.neuspectra.spectraplayer.player.PlayerHost
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlin.math.abs
import kotlin.math.min

enum class Panel(val label: String) {
    Playlist("Playlist"), Notes("Notes"), Bookmarks("Bookmarks"), Transcript("Transcript"), Focus("Focus"),
}

/** One row of the playlist: the file, how far through it the student is, and how long it is. */
data class PlaylistItem(
    val entry: MediaEntry,
    val progress: Double = 0.0,
    val watched: Boolean = false,
    /** null until measured; 0 if the length can't be read. */
    val durationMs: Long? = null,
)

/**
 * Everything the screens show, and everything they can ask for. Owns the player so playback survives rotation.
 * (The counterpart of the Windows app's main window logic.)
 */
class AppViewModel(app: Application) : AndroidViewModel(app) {
    private val library = Library(app)
    private val durations = DurationService(app, library)
    val host = PlayerHost(app)
    val focus = FocusTimer()
    private val models = ModelManager(app)
    private val transcription = TranscriptionService(app, library, models)

    // ---- what the screens read -------------------------------------------------------------------------------

    var tabs by mutableStateOf<List<TabRecord>>(emptyList()); private set
    var activeTabId by mutableLongStateOf(-1L); private set
    var items by mutableStateOf<List<PlaylistItem>>(emptyList()); private set
    var scanning by mutableStateOf(false); private set

    var current by mutableStateOf<MediaEntry?>(null); private set
    var notes by mutableStateOf<List<Note>>(emptyList()); private set
    var bookmarks by mutableStateOf<List<BookmarkRecord>>(emptyList()); private set

    /** A PotPlayer .pbf next to the current video, if there is one (offered for import). */
    var siblingBookmarkFile by mutableStateOf<String?>(null); private set

    var positionMs by mutableLongStateOf(0); private set
    var durationMs by mutableLongStateOf(0); private set
    var playing by mutableStateOf(false); private set
    var speed by mutableStateOf(1f); private set

    var panel by mutableStateOf(Panel.Playlist)
    var fullscreen by mutableStateOf(false)

    // transcripts
    var sectionStartMs by mutableStateOf<Long?>(null); private set
    var sectionEndMs by mutableStateOf<Long?>(null); private set
    var transcript by mutableStateOf<List<TranscriptSegment>>(emptyList()); private set
    var transcribing by mutableStateOf(false); private set
    var transcribeProgress by mutableStateOf(0f); private set
    var transcribeStage by mutableStateOf(""); private set
    var transcribeError by mutableStateOf<String?>(null); private set
    var transcribeModel by mutableStateOf(WhisperModels.default); private set
    var modelInstalled by mutableStateOf(false); private set

    /** The model the person is being asked to allow downloading (null when no question is open). */
    var modelPrompt by mutableStateOf<ModelInfo?>(null); private set

    /** Download progress 0–1 while a model is being fetched, otherwise null. */
    var modelDownload by mutableStateOf<Float?>(null); private set

    /** A short message for a snackbar; the screen shows it and calls [messageShown]. */
    var message by mutableStateOf<String?>(null); private set

    /** Changes whenever the focus timer does (it isn't observable by itself). */
    var focusVersion by mutableIntStateOf(0); private set

    val activeTab: TabRecord? get() = tabs.firstOrNull { it.id == activeTabId }
    val currentIndex: Int get() = items.indexOfFirst { it.entry.uri == current?.uri }

    /** Length of the whole playlist so far (videos not yet measured count as zero), and how many still wait. */
    val totalDurationMs: Long get() = items.sumOf { it.durationMs ?: 0 }
    val unmeasured: Int get() = items.count { it.durationMs == null }

    // ---- private state ---------------------------------------------------------------------------------------

    private var bookmarkFiles: Map<String, String> = emptyMap()
    private var measureJob: Job? = null
    private var scanJob: Job? = null
    private var pausedByFocus = false
    private var learnedDuration = false
    private var transcribeJob: Job? = null
    private var downloadJob: Job? = null

    init {
        host.onEnded = { onEnded() }
        host.onError = { text -> message = "Can't play this file: $text" }

        focus.onChanged = { focusVersion++ }
        focus.onPartEnded = { onFocusPartEnded(it) }

        transcribeModel = WhisperModels.byId(library.getSetting("model"))
        modelInstalled = models.isInstalled(transcribeModel)

        viewModelScope.launch {
            val (savedTabs, focusJson, activeId) = withContext(Dispatchers.IO) {
                Triple(library.getTabs(), library.getSetting("focus"), library.getSetting("activeTab")?.toLongOrNull())
            }
            focus.loadJson(focusJson)
            tabs = savedTabs
            val tab = savedTabs.firstOrNull { it.id == activeId } ?: savedTabs.firstOrNull()
            if (tab != null) selectTab(tab, autoPlay = false) // resume paused: no surprise sound at launch
        }

        viewModelScope.launch { tickLoop() }
    }

    // ---- the one loop that keeps the screen, progress and focus timer moving ---------------------------------

    private suspend fun tickLoop() {
        var last = SystemClock.elapsedRealtime()
        var saveTicks = 0
        var progressTicks = 0
        while (true) {
            delay(250)
            val now = SystemClock.elapsedRealtime()
            val elapsed = min(now - last, 1000L) // a stall (or sleep) isn't study time
            last = now

            positionMs = host.positionMs
            durationMs = host.durationMs
            playing = host.isPlaying
            speed = host.speed
            learnDurationOnce()

            if (focus.running) {
                // "Count only while the video plays" is about focusing; breaks always run (the video is paused for them).
                val counting = !(focus.countOnlyWhilePlaying && focus.phase == FocusPhase.Focus && !playing)
                if (counting) focus.tick(elapsed)
                if (++saveTicks >= 240) { // about once a minute, so today's total survives a crash
                    saveTicks = 0
                    saveFocus()
                }
            }

            if (playing && ++progressTicks >= 20) { // every ~5 s of playback
                progressTicks = 0
                saveCurrentProgress()
            }
        }
    }

    // ---- tabs and playlists ----------------------------------------------------------------------------------

    fun addFolder(treeUri: Uri) {
        viewModelScope.launch {
            val ctx = getApplication<Application>()
            val tab = withContext(Dispatchers.IO) {
                runCatching { ctx.contentResolver.takePersistableUriPermission(treeUri, Intent.FLAG_GRANT_READ_URI_PERMISSION) }
                library.addTab(FolderScanner.displayName(ctx, treeUri), treeUri.toString())
            }
            tabs = withContext(Dispatchers.IO) { library.getTabs() }
            selectTab(tab, autoPlay = true)
        }
    }

    fun selectTab(tab: TabRecord, autoPlay: Boolean, preferredUri: String? = null) {
        saveCurrentProgress()
        activeTabId = tab.id
        scanning = true
        measureJob?.cancel()
        scanJob?.cancel()
        scanJob = viewModelScope.launch {
            withContext(Dispatchers.IO) { library.setSetting("activeTab", tab.id.toString()) }
            val contents = withContext(Dispatchers.IO) {
                runCatching { FolderScanner.scan(getApplication(), Uri.parse(tab.treeUri)) }
            }.getOrElse {
                items = emptyList()
                scanning = false
                message = "Can't open this folder any more. Remove the tab and add the folder again."
                return@launch
            }
            if (activeTabId != tab.id) return@launch // the student moved on while the folder was being read

            bookmarkFiles = contents.bookmarkFiles
            val progress = withContext(Dispatchers.IO) { library.getProgress(contents.entries.map { it.uri }) }
            val known = withContext(Dispatchers.IO) { durations.loadKnown(contents.entries) }
            items = contents.entries.map { e -> toItem(e, progress[e.uri], known[e.uri]) }
            scanning = false

            val pick = items.firstOrNull { it.entry.uri == preferredUri }
                ?: items.firstOrNull { it.entry.uri == tab.lastVideo }
                ?: items.firstOrNull { !it.watched }
                ?: items.firstOrNull()
            if (pick != null) playEntry(pick.entry, paused = !autoPlay)
            else if (autoPlay) stopPlayback()

            startMeasuring(contents.entries.filter { it.uri !in known })
        }
    }

    private fun toItem(e: MediaEntry, p: ProgressRecord?, duration: Long?) =
        PlaylistItem(e, p?.fraction ?: 0.0, p?.isWatched ?: false, duration)

    private fun startMeasuring(pending: List<MediaEntry>) {
        if (pending.isEmpty()) return
        val tabId = activeTabId
        measureJob = viewModelScope.launch {
            durations.measure(pending) { uri, ms ->
                if (activeTabId != tabId) return@measure
                items = items.map { if (it.entry.uri == uri) it.copy(durationMs = ms) else it }
            }
        }
    }

    fun renameTab(tab: TabRecord, title: String) {
        val t = title.trim().ifEmpty { return }
        viewModelScope.launch(Dispatchers.IO) { library.renameTab(tab.id, t) }
        tabs = tabs.map { if (it.id == tab.id) it.copy(title = t) else it }
    }

    fun removeTab(tab: TabRecord) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) { library.removeTab(tab.id) }
            val remaining = withContext(Dispatchers.IO) { library.getTabs() }
            tabs = remaining
            if (tab.id == activeTabId) {
                if (remaining.isNotEmpty()) selectTab(remaining.first(), autoPlay = false)
                else {
                    activeTabId = -1
                    items = emptyList()
                    stopPlayback()
                }
            }
        }
    }

    /** Re-reads the active folder (new files appear) without disturbing what is playing. */
    fun rescan() {
        val tab = activeTab ?: return
        selectTab(tab, autoPlay = false, preferredUri = current?.uri)
    }

    // ---- playback --------------------------------------------------------------------------------------------

    fun play(item: PlaylistItem) = playEntry(item.entry, paused = false)

    /** Opens a single file chosen with the file picker or "Open with" (not part of any tab). */
    fun openExternal(uri: Uri) {
        val ctx = getApplication<Application>()
        runCatching { ctx.contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION) }
        var name = uri.lastPathSegment ?: "Video"
        ctx.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
            if (c.moveToFirst()) c.getString(0)?.let { name = it }
        }
        playEntry(MediaEntry(uri.toString(), name, name, 0, 0), paused = false)
    }

    private fun playEntry(entry: MediaEntry, paused: Boolean) {
        saveCurrentProgress()
        val record = library.getProgress(entry.uri)
        val start = resumePoint(record, rewind = !paused)

        current = entry
        positionMs = start
        durationMs = items.firstOrNull { it.entry.uri == entry.uri }?.durationMs ?: 0
        learnedDuration = false
        host.load(entry.uri, start, play = !paused)

        activeTab?.let { tab ->
            library.setLastVideo(tab.id, entry.uri)
            tabs = tabs.map { if (it.id == tab.id) it.copy(lastVideo = entry.uri) else it }
        }
        notes = library.getNotes(entry.uri)
        bookmarks = library.getBookmarks(entry.uri)
        siblingBookmarkFile = bookmarkFiles[entry.relativePath.substringBeforeLast('.').lowercase()]
        sectionStartMs = null
        sectionEndMs = null
        loadTranscript()
    }

    /** Where to resume a video, or 0 to start from the beginning (finished or barely begun). A 2 s lead-in gives context. */
    private fun resumePoint(record: ProgressRecord?, rewind: Boolean): Long {
        if (record == null || record.fraction >= 0.95 || record.positionMs < 5_000) return 0
        return if (rewind) record.positionMs - 2_000 else record.positionMs
    }

    fun playNext() {
        val i = currentIndex
        if (i >= 0 && i + 1 < items.size) playEntry(items[i + 1].entry, paused = false) else message = "Last video"
    }

    fun playPrevious() {
        // Like most players: a few seconds in, "previous" restarts the current video first.
        if (host.positionMs > 3_000) { host.seekTo(0); return }
        val i = currentIndex
        if (i > 0) playEntry(items[i - 1].entry, paused = false) else message = "First video"
    }

    private fun onEnded() {
        val cur = current ?: return
        val duration = host.durationMs
        if (duration > 0) {
            library.saveProgress(cur.uri, duration, duration)
            items = items.map { if (it.entry.uri == cur.uri) it.copy(progress = 1.0, watched = true) else it }
        }
        val i = currentIndex
        if (i >= 0 && i + 1 < items.size) playEntry(items[i + 1].entry, paused = false)
        else if (i >= 0) message = "End of playlist"
    }

    fun togglePlay() = host.toggle()
    fun seekTo(ms: Long) { host.seekTo(ms); positionMs = ms }
    fun seekBy(deltaMs: Long) { host.seekBy(deltaMs); positionMs = host.positionMs }

    fun changeSpeed(value: Float) { host.speed = value; speed = host.speed }

    fun stopPlayback() {
        saveCurrentProgress()
        host.stop()
        current = null
        notes = emptyList()
        bookmarks = emptyList()
        siblingBookmarkFile = null
        transcript = emptyList()
        sectionStartMs = null
        sectionEndMs = null
        positionMs = 0
        durationMs = 0
    }

    /** Remember where the student is, so a crash or a swipe away loses almost nothing. */
    fun saveCurrentProgress() {
        val cur = current ?: return
        val duration = host.durationMs
        if (duration <= 0) return
        val position = host.positionMs
        library.saveProgress(cur.uri, position, duration)
        val rec = ProgressRecord(cur.uri, position, duration)
        items = items.map { if (it.entry.uri == cur.uri) it.copy(progress = rec.fraction, watched = rec.isWatched) else it }
    }

    /** First time the player reports the real length: keep it (no need to measure that file later) and show it. */
    private fun learnDurationOnce() {
        val cur = current ?: return
        if (learnedDuration || durationMs <= 0) return
        learnedDuration = true
        val item = items.firstOrNull { it.entry.uri == cur.uri } ?: return
        if (item.durationMs != null && abs(item.durationMs - durationMs) < 1000) return
        durations.remember(cur, durationMs)
        items = items.map { if (it.entry.uri == cur.uri) it.copy(durationMs = durationMs) else it }
    }

    fun setWatched(item: PlaylistItem, watched: Boolean) {
        if (watched) library.markWatched(item.entry.uri, item.durationMs ?: 0) else library.clearProgress(item.entry.uri)
        items = items.map {
            if (it.entry.uri == item.entry.uri) it.copy(progress = if (watched) 1.0 else 0.0, watched = watched) else it
        }
    }

    // ---- notes -----------------------------------------------------------------------------------------------

    fun addNote(startMs: Long, text: String) {
        val cur = current ?: run { message = "Open a video first"; return }
        val t = text.trim().ifEmpty { return }
        library.addNote(cur.uri, startMs, t)
        notes = library.getNotes(cur.uri)
    }

    fun editNote(note: Note, text: String) {
        val t = text.trim().ifEmpty { return }
        library.updateNote(note.id, t)
        current?.let { notes = library.getNotes(it.uri) }
    }

    fun deleteNote(note: Note) {
        library.deleteNote(note.id)
        current?.let { notes = library.getNotes(it.uri) }
    }

    /** Markdown for the current video's notes, or for every video of the tab. Null when there is nothing to export. */
    fun notesMarkdown(wholeTab: Boolean): String? {
        if (!wholeTab) {
            val cur = current ?: return null
            return NotesMarkdown.forVideo(cur.title, library.getNotes(cur.uri))
        }
        val tab = activeTab ?: return null
        val all = library.getNotes(items.map { it.entry.uri }).groupBy { it.videoUri }
        val sections = items.map { NotesSection(it.entry.title, it.entry.subfolder.ifEmpty { null }, all[it.entry.uri].orEmpty()) }
        return NotesMarkdown.forTab(tab.title, sections)
    }

    // ---- bookmarks -------------------------------------------------------------------------------------------

    fun addBookmark() {
        val cur = current ?: run { message = "Open a video first"; return }
        val pos = host.positionMs
        library.addBookmark(cur.uri, pos, TimeFormat.format(pos))
        bookmarks = library.getBookmarks(cur.uri)
        message = "Bookmark added at ${TimeFormat.format(pos)}"
    }

    fun renameBookmark(b: BookmarkRecord, name: String) {
        val n = name.trim().ifEmpty { TimeFormat.format(b.positionMs) }
        library.renameBookmark(b.id, n)
        current?.let { bookmarks = library.getBookmarks(it.uri) }
    }

    fun deleteBookmark(b: BookmarkRecord) {
        library.deleteBookmark(b.id)
        current?.let { bookmarks = library.getBookmarks(it.uri) }
    }

    /** Imports a PotPlayer .pbf (from the file picker, or the one found next to the video). Skips bookmarks already there. */
    fun importPbf(uri: Uri) {
        val cur = current ?: run { message = "Open a video first"; return }
        viewModelScope.launch {
            val result = withContext(Dispatchers.IO) {
                runCatching {
                    val bytes = getApplication<Application>().contentResolver.openInputStream(uri)!!.use { it.readBytes() }
                    val parsed = PbfFile.parse(PbfFile.decode(bytes))
                    val existing = library.getBookmarks(cur.uri)
                    var added = 0
                    for (b in parsed) {
                        if (existing.any { abs(it.positionMs - b.positionMs) < 500 }) continue
                        library.addBookmark(cur.uri, b.positionMs, b.name, b.raw)
                        added++
                    }
                    added to parsed.size
                }
            }
            result.onSuccess { (added, total) ->
                bookmarks = library.getBookmarks(cur.uri)
                message = if (added == 0) "Nothing new: all $total bookmarks are already here" else "Imported $added of $total bookmarks"
            }.onFailure { message = "Couldn't read that bookmark file: ${it.message}" }
        }
    }

    fun importSiblingPbf() {
        siblingBookmarkFile?.let { importPbf(Uri.parse(it)) }
    }

    /** Writes the current video's bookmarks as a PotPlayer .pbf to the place the student chose. */
    fun exportPbf(uri: Uri) {
        val list = bookmarks.map { PbfBookmark(it.positionMs, it.name, it.raw) }
        viewModelScope.launch {
            val ok = withContext(Dispatchers.IO) {
                runCatching {
                    getApplication<Application>().contentResolver.openOutputStream(uri, "wt")!!.use {
                        it.write(PbfFile.encode(PbfFile.serialize(list)))
                    }
                }
            }
            message = if (ok.isSuccess) "Saved ${list.size} bookmarks" else "Couldn't save the file: ${ok.exceptionOrNull()?.message}"
        }
    }

    fun suggestedPbfName(): String = (current?.title ?: "bookmarks") + ".pbf"

    // ---- focus timer -----------------------------------------------------------------------------------------

    fun focusToggle(announce: Boolean = false) {
        focus.toggle()
        saveFocus()
        if (announce) message = if (focus.running) "Focus timer started (${focus.preset.label})" else "Focus timer paused"
    }

    fun focusReset() { if (focus.phase == FocusPhase.Break) releaseVideoFromBreak(); focus.reset() }
    fun focusSkip() { if (focus.phase == FocusPhase.Break) releaseVideoFromBreak(); focus.skip() }
    fun focusPreset(index: Int) { if (focus.phase == FocusPhase.Break) releaseVideoFromBreak(); focus.selectPreset(index); saveFocus() }

    fun focusOptionsChanged() = saveFocus()

    /** Makes the focus screens redraw after an option was changed directly on the timer. */
    fun touchFocus() { focusVersion++ }

    fun saveFocus() {
        viewModelScope.launch(Dispatchers.IO) { library.setSetting("focus", focus.toJson()) }
    }

    private fun onFocusPartEnded(ended: FocusPhase) {
        if (focus.chimeWhenPartEnds) {
            runCatching {
                val ctx = getApplication<Application>()
                RingtoneManager.getRingtone(ctx, RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION))?.play()
            }
        }
        if (ended == FocusPhase.Focus) {
            message = "Focus session done. Take a ${focus.preset.breakMinutes}-minute break."
            if (focus.pauseVideoOnBreaks && host.isPlaying) {
                host.pause()
                pausedByFocus = true
            }
        } else {
            message = "Break over. Back to focus."
            releaseVideoFromBreak()
        }
        saveFocus()
    }

    /** The break is over (or cut short): if the timer paused the video for it, play on. */
    private fun releaseVideoFromBreak() {
        if (pausedByFocus && current != null && !host.isPlaying) host.play()
        pausedByFocus = false
    }

    // ---- transcripts -----------------------------------------------------------------------------------------

    /** The marked section, once both ends are set and in order. */
    val section: MsRange?
        get() {
            val a = sectionStartMs ?: return null
            val b = sectionEndMs ?: return null
            return if (b > a) MsRange(a, b) else null
        }

    fun markSectionStart() {
        val pos = host.positionMs
        sectionStartMs = pos
        if ((sectionEndMs ?: Long.MAX_VALUE) <= pos) sectionEndMs = null
    }

    fun markSectionEnd() {
        val pos = host.positionMs
        sectionEndMs = pos
        if ((sectionStartMs ?: Long.MIN_VALUE) >= pos) sectionStartMs = null
    }

    fun selectLast(minutes: Int) {
        val end = if (durationMs > 0) min(host.positionMs, durationMs) else host.positionMs
        sectionEndMs = end
        sectionStartMs = maxOf(0L, end - minutes * 60_000L)
    }

    fun selectWholeVideo() {
        if (durationMs <= 0) { message = "The length of this video isn't known yet"; return }
        sectionStartMs = 0
        sectionEndMs = durationMs
    }

    fun clearSection() {
        sectionStartMs = null
        sectionEndMs = null
    }

    private fun loadTranscript() {
        val cur = current
        transcript = if (cur == null) emptyList() else library.getSegments(cur.uri, transcribeModel.id)
        transcribeError = null
    }

    /** Starts transcribing the marked section; asks permission to download the speech model first if it isn't on the phone. */
    fun requestTranscribe() {
        if (current == null) { message = "Open a video first"; return }
        if (section == null) { message = "Mark a section first (start and end)"; return }
        if (transcribing || modelDownload != null) return
        if (models.isInstalled(transcribeModel)) startTranscribe() else modelPrompt = transcribeModel
    }

    fun dismissModelPrompt() { modelPrompt = null }

    /** The person said yes to the download. */
    fun confirmModelDownload() {
        val model = modelPrompt ?: return
        modelPrompt = null
        downloadModel(model, thenTranscribe = true)
    }

    private fun downloadModel(model: ModelInfo, thenTranscribe: Boolean) {
        modelDownload = 0f
        transcribeError = null
        downloadJob = viewModelScope.launch {
            try {
                models.download(model) { done, total -> modelDownload = if (total > 0) done.toFloat() / total else 0f }
                modelInstalled = models.isInstalled(transcribeModel)
                message = "Speech model ready"
                modelDownload = null
                if (thenTranscribe) startTranscribe()
            } catch (e: CancellationException) {
                message = "Download cancelled. It will carry on from here next time."
            } catch (e: IOException) {
                transcribeError = e.message ?: "The download failed."
            } finally {
                modelDownload = null
            }
        }
    }

    private fun startTranscribe() {
        val cur = current ?: return
        val range = section ?: return
        val model = transcribeModel
        transcribing = true
        transcribeProgress = 0f
        transcribeStage = "Starting…"
        transcribeError = null
        transcribeJob = viewModelScope.launch {
            try {
                transcription.transcribe(cur.uri, range, durationMs, model) { fraction, stage ->
                    transcribeProgress = fraction
                    transcribeStage = stage
                }
                if (current?.uri == cur.uri) transcript = library.getSegments(cur.uri, model.id)
                message = "Transcript ready"
            } catch (e: CancellationException) {
                if (current?.uri == cur.uri) transcript = library.getSegments(cur.uri, model.id)
                message = "Cancelled. What was already done is kept."
            } catch (e: NoAudioException) {
                transcribeError = e.message
            } catch (e: IOException) {
                transcribeError = e.message ?: "Transcription failed."
            } catch (e: RuntimeException) {
                transcribeError = "Couldn't read the audio of this video: ${e.message}"
            } finally {
                transcribing = false
            }
        }
    }

    fun cancelTranscribe() {
        transcribeJob?.cancel()
        downloadJob?.cancel()
    }

    fun chooseModel(model: ModelInfo) {
        if (transcribing || modelDownload != null) return
        transcribeModel = model
        library.setSetting("model", model.id)
        modelInstalled = models.isInstalled(model)
        loadTranscript()
    }

    fun deleteModel() {
        if (transcribing || modelDownload != null) return
        models.delete(transcribeModel)
        modelInstalled = false
        message = "Speech model removed"
    }

    /** Forgets the transcript of the current video (for the chosen model), so the section can be done again. */
    fun clearTranscript() {
        val cur = current ?: return
        library.clearTranscript(cur.uri, transcribeModel.id)
        transcript = emptyList()
    }

    fun transcriptMarkdown(): String? = current?.let { TranscriptExport.markdown(it.title, transcript) }
    fun transcriptSrt(): String? = if (transcript.isEmpty()) null else TranscriptExport.srt(transcript)

    /** Turns a transcript line into a note on the video, at the moment it was said. */
    fun noteFromSegment(segment: TranscriptSegment) {
        addNote(segment.startMs, segment.text)
        message = "Note added at ${TimeFormat.format(segment.startMs)}"
    }

    // ---- misc ------------------------------------------------------------------------------------------------

    fun messageShown() { message = null }

    override fun onCleared() {
        transcribeJob?.cancel()
        downloadJob?.cancel()
        saveCurrentProgress()
        runCatching { library.setSetting("focus", focus.toJson()) }
        host.release()
        library.close()
        super.onCleared()
    }
}
