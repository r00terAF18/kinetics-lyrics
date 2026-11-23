using KineticLyrics;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KineticLyrics");
Directory.CreateDirectory(dataDir);
using var library = new MusicLibrary(Path.Combine(dataDir, "library.db"));

Console.WriteLine("Kinetic Lyrics CLI — Enter music folder path to index, or press Enter to monitor:");
while (true)
{
    Console.Write("Folder: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) break;
    var folder = input.Trim().Trim('"');
    if (Directory.Exists(folder)) await Task.Run(() => library.Scan(folder));
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var monitor = new MediaMonitor(library);
_ = monitor.RunAsync(cts.Token);

int lastIdx = -2;
while (!cts.IsCancellationRequested)
{
    var snap = monitor.GetSnapshot();
    if (snap.Lyrics is not null && snap.IsPlaying)
    {
        int idx = -1;
        for (int i = 0; i < snap.Lyrics.Lines.Count && snap.Lyrics.Lines[i].Time <= snap.InterpolatedPosition; i++)
            idx = i;
        if (idx != lastIdx && idx >= 0)
        {
            lastIdx = idx;
            Console.WriteLine($"[{TimeSpan.FromSeconds(snap.InterpolatedPosition):mm\\:ss}] {snap.Lyrics.Lines[idx].Text}");
        }
    }
    await Task.Delay(100, cts.Token);
}
