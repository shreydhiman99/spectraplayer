package com.neuspectra.spectraplayer.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.BookmarkAdd
import androidx.compose.material.icons.filled.EditNote
import androidx.compose.material.icons.filled.Forward10
import androidx.compose.material.icons.filled.Fullscreen
import androidx.compose.material.icons.filled.FullscreenExit
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Replay10
import androidx.compose.material.icons.filled.SkipNext
import androidx.compose.material.icons.filled.SkipPrevious
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.media3.ui.PlayerView
import com.neuspectra.spectraplayer.AppViewModel
import com.neuspectra.spectraplayer.Panel
import com.neuspectra.spectraplayer.core.TimeFormat
import kotlinx.coroutines.delay

private val speeds = listOf(0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f)

private fun speedText(s: Float): String = (if (s == s.toInt().toFloat()) s.toInt().toString() else s.toString().trimEnd('0')) + "×"

/** The video and its touch controls. Tap shows or hides them; double-tap the left or right half to skip 10 seconds. */
@Composable
fun PlayerSurface(vm: AppViewModel, modifier: Modifier = Modifier) {
    var controlsVisible by remember { mutableStateOf(true) }
    var touch by remember { mutableIntStateOf(0) }
    var skipHint by remember { mutableStateOf<String?>(null) }
    var speedMenu by remember { mutableStateOf(false) }
    var quickNoteAt by remember { mutableStateOf<Long?>(null) }

    LaunchedEffect(controlsVisible, vm.playing, touch, speedMenu, quickNoteAt) {
        if (controlsVisible && vm.playing && !speedMenu && quickNoteAt == null) {
            delay(3500)
            controlsVisible = false
        }
    }
    LaunchedEffect(skipHint) {
        if (skipHint != null) {
            delay(700)
            skipHint = null
        }
    }

    Box(modifier.background(Color.Black)) {
        AndroidView(
            factory = { ctx -> PlayerView(ctx).apply { useController = false; player = vm.host.exo } },
            update = { it.player = vm.host.exo },
            onRelease = { it.player = null },
            modifier = Modifier.fillMaxSize(),
        )

        // taps on the picture
        Box(
            Modifier.fillMaxSize().pointerInput(Unit) {
                detectTapGestures(
                    onTap = { controlsVisible = !controlsVisible; touch++ },
                    onDoubleTap = { o ->
                        val back = o.x < size.width / 2
                        vm.seekBy(if (back) -10_000 else 10_000)
                        skipHint = if (back) "−10 s" else "+10 s"
                        controlsVisible = true
                        touch++
                    },
                )
            },
        )

        skipHint?.let {
            Text(
                it, color = Color.White, fontSize = 22.sp, fontWeight = FontWeight.SemiBold,
                modifier = Modifier.align(Alignment.Center).clip(RoundedCornerShape(20.dp)).background(Color(0x99000000)).padding(horizontal = 18.dp, vertical = 8.dp),
            )
        }

        AnimatedVisibility(controlsVisible, enter = fadeIn(), exit = fadeOut()) {
            Column(
                Modifier.fillMaxSize().background(
                    Brush.verticalGradient(listOf(Color(0xB3000000), Color(0x00000000), Color(0x00000000), Color(0xCC000000))),
                ),
                verticalArrangement = Arrangement.SpaceBetween,
            ) {
                // top: what is playing
                Column(Modifier.fillMaxWidth().padding(start = 14.dp, end = 14.dp, top = 10.dp)) {
                    Text(vm.current?.title.orEmpty(), color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    vm.activeTab?.let { Text(it.title, color = Color(0xCCFFFFFF), fontSize = 12.sp, maxLines = 1) }
                }

                // middle: transport
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
                    IconButton(onClick = { vm.playPrevious(); touch++ }) { Icon(Icons.Filled.SkipPrevious, "Previous video", tint = Color.White, modifier = Modifier.size(30.dp)) }
                    IconButton(onClick = { vm.seekBy(-10_000); touch++ }) { Icon(Icons.Filled.Replay10, "Back 10 seconds", tint = Color.White, modifier = Modifier.size(30.dp)) }
                    Box(
                        Modifier.padding(horizontal = 10.dp).size(64.dp).clip(CircleShape).background(Palette.Accent).clickable { vm.togglePlay(); touch++ },
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(if (vm.playing) Icons.Filled.Pause else Icons.Filled.PlayArrow, if (vm.playing) "Pause" else "Play", tint = Color.White, modifier = Modifier.size(36.dp))
                    }
                    IconButton(onClick = { vm.seekBy(10_000); touch++ }) { Icon(Icons.Filled.Forward10, "Forward 10 seconds", tint = Color.White, modifier = Modifier.size(30.dp)) }
                    IconButton(onClick = { vm.playNext(); touch++ }) { Icon(Icons.Filled.SkipNext, "Next video", tint = Color.White, modifier = Modifier.size(30.dp)) }
                }

                // bottom: seek bar and tools
                Column(Modifier.fillMaxWidth().padding(horizontal = 8.dp)) {
                    SeekBar(
                        positionMs = vm.positionMs, durationMs = vm.durationMs,
                        bookmarks = vm.bookmarks.map { it.positionMs }, notes = vm.notes.map { it.startMs },
                        onSeek = { vm.seekTo(it); touch++ },
                        modifier = Modifier.fillMaxWidth(),
                        selectionStartMs = vm.sectionStartMs, selectionEndMs = vm.sectionEndMs,
                    )
                    Row(Modifier.fillMaxWidth().padding(bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            TimeFormat.format(vm.positionMs) + " / " + TimeFormat.format(vm.durationMs),
                            color = Color(0xE6FFFFFF), fontSize = 12.sp, fontFamily = FontFamily.Monospace, modifier = Modifier.padding(start = 6.dp),
                        )
                        FocusPill(vm, Modifier.padding(start = 10.dp))
                        Box(Modifier.weight(1f))

                        Box {
                            Text(
                                speedText(vm.speed), color = if (vm.speed == 1f) Color.White else Palette.AccentHover, fontSize = 13.sp, fontWeight = FontWeight.SemiBold,
                                modifier = Modifier.clip(RoundedCornerShape(6.dp)).clickable { speedMenu = true; touch++ }.padding(horizontal = 10.dp, vertical = 8.dp),
                            )
                            DropdownMenu(speedMenu, onDismissRequest = { speedMenu = false }) {
                                for (s in speeds) {
                                    DropdownMenuItem(
                                        text = { Text((if (s == vm.speed) "✓  " else "     ") + speedText(s) + if (s == 1f) "  Normal" else "") },
                                        onClick = { vm.changeSpeed(s); speedMenu = false },
                                    )
                                }
                            }
                        }
                        IconButton(onClick = { vm.addBookmark(); touch++ }) { Icon(Icons.Filled.BookmarkAdd, "Add a bookmark here", tint = Color.White) }
                        IconButton(onClick = { quickNoteAt = vm.host.positionMs; touch++ }) { Icon(Icons.Filled.EditNote, "Take a note here", tint = Color.White) }
                        IconButton(onClick = { vm.fullscreen = !vm.fullscreen; touch++ }) {
                            Icon(if (vm.fullscreen) Icons.Filled.FullscreenExit else Icons.Filled.Fullscreen, if (vm.fullscreen) "Leave fullscreen" else "Fullscreen", tint = Color.White)
                        }
                    }
                }
            }
        }
    }

    quickNoteAt?.let { at ->
        TextPromptDialog(
            title = "Note at ${TimeFormat.format(at)}", initial = "", confirmLabel = "Add note", singleLine = false,
            onDismiss = { quickNoteAt = null },
            onConfirm = { text -> vm.addNote(at, text); quickNoteAt = null },
        )
    }
}

/** The focus timer's small ring and time, shown while a timer is going. Tap to open the Focus tab. */
@Composable
fun FocusPill(vm: AppViewModel, modifier: Modifier = Modifier) {
    @Suppress("UNUSED_EXPRESSION") vm.focusVersion // the timer isn't observable: this read makes the pill redraw when it changes
    val f = vm.focus
    if (!f.isActive) return

    val seconds = f.secondsLeft
    Row(
        modifier.clip(RoundedCornerShape(14.dp)).background(Color(0x66000000)).clickable { vm.fullscreen = false; vm.panel = Panel.Focus }
            .padding(start = 5.dp, end = 10.dp, top = 3.dp, bottom = 3.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        RingProgress(
            f.fractionLeft.toFloat(), if (f.phase == com.neuspectra.spectraplayer.core.FocusPhase.Focus) Palette.Focus else Palette.AccentHover,
            Modifier.size(20.dp), track = Palette.Hover, strokeWidth = 2.5f,
        )
        Text("%02d:%02d".format(seconds / 60, seconds % 60), color = Color.White, fontSize = 12.sp, fontFamily = FontFamily.Monospace, modifier = Modifier.padding(start = 6.dp))
    }
}
