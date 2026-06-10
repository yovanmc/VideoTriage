using VideoTriage.Core.FileSystem;
using VideoTriage.Core.Models;
using VideoTriage.Core.Probing;
using VideoTriage.Core.Tools;

if (args.Length is 0 or > 2
    || (args.Length == 2 && !string.Equals(args[1], "--recursive", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("Usage: VideoTriage.Cli <folder> [--recursive]");
    return 2;
}

var folderPath = args[0];
var recursive = args.Any(arg => string.Equals(arg, "--recursive", StringComparison.OrdinalIgnoreCase));

if (!Directory.Exists(folderPath))
{
    Console.Error.WriteLine($"Folder does not exist: {folderPath}");
    return 2;
}

ToolLocation ffprobe;
try
{
    ffprobe = new ToolLocator().RequireOnPath("ffprobe");
}
catch (FileNotFoundException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}

var scanner = new FolderProbeScanner(
    new VideoFileDiscovery(),
    new FfprobeService(ffprobe.FullPath, new ProcessRunner(), new FfprobeJsonParser()),
    new BppClassifier());

Console.WriteLine("VideoTriage probe/classify scan");
Console.WriteLine($"Folder: {folderPath}");
Console.WriteLine($"Recursive: {recursive}");
Console.WriteLine();

var options = new TriageOptions();

var progress = new Progress<ProbeResult>(result =>
{
    if (result.Failure is not null)
    {
        Console.WriteLine($"INVALID\t{result.FilePath}\t{result.Failure.Message}");
        return;
    }

    var classification = result.Classification!;
    var stats = result.Stats!;
    Console.WriteLine(
        $"{classification.Outcome}\t{stats.BitsPerPixel:0.000}\t{stats.CodecName}\t{stats.Width}x{stats.Height}\t{result.FilePath}");
});

var summary = await scanner.ScanAsync(folderPath, options, recursive, progress);
Console.WriteLine();
Console.WriteLine($"Scanned: {summary.FilesDiscovered}");
Console.WriteLine($"Candidates: {summary.CandidateCount}");
return 0;
