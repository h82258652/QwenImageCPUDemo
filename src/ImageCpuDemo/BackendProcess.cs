using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ImageCpuDemo;

internal static class BackendProcess
{
    // model_index.json at Qwen/Qwen-Image-2.1-Turbo revision d65dbc9, plus the terminal zero.
    internal const string TurboSigmas = "1.0,0.978453,0.95418,0.926626,0.89508,0.845148,0.704534,0.414568,0.0";

    internal static string[] BuildArguments(Options options, Assets assets, string modelsDirectory,
        string promptFile, string outputFile) =>
    [
        "--backend", "cpu",
        "--params-backend", "cpu",
        "--diffusion-model", assets.ModelPath(modelsDirectory, "diffusion"),
        "--llm", assets.ModelPath(modelsDirectory, "llm"),
        "--vae", assets.ModelPath(modelsDirectory, "vae"),
        "--prompt-file", promptFile,
        "--width", options.Width.ToString(CultureInfo.InvariantCulture),
        "--height", options.Height.ToString(CultureInfo.InvariantCulture),
        "--steps", "8",
        "--cfg-scale", "1.0",
        "--sampling-method", "euler",
        "--sigmas", TurboSigmas,
        "--batch-count", "1",
        "--seed", options.Seed.ToString(CultureInfo.InvariantCulture),
        "--rng", "cpu",
        "--threads", options.Threads.ToString(CultureInfo.InvariantCulture),
        "--mmap",
        "--fa",
        "--vae-tiling",
        "--output", outputFile
    ];

    internal static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return start;
    }

    internal static async Task<int> RunAsync(ProcessStartInfo start, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException($"Could not start backend: {start.FileName}");
        var stdout = PumpAsync(process.StandardOutput, output);
        var stderr = PumpAsync(process.StandardError, error);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The child can exit between HasExited and Kill.
            }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            await Task.WhenAll(stdout, stderr);
        }
        return process.ExitCode;
    }

    private static async Task PumpAsync(StreamReader input, TextWriter output)
    {
        var buffer = new char[2048];
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory())) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count));
            await output.FlushAsync();
        }
    }
}
