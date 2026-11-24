package com.neuspectra.spectraplayer.ui

import android.content.res.Configuration
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CreateNewFolder
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.VideoFile
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.neuspectra.spectraplayer.AppViewModel
import com.neuspectra.spectraplayer.BuildConfig
import com.neuspectra.spectraplayer.Panel
import com.neuspectra.spectraplayer.R
import com.neuspectra.spectraplayer.data.TabRecord
import kotlinx.coroutines.flow.filterNotNull

@Composable
fun SpectraApp(vm: AppViewModel) {
    SpectraTheme {
        val snackbar = remember { SnackbarHostState() }
        LaunchedEffect(Unit) {
            snapshotFlow { vm.message }.filterNotNull().collect { text ->
                vm.messageShown()
                snackbar.showSnackbar(text)
            }
        }

        val addFolder = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri -> if (uri != null) vm.addFolder(uri) }
        val openFile = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri -> if (uri != null) vm.openExternal(uri) }
        val importPbf = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri -> if (uri != null) vm.importPbf(uri) }
        val exportPbf = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/octet-stream")) { uri -> if (uri != null) vm.exportPbf(uri) }

        val actions = Actions(
            addFolder = { addFolder.launch(null) },
            openFile = { openFile.launch(arrayOf("video/*", "audio/*")) },
            importPbf = { importPbf.launch(arrayOf("*/*")) },
            exportPbf = { name -> exportPbf.launch(name) },
        )

        // A Surface supplies the default text colour: outside one, text is black on our dark background.
        Surface(Modifier.fillMaxSize(), color = Palette.Bg, contentColor = Palette.Text) { Box(Modifier.fillMaxSize()) {
            when {
                vm.fullscreen && vm.current != null -> PlayerSurface(vm, Modifier.fillMaxSize())
                vm.tabs.isEmpty() && vm.current == null -> WelcomeScreen(actions)
                else -> MainLayout(vm, actions)
            }
            SnackbarHost(snackbar, Modifier.align(Alignment.BottomCenter).navigationBarsPadding().padding(bottom = 8.dp))
        } }
    }
}

class Actions(
    val addFolder: () -> Unit,
    val openFile: () -> Unit,
    val importPbf: () -> Unit,
    val exportPbf: (suggestedName: String) -> Unit,
)

// ---- welcome ----------------------------------------------------------------------------------------------------

