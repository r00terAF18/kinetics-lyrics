using KineticLyrics.Native.Models;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace KineticLyrics.Native.Views;

public enum LyricLayout
{
    Auto,
    Hero,
    Flow,
    Stack,
    Wave,
    Diagonal,
    Arc,
    Column
}

public sealed class KineticLyricsView : ContentView
{
    private readonly SKGLView _surface;
    private MediaTrackSnapshot? _track;
    private LyricsDocument? _lyrics;
    private SKBitmap? _artwork;
    private DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public LyricLayout LayoutMode { get; set; } = LyricLayout.Auto;
    public double LyricsOffsetSeconds { get; set; }
    public bool UseAlbumArt { get; set; } = true;

    public KineticLyricsView()
    {
        _surface = new SKGLView
        {
            HasRenderLoop = true,
            EnableTouchEvents = false,
            BackgroundColor = Colors.Black
        };
        _surface.PaintSurface += PaintSurface;
        Content = _surface;
    }

    public void SetMedia(MediaTrackSnapshot? track)
    {
        var changed = !string.Equals(_track?.Identity, track?.Identity, StringComparison.Ordinal) ||
            !string.Equals(_track?.Source, track?.Source, StringComparison.Ordinal);
        _track = track;
        if (changed)
        {
            _startedAt = DateTimeOffset.UtcNow;
            _artwork?.Dispose();
            _artwork = track?.Artwork is { Length: > 0 } bytes ? SKBitmap.Decode(bytes) : null;
        }
    }

    public void SetLyrics(LyricsDocument? lyrics) => _lyrics = lyrics;

    private void PaintSurface(object? sender, SKPaintGLSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        var width = args.Info.Width;
        var height = args.Info.Height;
        var now = DateTimeOffset.UtcNow;
        var position = _track?.CurrentPositionSeconds(now) ?? 0;
        var lyricPosition = Math.Max(0, position + LyricsOffsetSeconds);
        var currentIndex = _lyrics?.CurrentLineIndex(lyricPosition) ?? -1;
        var progress = currentIndex >= 0 ? Math.Clamp((lyricPosition - _lyrics!.Lines[currentIndex].TimeSeconds) / 1.7, 0, 1) : 0;

        canvas.Clear(new SKColor(7, 7, 10));
        DrawBackground(canvas, width, height, now);
        DrawVignette(canvas, width, height);

        if (_track is null)
        {
            DrawCentered(canvas, "KINETIC LYRICS", width * .5f, height * .46f, Math.Min(width, height) * .055f, new SKColor(242, 239, 232, 225), true);
            DrawCentered(canvas, "waiting for music…", width * .5f, height * .54f, Math.Min(width, height) * .024f, new SKColor(242, 239, 232, 132), false);
            return;
        }

        if (_lyrics is null || currentIndex < 0)
        {
            var message = _lyrics is null ? "finding lyrics…" : "";
            if (message.Length > 0)
                DrawCentered(canvas, message, width * .5f, height * .52f, Math.Min(width, height) * .03f, new SKColor(242, 239, 232, 150), false);
        }
        else
        {
            DrawLyricScene(canvas, width, height, lyricPosition, currentIndex, progress, now);
        }

        DrawTrackCaption(canvas, width, height);
    }

