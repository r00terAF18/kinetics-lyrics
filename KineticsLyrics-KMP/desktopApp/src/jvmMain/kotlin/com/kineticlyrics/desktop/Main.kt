package com.kineticlyrics.desktop

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material.Button
import androidx.compose.material.ButtonDefaults
import androidx.compose.material.MaterialTheme
import androidx.compose.material.Text
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Tray
import androidx.compose.ui.window.Window
import androidx.compose.ui.window.WindowPlacement
import androidx.compose.ui.window.application
import androidx.compose.ui.window.rememberWindowState
import com.kineticlyrics.shared.DesktopAudioAndMediaBridge
import com.kineticlyrics.shared.DesktopOS
import com.kineticlyrics.shared.DesktopWindowEmbedder
import com.kineticlyrics.shared.WallpaperSettings
import com.sun.net.httpserver.HttpServer
import kotlinx.coroutines.delay
import java.awt.Desktop
import java.awt.Dimension
import java.awt.Toolkit
import java.io.File
import java.net.InetSocketAddress
import java.net.URI
import javax.swing.JFileChooser

/**
 * Lightweight embedded HTTP server serving WebContent (index.html, pixi.min.js, gsap.min.js)
 * plus a live /state.json endpoint for the GPU renderer on Windows & macOS.
 */
class LocalWebServer(private val bridge: DesktopAudioAndMediaBridge) {
    private var server: HttpServer? = null
    var port: Int = 0
        private set

    fun start(): Int {
        val srv = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        port = srv.address.port

        srv.createContext("/") { ex ->
            val rawPath = ex.requestURI.path.trimStart('/').ifEmpty { "index.html" }
            if (rawPath == "state.json") {
                val snap = bridge.getSnapshot()
                val bins = bridge.getFftBins()
                val json = buildString {
                    append("{")
                    append("\"title\":\"${escapeJson(snap.title)}\",")
                    append("\"artist\":\"${escapeJson(snap.artist)}\",")
                    append("\"album\":\"${escapeJson(snap.album)}\",")
                    append("\"isPlaying\":${snap.isPlaying},")
                    append("\"position\":${snap.positionSeconds},")
                    append("\"duration\":${snap.durationSeconds},")
                    append("\"localRawLrc\":${snap.localRawLrc?.let { "\"${escapeJson(it)}\"" } ?: "null"},")
                    append("\"localSource\":${snap.localSource?.let { "\"${escapeJson(it)}\"" } ?: "null"},")
                    append("\"fft\":[${bins.joinToString(",")}]")
                    append("}")
                }
                val bytes = json.toByteArray(Charsets.UTF_8)
                ex.responseHeaders.add("Content-Type", "application/json; charset=utf-8")
                ex.responseHeaders.add("Cache-Control", "no-store")
                ex.sendResponseHeaders(200, bytes.size.toLong())
                ex.responseBody.use { it.write(bytes) }
                return@createContext
            }

            val stream = Thread.currentThread().contextClassLoader.getResourceAsStream("web/$rawPath")
            if (stream == null) {
                ex.sendResponseHeaders(404, -1)
                ex.close()
                return@createContext
            }

            val contentType = when {
                rawPath.endsWith(".html") -> "text/html; charset=utf-8"
                rawPath.endsWith(".js") -> "application/javascript; charset=utf-8"
                rawPath.endsWith(".json") -> "application/json; charset=utf-8"
                else -> "application/octet-stream"
            }
            val bytes = stream.use { it.readBytes() }
            ex.responseHeaders.add("Content-Type", contentType)
            ex.sendResponseHeaders(200, bytes.size.toLong())
            ex.responseBody.use { it.write(bytes) }
        }

        srv.start()
        server = srv
        return port
    }

    fun stop() {
        runCatching { server?.stop(0) }
    }

    private fun escapeJson(s: String): String =
        s.replace("\\", "\\\\")
            .replace("\"", "\\\"")
            .replace("\n", "\\n")
            .replace("\r", "\\r")
}

