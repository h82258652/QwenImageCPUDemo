using System.Globalization;
using System.Text;

namespace ImageCpuDemo;

internal sealed class UsageException(string message) : Exception(message);

internal sealed record Options
{
    internal const int MaxDimension = 2048;
    internal static int DefaultThreads => Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
    internal string Prompt { get; init; } = "";
    internal int Width { get; init; } = 512;
    internal int Height { get; init; } = 512;
    internal int Threads { get; init; } = DefaultThreads;
    internal long Seed { get; init; } = 42;
    internal string? Output { get; init; }
    internal string? ModelsDirectory { get; init; }
    internal bool DryRun { get; init; }
    internal bool Help { get; init; }

    internal const string HelpText = """
        CPU Image Demo - local text-to-image using Qwen-Image-2.1-Turbo

        ImageCpuDemo.exe --prompt "a red panda in a forest" --width 512 --height 512
        ImageCpuDemo.exe --prompt-file prompt.txt -W 512 -H 512 -o image.png
        ImageCpuDemo.exe                         Interactive prompt, width and height

        --prompt, -p TEXT        Prompt; Unicode, quotes and spaces are preserved.
        --prompt-file FILE      Read a UTF-8 prompt file instead of --prompt.
        --width, -W N           Width: 32..2048, divisible by 32 (default: 512).
        --height, -H N          Height: 32..2048, divisible by 32 (default: 512).
        --output, -o FILE       PNG destination; existing files are never overwritten.
        --seed, -s N            Nonnegative 64-bit seed (default: 42).
        --threads, -t N         CPU threads: 1..logical processor count (default: up to 8).
        --models-dir DIR        Model folder (default: project's models directory).
        --dry-run               Print the exact backend argument array; do not generate.
        --help, -h              Show this help.

        Fixed Turbo settings: Euler, 8 steps, CFG 1, official custom sigma schedule.
        The CPU-only backend also receives --backend cpu; no GPU or cloud API is used.
        Run build.cmd and prepare-models.cmd first. Keep bin, backend and models in
        their project layout. The executable requires the .NET 10 x64 runtime.
        Qwen Research License: research/evaluation only, not commercial use.
        """;

    internal static Options Parse(string[] args)
    {
        var options = new Options();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? promptFile = null;
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i] switch
            {
                "-p" => "--prompt", "-W" => "--width", "-H" => "--height",
                "-o" => "--output", "-s" => "--seed", "-t" => "--threads", "-h" => "--help",
                _ => args[i]
            };
            if (!seen.Add(name))
                throw new UsageException($"Repeated option: {name}");
            if (name is "--help" or "--dry-run")
            {
                options = name == "--help" ? options with { Help = true } : options with { DryRun = true };
                continue;
            }
            if (name is not ("--prompt" or "--prompt-file" or "--width" or "--height" or
                "--output" or "--seed" or "--threads" or "--models-dir"))
                throw new UsageException($"Unknown option: {name}");
            if (++i == args.Length)
                throw new UsageException($"Missing value after {name}.");
            var value = args[i];
            if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
                throw new UsageException($"The value for {name} must not be empty or contain NUL.");
            switch (name)
            {
                case "--prompt": options = options with { Prompt = value }; break;
                case "--prompt-file": promptFile = value; break;
                case "--width": options = options with { Width = ParseDimension(value, "width") }; break;
                case "--height": options = options with { Height = ParseDimension(value, "height") }; break;
                case "--threads":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var threads) ||
                        threads < 1 || threads > Environment.ProcessorCount)
                        throw new UsageException($"Threads must be between 1 and {Environment.ProcessorCount}.");
                    options = options with { Threads = threads };
                    break;
                case "--seed":
                    if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
                        throw new UsageException("Seed must be an integer from 0 to 9223372036854775807.");
                    options = options with { Seed = seed };
                    break;
                case "--output": options = options with { Output = value }; break;
                case "--models-dir": options = options with { ModelsDirectory = value }; break;
            }
        }
        if (options.Help)
            return options;
        if (promptFile is not null)
        {
            if (seen.Contains("--prompt"))
                throw new UsageException("Use either --prompt or --prompt-file, not both.");
            options = options with { Prompt = File.ReadAllText(promptFile, new UTF8Encoding(false, true)) };
        }
        ValidatePrompt(options.Prompt);
        if (options.Output is not null && !Path.GetExtension(options.Output).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new UsageException("Output must have a .png extension.");
        return options;
    }

    internal static Options ReadInteractive(TextReader input, TextWriter output)
    {
        output.Write("Prompt: ");
        var prompt = input.ReadLine() ?? throw new UsageException("Input ended before a prompt was supplied.");
        ValidatePrompt(prompt);
        output.Write("Width [512; multiple of 32]: ");
        var width = input.ReadLine() ?? throw new UsageException("Input ended before width was supplied.");
        output.Write("Height [512; multiple of 32]: ");
        var height = input.ReadLine() ?? throw new UsageException("Input ended before height was supplied.");
        return new Options
        {
            Prompt = prompt,
            Width = ParseDimension(string.IsNullOrWhiteSpace(width) ? "512" : width.Trim(), "width"),
            Height = ParseDimension(string.IsNullOrWhiteSpace(height) ? "512" : height.Trim(), "height")
        };
    }

    internal static int ParseDimension(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ||
            result < 32 || result > MaxDimension || result % 32 != 0)
            throw new UsageException($"{name} must be between 32 and {MaxDimension} and divisible by 32.");
        return result;
    }

    private static void ValidatePrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Contains('\0'))
            throw new UsageException("Supply a nonempty prompt without NUL using --prompt or --prompt-file.");
    }
}
