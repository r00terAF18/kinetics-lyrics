using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KineticLyrics;

namespace KineticWallpaper;

public enum LayoutKind
{
    Hero, Stack, Flow, Diagonal, Staircase, Column, Split, Grid,
    Arc, Pyramid, BigInitial, Wave, Sandwich, Billboard, Echo,
    Mirror, Quote, Typewriter, Headline, Pillars, Banners, Ransom, Tilt, Diamond, Ring
}

public enum TransitionKind
{
    Slam, Drop, Zoom, Spin, Fade, Rise, Pop, Flip, Stretch, Skew, Fly, Orbit
}

public sealed record LayoutUnit(
    IReadOnlyList<int> WordIndices,
    string? DecorativeText,
    FontFamily Font,
    FontWeight Weight,
    FontStyle Style,
    bool Uppercase,
    double FontSize,
    double X,
    double Y,
    double AngleDeg,
    double Opacity,
    bool IsAccent,
    bool IsOutline,
    bool IsMirror,
    double FromX,
    double FromY,
    double FromAngleDeg,
    double FromScale);

public static class KineticLayoutEngine
{
    private static readonly FontFamily FontCondensed = new("Bahnschrift, Segoe UI");
    private static readonly FontFamily FontBlack = new("Segoe UI Black, Arial Black");
    private static readonly FontFamily FontImpact = new("Impact, Haettenschweiler");
    private static readonly FontFamily FontSerif = new("Georgia, Times New Roman");
    private static readonly FontFamily FontMono = new("Consolas, Cascadia Mono");

