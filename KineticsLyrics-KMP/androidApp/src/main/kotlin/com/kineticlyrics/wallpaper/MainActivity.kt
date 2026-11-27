package com.kineticlyrics.wallpaper

import android.Manifest
import android.app.Activity
import android.app.WallpaperManager
import android.content.ComponentName
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.graphics.Typeface
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import com.kineticlyrics.shared.AndroidMediaBridge

class MainActivity : Activity() {
    private var statusView: TextView? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        AndroidMediaBridge.init(applicationContext)

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.rgb(8, 8, 11))
            setPadding(64, 96, 64, 64)
        }

        val title = TextView(this).apply {
            text = "KINETIC LYRICS"
            textSize = 28f
            setTextColor(Color.rgb(255, 77, 109))
            setTypeface(Typeface.DEFAULT_BOLD, Typeface.BOLD)
        }
        root.addView(title)

        val subtitle = TextView(this).apply {
            text = "Kotlin Multiplatform Live Wallpaper\n\nGrant Notification Access for media sync and Microphone/Audio permission for live FFT beat reactivity, then set as your live wallpaper."
            textSize = 14f
            setTextColor(Color.rgb(220, 216, 208))
            setPadding(0, 24, 0, 48)
        }
        root.addView(subtitle)

        statusView = TextView(this).apply {
            textSize = 13f
            setTextColor(Color.rgb(160, 230, 180))
            setPadding(0, 0, 0, 36)
        }
        root.addView(statusView)

        addButton(root, "Wallpaper Settings", accent = true) {
            startActivity(Intent(this, SettingsActivity::class.java))
        }

        addButton(root, "1. Grant Audio & Storage Permissions") {
            val perms = mutableListOf(Manifest.permission.RECORD_AUDIO)
            if (Build.VERSION.SDK_INT >= 33) perms.add(Manifest.permission.READ_MEDIA_AUDIO)
            else perms.add(Manifest.permission.READ_EXTERNAL_STORAGE)
            requestPermissions(perms.toTypedArray(), 101)
        }

        addButton(root, "2. Enable Media Notification Access") {
            startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS))
        }

        addButton(root, "3. Set as Live Wallpaper", accent = true) {
            val intent = Intent(WallpaperManager.ACTION_CHANGE_LIVE_WALLPAPER).apply {
                putExtra(
                    WallpaperManager.EXTRA_LIVE_WALLPAPER_COMPONENT,
                    ComponentName(this@MainActivity, KineticWallpaperService::class.java)
                )
            }
            startActivity(intent)
        }

        setContentView(root)
    }

    override fun onResume() {
        super.onResume()
        refreshStatus()
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        WallpaperSettingsStore.scanMusicLibrary(this)
        refreshStatus()
    }

    private fun refreshStatus() {
        val audioOk = checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED
        val listeners = Settings.Secure.getString(contentResolver, "enabled_notification_listeners") ?: ""
        val mediaOk = listeners.contains(packageName, ignoreCase = true)

        statusView?.text =
            "• Audio Beat Visualizer Permission: ${if (audioOk) "GRANTED" else "NOT GRANTED"}\n" +
            "• Media Session Notification Sync: ${if (mediaOk) "ENABLED" else "NOT ENABLED"}"
    }

    private fun addButton(parent: LinearLayout, label: String, accent: Boolean = false, onClick: () -> Unit) {
        val btn = Button(this).apply {
            text = label
            isAllCaps = false
            textSize = 15f
            setBackgroundColor(if (accent) Color.rgb(255, 77, 109) else Color.rgb(28, 28, 36))
            setTextColor(Color.WHITE)
            setPadding(36, 28, 36, 28)
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT
            ).apply { setMargins(0, 0, 0, 24) }
            setOnClickListener { onClick() }
        }
        parent.addView(btn)
    }
}
