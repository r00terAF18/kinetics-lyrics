using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Service.Wallpaper;
using Android.Views;
using KineticLyrics.Native.Models;
using KineticLyrics.Native.Services;
using Color = Android.Graphics.Color;
using OperationCanceledException = System.OperationCanceledException;
using Paint = Android.Graphics.Paint;
using RectF = Android.Graphics.RectF;
using TypefaceStyle = Android.Graphics.TypefaceStyle;

namespace KineticLyrics.Native.Platforms;

[Service(
    Name = "com.kineticslyrics.app.KineticLyricsWallpaperService",
    Permission = "android.permission.BIND_WALLPAPER",
    Exported = true,
    Label = "Kinetic Lyrics")]
[IntentFilter(["android.service.wallpaper.WallpaperService"])]
[MetaData("android.service.wallpaper", Resource = "@xml/kinetic_lyrics_wallpaper")]
public sealed class KineticLyricsWallpaperService : WallpaperService
{
    public override Engine OnCreateEngine() => new LyricsEngine(this);

    private sealed class LyricsEngine : WallpaperService.Engine
    {
        private readonly Handler _handler = new(Looper.MainLooper!);
        private readonly Java.Lang.Runnable _drawRunnable;
        private readonly PlatformMediaSession _mediaSession = new();
        private readonly LyricsService _lyricsService = new(new LocalLyricsProvider());
        private readonly CancellationTokenSource _lifetime = new();
        private MediaTrackSnapshot? _track;
        private LyricsDocument? _lyrics;
        private Bitmap? _artwork;
        private string? _lyricsIdentity;
        private double _lyricsOffset;
        private bool _visible;
        private bool _destroyed;
        private int _width;
        private int _height;
        private DateTimeOffset _animationStarted = DateTimeOffset.UtcNow;

        public LyricsEngine(KineticLyricsWallpaperService service) : base(service)
        {
            SetTouchEventsEnabled(false);
            _drawRunnable = new Java.Lang.Runnable(DrawFrame);
            _mediaSession.TrackChanged += OnTrackChanged;
            _ = _mediaSession.StartAsync(_lifetime.Token);
            try { _lyricsOffset = Preferences.Default.Get("lyrics_offset_seconds", 0d); }
            catch { _lyricsOffset = 0; }
        }

        public override void OnVisibilityChanged(bool visible)
        {
            base.OnVisibilityChanged(visible);
            _visible = visible;
            _handler.RemoveCallbacks(_drawRunnable);
            if (_visible && !_destroyed)
                _handler.Post(_drawRunnable);
        }

        public override void OnSurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
        {
            base.OnSurfaceChanged(holder, format, width, height);
            _width = width;
            _height = height;
            if (_visible)
                _handler.Post(_drawRunnable);
        }

        public override void OnSurfaceDestroyed(ISurfaceHolder holder)
        {
            _handler.RemoveCallbacks(_drawRunnable);
            base.OnSurfaceDestroyed(holder);
        }

        public override void OnDestroy()
        {
            _destroyed = true;
            _visible = false;
            _handler.RemoveCallbacks(_drawRunnable);
            _mediaSession.TrackChanged -= OnTrackChanged;
            _mediaSession.Stop();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _artwork?.Recycle();
            _artwork = null;
            base.OnDestroy();
        }

        private void OnTrackChanged(object? sender, MediaTrackSnapshot? track)
        {
            _handler.Post(() =>
            {
                if (_destroyed)
                    return;

                var identity = track?.Identity + "|" + track?.Source;
                var changed = !string.Equals(identity, _lyricsIdentity, StringComparison.Ordinal);
                _track = track;
                if (changed)
                {
                    _lyricsIdentity = identity;
                    _lyrics = null;
                    _animationStarted = DateTimeOffset.UtcNow;
                    _artwork?.Recycle();
                    _artwork = track?.Artwork is { Length: > 0 } bytes
                        ? BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length)
                        : null;
                    if (track is not null)
                        _ = LoadLyricsAsync(track, identity, _lifetime.Token);
                }

                if (_visible)
                    _handler.Post(_drawRunnable);
            });
        }

