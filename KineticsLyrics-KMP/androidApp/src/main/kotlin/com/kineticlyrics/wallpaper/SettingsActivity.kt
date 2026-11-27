package com.kineticlyrics.wallpaper

import android.app.Activity
import android.graphics.Color
import android.graphics.Typeface
import android.os.Bundle
import android.os.Environment
import android.text.InputType
import android.view.ViewGroup
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Spinner
import android.widget.Switch
import android.widget.TextView
import android.widget.AdapterView
import com.kineticlyrics.shared.WallpaperSettings

class SettingsActivity : Activity() {
    private var settings = WallpaperSettings()
    private lateinit var offsetInput: EditText
    private lateinit var foldersInput: EditText
    private lateinit var musixmatchKeyInput: EditText

    private val pageColor = Color.rgb(7, 10, 16)
    private val panelColor = Color.rgb(15, 21, 31)
    private val textColor = Color.rgb(226, 242, 247)
    private val mutedColor = Color.rgb(139, 166, 179)
    private val cyan = Color.rgb(0, 229, 255)
    private val pink = Color.rgb(255, 42, 133)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        settings = WallpaperSettingsStore.load(this)

        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(24), dp(22), dp(24), dp(30))
            setBackgroundColor(pageColor)
        }
        val scroll = ScrollView(this).apply { addView(content) }

        content.addView(label("KINETIC // CONFIGURATION", 22f, cyan, bold = true))
        content.addView(label("TUNE THE WALLPAPER SIGNAL", 11f, mutedColor).apply {
            letterSpacing = .18f
            setPadding(0, dp(4), 0, dp(16))
        })

        section(content, "VISUAL IDENTITY", "Choose the motion preset and the look layered over the lyrics.")
        spinner(content, "Motion preset", listOf(
            "Custom" to "custom", "Minimal" to "minimal", "Calm" to "calm", "Balanced" to "balanced",
            "Hype" to "hype", "Chaos" to "chaos", "Synthwave '84" to "synthwave", "Cyberpunk" to "cyberpunk",
            "🧠 Brainrot" to "brainrot"
        ), settings.preset) { settings = settings.copy(preset = it) }
        spinner(content, "Theme finish", listOf(
            "Follow preset" to "auto", "Default" to "default", "Synthwave · VHS / neon grid" to "synthwave",
            "Cyberpunk · city terminal" to "cyberpunk", "🧠 Brainrot · meme overdrive" to "brainrot"
        ), settings.visualStyle) { settings = settings.copy(visualStyle = it) }
        spinner(content, "Word layout", listOf(
            "Auto" to "auto", "Flow" to "flow", "Stack" to "stack", "Hero" to "hero", "Staircase" to "stair",
            "Column" to "column", "Split" to "split", "Grid" to "grid", "Diagonal" to "diag", "Centered" to "center"
        ), settings.layout) { settings = settings.copy(layout = it) }
        spinner(content, "Transition", listOf(
            "Mixed" to "mixed", "Smooth" to "smooth", "Energetic" to "energetic"
        ), settings.transition) { settings = settings.copy(transition = it) }
        spinner(content, "Background", listOf(
            "Album art" to "art", "Color field" to "color", "Drift" to "drift", "Solid" to "solid"
        ), settings.bgMode) { settings = settings.copy(bgMode = it) }
        seek(content, "Motion chaos", settings.chaos) { settings = settings.copy(chaos = it) }
        seek(content, "Audio reactivity", settings.reactivity) { settings = settings.copy(reactivity = it) }
        seek(content, "Background dim", settings.dim) { settings = settings.copy(dim = it) }
        seek(content, "HUD lift", settings.hudLift) { settings = settings.copy(hudLift = it) }

        section(content, "LYRICS PIPELINE", "Offline sources are checked first: embedded audio lyrics, then matching .lrc or .txt sidecars. Online providers run after those. Genius opens a song search when synced lyrics are unavailable.")
        spinner(content, "Online provider", listOf(
            "Automatic" to "auto", "LRCLIB" to "lrclib", "Musixmatch" to "musixmatch", "NetEase" to "netease",
            "Kugou" to "kugou", "QQ Music" to "qq", "Genius search" to "genius"
        ), settings.provider) { settings = settings.copy(provider = it) }
        musixmatchKeyInput = edit(content, "Musixmatch API key (optional)", settings.musixmatchApiKey).apply {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        content.addView(label("Stored in this device's settings and sent to Musixmatch for lyric requests.", 12f, mutedColor))
        offsetInput = edit(content, "Global timing offset (milliseconds)", settings.offsetMs.toString(), signed = true)
        content.addView(label("Positive values bring lyrics forward; negative values delay them.", 12f, mutedColor))
        val defaultMusic = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MUSIC)?.absolutePath.orEmpty()
        foldersInput = edit(content, "Music folders (one path per line)", settings.musicFolders.ifEmpty { listOf(defaultMusic) }.joinToString("\n"), multiline = true)

        section(content, "DISPLAY & RESPONSE", "Small controls that change what the wallpaper shows and how it reacts.")
        toggle(content, "Show song information", settings.showInfo) { settings = settings.copy(showInfo = it) }
        toggle(content, "Show clock while idle", settings.idleClock) { settings = settings.copy(idleClock = it) }
        toggle(content, "Use a 24-hour clock", settings.clock24) { settings = settings.copy(clock24 = it) }
        toggle(content, "Auto accent from album art", settings.autoAccent) { settings = settings.copy(autoAccent = it) }
        toggle(content, "Invert colors", settings.invert) { settings = settings.copy(invert = it) }
        toggle(content, "Choreograph lyric sections", settings.choreo) { settings = settings.copy(choreo = it) }
        toggle(content, "Carry words between lines", settings.carry) { settings = settings.copy(carry = it) }
        toggle(content, "Beat flashes", settings.flashes) { settings = settings.copy(flashes = it) }
        toggle(content, "Mood analysis", settings.moodOn) { settings = settings.copy(moodOn = it) }
        toggle(content, "Low-power mode", settings.lowPower) { settings = settings.copy(lowPower = it) }

        val actions = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(0, dp(22), 0, 0)
        }
        val save = button("SAVE SETTINGS", cyan, Color.rgb(2, 15, 20))
        save.layoutParams = LinearLayout.LayoutParams(0, dp(52), 1f).apply { marginEnd = dp(8) }
        save.setOnClickListener { saveSettings() }
        val cancel = button("CANCEL", panelColor, textColor)
        cancel.layoutParams = LinearLayout.LayoutParams(0, dp(52), 1f).apply { marginStart = dp(8) }
        cancel.setOnClickListener { finish() }
        actions.addView(save)
        actions.addView(cancel)
        content.addView(actions)
        setContentView(scroll)
    }

    private fun saveSettings() {
        val offset = offsetInput.text.toString().trim().toIntOrNull()
        if (offset == null) {
            offsetInput.error = "Enter a whole number"
            return
        }
        val folders = foldersInput.text.toString().lineSequence().map { it.trim() }.filter { it.isNotEmpty() }.distinct().toList()
        settings = settings.copy(offsetMs = offset, musicFolders = folders, musixmatchApiKey = musixmatchKeyInput.text.toString().trim())
        WallpaperSettingsStore.save(this, settings)
        WallpaperSettingsStore.scanMusicLibrary(this)
        setResult(RESULT_OK)
        finish()
    }

    private fun section(parent: LinearLayout, title: String, description: String) {
        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(14), dp(13), dp(14), dp(13))
            setBackgroundColor(panelColor)
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                .apply { topMargin = dp(18); bottomMargin = dp(7) }
        }
        card.addView(label(title, 14f, if (title == "LYRICS PIPELINE") pink else cyan, bold = true))
        card.addView(label(description, 12f, mutedColor).apply { setPadding(0, dp(5), 0, 0) })
        parent.addView(card)
    }

    private fun spinner(parent: LinearLayout, title: String, choices: List<Pair<String, String>>, selected: String, onChanged: (String) -> Unit) {
        parent.addView(label(title, 13f, textColor, bold = true).apply { setPadding(0, dp(12), 0, dp(5)) })
        val spinner = Spinner(this)
        val adapter = ArrayAdapter(this, android.R.layout.simple_spinner_item, choices.map { it.first })
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        spinner.adapter = adapter
        val selectedIndex = choices.indexOfFirst { it.second == selected }.coerceAtLeast(0)
        spinner.setSelection(selectedIndex)
        spinner.onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
            override fun onItemSelected(parentView: android.widget.AdapterView<*>?, view: android.view.View?, position: Int, id: Long) {
                onChanged(choices[position].second)
            }
            override fun onNothingSelected(parentView: android.widget.AdapterView<*>?) = Unit
        }
        parent.addView(spinner, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(48)))
    }

    private fun seek(parent: LinearLayout, title: String, initial: Int, onChanged: (Int) -> Unit) {
        val value = label("$title  ·  ${initial.coerceIn(0, 100)}", 13f, textColor, bold = true)
        value.setPadding(0, dp(12), 0, 0)
        parent.addView(value)
        val seek = SeekBar(this).apply {
            max = 100
            progress = initial.coerceIn(0, 100)
            progressTintList = android.content.res.ColorStateList.valueOf(cyan)
            thumbTintList = android.content.res.ColorStateList.valueOf(pink)
        }
        seek.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(bar: SeekBar?, progress: Int, fromUser: Boolean) {
                value.text = "$title  ·  $progress"
                if (fromUser) onChanged(progress)
            }
            override fun onStartTrackingTouch(bar: SeekBar?) = Unit
            override fun onStopTrackingTouch(bar: SeekBar?) = Unit
        })
        parent.addView(seek)
    }

    private fun toggle(parent: LinearLayout, title: String, checked: Boolean, onChanged: (Boolean) -> Unit) {
        val control = Switch(this).apply {
            text = title
            textSize = 14f
            setTextColor(textColor)
            isChecked = checked
            thumbTintList = android.content.res.ColorStateList.valueOf(if (checked) cyan else mutedColor)
            setPadding(0, dp(3), 0, dp(3))
            setOnCheckedChangeListener { _, isChecked -> onChanged(isChecked) }
        }
        parent.addView(control, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
    }

    private fun edit(parent: LinearLayout, title: String, initial: String, signed: Boolean = false, multiline: Boolean = false): EditText {
        parent.addView(label(title, 13f, textColor, bold = true).apply { setPadding(0, dp(12), 0, dp(5)) })
        val field = EditText(this).apply {
            setText(initial)
            setTextColor(textColor)
            setHintTextColor(mutedColor)
            setPadding(dp(12), dp(10), dp(12), dp(10))
            setBackgroundColor(panelColor)
            if (multiline) {
                inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE
                minLines = 2
                gravity = android.view.Gravity.TOP
            } else {
                inputType = InputType.TYPE_CLASS_NUMBER or (if (signed) InputType.TYPE_NUMBER_FLAG_SIGNED else 0)
            }
        }
        parent.addView(field, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, if (multiline) dp(82) else dp(50)))
        return field
    }

    private fun label(text: String, size: Float, color: Int, bold: Boolean = false) = TextView(this).apply {
        this.text = text
        textSize = size
        setTextColor(color)
        if (bold) typeface = Typeface.create("sans-serif-condensed", Typeface.BOLD)
    }

    private fun button(text: String, background: Int, foreground: Int) = Button(this).apply {
        this.text = text
        isAllCaps = false
        textSize = 13f
        typeface = Typeface.create("sans-serif-condensed", Typeface.BOLD)
        setBackgroundColor(background)
        setTextColor(foreground)
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()
}