@Composable
private fun WelcomeScreen(actions: Actions) {
    Column(
        Modifier.fillMaxSize().safeDrawingPadding().verticalScroll(rememberScrollState()).padding(horizontal = 28.dp, vertical = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center,
    ) {
        Image(painterResource(R.drawable.spectraplayer_logo), null, Modifier.size(84.dp))
        Text("Spectraplayer", fontSize = 28.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 10.dp))
        Text("Study with your videos", color = Palette.TextDim, fontSize = 14.sp, modifier = Modifier.padding(top = 2.dp))

        Column(Modifier.padding(top = 28.dp).fillMaxWidth().widthIn(max = 560.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Button(onClick = actions.addFolder, modifier = Modifier.fillMaxWidth().height(52.dp), shape = RoundedCornerShape(10.dp)) {
                Icon(Icons.Filled.CreateNewFolder, null, Modifier.padding(end = 10.dp))
                Text("Add a folder as a tab", fontSize = 15.sp)
            }
            OutlinedButton(onClick = actions.openFile, modifier = Modifier.fillMaxWidth().height(52.dp), shape = RoundedCornerShape(10.dp)) {
                Icon(Icons.Filled.VideoFile, null, Modifier.padding(end = 10.dp))
                Text("Open a video", fontSize = 15.sp)
            }
        }

        Column(Modifier.padding(top = 28.dp).fillMaxWidth().widthIn(max = 560.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text("One tab per subject: pick the folder that holds its lectures.", color = Palette.TextDim, fontSize = 13.sp)
            Text("Double-tap the left or right of the picture to skip 10 seconds.", color = Palette.TextDim, fontSize = 13.sp)
            Text("Add bookmarks and notes while you watch. PotPlayer .pbf bookmark files can be imported and exported.", color = Palette.TextDim, fontSize = 13.sp)
        }

        Row(Modifier.padding(top = 30.dp), verticalAlignment = Alignment.CenterVertically) {
            Image(painterResource(R.drawable.neuspectra_light), null, Modifier.height(34.dp), contentScale = ContentScale.Fit)
            Column(Modifier.padding(start = 12.dp)) {
                Text("Spectraplayer by NeuSpectra", fontSize = 12.5.sp)
                Text("© 2026 NeuSpectra", color = Palette.TextDim, fontSize = 11.5.sp)
            }
        }
    }
}

// ---- the main layout --------------------------------------------------------------------------------------------

@Composable
private fun MainLayout(vm: AppViewModel, actions: Actions) {
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE

    Column(Modifier.fillMaxSize().safeDrawingPadding()) {
        TopBar(vm, actions)
        SubjectTabs(vm)
        HorizontalDivider(color = Palette.Line)

        if (landscape) {
            Row(Modifier.weight(1f)) {
                VideoArea(vm, Modifier.weight(1f).fillMaxHeight())
                Box(Modifier.width(1.dp).fillMaxHeight().background(Palette.Line))
                PanelArea(vm, actions, Modifier.width(340.dp).fillMaxHeight())
            }
        } else {
            Column(Modifier.weight(1f)) {
                VideoArea(vm, Modifier.fillMaxWidth().aspectRatio(16f / 9f))
                PanelArea(vm, actions, Modifier.weight(1f).fillMaxWidth())
            }
        }
    }
}

@Composable
private fun VideoArea(vm: AppViewModel, modifier: Modifier) {
    if (vm.current != null) PlayerSurface(vm, modifier)
    else Box(modifier.background(Color.Black), contentAlignment = Alignment.Center) {
        Text(
            if (vm.scanning) "Reading the folder…" else "Pick a video from the playlist",
            color = Palette.TextDim, fontSize = 14.sp, textAlign = TextAlign.Center, modifier = Modifier.padding(24.dp),
        )
    }
}

@Composable
private fun TopBar(vm: AppViewModel, actions: Actions) {
    var menu by remember { mutableStateOf(false) }
    var renaming by remember { mutableStateOf<TabRecord?>(null) }
    var removing by remember { mutableStateOf<TabRecord?>(null) }
    var about by remember { mutableStateOf(false) }

    Row(Modifier.fillMaxWidth().height(52.dp).padding(start = 14.dp, end = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Image(painterResource(R.drawable.spectraplayer_logo), null, Modifier.size(26.dp))
        Text("Spectraplayer", fontSize = 17.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 10.dp).weight(1f))
        IconButton(onClick = actions.openFile) { Icon(Icons.Filled.VideoFile, "Open a video") }
        IconButton(onClick = actions.addFolder) { Icon(Icons.Filled.CreateNewFolder, "Add a folder as a tab") }
        Box {
            IconButton(onClick = { menu = true }) { Icon(Icons.Filled.MoreVert, "More") }
            DropdownMenu(menu, onDismissRequest = { menu = false }) {
                val tab = vm.activeTab
                if (tab != null) {
                    DropdownMenuItem(text = { Text("Rescan this folder") }, onClick = { menu = false; vm.rescan() })
                    DropdownMenuItem(text = { Text("Rename this tab…") }, onClick = { menu = false; renaming = tab })
                    DropdownMenuItem(text = { Text("Close this tab…") }, onClick = { menu = false; removing = tab })
                }
                DropdownMenuItem(text = { Text("About Spectraplayer") }, onClick = { menu = false; about = true })
            }
        }
    }

    renaming?.let { tab ->
        TextPromptDialog("Rename tab", tab.title, "Rename", onDismiss = { renaming = null }, onConfirm = { vm.renameTab(tab, it); renaming = null })
    }
    removing?.let { tab ->
        AlertDialog(
            onDismissRequest = { removing = null }, containerColor = Palette.Panel,
            title = { Text("Close “${tab.title}”?") },
            text = { Text("Your videos are not touched. Notes, bookmarks and progress stay in the library if you add the folder again.") },
            confirmButton = { TextButton(onClick = { vm.removeTab(tab); removing = null }) { Text("Close tab") } },
            dismissButton = { TextButton(onClick = { removing = null }) { Text("Cancel") } },
        )
    }
    if (about) {
        AlertDialog(
            onDismissRequest = { about = false }, containerColor = Palette.Panel,
            title = { Text("Spectraplayer ${BuildConfig.VERSION_NAME}") },
            text = {
                Text(
                    "A video player for studying, by NeuSpectra.\n\nPlays with Android Media3 (Apache 2.0). " +
                        "Bookmarks use PotPlayer's .pbf format.\n\n© 2026 NeuSpectra",
                )
            },
            confirmButton = { TextButton(onClick = { about = false }) { Text("Close") } },
        )
    }
}

@Composable
private fun SubjectTabs(vm: AppViewModel) {
    if (vm.tabs.isEmpty()) return
    LazyRow(Modifier.fillMaxWidth().height(40.dp), verticalAlignment = Alignment.CenterVertically) {
        items(vm.tabs, key = { it.id }) { tab ->
            val active = tab.id == vm.activeTabId
            Column(
                Modifier.fillMaxHeight().clickable { if (!active) vm.selectTab(tab, autoPlay = true) }.padding(horizontal = 14.dp),
                horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center,
            ) {
                Text(
                    tab.title, fontSize = 13.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
                    color = if (active) Palette.Text else Palette.TextDim, fontWeight = if (active) FontWeight.SemiBold else FontWeight.Normal,
                    modifier = Modifier.widthIn(max = 180.dp),
                )
                Box(Modifier.padding(top = 5.dp).height(2.dp).width(24.dp).background(if (active) Palette.Accent else Color.Transparent))
            }
        }
    }
}

// ---- the panel under (or beside) the video ---------------------------------------------------------------------

@Composable
private fun PanelArea(vm: AppViewModel, actions: Actions, modifier: Modifier) {
    Column(modifier.background(Palette.Panel)) {
        Row(Modifier.fillMaxWidth()) {
            for (p in Panel.values()) {
                val selected = vm.panel == p
                Column(Modifier.weight(1f).clickable { vm.panel = p }.padding(top = 10.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(p.label, fontSize = 12.sp, color = if (selected) Palette.Text else Palette.TextDim, maxLines = 1, softWrap = false)
                    Box(Modifier.padding(top = 8.dp).fillMaxWidth().height(2.dp).background(if (selected) Palette.Accent else Color.Transparent))
                }
            }
        }
        HorizontalDivider(color = Palette.Line)

        when (vm.panel) {
            Panel.Playlist -> PlaylistPanel(vm, Modifier.weight(1f).fillMaxWidth())
            Panel.Notes -> NotesPanel(vm, Modifier.weight(1f).fillMaxWidth())
            Panel.Bookmarks -> BookmarksPanel(vm, onImportFile = actions.importPbf, onExportFile = actions.exportPbf, modifier = Modifier.weight(1f).fillMaxWidth())
            Panel.Transcript -> TranscriptPanel(vm, Modifier.weight(1f).fillMaxWidth())
            Panel.Focus -> FocusPanel(vm, Modifier.weight(1f).fillMaxWidth())
        }
    }
}