        private async Task LoadLyricsAsync(MediaTrackSnapshot track, string? identity, CancellationToken cancellationToken)
        {
            try
            {
                var found = await _lyricsService.FindAsync(track, cancellationToken).ConfigureAwait(false);
                _handler.Post(() =>
                {
                    if (_destroyed || !string.Equals(identity, _lyricsIdentity, StringComparison.Ordinal))
                        return;
                    _lyrics = found;
                    if (_visible)
                        _handler.Post(_drawRunnable);
                });
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private void DrawFrame()
        {
            if (_destroyed || !_visible || _width <= 0 || _height <= 0)
                return;

            var holder = SurfaceHolder;
            if (holder?.Surface?.IsValid != true)
                return;

            Canvas? canvas = null;
            try
            {
                canvas = Build.VERSION.SdkInt >= BuildVersionCodes.O
                    ? holder.LockHardwareCanvas()
                    : holder.LockCanvas();
                if (canvas is null)
                    return;
                DrawScene(canvas, _width, _height);
            }
            catch (Java.Lang.IllegalArgumentException) { }
            catch (Java.Lang.IllegalStateException) { }
            finally
            {
                if (canvas is not null)
                {
                    try { holder.UnlockCanvasAndPost(canvas); }
                    catch (Java.Lang.IllegalArgumentException) { }
                    catch (Java.Lang.IllegalStateException) { }
                }
            }

            if (_visible && !_destroyed)
                _handler.PostDelayed(_drawRunnable, 40);
        }

        private void DrawScene(Canvas canvas, int width, int height)
        {
            var now = DateTimeOffset.UtcNow;
            var position = _track?.CurrentPositionSeconds(now) ?? 0;
            var lyricPosition = Math.Max(0, position + _lyricsOffset);
            var currentIndex = _lyrics?.CurrentLineIndex(lyricPosition) ?? -1;
            var save = canvas.Save();
            canvas.DrawColor(Color.Rgb(7, 7, 10));
            DrawBackground(canvas, width, height, now);
            DrawVignette(canvas, width, height);

            if (_track is null)
            {
                DrawCentered(canvas, "KINETIC LYRICS", width * .5f, height * .46f, Math.Min(width, height) * .055f, Color.Argb(225, 242, 239, 232), true);
                DrawCentered(canvas, "waiting for music…", width * .5f, height * .54f, Math.Min(width, height) * .024f, Color.Argb(132, 242, 239, 232), false);
            }
            else if (_lyrics is null)
            {
                DrawCentered(canvas, "finding lyrics…", width * .5f, height * .53f, Math.Min(width, height) * .03f, Color.Argb(165, 242, 239, 232), false);
            }
            else if (currentIndex >= 0)
            {
                var line = _lyrics.Lines[currentIndex];
                if (currentIndex > 0)
                    DrawGhost(canvas, _lyrics.Lines[currentIndex - 1].Text, width, height * .27f, Math.Min(width, height) * .035f, 54);
                DrawCurrentLine(canvas, line, width, height, lyricPosition, now);
                if (currentIndex + 1 < _lyrics.Lines.Count)
                    DrawGhost(canvas, _lyrics.Lines[currentIndex + 1].Text, width, height * .76f, Math.Min(width, height) * .035f, 45);
                DrawProgress(canvas, line, lyricPosition, width, height);
            }

            DrawTrackCaption(canvas, width, height);
            canvas.RestoreToCount(save);
        }

        private void DrawBackground(Canvas canvas, int width, int height, DateTimeOffset now)
        {
            using var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
            if (_artwork is { IsRecycled: false, Width: > 0, Height: > 0 })
            {
                var scale = Math.Max((float)width / _artwork.Width, (float)height / _artwork.Height);
                var drawWidth = _artwork.Width * scale * 1.12f;
                var drawHeight = _artwork.Height * scale * 1.12f;
                var drift = (float)Math.Sin((now - _animationStarted).TotalSeconds * .08) * width * .012f;
                var destination = new RectF((width - drawWidth) / 2 + drift, (height - drawHeight) / 2, (width + drawWidth) / 2 + drift, (height + drawHeight) / 2);
                paint.Alpha = 150;
                canvas.DrawBitmap(_artwork, null, destination, paint);
                paint.Alpha = 255;
                paint.Color = Color.Argb(164, 4, 4, 8);
                canvas.DrawRect(0, 0, width, height, paint);
            }
            else
            {
                using var gradient = new LinearGradient(0, 0, width, height,
                    new[] { Color.Rgb(17, 15, 23), Color.Rgb(6, 7, 11) }, new float[] { 0, 1 }, Shader.TileMode.Clamp);
                paint.Shader = gradient;
                canvas.DrawRect(0, 0, width, height, paint);
                paint.Shader = null;
            }
        }

        private static void DrawVignette(Canvas canvas, int width, int height)
        {
            using var shader = new RadialGradient(width / 2f, height / 2f, Math.Max(width, height) * .75f,
                new[] { Color.Argb(0, 0, 0, 0), Color.Argb(160, 0, 0, 0) }, new float[] { 0.25f, 1f }, Shader.TileMode.Clamp);
            using var paint = new Paint(PaintFlags.AntiAlias) { Shader = shader };
            canvas.DrawRect(0, 0, width, height, paint);
        }

        private void DrawCurrentLine(Canvas canvas, LyricLine line, int width, int height, double position, DateTimeOffset now)
        {
            var words = line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return;

            var fontSize = Math.Clamp(Math.Min(width, height) * .09f, 32, 112);
            var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.SubpixelText)
            {
                Typeface = Typeface.Create("sans-serif", TypefaceStyle.Bold),
                TextSize = fontSize,
                TextAlign = Paint.Align.Left,
                Color = Color.Rgb(244, 241, 234)
            };
            using (paint)
            {
                paint.SetShadowLayer(8, 0, 2, Color.Argb(170, 0, 0, 0));
                var widths = words.Select(word => paint.MeasureText(word)).ToArray();
                var gap = fontSize * .28f;
                var total = widths.Sum() + gap * Math.Max(0, words.Length - 1);
                var blockWidth = Math.Min(width * .88f, Math.Max(fontSize * 2.2f, total));
                if (total > blockWidth && total > 0)
                {
                    fontSize *= blockWidth / total;
                    paint.TextSize = fontSize;
                    for (var i = 0; i < words.Length; i++)
                        widths[i] = paint.MeasureText(words[i]);
                    gap = fontSize * .28f;
                    total = widths.Sum() + gap * Math.Max(0, words.Length - 1);
                }

                var x = (width - total) / 2;
                var baseline = height * .51f + fontSize * .34f;
                for (var i = 0; i < words.Length; i++)
                {
                    var wordTime = line.WordTimes is { Count: > 0 } times && i < times.Count
                        ? times[i]
                        : line.TimeSeconds + i * .2;
                    paint.Color = position >= wordTime ? Color.Rgb(255, 77, 109) : Color.Rgb(244, 241, 234);
                    var entry = (float)Math.Clamp((position - line.TimeSeconds) / .32, 0, 1);
                    var pop = position >= wordTime ? 1.035f : .94f;
                    var wordY = baseline + (1 - entry) * fontSize * .2f + (float)Math.Sin((now - _animationStarted).TotalSeconds * 1.4 + i * .85) * fontSize * .025f;
                    canvas.Save();
                    canvas.Translate(x, wordY);
                    canvas.Scale(pop, pop);
                    canvas.DrawText(words[i], 0, 0, paint);
                    canvas.Restore();
                    x += widths[i] + gap;
                }
            }
        }

        private static void DrawGhost(Canvas canvas, string text, int width, float y, float size, int alpha)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            if (text.Length > 100)
                text = text[..100];
            using var paint = new Paint(PaintFlags.AntiAlias)
            {
                Typeface = Typeface.Create("sans-serif", TypefaceStyle.Bold),
                TextSize = size,
                TextAlign = Paint.Align.Center,
                Color = Color.Argb(alpha, 244, 241, 234)
            };
            while (paint.MeasureText(text) > width * .86f && paint.TextSize > 8)
                paint.TextSize *= .92f;
            canvas.DrawText(text, width * .5f, y, paint);
        }

