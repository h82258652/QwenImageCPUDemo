using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ImageCpuDemo;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var options = args.Length == 0 ? Options.ReadInteractive(Console.In, Console.Out) : Options.Parse(args);
            if (options.Help)
            {
                Console.WriteLine(Options.HelpText);
                return 0;
            }
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            var assets = Assets.Load(Path.Combine(AppContext.BaseDirectory, "assets.lock.json"));
            var backend = Path.Combine(root, "backend", assets.Backend.Directory, "sd-cli.exe");
            var models = Path.GetFullPath(options.ModelsDirectory ?? Path.Combine(root, "models"));
            var destination = Path.GetFullPath(options.Output ??
                Path.Combine(root, "output", $"image-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{options.Seed}.png"));
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new UsageException($"Output already exists; choose a new path: {destination}");
            // The pinned native CLI uses narrow Windows paths. Keep its temporary paths ASCII;
            // .NET writes the UTF-8 prompt and moves the PNG to the user's Unicode destination.
            RequireAsciiPath(root);
            RequireAsciiPath(models);
            var work = Path.Combine(root, ".work", Guid.NewGuid().ToString("N"));
            var promptFile = Path.Combine(work, "prompt.txt");
            var generated = Path.Combine(work, "result.png");
            var arguments = BackendProcess.BuildArguments(options, assets, models, promptFile, generated);
            if (options.DryRun)
            {
                Console.WriteLine("DRY RUN: no model is loaded, no file is written, and no image is generated.");
                Console.WriteLine(JsonSerializer.Serialize(new { backend, arguments, prompt = options.Prompt, output = destination },
                    new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            if (!File.Exists(backend))
                throw new FileNotFoundException($"CPU backend missing: {backend}. Run build.cmd.");
            assets.CheckModelFiles(models);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.CreateDirectory(work);
            try
            {
                File.WriteAllText(promptFile, options.Prompt, new UTF8Encoding(false, true));
                Console.WriteLine("Local CPU only: Qwen-Image-2.1-Turbo (AD-Q4_K), Qwen3-VL-8B (Q4_K_M), Qwen 2.1 VAE.");
                Console.WriteLine($"{options.Width}x{options.Height}; {options.Threads} threads; seed {options.Seed}; Euler / 8 steps / CFG 1.");
                Console.WriteLine("Model loading and CPU inference can take a very long time. Ctrl+C cancels this child process.");
                Console.WriteLine("Qwen Research License: research/evaluation only. PNG metadata includes your prompt.");
                var timer = Stopwatch.StartNew();
                using var cancellation = new CancellationTokenSource();
                ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
                Console.CancelKeyPress += cancel;
                int exitCode;
                try
                {
                    exitCode = await BackendProcess.RunAsync(BackendProcess.CreateStartInfo(backend, arguments),
                        Console.Out, Console.Error, cancellation.Token);
                }
                finally
                {
                    Console.CancelKeyPress -= cancel;
                }
                if (exitCode != 0)
                {
                    Console.Error.WriteLine($"ERROR: CPU backend exited with {exitCode} (0x{unchecked((uint)exitCode):X8}). No output published. See the backend error above; check RAM/commit headroom and model hashes.");
                    return 4;
                }
                PngValidator.Validate(generated, options.Width, options.Height);
                File.Move(generated, destination, overwrite: false);
                Console.WriteLine($"\nSaved: {destination}");
                Console.WriteLine($"PNG: {options.Width}x{options.Height}; elapsed {timer.Elapsed:g}.");
                return 0;
            }
            finally
            {
                try
                {
                    Directory.Delete(work, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"WARNING: could not clean this run's temporary directory '{work}': {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("\nCancelled. The backend was stopped; no output was published.");
            return 130;
        }
        catch (UsageException exception)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}\nUse --help for usage.");
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException or Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return 3;
        }
    }

    private static void RequireAsciiPath(string path)
    {
        if (path.Any(character => character > 127))
            throw new UsageException("The pinned native backend requires an ASCII project/model path on Windows. Keep this project in C:\\Repos\\QwenImageCPUDemo. Prompts, prompt-file input paths and final PNG paths may contain Unicode.");
    }
}