    private static readonly HashSet<string> SmallWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","to","of","in","on","at","by","for","and","or","but","so","if",
        "is","am","are","was","be","i","my","me","you","your","we","our","it","its",
        "it's","i'm","i'll","you're","that","this","with","from","as","up","oh","yeah","no"
    };

    public static uint HashString(string text)
    {
        uint h = 2166136261;
        foreach (char c in text)
            h = (h ^ c) * 16777619;
        return h;
    }

    public static (LayoutKind Layout, TransitionKind Transition, IReadOnlyList<LayoutUnit> Units) Compose(
        LyricLine line, double width, double height, double chaos, LayoutKind? previousLayout, TransitionKind? previousTransition)
    {
        var words = line.Words.Select(w => w.Text).ToList();
        if (words.Count == 0)
            return (LayoutKind.Hero, TransitionKind.Fade, []);

        uint seed = HashString($"{line.Text.ToLowerInvariant()}|{line.RepeatIndex}");
        var rng = new Random((int)seed);

        var layout = ChooseLayout(line, words, chaos, previousLayout, seed);
        var transition = ChooseTransition(line, previousTransition, seed);

        var rawUnits = BuildLayout(layout, words, width, height, rng);
        if (line.IsChorus && rawUnits.Count > 0)
        {
            int biggest = 0;
            for (int i = 1; i < rawUnits.Count; i++)
                if (rawUnits[i].DecorativeText is null && rawUnits[i].FontSize > rawUnits[biggest].FontSize)
                    biggest = i;
            rawUnits[biggest] = rawUnits[biggest] with { IsAccent = true, IsOutline = false };
        }

        var resolved = DeclutterAndFit(rawUnits, words, width, height);
        return (layout, transition, resolved);
    }

    public static IReadOnlyList<LayoutUnit> ComposeClock(string time, string day, string date, double width, double height)
    {
        var mTime = Measure(time, FontCondensed, FontWeights.Bold, FontStyles.Normal, false, 100);
        double fsTime = Math.Min(100 * width * 0.55 / mTime.Width, 100 * height * 0.45 / mTime.Height);
        double hTime = mTime.Height * fsTime / 100.0;

        var mDay = Measure(day, FontSerif, FontWeights.Normal, FontStyles.Italic, false, 100);
        double fsDay = Math.Min(height * 0.09, 100 * width * 0.4 / mDay.Width);

        var mDate = Measure(date.ToUpperInvariant(), FontMono, FontWeights.Normal, FontStyles.Normal, true, 100);
        double fsDate = Math.Min(height * 0.038, 100 * width * 0.5 / mDate.Width);

        double cx = width / 2, cy = height * 0.5;
        return
        [
            new([0], null, FontCondensed, FontWeights.Bold, FontStyles.Normal, false, fsTime, cx, cy, 0, 0.92, false, false, false, cx, cy, 0, 1),
            new([1], null, FontSerif, FontWeights.Normal, FontStyles.Italic, false, fsDay, cx, cy - hTime * 0.55 - fsDay * 0.5, 0, 0.95, true, false, false, cx, cy - hTime * 0.55 - fsDay * 0.5, 0, 1),
            new([2], null, FontMono, FontWeights.Normal, FontStyles.Normal, true, fsDate, cx, cy + hTime * 0.55 + fsDate * 0.9, 0, 0.65, false, false, false, cx, cy + hTime * 0.55 + fsDate * 0.9, 0, 1)
        ];
    }

    private static LayoutKind ChooseLayout(LyricLine line, List<string> words, double chaos, LayoutKind? prev, uint seed)
    {
        int n = words.Count;
        if (line.IsAdLibOnly || n == 1) return LayoutKind.Hero;

        var sec = line.Section?.Type;
        LayoutKind[] pool = sec switch
        {
            SectionType.Chorus => [LayoutKind.Hero, LayoutKind.Stack, LayoutKind.Diagonal, LayoutKind.Billboard, LayoutKind.Pyramid, LayoutKind.Headline, LayoutKind.Ransom, LayoutKind.Banners],
            SectionType.PreChorus => [LayoutKind.Pyramid, LayoutKind.Wave, LayoutKind.Arc, LayoutKind.Echo, LayoutKind.Diamond, LayoutKind.Stack],
            SectionType.Bridge => [LayoutKind.Quote, LayoutKind.Mirror, LayoutKind.Ring, LayoutKind.BigInitial, LayoutKind.Flow, LayoutKind.Billboard],
            _ when n <= 3 => [LayoutKind.Hero, LayoutKind.Staircase, LayoutKind.Column, LayoutKind.Split, LayoutKind.Echo, LayoutKind.Mirror],
            _ when n <= 7 => [LayoutKind.Flow, LayoutKind.Stack, LayoutKind.Staircase, LayoutKind.Split, LayoutKind.Grid, LayoutKind.Arc, LayoutKind.Sandwich, LayoutKind.Headline, LayoutKind.Tilt, LayoutKind.Wave],
            _ => [LayoutKind.Flow, LayoutKind.Stack, LayoutKind.Typewriter, LayoutKind.Quote, LayoutKind.Pyramid]
        };

        if ((seed % 100) < chaos * 100 && n >= 3)
            pool = [LayoutKind.Ransom, LayoutKind.Banners, LayoutKind.Pillars, LayoutKind.Tilt, LayoutKind.Diagonal, LayoutKind.Diamond, LayoutKind.Ring];

        int idx = (int)((seed >> 8) % (uint)pool.Length);
        var chosen = pool[idx];
        if (chosen == prev && pool.Length > 1)
            chosen = pool[(idx + 1) % pool.Length];
        return chosen;
    }

    private static TransitionKind ChooseTransition(LyricLine line, TransitionKind? prev, uint seed)
    {
        var sec = line.Section?.Type;
        TransitionKind[] pool = sec switch
        {
            SectionType.Chorus => [TransitionKind.Slam, TransitionKind.Drop, TransitionKind.Zoom, TransitionKind.Pop, TransitionKind.Flip],
            SectionType.Bridge => [TransitionKind.Fade, TransitionKind.Orbit, TransitionKind.Rise],
            _ => [TransitionKind.Slam, TransitionKind.Rise, TransitionKind.Fade, TransitionKind.Pop, TransitionKind.Skew, TransitionKind.Fly, TransitionKind.Stretch]
        };

        int idx = (int)((seed >> 16) % (uint)pool.Length);
        var chosen = pool[idx];
        if (chosen == prev && pool.Length > 1)
            chosen = pool[(idx + 1) % pool.Length];
        return chosen;
    }

    private static List<LayoutUnit> BuildLayout(LayoutKind kind, List<string> words, double W, double H, Random rng)
    {
        int n = words.Count;
        int longest = 0;
        for (int i = 1; i < n; i++)
            if (words[i].Length > words[longest].Length) longest = i;

        switch (kind)
        {
            case LayoutKind.Hero:
            {
                var font = Pick(rng, FontImpact, FontBlack, FontCondensed);
                var m = Measure(words[longest], font, FontWeights.Black, FontStyles.Normal, true, 100);
                double fs = Math.Min(100 * W * 0.82 / m.Width, 100 * H * 0.48 / m.Height);
                double hh = m.Height * fs / 100.0;
                double angle = n > 4 ? Pick(rng, 0.0, -3.5, 3.5) : Pick(rng, 0.0, -4.0, 4.0);
                var list = new List<LayoutUnit>
                {
                    new([longest], null, font, FontWeights.Black, FontStyles.Normal, true, fs, W / 2, H / 2, angle, 1, rng.NextDouble() < 0.5, rng.NextDouble() < 0.3, false, W / 2, H / 2, angle * 2, 2.4)
                };

                var before = Enumerable.Range(0, longest).ToArray();
                var after = Enumerable.Range(longest + 1, n - longest - 1).ToArray();
                if (before.Length > 0)
                {
                    string txt = string.Join(" ", before.Select(i => words[i]));
                    var bm = Measure(txt, FontMono, FontWeights.Normal, FontStyles.Normal, true, 100);
                    double bfs = Math.Min(H * 0.045, 100 * W * 0.72 / bm.Width);
                    double y = H / 2 - hh * 0.55 - bfs * 0.9;
                    list.Add(new(before, null, FontMono, FontWeights.Normal, FontStyles.Normal, true, bfs, W / 2, y, 0, 0.9, false, false, false, W / 2, y - 60, 0, 1));
                }
                if (after.Length > 0)
                {
                    string txt = string.Join(" ", after.Select(i => words[i]));
                    var am = Measure(txt, FontMono, FontWeights.Normal, FontStyles.Normal, true, 100);
                    double afs = Math.Min(H * 0.045, 100 * W * 0.72 / am.Width);
                    double y = H / 2 + hh * 0.55 + afs * 0.9;
                    list.Add(new(after, null, FontMono, FontWeights.Normal, FontStyles.Normal, true, afs, W / 2, y, 0, 0.9, false, false, false, W / 2, y + 60, 0, 1));
                }
                return list;
            }

            case LayoutKind.Diagonal:
            {
                string full = string.Join(" ", words);
                var font = Pick(rng, FontCondensed, FontImpact, FontBlack);
                var m = Measure(full, font, FontWeights.Black, FontStyles.Normal, true, 100);
                double angle = Pick(rng, -10.0, -7.0, 7.0, 10.0);
                double fs = Math.Min(100 * W * 0.88 / m.Width, 100 * H * 0.24 / m.Height);
                double rad = angle * Math.PI / 180.0;
                double ox = -Math.Sin(rad) * (m.Height * fs / 100.0) * 1.05;
                double oy = Math.Cos(rad) * (m.Height * fs / 100.0) * 1.05;

                var list = new List<LayoutUnit>
                {
                    new(Enumerable.Range(0, n).ToArray(), null, font, FontWeights.Black, FontStyles.Normal, true, fs, W / 2, H / 2, angle, 1, rng.NextDouble() < 0.5, false, false, W / 2 - Math.Cos(rad) * W * 0.4, H / 2 - Math.Sin(rad) * W * 0.4, angle, 1)
                };
                foreach (int k in new[] { -2, -1, 1, 2 })
                {
                    list.Add(new([], full, font, FontWeights.Black, FontStyles.Normal, true, fs, W / 2 + k * ox, H / 2 + k * oy, angle, 0.28 / Math.Abs(k), false, true, false, W / 2 + k * ox, H / 2 + k * oy, angle, 1));
                }
                return list;
            }

            case LayoutKind.Staircase:
            {
                var font = Pick(rng, FontCondensed, FontBlack);
                var ms = words.Select(w => Measure(w, font, FontWeights.Bold, FontStyles.Normal, true, 100)).ToArray();
                double pad = Math.Min(W, H) * 0.025;
                double fs = Math.Min(100 * (H * 0.82 - pad * n) / n / ms[0].Height, H * 0.26);
                double maxW = ms.Max(m => m.Width) * fs / 100.0;
                if (maxW > W * 0.55) fs *= (W * 0.55) / maxW;

                double rowH = ms[0].Height * fs / 100.0 + pad;
                double stepX = Math.Max(0, (W * 0.82 - ms.Max(m => m.Width) * fs / 100.0) / Math.Max(1, n - 1));
                double top = (H - rowH * n) / 2.0;

                var list = new List<LayoutUnit>();
                for (int i = 0; i < n; i++)
                {
                    double ww = ms[i].Width * fs / 100.0;
                    double x = W * 0.09 + i * stepX + ww / 2.0;
                    double y = top + rowH * (i + 0.5);
                    list.Add(new([i], null, font, FontWeights.Bold, FontStyles.Normal, true, fs, x, y, 0, 1, i == n - 1, i % 2 == 1, false, x - 140, y, 0, 1));
                }
                return list;
            }

            case LayoutKind.Column:
            {
                var font = Pick(rng, FontCondensed, FontImpact, FontBlack);
                double colW = W * (0.32 + rng.NextDouble() * 0.12);
                double cx = Pick(rng, W * 0.32, W * 0.5, W * 0.68);
                var items = words.Select((w, i) =>
                {
                    var m = Measure(w, font, FontWeights.Black, FontStyles.Normal, true, 100);
                    double fs = 100 * colW / m.Width;
                    return (Idx: i, Fs: fs, H: m.Height * fs / 100.0);
                }).ToList();

                double pad = H * 0.02;
                double totalH = items.Sum(x => x.H + pad);
                double scale = Math.Min(1.0, H * 0.84 / totalH);
                totalH *= scale;

                double y = (H - totalH) / 2.0;
                var list = new List<LayoutUnit>();
                for (int i = 0; i < items.Count; i++)
                {
                    double fs = items[i].Fs * scale;
                    double hh = items[i].H * scale;
                    double yy = y + hh / 2.0;
                    y += hh + pad * scale;
                    list.Add(new([i], null, font, FontWeights.Black, FontStyles.Normal, true, fs, cx, yy, 0, 1, i == longest, i % 3 == 1, false, cx, yy + 50, 0, 1));
                }
                return list;
            }

            case LayoutKind.Split:
            {
                int half = (int)Math.Ceiling(n / 2.0);
                var first = Enumerable.Range(0, half).ToArray();
                var second = Enumerable.Range(half, n - half).ToArray();
                string tA = string.Join(" ", first.Select(i => words[i]));
                string tB = string.Join(" ", second.Select(i => words[i]));
                var mA = Measure(tA, FontSerif, FontWeights.Normal, FontStyles.Italic, false, 100);
                var mB = Measure(tB, FontBlack, FontWeights.Black, FontStyles.Normal, true, 100);
                double fsA = Math.Min(100 * W * 0.64 / mA.Width, H * 0.26);
                double fsB = Math.Min(100 * W * 0.64 / mB.Width, H * 0.26);
                double wA = mA.Width * fsA / 100.0;
                double wB = mB.Width * fsB / 100.0;

                return
                [
                    new(first, null, FontSerif, FontWeights.Normal, FontStyles.Italic, false, fsA, W * 0.08 + wA / 2.0, H * 0.34, 0, 1, false, false, false, -W * 0.2, H * 0.34, 0, 1),
                    new(second, null, FontBlack, FontWeights.Black, FontStyles.Normal, true, fsB, W * 0.92 - wB / 2.0, H * 0.66, 0, 1, true, false, false, W * 1.2, H * 0.66, 0, 1)
                ];
            }

            case LayoutKind.Wave:
            case LayoutKind.Arc:
            {
                var font = Pick(rng, FontCondensed, FontBlack);
                var ms = words.Select(w => Measure(w, font, FontWeights.Bold, FontStyles.Normal, true, 100)).ToArray();
                double fs = Math.Min(H * 0.22, 100 * W * 0.72 / ms.Sum(m => m.Width + 24));
                double totalW = ms.Sum(m => m.Width * fs / 100.0) + fs * 0.35 * (n - 1);
                double x = (W - totalW) / 2.0;
                var list = new List<LayoutUnit>();

                for (int i = 0; i < n; i++)
                {
                    double ww = ms[i].Width * fs / 100.0;
                    double cx = x + ww / 2.0;
                    x += ww + fs * 0.35;
                    double norm = n > 1 ? (i / (double)(n - 1)) * 2.0 - 1.0 : 0;
                    double y = kind == LayoutKind.Arc
                        ? H * 0.44 + (norm * norm) * H * 0.14
                        : H * 0.50 + Math.Sin(norm * Math.PI * 1.2) * H * 0.12;
                    double ang = kind == LayoutKind.Arc ? norm * 14.0 : Math.Cos(norm * Math.PI * 1.2) * 8.0;
                    list.Add(new([i], null, font, FontWeights.Bold, FontStyles.Normal, true, fs, cx, y, ang, 1, i == longest, i % 3 == 2, false, cx, y + (i % 2 == 0 ? 120 : -120), 0, 1));
                }
                return list;
            }

            case LayoutKind.Mirror:
            {
                string full = string.Join(" ", words);
                var font = Pick(rng, FontBlack, FontCondensed, FontImpact);
                var m = Measure(full, font, FontWeights.Black, FontStyles.Normal, true, 100);
                double fs = Math.Min(100 * W * 0.84 / m.Width, 100 * H * 0.28 / m.Height);
                double hh = m.Height * fs / 100.0;
                double y = H * 0.42;
                return
                [
                    new(Enumerable.Range(0, n).ToArray(), null, font, FontWeights.Black, FontStyles.Normal, true, fs, W / 2, y, 0, 1, false, false, false, W / 2, y - 90, 0, 1),
                    new([], full, font, FontWeights.Black, FontStyles.Normal, true, fs, W / 2, y + hh * 0.95, 0, 0.25, true, false, true, W / 2, y + hh * 0.95 + 90, 0, 1)
                ];
            }

            case LayoutKind.Billboard:
            {
                var dm = Measure(words[longest], FontImpact, FontWeights.Black, FontStyles.Normal, true, 100);
                double dfs = Math.Min(100 * W * 0.92 / dm.Width, 100 * H * 0.68 / dm.Height);
                var list = new List<LayoutUnit>
                {
                    new([], words[longest], FontImpact, FontWeights.Black, FontStyles.Normal, true, dfs, W / 2, H / 2, 0, 0.18, true, true, false, W / 2, H / 2, 0, 1.2)
                };
                list.AddRange(BuildStack(words, W * 0.84, H * 0.65, W * 0.08, H * 0.175, rng));
                return list;
            }

            case LayoutKind.Quote:
            {
                double qfs = H * 0.42;
                var list = new List<LayoutUnit>
                {
                    new([], "“", FontSerif, FontWeights.Bold, FontStyles.Italic, false, qfs, W * 0.12, H * 0.34, 0, 0.45, true, false, false, W * 0.12, H * 0.34, 0, 1.3),
                    new([], "”", FontSerif, FontWeights.Bold, FontStyles.Italic, false, qfs, W * 0.88, H * 0.68, 0, 0.45, true, false, false, W * 0.88, H * 0.68, 0, 1.3)
                };
                list.AddRange(BuildStack(words, W * 0.66, H * 0.64, W * 0.17, H * 0.18, rng, FontSerif, FontStyles.Italic, false));
                return list;
            }

            case LayoutKind.Ransom:
            {
                FontFamily[] faces = [FontCondensed, FontSerif, FontImpact, FontBlack, FontMono];
                var rows = ChunkIndices(n, 3);
                double rowH = H * 0.72 / rows.Count;
                double y = (H - rowH * rows.Count) / 2.0;
                var list = new List<LayoutUnit>();

                foreach (var row in rows)
                {
                    double slotW = W * 0.84 / row.Length;
                    for (int c = 0; c < row.Length; c++)
                    {
                        int idx = row[c];
                        int faceIndex = (int)(((ulong)(uint)Math.Max(0, idx) * 3UL + HashString(words[idx])) % (ulong)faces.Length);
                        var f = faces[faceIndex];
                        var m = Measure(words[idx], f, FontWeights.Bold, FontStyles.Normal, true, 100);
                        double fs = Math.Min(100 * slotW * 0.86 / m.Width, 100 * rowH * 0.76 / m.Height);
                        double cx = W * 0.08 + slotW * (c + 0.5);
                        double cy = y + rowH * 0.5;
                        double ang = rng.Next(-6, 7);
                        list.Add(new([idx], null, f, FontWeights.Bold, FontStyles.Normal, true, fs, cx, cy, ang, 1, idx == longest, idx % 4 == 2, false, cx, cy, ang * 3, 0.2));
                    }
                    y += rowH;
                }
                return list;
            }

            case LayoutKind.Flow:
            {
                var list = new List<LayoutUnit>();
                var rows = ChunkIndices(n, n > 7 ? 4 : 3);
                double y = H * 0.22;
                double stepY = (H * 0.56) / Math.Max(1, rows.Count - 1);
                if (rows.Count == 1) y = H * 0.5;

                for (int r = 0; r < rows.Count; r++)
                {
                    var row = rows[r];
                    var specs = row.Select(i =>
                    {
                        bool small = SmallWords.Contains(words[i]) && n > 2;
                        var f = small ? FontSerif : FontBlack;
                        var st = small ? FontStyles.Italic : FontStyles.Normal;
                        double baseFs = small ? H * 0.065 : H * 0.145;
                        var m = Measure(words[i], f, FontWeights.Bold, st, !small, 100);
                        return (Idx: i, Font: f, Style: st, Upper: !small, Fs: baseFs, W: m.Width * baseFs / 100.0);
                    }).ToList();

                    double totalW = specs.Sum(s => s.W) + H * 0.03 * (specs.Count - 1);
                    double scale = Math.Min(1.0, W * 0.84 / totalW);
                    double x = (W - totalW * scale) / 2.0;
                    double cy = rows.Count == 1 ? H * 0.5 : y + r * stepY;

                    foreach (var s in specs)
                    {
                        double ww = s.W * scale;
                        list.Add(new([s.Idx], null, s.Font, FontWeights.Bold, s.Style, s.Upper, s.Fs * scale, x + ww / 2.0, cy, 0, 1, s.Idx == longest, false, false, x + ww / 2.0, cy + 50, 0, 1));
                        x += ww + H * 0.03 * scale;
                    }
                }
                return list;
            }

            case LayoutKind.Ring:
            {
                var centerM = Measure(words[0], FontBlack, FontWeights.Black, FontStyles.Normal, true, 100);
                double centerFs = Math.Min(100 * W * 0.36 / centerM.Width, 100 * H * 0.26 / centerM.Height);
                var list = new List<LayoutUnit>
                {
                    new([0], null, FontBlack, FontWeights.Black, FontStyles.Normal, true, centerFs, W / 2, H / 2, 0, 1, true, false, false, W / 2, H / 2, 0, 0.3)
                };
                int restCount = n - 1;
                double rx = W * 0.34, ry = H * 0.33;
                for (int j = 0; j < restCount; j++)
                {
                    int idx = j + 1;
                    double a = -Math.PI / 2.0 + j * 2.0 * Math.PI / restCount;
                    var m = Measure(words[idx], FontCondensed, FontWeights.Bold, FontStyles.Normal, true, 100);
                    double fs = Math.Min(100 * W * 0.18 / m.Width, H * 0.09);
                    double x = W / 2 + rx * Math.Cos(a);
                    double y = H / 2 + ry * Math.Sin(a);
                    list.Add(new([idx], null, FontCondensed, FontWeights.Bold, FontStyles.Normal, true, fs, x, y, 0, 1, false, j % 2 == 1, false, W / 2, H / 2, 0, 0.1));
                }
                return list;
            }

            default:
                return BuildStack(words, W * 0.82, H * 0.78, W * 0.09, H * 0.11, rng,
                    tilt: kind == LayoutKind.Tilt,
                    pyramid: kind is LayoutKind.Pyramid or LayoutKind.Diamond);
        }
    }

    private static List<LayoutUnit> BuildStack(
        List<string> words, double boxW, double boxH, double offsetX, double offsetY, Random rng,
        FontFamily? overrideFont = null, FontStyle overrideStyle = default, bool uppercase = true,
        bool tilt = false, bool pyramid = false)
    {
        var font = overrideFont ?? Pick(rng, FontCondensed, FontBlack, FontImpact);
        int perRow = pyramid ? 2 : (words.Count > 6 ? 3 : 2);
        var rows = ChunkIndices(words.Count, perRow);

        var rowItems = rows.Select((r, rowIdx) =>
        {
            string text = string.Join(" ", r.Select(i => words[i]));
            var m = Measure(text, font, FontWeights.Bold, overrideStyle, uppercase, 100);
            double widthFactor = pyramid ? 0.45 + 0.55 * ((rowIdx + 1.0) / rows.Count) : 1.0;
            double fs = Math.Min(100 * (boxW * widthFactor) / m.Width, 100 * (boxH * 0.34) / m.Height);
            return (Indices: r, Fs: fs, H: m.Height * fs / 100.0);
        }).ToList();

        double totalH = rowItems.Sum(x => x.H) * 1.05;
        double scale = Math.Min(1.0, boxH / totalH);
        totalH *= scale;

        double y = offsetY + (boxH - totalH) / 2.0;
        var list = new List<LayoutUnit>();

        for (int i = 0; i < rowItems.Count; i++)
        {
            double fs = rowItems[i].Fs * scale;
            double hh = rowItems[i].H * scale;
            double cy = y + hh / 2.0;
            y += hh * 1.05;

            double cx = offsetX + boxW / 2.0;
            double ang = tilt ? (i % 2 == 0 ? -3.2 : 3.2) : 0;
            double side = i % 2 == 0 ? -1 : 1;
            list.Add(new(rowItems[i].Indices, null, font, FontWeights.Bold, overrideStyle, uppercase, fs, cx, cy, ang, 1, i == rowItems.Count - 1, i % 2 == 1 && !tilt, false, cx + side * boxW * 0.45, cy, ang * 2, 1));
        }
        return list;
    }

    private static List<LayoutUnit> DeclutterAndFit(List<LayoutUnit> units, List<string> words, double W, double H)
    {
        double marginX = W * 0.05;
        double marginY = H * 0.06;

        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (u.DecorativeText is not null) continue;

            string text = string.Join(" ", u.WordIndices.Select(idx => words[idx]));
            for (int iter = 0; iter < 10; iter++)
            {
                var m = Measure(text, u.Font, u.Weight, u.Style, u.Uppercase, u.FontSize);
                double rad = Math.Abs(u.AngleDeg) * Math.PI / 180.0;
                double bw = m.Width * Math.Cos(rad) + m.Height * Math.Sin(rad);
                double bh = m.Width * Math.Sin(rad) + m.Height * Math.Cos(rad);

                if (bw > W - 2 * marginX || bh > H - 2 * marginY)
                {
                    u = u with { FontSize = u.FontSize * 0.88 };
                    continue;
                }

                double x = Math.Clamp(u.X, marginX + bw / 2.0, W - marginX - bw / 2.0);
                double y = Math.Clamp(u.Y, marginY + bh / 2.0, H - marginY - bh / 2.0);
                u = u with { X = x, Y = y };
                break;
            }
            units[i] = u;
        }
        return units;
    }

    private static List<int[]> ChunkIndices(int count, int maxPerRow)
    {
        var rows = new List<int[]>();
        for (int i = 0; i < count; i += maxPerRow)
            rows.Add(Enumerable.Range(i, Math.Min(maxPerRow, count - i)).ToArray());
        return rows;
    }

    public static Size Measure(string text, FontFamily font, FontWeight weight, FontStyle style, bool uppercase, double fontSize)
    {
        if (uppercase) text = text.ToUpperInvariant();
        var ft = new FormattedText(
            string.IsNullOrEmpty(text) ? " " : text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(font, style, weight, FontStretches.Normal),
            Math.Max(1, fontSize),
            Brushes.White,
            1.0);
        return new Size(Math.Max(1, ft.WidthIncludingTrailingWhitespace), Math.Max(1, ft.Height));
    }

    private static T Pick<T>(Random rng, params T[] items) => items[rng.Next(items.Length)];
}
