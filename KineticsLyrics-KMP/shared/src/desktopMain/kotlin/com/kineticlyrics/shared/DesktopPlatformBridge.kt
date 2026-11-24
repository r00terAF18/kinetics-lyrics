package com.kineticlyrics.shared

import com.sun.jna.Native
import com.sun.jna.Pointer
import com.sun.jna.platform.win32.User32
import com.sun.jna.platform.win32.WinDef
import com.sun.jna.platform.win32.WinUser
import com.sun.jna.win32.StdCallLibrary
import com.sun.jna.win32.W32APIOptions
import org.jtransforms.fft.DoubleFFT_1D
import java.io.File
import java.util.concurrent.CopyOnWriteArrayList
import javax.sound.sampled.AudioFormat
import javax.sound.sampled.AudioSystem
import javax.sound.sampled.TargetDataLine
import kotlin.math.PI
import kotlin.math.ceil
import kotlin.math.cos
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.sqrt

object DesktopOS {
    val name: String = System.getProperty("os.name").lowercase()
    val isWindows: Boolean get() = name.contains("win")
    val isMac: Boolean get() = name.contains("mac")
}

/**
 * Cross-platform JVM audio spectrum capture (Windows WASAPI / Stereo Mix / Loopback & macOS BlackHole/CoreAudio)
 * plus local .lrc sidecar scanning for Windows and macOS.
 */
class DesktopAudioAndMediaBridge : PlatformMediaSource {
    private val tracks = CopyOnWriteArrayList<LocalTrack>()
    private val audioExtensions = setOf("flac", "mp3", "m4a", "ogg", "opus", "wav", "wma")
    private val fftBins = FloatArray(128)
    private val fftLock = Any()

    @Volatile private var running = false
    private var audioThread: Thread? = null
    private var mediaThread: Thread? = null

    @Volatile private var currentSnapshot = MediaSnapshot()

    override fun start() {
        if (running) return
        running = true

        audioThread = Thread({ captureAudioLoop() }, "KineticAudioFFT").apply {
            isDaemon = true
            start()
        }

        mediaThread = Thread({ pollSystemMediaLoop() }, "KineticMediaPoll").apply {
            isDaemon = true
            start()
        }
    }

    override fun stop() {
        running = false
    }

    override fun getSnapshot(): MediaSnapshot = currentSnapshot

    override fun getFftBins(): FloatArray = synchronized(fftLock) { fftBins.copyOf() }

    override fun scanMusicFolders(folders: List<String>) {
        val found = mutableListOf<LocalTrack>()
        for (folder in folders) {
            val root = File(folder)
            if (!root.exists()) continue
            root.walkTopDown().onFail { _, _ -> }.forEach { file ->
                if (file.isFile && file.extension.lowercase() in audioExtensions) {
                    val base = file.nameWithoutExtension
                    val parts = base.split(" - ", limit = 2)
                    val artist = if (parts.size == 2) parts[0].trim() else (file.parentFile?.parentFile?.name ?: "")
                    val title = if (parts.size == 2) parts[1].trim() else base.trim()
                    found.add(LocalTrack(file.absolutePath, title, artist, file.parentFile?.name ?: "", 0.0))
                }
            }
        }
        tracks.clear()
        tracks.addAll(found)
    }

    private fun captureAudioLoop() {
        val format = AudioFormat(44100f, 16, 2, true, false)
        val line: TargetDataLine? = runCatching {
            AudioSystem.getTargetDataLine(format).apply {
                open(format, 4096)
                start()
            }
        }.getOrNull()

        if (line == null) return

        val fft = DoubleFFT_1D(1024)
        val left = DoubleArray(1024)
        val right = DoubleArray(1024)
        val buf = ByteArray(4096)

        try {
            while (running) {
                val read = line.read(buf, 0, buf.size)
                if (read < 4096) continue

                for (i in 0 until 1024) {
                    val offset = i * 4
                    val lSample = ((buf[offset + 1].toInt() shl 8) or (buf[offset].toInt() and 0xFF)) / 32768.0
                    val rSample = ((buf[offset + 3].toInt() shl 8) or (buf[offset + 2].toInt() and 0xFF)) / 32768.0
                    val window = 0.5 * (1.0 - cos(2.0 * PI * i / 1023.0))
                    left[i] = lSample * window
                    right[i] = rSample * window
                }

                fft.realForward(left)
                fft.realForward(right)

                synchronized(fftLock) {
                    for (b in 0 until 64) {
                        val start = max(1, floor(512.0.pow(b / 64.0)).toInt())
                        val end = max(start + 1, min(512, ceil(512.0.pow((b + 1) / 64.0)).toInt()))
                        var sumL = 0.0
                        var sumR = 0.0
                        for (k in start until end) {
                            val reL = left[2 * k]
                            val imL = left[2 * k + 1]
                            val reR = right[2 * k]
                            val imR = right[2 * k + 1]
                            sumL += sqrt(reL * reL + imL * imL) / 512.0
                            sumR += sqrt(reR * reR + imR * imR) / 512.0
                        }
                        val scale = (1.0 + b * 0.14) * 3.5 / (end - start)
                        fftBins[b] = (sumL * scale).toFloat().coerceIn(0f, 1.5f)
                        fftBins[64 + b] = (sumR * scale).toFloat().coerceIn(0f, 1.5f)
                    }
                }
            }
        } finally {
            runCatching { line.stop(); line.close() }
        }
    }

