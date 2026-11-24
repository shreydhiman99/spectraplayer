package com.neuspectra.spectraplayer

import android.content.Intent
import android.content.pm.ActivityInfo
import android.graphics.Color
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.runtime.LaunchedEffect
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.neuspectra.spectraplayer.ui.SpectraApp

class MainActivity : ComponentActivity() {
    private val vm: AppViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val bg = Color.parseColor("#0B0C10")
        enableEdgeToEdge(statusBarStyle = SystemBarStyle.dark(bg), navigationBarStyle = SystemBarStyle.dark(bg))

        if (savedInstanceState == null) handle(intent)

        setContent {
            val fullscreen = vm.fullscreen
            LaunchedEffect(fullscreen) { applyFullscreen(fullscreen) }
            val busy = vm.playing || vm.transcribing || vm.modelDownload != null
            LaunchedEffect(busy) { // keep the screen on while a video plays or a transcript is being made
                if (busy) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
            }
            BackHandler(enabled = fullscreen) { vm.fullscreen = false }
            SpectraApp(vm)
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handle(intent)
    }

    /** "Open with Spectraplayer" from a file manager or another app. */
    private fun handle(intent: Intent?) {
        if (intent?.action == Intent.ACTION_VIEW) intent.data?.let { vm.openExternal(it) }
    }

    /** Fullscreen: landscape, no system bars (a swipe from the edge brings them back for a moment). */
    private fun applyFullscreen(on: Boolean) {
        val controller = WindowCompat.getInsetsController(window, window.decorView)
        if (on) {
            requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
            controller.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            controller.hide(WindowInsetsCompat.Type.systemBars())
        } else {
            requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
            controller.show(WindowInsetsCompat.Type.systemBars())
        }
    }

    override fun onStop() {
        // Leaving the app: remember where the student is, and don't keep playing to an empty screen.
        vm.saveCurrentProgress()
        vm.host.pause()
        super.onStop()
    }
}
