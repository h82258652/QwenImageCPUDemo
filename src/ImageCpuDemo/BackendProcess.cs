using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ImageCpuDemo;

internal sealed record GpuDevice(string Name, string Description)
{
    public override string ToString() => $"{Description} [{Name}]";
}

internal static class BackendProcess
{
    // model_index.json at Qwen/Qwen-Image-2.1-Turbo revision d65dbc9, plus the terminal zero.
    internal const string TurboSigmas = "1.0,0.978453,0.95418,0.926626,0.89508,0.845148,0.704534,0.414568,0.0";

    internal static string[] BuildArguments(Options options, Assets assets, string modelsDirectory,
        string promptFile, string outputFile, string? preparedImageFile = null)
    {
        options.Validate();
        List<string> arguments =
        [
        "--backend", options.Device == ComputeDevice.Cpu ? "cpu" : $"all=cpu,diffusion={options.GpuDevice}",
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
        if (options.InitImage is not null)
        {
            if (preparedImageFile is null)
                throw new InvalidOperationException("Image-to-image requires a prepared input image path.");
            arguments.AddRange(["--init-img", preparedImageFile, "--strength", options.Strength.ToString(CultureInfo.InvariantCulture)]);
        }
        return arguments.ToArray();
    }

    internal static bool IsGpuDeviceName(string? name) =>
        name is { Length: > 6 } && name.StartsWith("Vulkan", StringComparison.OrdinalIgnoreCase) &&
        name.AsSpan(6).IndexOfAnyExceptInRange('0', '9') < 0;

    internal static GpuDevice[] ParseGpuDevices(string output)
    {
        var devices = new List<GpuDevice>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('\t');
            if (separator < 1)
                continue;
            var name = line[..separator];
            var description = line[(separator + 1)..].Trim();
            if (IsGpuDeviceName(name) && description.Length > 0 && names.Add(name))
                devices.Add(new GpuDevice(name, description));
        }
        return devices.ToArray();
    }

    internal static async Task<GpuDevice[]> GetGpuDevicesAsync(string backend, CancellationToken cancellationToken)
    {
        if (!File.Exists(backend))
            throw new FileNotFoundException($"Vulkan GPU backend missing: {backend}. Run build.cmd without -CpuOnly.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        int code;
        try
        {
            code = await RunAsync(CreateStartInfo(backend, ["--list-devices"]), output, error, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Vulkan device detection timed out. Check the graphics driver; no CPU fallback was used.");
        }
        if (code != 0)
            throw new InvalidOperationException($"Vulkan device detection failed (exit {code}). Check the graphics driver.\n{error}{output}");
        return ParseGpuDevices(output.ToString());
    }

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
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
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
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static async Task PumpAsync(StreamReader input, TextWriter output)
    {
        var buffer = new char[2048];
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count)).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }
    }
}