    private void DrawBackground(SKCanvas canvas, int width, int height, DateTimeOffset now)
    {
        if (UseAlbumArt && _artwork is not null)
        {
            var scale = Math.Max((float)width / _artwork.Width, (float)height / _artwork.Height);
            var drawWidth = _artwork.Width * scale * 1.12f;
            var drawHeight = _artwork.Height * scale * 1.12f;
            var drift = (float)Math.Sin((now - _startedAt).TotalSeconds * .08) * width * .012f;
            var destination = new SKRect((width - drawWidth) / 2 + drift, (height - drawHeight) / 2, (width + drawWidth) / 2 + drift, (height + drawHeight) / 2);
            using var artPaint = new SKPaint { Color = new SKColor(255, 255, 255, 142), FilterQuality = SKFilterQuality.Medium };
            canvas.DrawBitmap(_artwork, destination, artPaint);
            using var shade = new SKPaint { Color = new SKColor(4, 4, 8, 164) };
            canvas.DrawRect(0, 0, width, height, shade);
        }
        else
        {
            using var gradient = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(width, height),
                [new SKColor(17, 15, 23), new SKColor(6, 7, 11)], null, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = gradient };
            canvas.DrawRect(0, 0, width, height, paint);
        }
    }

    private static void DrawVignette(SKCanvas canvas, int width, int height)
    {
        using var shader = SKShader.CreateRadialGradient(new SKPoint(width / 2f, height / 2f), Math.Max(width, height) * .75f,
            [SKColors.Transparent, new SKColor(0, 0, 0, 160)], [0.25f, 1], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, paint);
    }

    private void DrawLyricScene(SKCanvas canvas, int width, int height, double position, int currentIndex, double progress, DateTimeOffset now)
    {
        var safeIndex = Math.Clamp(currentIndex, 0, _lyrics!.Lines.Count - 1);
        var current = _lyrics.Lines[safeIndex];
        var titleSize = Math.Clamp(Math.Min(width, height) * .09f, 32, 112);
        var fade = (float)(.35 + .65 * Math.Sin(Math.PI * Math.Min(1, progress * .92)));
        var layout = ResolveLayout(safeIndex, current.Text);

        if (safeIndex > 0)
            DrawGhostLine(canvas, _lyrics.Lines[safeIndex - 1], width, height, titleSize * .34f, height * .27f, 48);
        if (safeIndex + 1 < _lyrics.Lines.Count)
            DrawGhostLine(canvas, _lyrics.Lines[safeIndex + 1], width, height, titleSize * .34f, height * .76f, 38);

        DrawCurrentLine(canvas, current, width, height, position, titleSize, fade, layout, now);

        if (safeIndex + 2 < _lyrics.Lines.Count)
            DrawGhostLine(canvas, _lyrics.Lines[safeIndex + 2], width, height, titleSize * .24f, height * .88f, 22);
    }

    private void DrawCurrentLine(SKCanvas canvas, LyricLine line, int width, int height, double position, float fontSize, float fade, LyricLayout layout, DateTimeOffset now)
    {
        var words = line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return;

        using var normal = new SKPaint { Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), TextSize = fontSize, IsAntialias = true, SubpixelText = true };
        using var hot = new SKPaint { Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), TextSize = fontSize, IsAntialias = true, SubpixelText = true };
        using var shadow = new SKPaint { Typeface = normal.Typeface, TextSize = fontSize, IsAntialias = true, Color = new SKColor(0, 0, 0, 170), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8) };

        var widths = words.Select(word => normal.MeasureText(word)).ToArray();
        var gap = fontSize * .28f;
        var total = widths.Sum() + gap * Math.Max(0, words.Length - 1);
        var centerX = width * .5f;
        var centerY = height * .51f;
        var blockWidth = Math.Min(width * .88f, Math.Max(fontSize * 2.2f, total));
        var blockScale = total > blockWidth && total > 0 ? blockWidth / total : 1f;
        var scaledFontSize = fontSize * blockScale;
        normal.TextSize = scaledFontSize;
        hot.TextSize = scaledFontSize;
        shadow.TextSize = scaledFontSize;
        var startX = centerX - total * blockScale * .5f;

        for (var i = 0; i < words.Length; i++)
        {
            var wordTime = line.WordTimes is { Count: > 0 } wt && i < wt.Count ? wt[i] : line.TimeSeconds + i * .2;
            var isHot = position >= wordTime;
            var wordWidth = widths[i] * blockScale;
            float x;
            float y;
            float rotation = 0;
            switch (layout)
            {
                case LyricLayout.Stack:
                case LyricLayout.Column:
                    x = centerX;
                    y = centerY - (words.Length - 1) * scaledFontSize * .45f + i * scaledFontSize * .9f;
                    break;
                case LyricLayout.Diagonal:
                    x = centerX - total * blockScale * .36f + widths.Take(i).Sum() * blockScale + gap * i * blockScale;
                    y = centerY - scaledFontSize * .35f * words.Length + i * scaledFontSize * .58f;
                    rotation = -8;
                    break;
                case LyricLayout.Wave:
                    x = startX + widths.Take(i).Sum() * blockScale + gap * i * blockScale + wordWidth * .5f;
                    y = centerY + (float)Math.Sin(i * .85 + (now - _startedAt).TotalSeconds * 1.4) * scaledFontSize * .23f;
                    rotation = (float)Math.Cos(i * .85 + (now - _startedAt).TotalSeconds) * 4;
                    break;
                case LyricLayout.Arc:
                    x = startX + widths.Take(i).Sum() * blockScale + gap * i * blockScale + wordWidth * .5f;
                    var t = words.Length <= 1 ? .5 : (double)i / (words.Length - 1);
                    y = centerY + (float)(Math.Sin(t * Math.PI) * -scaledFontSize * .48f + scaledFontSize * .12f);
                    rotation = (float)((.5 - t) * 16);
                    break;
                default:
                    x = startX + widths.Take(i).Sum() * blockScale + gap * i * blockScale;
                    y = centerY;
                    break;
            }

            var entry = (float)Math.Clamp((position - line.TimeSeconds) / .32, 0, 1);
            var pop = isHot ? 1.035f : .94f;
            var alpha = (byte)Math.Clamp((int)(fade * 255), 0, 255);
            var wordColor = isHot ? new SKColor(255, 77, 109, alpha) : new SKColor(244, 241, 234, alpha);
            var paint = isHot ? hot : normal;
            paint.Color = wordColor;

            canvas.Save();
            canvas.Translate(x, y + (1 - entry) * scaledFontSize * .2f);
            canvas.RotateDegrees(rotation);
            canvas.Scale(pop, pop);
            var drawX = layout is LyricLayout.Stack or LyricLayout.Column or LyricLayout.Wave or LyricLayout.Arc
                ? -widths[i] * blockScale / 2
                : 0;
            canvas.DrawText(words[i], drawX, scaledFontSize * .34f, shadow);
            canvas.DrawText(words[i], drawX, scaledFontSize * .34f, paint);
            canvas.Restore();
        }

        DrawProgress(canvas, width, height, line, position, fade);
    }

    private static void DrawGhostLine(SKCanvas canvas, LyricLine line, int width, int height, float fontSize, float y, byte alpha)
    {
        if (line.IsInstrumental)
            return;
        using var paint = new SKPaint { Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), TextSize = fontSize, IsAntialias = true, Color = new SKColor(244, 241, 234, alpha), TextAlign = SKTextAlign.Center };
        var text = line.Text.Length > 100 ? line.Text[..100] : line.Text;
        var maxWidth = width * .86f;
        while (paint.MeasureText(text) > maxWidth && text.Length > 4)
        {
            paint.TextSize *= .92f;
        }
        canvas.DrawText(text, width * .5f, y, paint);
    }

    private static void DrawProgress(SKCanvas canvas, int width, int height, LyricLine line, double position, float alpha)
    {
        using var paint = new SKPaint { Color = new SKColor(255, 77, 109, (byte)(alpha * 180)), StrokeWidth = 2, IsAntialias = true };
        var end = width * .78f;
        var start = width * .22f;
        var normalized = Math.Clamp((position - line.TimeSeconds) / 4, 0, 1);
        canvas.DrawLine(start, height * .67f, start + (end - start) * (float)normalized, height * .67f, paint);
    }

    private void DrawTrackCaption(SKCanvas canvas, int width, int height)
    {
        if (_track is null)
            return;
        using var titlePaint = new SKPaint { Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), TextSize = Math.Clamp(Math.Min(width, height) * .021f, 12, 22), IsAntialias = true, Color = new SKColor(244, 241, 234, 190) };
        using var artistPaint = new SKPaint { Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal), TextSize = titlePaint.TextSize * .78f, IsAntialias = true, Color = new SKColor(244, 241, 234, 125) };
        canvas.DrawText(_track.Title, width * .04f, height * .935f, titlePaint);
        canvas.DrawText(_track.Artist, width * .04f, height * .968f, artistPaint);
    }

    private LyricLayout ResolveLayout(int index, string text)
    {
        if (LayoutMode != LyricLayout.Auto)
            return LayoutMode;
        var choices = new[] { LyricLayout.Hero, LyricLayout.Flow, LyricLayout.Stack, LyricLayout.Wave, LyricLayout.Diagonal, LyricLayout.Arc, LyricLayout.Column };
        var hash = StringComparer.Ordinal.GetHashCode(text + "|" + index);
        return choices[(hash & int.MaxValue) % choices.Length];
    }

    private static void DrawCentered(SKCanvas canvas, string text, float x, float y, float size, SKColor color, bool bold)
    {
        using var paint = new SKPaint { Typeface = SKTypeface.FromFamilyName("Arial", bold ? SKFontStyle.Bold : SKFontStyle.Normal), TextSize = size, IsAntialias = true, Color = color, TextAlign = SKTextAlign.Center };
        canvas.DrawText(text, x, y, paint);
    }
}
