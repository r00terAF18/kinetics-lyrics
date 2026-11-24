package com.kineticlyrics.shared

object BridgeScript {
    /**
     * Injected into WebView2 (Windows), WKWebView (macOS), and Android WebView before index.html runs.
     * Exposes the exact Wallpaper Engine media/audio/property listener API expected by index.html.
     */
    const val JS = """
(function() {
  const listeners = { status: [], props: [], thumb: [], playback: [], timeline: [], audio: [] };
  window.wallpaperMediaIntegration = { PLAYBACK_PLAYING: 1, PLAYBACK_PAUSED: 2, PLAYBACK_STOPPED: 3 };
  window.__klLocalLrc = null;
  window.__klTargetFps = 60;

  const origRaf = window.requestAnimationFrame.bind(window);
  let lastRaf = 0;
  window.requestAnimationFrame = function(cb) {
    const fps = window.__klTargetFps || 0;
    if (fps <= 0 || fps >= 60) return origRaf(cb);
    const minInterval = 1000 / fps;
    return origRaf(function(ts) {
      if (ts - lastRaf >= minInterval * 0.85) {
        lastRaf = ts;
        cb(ts);
      } else {
        window.requestAnimationFrame(cb);
      }
    });
  };

  window.wallpaperRegisterMediaStatusListener = fn => { if (typeof fn === 'function') { listeners.status.push(fn); fn({ enabled: true }); } };
  window.wallpaperRegisterMediaPropertiesListener = fn => { if (typeof fn === 'function') listeners.props.push(fn); };
  window.wallpaperRegisterMediaThumbnailListener = fn => { if (typeof fn === 'function') listeners.thumb.push(fn); };
  window.wallpaperRegisterMediaPlaybackListener = fn => { if (typeof fn === 'function') listeners.playback.push(fn); };
  window.wallpaperRegisterMediaTimelineListener = fn => { if (typeof fn === 'function') listeners.timeline.push(fn); };
  window.wallpaperRegisterAudioListener = fn => { if (typeof fn === 'function') listeners.audio.push(fn); };

  window.__klReceive = function(msg) {
    if (!msg || typeof msg !== 'object') return;
    switch (msg.type) {
      case 'localLyrics':
        window.__klLocalLrc = msg.data;
        if (msg.data && msg.data.lrc && typeof window.fetchLyrics === 'function') window.fetchLyrics();
        break;
      case 'fps':
        window.__klTargetFps = msg.data || 60;
        break;
      case 'props':
        window.__klLocalLrc = null;
        listeners.props.forEach(fn => { try { fn(msg.data); } catch (e) {} });
        break;
      case 'thumb':
        listeners.thumb.forEach(fn => { try { fn(msg.data); } catch (e) {} });
        break;
      case 'playback':
        listeners.playback.forEach(fn => { try { fn(msg.data); } catch (e) {} });
        break;
      case 'timeline':
        listeners.timeline.forEach(fn => { try { fn(msg.data); } catch (e) {} });
        break;
      case 'audio':
        listeners.audio.forEach(fn => { try { fn(msg.data); } catch (e) {} });
        break;
      case 'settings':
        if (window.wallpaperPropertyListener && window.wallpaperPropertyListener.applyUserProperties) {
          try { window.wallpaperPropertyListener.applyUserProperties(msg.data); } catch (e) {}
        }
        break;
    }
  };
})();
"""
}