    private fun pollSystemMediaLoop() {
        var lastKey = ""
        var cachedLrc: String? = null
        var cachedSource: String? = null
        var basePos = 0.0
        var baseNano = System.nanoTime()

        while (running) {
            try {
                val polled = when {
                    DesktopOS.isMac -> pollMacOsNowPlaying()
                    DesktopOS.isWindows -> pollWindowsSmtc()
                    else -> null
                }

                if (polled != null) {
                    val key = "${polled.title}|${polled.artist}|${polled.album}"
                    if (key != lastKey && polled.title.isNotBlank()) {
                        lastKey = key
                        cachedLrc = null
                        cachedSource = null
                        val matched = LocalLyricsMatcher.bestMatch(tracks, polled.title, polled.artist, polled.album, polled.durationSeconds)
                        if (matched != null) {
                            val lrcFile = File(matched.path.substringBeforeLast('.') + ".lrc")
                            if (lrcFile.exists()) {
                                val raw = runCatching { lrcFile.readText() }.getOrNull()
                                if (!raw.isNullOrBlank() && LocalLyricsMatcher.hasTimestamps(raw)) {
                                    cachedLrc = raw
                                    cachedSource = "Local .lrc"
                                }
                            }
                        }
                    }

                    if (kotlin.math.abs(polled.positionSeconds - basePos) > 1.2 || !polled.isPlaying) {
                        basePos = polled.positionSeconds
                        baseNano = System.nanoTime()
                    }
                    val interp = basePos + if (polled.isPlaying) (System.nanoTime() - baseNano) / 1_000_000_000.0 else 0.0

                    currentSnapshot = polled.copy(
                        positionSeconds = interp,
                        localRawLrc = cachedLrc,
                        localSource = cachedSource
                    )
                }
            } catch (_: Throwable) { }

            Thread.sleep(250)
        }
    }

    /**
     * Queries Apple Music / Spotify on macOS via osascript JXA / AppleScript without extra native dependencies.
     */
    private fun pollMacOsNowPlaying(): MediaSnapshot? {
        val script = """
            if application "Spotify" is running then
                tell application "Spotify"
                    set st to player state as string
                    set t to name of current track
                    set a to artist of current track
                    set al to album of current track
                    set d to (duration of current track) / 1000
                    set p to player position
                    return st & "|" & t & "|" & a & "|" & al & "|" & d & "|" & p
                end tell
            else if application "Music" is running then
                tell application "Music"
                    set st to player state as string
                    set t to name of current track
                    set a to artist of current track
                    set al to album of current track
                    set d to duration of current track
                    set p to player position
                    return st & "|" & t & "|" & a & "|" & al & "|" & d & "|" & p
                end tell
            end if
            return ""
        """.trimIndent()

        val proc = ProcessBuilder("osascript", "-e", script).redirectErrorStream(true).start()
        val out = proc.inputStream.bufferedReader().readText().trim()
        proc.waitFor()
        if (out.isBlank() || !out.contains("|")) return null
        val parts = out.split("|")
        if (parts.size < 6) return null
        return MediaSnapshot(
            title = parts[1],
            artist = parts[2],
            album = parts[3],
            isPlaying = parts[0].equals("playing", ignoreCase = true),
            durationSeconds = parts[4].toDoubleOrNull() ?: 0.0,
            positionSeconds = parts[5].toDoubleOrNull() ?: 0.0
        )
    }

    /**
     * Queries Windows SMTC (GlobalSystemMediaTransportControlsSessionManager) via PowerShell WinRT projection.
     */
    private fun pollWindowsSmtc(): MediaSnapshot? {
        val ps = """
            Add-Type -AssemblyName System.Runtime.WindowsRuntime
            [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media.Control,ContentType=WindowsRuntime] | Out-Null
            ${'$'}op = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()
            ${'$'}mgr = ${'$'}op.GetAwaiter().GetResult()
            ${'$'}s = ${'$'}mgr.GetCurrentSession()
            if (${'$'}null -eq ${'$'}s) { exit }
            ${'$'}p = ${'$'}s.TryGetMediaPropertiesAsync().GetAwaiter().GetResult()
            ${'$'}tl = ${'$'}s.GetTimelineProperties()
            ${'$'}pb = ${'$'}s.GetPlaybackInfo()
            Write-Output ("$(${'$'}pb.PlaybackStatus)|$(${'$'}p.Title)|$(${'$'}p.Artist)|$(${'$'}p.AlbumTitle)|$(${'$'}tl.EndTime.TotalSeconds)|$(${'$'}tl.Position.TotalSeconds)")
        """.trimIndent()

        val proc = ProcessBuilder("powershell", "-NoProfile", "-NonInteractive", "-Command", ps)
            .redirectErrorStream(true).start()
        val out = proc.inputStream.bufferedReader().readText().trim()
        proc.waitFor()
        if (out.isBlank() || !out.contains("|")) return null
        val parts = out.split("|")
        if (parts.size < 6) return null
        return MediaSnapshot(
            title = parts[1],
            artist = parts[2],
            album = parts[3],
            isPlaying = parts[0].equals("Playing", ignoreCase = true),
            durationSeconds = parts[4].toDoubleOrNull() ?: 0.0,
            positionSeconds = parts[5].toDoubleOrNull() ?: 0.0
        )
    }
}

