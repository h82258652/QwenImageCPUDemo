using System.Globalization;
using System.Text;

namespace ImageCpuDemo;

internal sealed class UsageException(string message) : Exception(message);

internal enum ComputeDevice { Cpu, Gpu }

internal sealed record Options
{
    internal const int MaxDimension = 2048;
    internal const double MinimumStrength = 0.01;
    internal static int DefaultThreads => Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
    internal string Prompt { get; init; } = "";
    internal int Width { get; init; } = 512;
    internal int Height { get; init; } = 512;
    internal int Threads { get; init; } = DefaultThreads;
    internal long Seed { get; init; } = 42;
    internal string? Output { get; init; }
    internal string? ModelsDirectory { get; init; }
    internal string? InitImage { get; init; }
    internal double Strength { get; init; } = 0.75;
    internal ComputeDevice Device { get; init; } = ComputeDevice.Cpu;
    internal string GpuDevice { get; init; } = "Vulkan0";
    internal bool DryRun { get; init; }
    internal bool Help { get; init; }
    internal bool Interactive { get; init; }

    internal const string HelpText = """
        Qwen-Image-2.1-Turbo - local WinForms text-to-image and image-to-image

        ImageCpuDemo.exe                         Open the WinForms interface
        dotnet ImageCpuDemo.dll --prompt "a red panda in a forest" -W 512 -H 512
        dotnet ImageCpuDemo.dll --prompt-file prompt.txt --device gpu -o image.png
        dotnet ImageCpuDemo.dll -p "watercolor cat" --init-image cat.png --strength 0.5
        dotnet ImageCpuDemo.dll --interactive    Console prompt, width and height

        --prompt, -p TEXT        Prompt; Unicode, quotes and spaces are preserved.
        --prompt-file FILE      Read a UTF-8 prompt file instead of --prompt.
        --init-image FILE       Redraw a PNG/JPEG/BMP; omit for text-to-image.
        --strength N            Redraw strength: 0.01..1 (default: 0.75).
                                Requires --init-image; larger values change more.
        --device cpu|gpu        Compute selection for either mode (default: cpu).
        --gpu-device NAME       Vulkan device name (default: Vulkan0; GPU mode only).
        --width, -W N           Width: 32..2048, divisible by 32 (default: 512).
        --height, -H N          Height: 32..2048, divisible by 32 (default: 512).
        --output, -o FILE       PNG destination; existing files are never overwritten.
        --seed, -s N            Nonnegative 64-bit seed (default: 42).
        --threads, -t N         CPU threads: 1..logical processor count (default: up to 8).
        --models-dir DIR        Model folder (default: project's models directory).
        --dry-run               Print the exact backend argument array; do not generate.
        --help, -h              Show this help.

        Fixed Turbo settings: Euler, 8 steps, CFG 1, official custom sigma schedule.
        Image-to-image uses a strength-dependent suffix of that 8-step schedule.
        Input images are fitted to the output size with white padding, not stretched.
        GPU mode uses Vulkan for diffusion, CPU for text encoding/VAE, and RAM for
        weights. GPU memory can still be insufficient; there is no silent CPU fallback.
        The UI lists detected GPUs. CPU mode uses the separate CPU-only backend.
        Run build.cmd and prepare-models.cmd first. Keep bin, backend and models in
        their project layout. Requires the .NET 10 x64 Windows Desktop runtime.
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
                "-p" => "--prompt",
                "-W" => "--width",
                "-H" => "--height",
                "-o" => "--output",
                "-s" => "--seed",
                "-t" => "--threads",
                "-h" => "--help",
                _ => args[i]
            };
            if (!seen.Add(name))
                throw new UsageException($"Repeated option: {name}");
            if (name is "--help" or "--dry-run" or "--interactive")
            {
                options = name switch
                {
                    "--help" => options with { Help = true },
                    "--dry-run" => options with { DryRun = true },
                    _ => options with { Interactive = true }
                };
                continue;
            }
            if (name is not ("--prompt" or "--prompt-file" or "--width" or "--height" or
                "--output" or "--seed" or "--threads" or "--models-dir" or "--init-image" or
                "--strength" or "--device" or "--gpu-device"))
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
                case "--init-image": options = options with { InitImage = value }; break;
                case "--strength":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var strength))
                        throw new UsageException("Strength must be a number from 0.01 to 1, using a decimal point.");
                    options = options with { Strength = strength };
                    break;
                case "--device":
                    options = options with
                    {
                        Device = value.ToLowerInvariant() switch
                        {
                            "cpu" => ComputeDevice.Cpu,
                            "gpu" => ComputeDevice.Gpu,
                            _ => throw new UsageException("Device must be cpu or gpu.")
                        }
                    };
                    break;
                case "--gpu-device": options = options with { GpuDevice = value }; break;
            }
        }
        if (options.Help)
            return options;
        if (options.Interactive)
        {
            if (seen.Count != 1)
                throw new UsageException("Use --interactive without other options.");
            return options;
        }
        if (promptFile is not null)
        {
            if (seen.Contains("--prompt"))
                throw new UsageException("Use either --prompt or --prompt-file, not both.");
            options = options with { Prompt = File.ReadAllText(promptFile, new UTF8Encoding(false, true)) };
        }
        if (seen.Contains("--strength") && options.InitImage is null)
            throw new UsageException("--strength requires --init-image.");
        if (seen.Contains("--gpu-device") && options.Device != ComputeDevice.Gpu)
            throw new UsageException("--gpu-device requires --device gpu.");
        options.Validate();
        return options;
    }

    internal void Validate()
    {
        ValidatePrompt(Prompt);
        ParseDimension(Width.ToString(CultureInfo.InvariantCulture), "width");
        ParseDimension(Height.ToString(CultureInfo.InvariantCulture), "height");
        if (Threads < 1 || Threads > Environment.ProcessorCount)
            throw new UsageException($"Threads must be between 1 and {Environment.ProcessorCount}.");
        if (Seed < 0)
            throw new UsageException("Seed must be an integer from 0 to 9223372036854775807.");
        if (!double.IsFinite(Strength) || Strength < MinimumStrength || Strength > 1)
            throw new UsageException("Strength must be between 0.01 and 1.");
        if (!Enum.IsDefined(Device))
            throw new UsageException("Device must be cpu or gpu.");
        if (!BackendProcess.IsGpuDeviceName(GpuDevice))
            throw new UsageException("GPU device must be a Vulkan device name such as Vulkan0.");
        foreach (var path in new[] { Output, ModelsDirectory, InitImage })
        {
            if (path is not null && (string.IsNullOrWhiteSpace(path) || path.Contains('\0')))
                throw new UsageException("File and directory paths must not be empty or contain NUL.");
        }
        if (Output is not null && !Path.GetExtension(Output).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new UsageException("Output must have a .png extension.");
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
