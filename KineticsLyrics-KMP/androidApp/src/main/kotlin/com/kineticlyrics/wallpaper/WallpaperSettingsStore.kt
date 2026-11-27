package com.kineticlyrics.wallpaper

import android.content.Context
import android.os.Environment
import com.kineticlyrics.shared.AndroidMediaBridge
import com.kineticlyrics.shared.WallpaperSettings
import kotlinx.serialization.json.Json

object WallpaperSettingsStore {
    private const val PREFS_NAME = "kinetic_wallpaper_settings"
    private const val SETTINGS_KEY = "settings_json"
    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }

    fun load(context: Context): WallpaperSettings {
        val raw = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .getString(SETTINGS_KEY, null) ?: return WallpaperSettings()
        return runCatching { json.decodeFromString(WallpaperSettings.serializer(), raw) }
            .getOrDefault(WallpaperSettings())
    }

    fun save(context: Context, settings: WallpaperSettings) {
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit()
            .putString(SETTINGS_KEY, json.encodeToString(WallpaperSettings.serializer(), settings))
            .putStringSet("music_folders", settings.musicFolders.toSet())
            .apply()
    }

    fun scanMusicLibrary(context: Context) {
        val folders = load(context).musicFolders.ifEmpty {
            listOfNotNull(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MUSIC)?.absolutePath)
        }
        Thread { AndroidMediaBridge.scanMusicFolders(folders) }.start()
    }
}
