using KineticLyrics;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KineticLyrics");
Directory.CreateDirectory(dataDir);
using var library = new MusicLibrary(Path.Combine(dataDir, "library.db"));
Console.WriteLine("Kinetic Lyrics prototype — Windows media sessions + local lyrics");
Console.WriteLine($"Database: {dataDir}");
Console.WriteLine("Enter music folder paths to index, one per line. Leave blank to start watching.");
Console.WriteLine("Previously indexed files are retained. Ctrl+C exits.");
while (true)
{
    Console.Write("Folder: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) break;
    var folder = input.Trim().Trim('"');
    if (!Directory.Exists(folder)) { Console.WriteLine("Folder does not exist."); continue; }
    await Task.Run(() => library.Scan(folder));
}
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
try { await MediaMonitor.RunAsync(library, cancel.Token); }
catch (OperationCanceledException) { }
