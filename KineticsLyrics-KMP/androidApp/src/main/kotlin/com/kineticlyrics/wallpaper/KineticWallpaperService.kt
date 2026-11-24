package com.kineticlyrics.wallpaper

import android.app.Presentation
import android.content.Context
import android.graphics.Bitmap
import android.graphics.Color
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.os.Handler
import android.os.Looper
import android.service.wallpaper.WallpaperService
import android.view.Display
import android.view.MotionEvent
import android.view.SurfaceHolder
import android.view.View
import android.webkit.WebView
import android.webkit.WebViewClient
import com.kineticlyrics.shared.AndroidMediaBridge
import com.kineticlyrics.shared.BridgeDispatcher
import com.kineticlyrics.shared.BridgeScript

class KineticWallpaperService : WallpaperService() {
    override fun onCreateEngine(): Engine = KineticWallpaperEngine(this)

    inner class KineticWallpaperEngine(
        private val service: KineticWallpaperService
    ) : Engine() {

        private val handler = Handler(Looper.getMainLooper())
        private var virtualDisplay: VirtualDisplay? = null
        private var presentation: Presentation? = null
        private var webView: WebView? = null
        private var pageLoaded = false
        private var isVisible = false

        private val dispatcher = BridgeDispatcher { js ->
            if (pageLoaded) {
                webView?.evaluateJavascript(js, null)
            }
        }

        private val tickRunnable = object : Runnable {
            override fun run() {
                if (!isVisible) return
                if (pageLoaded) {
                    dispatcher.pushFrame(AndroidMediaBridge.getSnapshot(), AndroidMediaBridge.getFftBins())
                }
                handler.postDelayed(this, 33L)
            }
        }

        override fun onCreate(surfaceHolder: SurfaceHolder?) {
            super.onCreate(surfaceHolder)
            AndroidMediaBridge.init(service.applicationContext)
            setTouchEventsEnabled(true)
        }

        override fun onSurfaceChanged(holder: SurfaceHolder?, format: Int, width: Int, height: Int) {
            super.onSurfaceChanged(holder, format, width, height)
            val surface = holder?.surface ?: return
            if (width <= 0 || height <= 0) return

            tearDownPresentation()

            val dm = service.getSystemService(Context.DISPLAY_SERVICE) as? DisplayManager ?: return
            val densityDpi = service.resources.displayMetrics.densityDpi

            virtualDisplay = dm.createVirtualDisplay(
                "KineticLyricsWallpaper",
                width,
                height,
                densityDpi,
                surface,
                DisplayManager.VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY
            )

            val display: Display = virtualDisplay?.display ?: return
            val pres = Presentation(service, display)
            val wv = createWebView(service)
            pres.setContentView(wv)
            pres.show()
            wv.loadUrl("file:///android_asset/index.html")

            presentation = pres
            webView = wv
        }

        override fun onVisibilityChanged(visible: Boolean) {
            super.onVisibilityChanged(visible)
            isVisible = visible
            handler.removeCallbacks(tickRunnable)
            if (visible) {
                webView?.onResume()
                AndroidMediaBridge.start()
                handler.post(tickRunnable)
            } else {
                AndroidMediaBridge.stop()
                webView?.onPause()
            }
        }

        override fun onTouchEvent(event: MotionEvent?) {
            super.onTouchEvent(event)
            if (event != null) {
                webView?.dispatchTouchEvent(event)
            }
        }

        override fun onSurfaceDestroyed(holder: SurfaceHolder?) {
            isVisible = false
            handler.removeCallbacks(tickRunnable)
            AndroidMediaBridge.stop()
            tearDownPresentation()
            super.onSurfaceDestroyed(holder)
        }

        private fun tearDownPresentation() {
            pageLoaded = false
            dispatcher.reset()
            runCatching {
                webView?.stopLoading()
                webView?.destroy()
            }
            webView = null
            runCatching { presentation?.dismiss() }
            presentation = null
            runCatching { virtualDisplay?.release() }
            virtualDisplay = null
        }

        private fun createWebView(ctx: Context): WebView {
            return WebView(ctx).apply {
                setBackgroundColor(Color.rgb(8, 8, 11))
                setLayerType(View.LAYER_TYPE_HARDWARE, null)
                settings.apply {
                    javaScriptEnabled = true
                    domStorageEnabled = true
                    allowFileAccess = true
                    @Suppress("DEPRECATION")
                    allowUniversalAccessFromFileURLs = true
                    mediaPlaybackRequiresUserGesture = false
                }
                webViewClient = object : WebViewClient() {
                    override fun onPageStarted(view: WebView?, url: String?, favicon: Bitmap?) {
                        super.onPageStarted(view, url, favicon)
                        view?.evaluateJavascript(BridgeScript.JS, null)
                    }

                    override fun onPageFinished(view: WebView?, url: String?) {
                        super.onPageFinished(view, url)
                        view?.evaluateJavascript(BridgeScript.JS, null)
                        pageLoaded = true
                    }
                }
            }
        }
    }
}