fun main() = application {
    val bridge = remember { DesktopAudioAndMediaBridge() }
    val webServer = remember { LocalWebServer(bridge) }
    var settings by remember { mutableStateOf(WallpaperSettings()) }
    var serverUrl by remember { mutableStateOf("") }
    var showControlWindow by remember { mutableStateOf(true) }
    var embeddedToDesktop by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) {
        val port = webServer.start()
        serverUrl = "http://127.0.0.1:$port/index.html"
        val defaultMusic = File(System.getProperty("user.home"), "Music")
        if (defaultMusic.exists()) {
            bridge.scanMusicFolders(listOf(defaultMusic.absolutePath))
        }
        bridge.start()
    }

    // System Tray / macOS Menu Bar Icon
    Tray(
        icon = androidx.compose.ui.graphics.painter.ColorPainter(Color(0xFFFF4D6D)),
        tooltip = "Kinetic Lyrics Wallpaper (KMP)",
        onAction = { showControlWindow = !showControlWindow }
    ) {
        Item("Open Wallpaper Control Panel") { showControlWindow = true }
        Item("Open GPU Stage in Browser") {
            if (serverUrl.isNotEmpty() && Desktop.isDesktopSupported()) {
                Desktop.getDesktop().browse(URI(serverUrl))
            }
        }
        Separator()
        Item("Add Local Music Folder…") {
            val chooser = JFileChooser().apply { fileSelectionMode = JFileChooser.DIRECTORIES_ONLY }
            if (chooser.showOpenDialog(null) == JFileChooser.APPROVE_OPTION) {
                val dir = chooser.selectedFile.absolutePath
                settings = settings.copy(musicFolders = settings.musicFolders + dir)
                Thread { bridge.scanMusicFolders(settings.musicFolders) }.start()
            }
        }
        Separator()
        Item("Exit") {
            bridge.stop()
            webServer.stop()
            exitApplication()
        }
    }

    // Desktop Wallpaper Host Window (Windows WorkerW / macOS Desktop Level)
    val screenSize: Dimension = Toolkit.getDefaultToolkit().screenSize
    val wallpaperState = rememberWindowState(
        placement = WindowPlacement.Floating,
        width = screenSize.width.dp,
        height = screenSize.height.dp
    )

    if (embeddedToDesktop) {
        Window(
            onCloseRequest = { embeddedToDesktop = false },
            state = wallpaperState,
            undecorated = true,
            resizable = false,
            focusable = false,
            title = "Kinetic Lyrics Desktop Surface"
        ) {
            LaunchedEffect(window) {
                if (DesktopOS.isWindows) {
                    DesktopWindowEmbedder.embedWindowsDesktop(window)
                } else if (DesktopOS.isMac) {
                    DesktopWindowEmbedder.configureMacDesktopWindow(window)
                }
            }

            var trackTitle by remember { mutableStateOf("KINETIC LYRICS") }
            var trackArtist by remember { mutableStateOf("") }
            LaunchedEffect(Unit) {
                while (true) {
                    val snap = bridge.getSnapshot()
                    trackTitle = snap.title.ifBlank { "KINETIC LYRICS" }
                    trackArtist = snap.artist
                    delay(200)
                }
            }

            Column(
                modifier = Modifier.fillMaxSize().background(Color(0xFF08080B)).padding(48.dp),
                verticalArrangement = Arrangement.Center
            ) {
                Text(
                    text = trackTitle.uppercase(),
                    color = Color(0xFFF4F1EA),
                    fontSize = 56.sp,
                    fontWeight = FontWeight.Black
                )
                if (trackArtist.isNotBlank()) {
                    Text(
                        text = trackArtist,
                        color = Color(0xFFFF4D6D),
                        fontSize = 24.sp
                    )
                }
            }
        }
    }

    // Control & Settings Window
    if (showControlWindow) {
        Window(
            onCloseRequest = { showControlWindow = false },
            title = "Kinetic Lyrics — Kotlin Multiplatform (Windows & macOS)"
        ) {
            var nowPlaying by remember { mutableStateOf("Waiting for music…") }
            LaunchedEffect(Unit) {
                while (true) {
                    val snap = bridge.getSnapshot()
                    nowPlaying = if (snap.title.isBlank()) "Waiting for music…"
                    else "${snap.title} — ${snap.artist} [${snap.localSource ?: "Online"}]"
                    delay(500)
                }
            }

            MaterialTheme {
                Column(
                    modifier = Modifier.fillMaxSize().background(Color(0xFF0E0E14)).padding(28.dp)
                ) {
                    Text(
                        text = "KINETIC LYRICS KMP",
                        color = Color(0xFFFF4D6D),
                        fontSize = 24.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Spacer(Modifier.height(8.dp))
                    Text(
                        text = "Platform: ${System.getProperty("os.name")}  |  Local GPU Server: $serverUrl",
                        color = Color(0xFFA0A0B0),
                        fontSize = 12.sp
                    )
                    Spacer(Modifier.height(20.dp))
                    Text(
                        text = "Now Playing: $nowPlaying",
                        color = Color(0xFFF4F1EA),
                        fontSize = 15.sp
                    )
                    Spacer(Modifier.height(24.dp))
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Button(
                            onClick = { embeddedToDesktop = !embeddedToDesktop },
                            colors = ButtonDefaults.buttonColors(backgroundColor = Color(0xFFFF4D6D))
                        ) {
                            Text(
                                text = if (embeddedToDesktop) "Detach Desktop Layer" else "Attach to Desktop Wallpaper",
                                color = Color.White
                            )
                        }
                        Button(
                            onClick = {
                                if (serverUrl.isNotEmpty() && Desktop.isDesktopSupported()) {
                                    Desktop.getDesktop().browse(URI(serverUrl))
                                }
                            },
                            colors = ButtonDefaults.buttonColors(backgroundColor = Color(0xFF22222E))
                        ) {
                            Text("Launch GPU Web Stage", color = Color.White)
                        }
                    }
                }
            }
        }
    }
}