        private static void DrawProgress(Canvas canvas, LyricLine line, double position, int width, int height)
        {
            using var paint = new Paint(PaintFlags.AntiAlias)
            {
                Color = Color.Argb(180, 255, 77, 109),
                StrokeWidth = 2
            };
            var start = width * .22f;
            var end = width * .78f;
            var progress = (float)Math.Clamp((position - line.TimeSeconds) / 4, 0, 1);
            canvas.DrawLine(start, height * .67f, start + (end - start) * progress, height * .67f, paint);
        }

        private void DrawTrackCaption(Canvas canvas, int width, int height)
        {
            if (_track is null)
                return;
            using var title = new Paint(PaintFlags.AntiAlias)
            {
                Typeface = Typeface.Create("sans-serif", TypefaceStyle.Bold),
                TextSize = Math.Clamp(Math.Min(width, height) * .021f, 12, 22),
                Color = Color.Argb(190, 244, 241, 234)
            };
            using var artist = new Paint(PaintFlags.AntiAlias)
            {
                Typeface = Typeface.Create("sans-serif", TypefaceStyle.Normal),
                TextSize = title.TextSize * .78f,
                Color = Color.Argb(125, 244, 241, 234)
            };
            canvas.DrawText(_track.Title, width * .04f, height * .935f, title);
            canvas.DrawText(_track.Artist, width * .04f, height * .968f, artist);
        }

        private static void DrawCentered(Canvas canvas, string text, float x, float y, float size, int color, bool bold)
        {
            using var paint = new Paint(PaintFlags.AntiAlias)
            {
                Typeface = Typeface.Create("sans-serif", bold ? TypefaceStyle.Bold : TypefaceStyle.Normal),
                TextSize = size,
                TextAlign = Paint.Align.Center,
                Color = color
            };
            canvas.DrawText(text, x, y, paint);
        }
    }
}