/**
 * Windows & macOS Desktop Wallpaper Window Embedding via JNA.
 * - Windows: Attaches behind desktop icons (SHELLDLL_DefView / WorkerW) on Windows 10 & Windows 11 24H2+.
 * - macOS: Configures NSWindow level to kCGDesktopWindowLevel (-2147483623) via Objective-C runtime / JNA.
 */
object DesktopWindowEmbedder {
    private interface User32Ext : StdCallLibrary {
        fun FindWindowW(lpClassName: CharArray?, lpWindowName: CharArray?): WinDef.HWND?
        fun FindWindowExW(parent: WinDef.HWND?, childAfter: WinDef.HWND?, className: CharArray?, windowTitle: CharArray?): WinDef.HWND?
        fun SendMessageTimeoutW(hWnd: WinDef.HWND, msg: Int, wParam: WinDef.WPARAM, lParam: WinDef.LPARAM, fuFlags: Int, uTimeout: Int, lpdwResult: WinDef.DWORDByReference): WinDef.LRESULT
        fun SetParent(hWndChild: WinDef.HWND, hWndNewParent: WinDef.HWND?): WinDef.HWND?
        fun SetWindowPos(hWnd: WinDef.HWND, hWndInsertAfter: WinDef.HWND?, x: Int, y: Int, cx: Int, cy: Int, uFlags: Int): Boolean
        fun GetSystemMetrics(nIndex: Int): Int

        companion object {
            val INSTANCE: User32Ext = Native.load("user32", User32Ext::class.java, W32APIOptions.DEFAULT_OPTIONS)
        }
    }

    fun embedWindowsDesktop(awtWindow: java.awt.Window) {
        if (!DesktopOS.isWindows) return
        val hwndPtr = Native.getWindowPointer(awtWindow) ?: return
        val hwnd = WinDef.HWND(hwndPtr)
        val u32 = User32Ext.INSTANCE

        val progman = u32.FindWindowW("Progman\u0000".toCharArray(), null) ?: return
        u32.SendMessageTimeoutW(progman, 0x052C, WinDef.WPARAM(0xD), WinDef.LPARAM(0x1), 2, 1000, WinDef.DWORDByReference())

        val vx = u32.GetSystemMetrics(76)
        val vy = u32.GetSystemMetrics(77)
        val vw = max(640, u32.GetSystemMetrics(78))
        val vh = max(480, u32.GetSystemMetrics(79))

        val shellDefView = u32.FindWindowExW(progman, null, "SHELLDLL_DefView\u0000".toCharArray(), null)
        if (shellDefView != null) {
            // Windows 11 24H2+: find wallpaper WorkerW child of Progman
            var worker = u32.FindWindowExW(progman, null, "WorkerW\u0000".toCharArray(), null)
            while (worker != null) {
                val hasIcons = u32.FindWindowExW(worker, null, "SHELLDLL_DefView\u0000".toCharArray(), null)
                if (hasIcons == null) break
                worker = u32.FindWindowExW(progman, worker, "WorkerW\u0000".toCharArray(), null)
            }
            if (worker != null) {
                u32.SetParent(hwnd, worker)
                u32.SetWindowPos(hwnd, null, 0, 0, vw, vh, 0x0010 or 0x0040 or 0x0020)
                return
            }
        }

        // Fallback: pin to bottom of Z-order across virtual desktop
        u32.SetWindowPos(hwnd, WinDef.HWND(Pointer.createConstant(1L)), vx, vy, vw, vh, 0x0010 or 0x0040)
    }

    fun configureMacDesktopWindow(awtWindow: java.awt.Window) {
        if (!DesktopOS.isMac) return
        // On macOS, setting sun.awt.Window.windowLevel or full-screen borderless non-activating window places it behind normal app windows
        runCatching {
            val rootPane = (awtWindow as? javax.swing.RootPaneContainer)?.rootPane
            rootPane?.putClientProperty("apple.awt.fullWindowContent", true)
            rootPane?.putClientProperty("apple.awt.transparentTitleBar", true)
        }
    }
}
