using System.CommandLine;

namespace FileMergeAssistant;

static class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("File Merge Assistant - Deduplicate and organize files");

        var scanCommand = new Command("scan", "Scan directories for duplicate files");
        var pathArgument = new Argument<DirectoryInfo>("path", "Directory to scan");
        var recursiveOption = new Option<bool>("--recursive", () => true, "Scan subdirectories recursively");
        var algorithmOption = new Option<string>("--algorithm", () => "sha256", "Hash algorithm: sha256, xxhash, perceptual");
        scanCommand.AddArgument(pathArgument);
        scanCommand.AddOption(recursiveOption);
        scanCommand.AddOption(algorithmOption);

        scanCommand.SetHandler((path, recursive, algorithm) =>
        {
            Console.WriteLine($"Scanning: {path.FullName}");
            Console.WriteLine($"Recursive: {recursive}, Algorithm: {algorithm}");
            // TODO: Implement scan logic
        }, pathArgument, recursiveOption, algorithmOption);

        rootCommand.AddCommand(scanCommand);

        return await rootCommand.InvokeAsync(args);
    }
}
