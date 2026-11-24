package com.neuspectra.spectraplayer.ui

import android.content.Context
import android.content.Intent
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Share
import androidx.compose.material.icons.filled.SkipNext
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.neuspectra.spectraplayer.AppViewModel
import com.neuspectra.spectraplayer.PlaylistItem
import com.neuspectra.spectraplayer.core.FocusPhase
import com.neuspectra.spectraplayer.core.FocusTimer
import com.neuspectra.spectraplayer.core.Note
import com.neuspectra.spectraplayer.core.TimeFormat
import com.neuspectra.spectraplayer.data.BookmarkRecord
import kotlin.math.max

@Composable
private fun Hint(text: String, modifier: Modifier = Modifier) =
    Text(text, color = Palette.TextDim, fontSize = 13.sp, modifier = modifier.padding(horizontal = 18.dp, vertical = 12.dp))

// ---- playlist -------------------------------------------------------------------------------------------------

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun PlaylistPanel(vm: AppViewModel, modifier: Modifier = Modifier) {
    val tab = vm.activeTab
    val listState = rememberLazyListState()

    // keep the current video in view
    val currentIndex = vm.currentIndex
    LaunchedEffect(vm.current?.uri, vm.items.size) {
        if (currentIndex >= 0) listState.animateScrollToItem(max(0, currentIndex - 2))
    }

    Column(modifier) {
        Row(Modifier.fillMaxWidth().padding(start = 14.dp, end = 14.dp, top = 12.dp, bottom = 8.dp), verticalAlignment = Alignment.Top) {
            Column(Modifier.weight(1f)) {
                Text(tab?.title.orEmpty(), fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                val n = vm.items.size
                if (n > 0) Text("$n video${if (n == 1) "" else "s"} · ${vm.items.count { it.watched }} watched", color = Palette.TextDim, fontSize = 12.sp)
            }
            // the length of the whole playlist, as a clock (with a + while videos are still being measured)
            if (vm.items.isNotEmpty() && (vm.totalDurationMs > 0 || vm.unmeasured == 0)) {
                Column(horizontalAlignment = Alignment.End, modifier = Modifier.padding(start = 12.dp)) {
                    Text(
                        TimeFormat.clock(vm.totalDurationMs) + if (vm.unmeasured > 0) "+" else "",
                        fontFamily = FontFamily.Monospace, fontSize = 16.sp, fontWeight = FontWeight.SemiBold,
                    )
                    Text("total length", color = Palette.TextDim, fontSize = 11.sp)
                }
            }
        }

        when {
            tab == null -> Hint("No folders yet. Add a folder with the + button, for example one folder per subject.")
            vm.scanning && vm.items.isEmpty() -> Hint("Scanning folder…")
            vm.items.isEmpty() -> Hint("No video files in this folder.")
            else -> LazyColumn(state = listState, modifier = Modifier.fillMaxSize()) {
                itemsIndexed(vm.items, key = { _, it -> it.entry.uri }) { _, item ->
                    PlaylistRow(item, current = item.entry.uri == vm.current?.uri, vm = vm)
                }
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun PlaylistRow(item: PlaylistItem, current: Boolean, vm: AppViewModel) {
    var menu by remember { mutableStateOf(false) }
    Box {
        Row(
            Modifier.fillMaxWidth()
                .background(if (current) Palette.Hover else Color.Transparent)
                .combinedClickable(onClick = { vm.play(item) }, onLongClick = { menu = true })
                .padding(horizontal = 14.dp, vertical = 10.dp),
        ) {
            Box(Modifier.width(24.dp).padding(top = 2.dp)) {
                when {
                    current -> Icon(Icons.Filled.PlayArrow, "Playing", tint = Palette.Accent, modifier = Modifier.size(16.dp))
                    item.watched -> Icon(Icons.Filled.Check, "Watched", tint = Palette.TextDim, modifier = Modifier.size(16.dp))
                }
            }
            Column(Modifier.weight(1f)) {
                Text(
                    item.entry.title, fontSize = 14.sp, maxLines = 2, overflow = TextOverflow.Ellipsis,
                    fontWeight = if (current) FontWeight.SemiBold else FontWeight.Normal,
                    color = if (item.watched && !current) Palette.TextDim else Palette.Text,
                )
                if (item.entry.subfolder.isNotEmpty()) Text(item.entry.subfolder, color = Palette.TextDim, fontSize = 11.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                if (item.progress > 0.005 && !item.watched) {
                    LinearProgressIndicator(
                        progress = { item.progress.toFloat() }, color = Palette.Accent, trackColor = Palette.PanelAlt,
                        modifier = Modifier.padding(top = 6.dp).fillMaxWidth().height(3.dp),
                    )
                }
            }
            val d = item.durationMs
            if (d != null && d > 0) {
                Text(TimeFormat.format(d), color = Palette.TextDim, fontSize = 12.sp, fontFamily = FontFamily.Monospace, modifier = Modifier.padding(start = 10.dp, top = 2.dp))
            }
        }
        DropdownMenu(menu, onDismissRequest = { menu = false }) {
            DropdownMenuItem(text = { Text("Play") }, onClick = { menu = false; vm.play(item) })
            DropdownMenuItem(
                text = { Text(if (item.watched) "Mark as unwatched" else "Mark as watched") },
                onClick = { menu = false; vm.setWatched(item, !item.watched) },
            )
        }
    }
}

// ---- notes ----------------------------------------------------------------------------------------------------

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun NotesPanel(vm: AppViewModel, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    var draft by remember { mutableStateOf("") }
    var stamp by remember { mutableStateOf<Long?>(null) } // the moment the thought began, locked when typing starts
    var editing by remember { mutableStateOf<Note?>(null) }
    var shareMenu by remember { mutableStateOf(false) }

    Column(modifier) {
        Row(Modifier.fillMaxWidth().padding(start = 14.dp, end = 4.dp, top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(vm.current?.title ?: "Notes", fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
            if (vm.notes.isNotEmpty()) Text("${vm.notes.size}", color = Palette.TextDim, modifier = Modifier.padding(end = 4.dp))
            Box {
                IconButton(onClick = { shareMenu = true }, enabled = vm.current != null) { Icon(Icons.Filled.Share, "Share notes", tint = Palette.TextDim) }
                DropdownMenu(shareMenu, onDismissRequest = { shareMenu = false }) {
                    DropdownMenuItem(text = { Text("Share this video's notes") }, onClick = {
                        shareMenu = false
                        vm.notesMarkdown(wholeTab = false)?.let { shareText(context, vm.current?.title ?: "Notes", it) }
                    })
                    if (vm.activeTab != null) DropdownMenuItem(text = { Text("Share the whole tab's notes") }, onClick = {
                        shareMenu = false
                        vm.notesMarkdown(wholeTab = true)?.let { shareText(context, vm.activeTab?.title ?: "Notes", it) }
                    })
                }
            }
        }

        Box(Modifier.weight(1f).fillMaxWidth()) {
            if (vm.notes.isEmpty()) {
                Hint(if (vm.current == null) "Open a video to take notes." else "No notes on this video yet. Write one below, or tap the note button on the video.")
            } else {
                LazyColumn(Modifier.fillMaxSize()) {
                    itemsIndexed(vm.notes, key = { _, n -> n.id }) { _, note -> NoteRow(note, vm) { editing = note } }
                }
            }
        }

        HorizontalDivider(color = Palette.Line)
        Column(Modifier.padding(12.dp)) {
            val label = stamp?.let { "at ${TimeFormat.format(it)}" } ?: "now ${TimeFormat.format(vm.positionMs)}"
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(label, color = Palette.Note, fontFamily = FontFamily.Monospace, fontSize = 12.5.sp, modifier = Modifier.weight(1f))
                if (stamp != null) TextButton(onClick = { stamp = vm.host.positionMs }) { Text("Use the current time", fontSize = 12.sp) }
            }
            OutlinedTextField(
                value = draft,
                onValueChange = { v ->
                    if (draft.isEmpty() && v.isNotEmpty()) stamp = vm.host.positionMs
                    if (v.isEmpty()) stamp = null
                    draft = v
                },
                placeholder = { Text("Write a note…", color = Palette.TextDim) },
                minLines = 2, maxLines = 5, modifier = Modifier.fillMaxWidth(),
            )
            Button(
                onClick = { vm.addNote(stamp ?: vm.host.positionMs, draft); draft = ""; stamp = null },
                enabled = draft.isNotBlank() && vm.current != null,
                modifier = Modifier.align(Alignment.End).padding(top = 8.dp),
            ) { Text("Add note") }
        }
    }

    editing?.let { n ->
        TextPromptDialog("Edit note at ${n.timeLabel}", n.text, "Save", singleLine = false, onDismiss = { editing = null }, onConfirm = { vm.editNote(n, it); editing = null })
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun NoteRow(note: Note, vm: AppViewModel, onEdit: () -> Unit) {
    var menu by remember { mutableStateOf(false) }
    Box {
        Row(
            Modifier.fillMaxWidth().combinedClickable(onClick = { vm.seekTo(note.startMs) }, onLongClick = { menu = true }).padding(horizontal = 14.dp, vertical = 9.dp),
        ) {
            Text(note.timeLabel, color = Palette.Note, fontFamily = FontFamily.Monospace, fontSize = 12.5.sp, modifier = Modifier.width(54.dp).padding(top = 1.dp))
            Text(note.text, fontSize = 14.sp, modifier = Modifier.weight(1f))
        }
        DropdownMenu(menu, onDismissRequest = { menu = false }) {
            DropdownMenuItem(text = { Text("Go to") }, onClick = { menu = false; vm.seekTo(note.startMs) })
            DropdownMenuItem(text = { Text("Edit…") }, onClick = { menu = false; onEdit() })
            DropdownMenuItem(text = { Text("Delete") }, onClick = { menu = false; vm.deleteNote(note) })
        }
    }
}

private fun shareText(context: Context, title: String, text: String) {
    val send = Intent(Intent.ACTION_SEND).apply {
        type = "text/plain"
        putExtra(Intent.EXTRA_SUBJECT, title)
        putExtra(Intent.EXTRA_TEXT, text)
    }
    context.startActivity(Intent.createChooser(send, "Share notes"))
}

// ---- bookmarks ------------------------------------------------------------------------------------------------

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun BookmarksPanel(vm: AppViewModel, onImportFile: () -> Unit, onExportFile: (String) -> Unit, modifier: Modifier = Modifier) {
    var renaming by remember { mutableStateOf<BookmarkRecord?>(null) }

    Column(modifier) {
        Row(Modifier.fillMaxWidth().padding(start = 14.dp, end = 14.dp, top = 12.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(vm.current?.title ?: "Bookmarks", fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
            if (vm.bookmarks.isNotEmpty()) Text("${vm.bookmarks.size}", color = Palette.TextDim)
        }

        // a PotPlayer bookmark file found next to the video
        if (vm.siblingBookmarkFile != null) {
            Row(
                Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp).clip(RoundedCornerShape(8.dp)).background(Palette.PanelAlt).padding(start = 12.dp, end = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text("PotPlayer bookmarks found next to this video.", fontSize = 12.5.sp, modifier = Modifier.weight(1f).padding(vertical = 8.dp))
                TextButton(onClick = { vm.importSiblingPbf() }) { Text("Import") }
            }
        }

        Box(Modifier.weight(1f).fillMaxWidth()) {
            if (vm.bookmarks.isEmpty()) {
                Hint(if (vm.current == null) "Open a video to add bookmarks." else "No bookmarks yet. Tap the flag button on the video while playing to add one.")
            } else {
                LazyColumn(Modifier.fillMaxSize()) {
                    itemsIndexed(vm.bookmarks, key = { _, b -> b.id }) { _, b -> BookmarkRow(b, vm) { renaming = b } }
                }
            }
        }

        HorizontalDivider(color = Palette.Line)
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = { vm.addBookmark() }, enabled = vm.current != null, modifier = Modifier.fillMaxWidth()) { Text("Add bookmark here") }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = onImportFile, enabled = vm.current != null, modifier = Modifier.weight(1f)) { Text("Import .pbf…", fontSize = 13.sp) }
                OutlinedButton(onClick = { onExportFile(vm.suggestedPbfName()) }, enabled = vm.bookmarks.isNotEmpty(), modifier = Modifier.weight(1f)) { Text("Export .pbf…", fontSize = 13.sp) }
            }
        }
    }

    renaming?.let { b ->
        TextPromptDialog("Rename bookmark at ${TimeFormat.format(b.positionMs)}", b.name, "Rename", onDismiss = { renaming = null }, onConfirm = { vm.renameBookmark(b, it); renaming = null })
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun BookmarkRow(b: BookmarkRecord, vm: AppViewModel, onRename: () -> Unit) {
    var menu by remember { mutableStateOf(false) }
    Box {
        Row(
            Modifier.fillMaxWidth().combinedClickable(onClick = { vm.seekTo(b.positionMs) }, onLongClick = { menu = true }).padding(horizontal = 14.dp, vertical = 11.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(TimeFormat.format(b.positionMs), color = Palette.Marker, fontFamily = FontFamily.Monospace, fontSize = 13.sp, modifier = Modifier.width(62.dp))
            Text(b.name, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
        }
        DropdownMenu(menu, onDismissRequest = { menu = false }) {
            DropdownMenuItem(text = { Text("Go to") }, onClick = { menu = false; vm.seekTo(b.positionMs) })
            DropdownMenuItem(text = { Text("Rename…") }, onClick = { menu = false; onRename() })
            DropdownMenuItem(text = { Text("Delete") }, onClick = { menu = false; vm.deleteBookmark(b) })
        }
    }
}

// ---- focus timer ----------------------------------------------------------------------------------------------

@Composable
fun FocusPanel(vm: AppViewModel, modifier: Modifier = Modifier) {
    @Suppress("UNUSED_EXPRESSION") vm.focusVersion // the timer isn't observable: this read makes the panel redraw when it changes
    val f = vm.focus
    val focusing = f.phase == FocusPhase.Focus
    val arc = if (focusing) Palette.Focus else Palette.AccentHover
    val seconds = f.secondsLeft

    Column(modifier.verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 14.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.Bottom) {
            Text("Focus session", fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
            Text(f.sessionText, color = Palette.TextDim, fontSize = 12.sp)
        }

        Box(Modifier.padding(top = 12.dp).size(168.dp), contentAlignment = Alignment.Center) {
            RingProgress(f.fractionLeft.toFloat(), arc, Modifier.fillMaxSize())
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text("%02d:%02d".format(seconds / 60, seconds % 60), fontFamily = FontFamily.Monospace, fontSize = 38.sp)
                Text(if (focusing) "FOCUS" else "BREAK", color = Palette.TextDim, fontSize = 11.sp, modifier = Modifier.padding(top = 2.dp))
            }
        }

        Row(Modifier.padding(top = 12.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { vm.focusReset() }, modifier = Modifier.size(44.dp)) { Icon(Icons.Filled.Refresh, "Reset the timer") }
            Box(
                Modifier.padding(horizontal = 18.dp).size(60.dp).clip(CircleShape).background(Palette.Accent).clickable { vm.focusToggle() },
                contentAlignment = Alignment.Center,
            ) {
                Icon(if (f.running) Icons.Filled.Pause else Icons.Filled.PlayArrow, if (f.running) "Pause" else "Start", tint = Color.White, modifier = Modifier.size(32.dp))
            }
            IconButton(onClick = { vm.focusSkip() }, modifier = Modifier.size(44.dp)) { Icon(Icons.Filled.SkipNext, "Skip to the next part") }
        }

        // session dots
        Row(Modifier.padding(top = 14.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            for (i in 0 until FocusTimer.SESSIONS_PER_ROUND) {
                SessionDot(filled = i < f.completedSessions, lit = i <= f.completedSessions)
            }
        }

        HorizontalDivider(color = Palette.Line, modifier = Modifier.padding(top = 16.dp))
        Text("Length (focus / break, minutes)", color = Palette.TextDim, fontSize = 12.sp, modifier = Modifier.fillMaxWidth().padding(top = 12.dp))
        Row(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            FocusTimer.presets.forEachIndexed { i, p ->
                val selected = i == f.presetIndex
                Box(
                    Modifier.weight(1f).height(36.dp).clip(RoundedCornerShape(6.dp))
                        .background(if (selected) Palette.PanelAlt else Color.Transparent)
                        .clickable { vm.focusPreset(i) },
                    contentAlignment = Alignment.Center,
                ) {
                    ChipBorder(selected)
                    Text(p.label, fontSize = 12.5.sp)
                }
            }
        }

        Column(Modifier.fillMaxWidth().padding(top = 8.dp)) {
            SwitchRow("Pause the video during breaks", f.pauseVideoOnBreaks) { f.pauseVideoOnBreaks = it; vm.focusOptionsChanged(); vm.touchFocus() }
            SwitchRow("Soft chime when a part ends", f.chimeWhenPartEnds) { f.chimeWhenPartEnds = it; vm.focusOptionsChanged(); vm.touchFocus() }
            SwitchRow("Count only while the video plays", f.countOnlyWhilePlaying) { f.countOnlyWhilePlaying = it; vm.focusOptionsChanged(); vm.touchFocus() }
        }

        val today = if (f.focusedTodayMs < 30_000 && f.sessionsToday == 0) "Nothing focused yet today"
        else "Today: ${TimeFormat.total(f.focusedTodayMs)} focused · ${f.sessionsToday} session${if (f.sessionsToday == 1) "" else "s"}"
        Text(today, color = Palette.TextDim, fontSize = 12.sp, textAlign = TextAlign.Start, modifier = Modifier.fillMaxWidth().padding(top = 14.dp))
    }
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 2.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(label, fontSize = 13.sp, modifier = Modifier.weight(1f))
        Switch(
            checked = checked, onCheckedChange = onChange,
            colors = SwitchDefaults.colors(checkedTrackColor = Palette.Accent, checkedThumbColor = Color.White, uncheckedTrackColor = Palette.Hover, uncheckedThumbColor = Palette.TextDim, uncheckedBorderColor = Palette.Hover),
        )
    }
}

/** One of the four session dots: filled once the session is done, outlined in green for the current one. */
@Composable
private fun SessionDot(filled: Boolean, lit: Boolean) {
    androidx.compose.foundation.Canvas(Modifier.size(12.dp)) {
        if (filled) drawCircle(Palette.Focus)
        drawCircle(
            if (lit) Palette.Focus else Color(0xFF3A4056), radius = size.minDimension / 2 - 1.dp.toPx(),
            style = androidx.compose.ui.graphics.drawscope.Stroke(2.dp.toPx()),
        )
    }
}

/** The outline of a length chip (lit when chosen). */
@Composable
private fun ChipBorder(selected: Boolean) {
    androidx.compose.foundation.Canvas(Modifier.fillMaxSize()) {
        drawRoundRect(
            color = if (selected) Palette.Accent else Palette.Line,
            cornerRadius = androidx.compose.ui.geometry.CornerRadius(6.dp.toPx()),
            style = androidx.compose.ui.graphics.drawscope.Stroke(1.dp.toPx()),
        )
    }
}
