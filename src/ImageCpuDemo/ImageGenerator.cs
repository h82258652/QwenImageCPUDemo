using System.Diagnostics;
using System.Text;

namespace ImageCpuDemo;

internal sealed class InferenceException(int exitCode) : Exception(
    $"Backend exited with {exitCode} (0x{unchecked((uint)exitCode):X8}). No output published. " +
    "See the backend log; check RAM/VRAM headroom, the graphics driver and model hashes.")
{
    internal int ExitCode { get; } = exitCode;
}

internal sealed record GenerationPlan(Options Options, string Backend, string ModelsDirectory,
    string Destination, string WorkDirectory, string PromptFile, string GeneratedFile,
    string? PreparedImageFile, string[] Arguments);

internal sealed record GenerationResult(string Path, TimeSpan Elapsed);

internal sealed class ImageGenerator(string root, Assets assets)
{
    internal string Root { get; } = Path.GetFullPath(root);
    internal string DefaultModelsDirectory => Path.Combine(Root, "models");

    internal static ImageGenerator FromPublishedApp()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        return new ImageGenerator(root, Assets.Load(Path.Combine(AppContext.BaseDirectory, "assets.lock.json")));
    }

    internal Task<GpuDevice[]> GetGpuDevicesAsync(CancellationToken cancellationToken) =>
        BackendProcess.GetGpuDevicesAsync(assets.BackendPath(Root, ComputeDevice.Gpu), cancellationToken);

    internal GenerationPlan CreatePlan(Options options)
    {
        options.Validate();
        var models = Path.GetFullPath(options.ModelsDirectory ?? DefaultModelsDirectory);
        var destination = Path.GetFullPath(options.Output ??
            Path.Combine(Root, "output", $"image-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{options.Seed}-{Guid.NewGuid():N}.png"));
        CheckDestination(destination);
        RequireAsciiPath(Root);
        RequireAsciiPath(models);
        var work = Path.Combine(Root, ".work", Guid.NewGuid().ToString("N"));
        var prompt = Path.Combine(work, "prompt.txt");
        var generated = Path.Combine(work, "result.png");
        var input = options.InitImage is null ? null : Path.Combine(work, "input.png");
        return new GenerationPlan(options, assets.BackendPath(Root, options.Device), models, destination, work,
            prompt, generated, input, BackendProcess.BuildArguments(options, assets, models, prompt, generated, input));
    }

    internal async Task<GenerationResult> GenerateAsync(GenerationPlan plan, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.Options.DryRun || plan.Options.Help || plan.Options.Interactive)
            throw new InvalidOperationException("An inspection/help request cannot be executed as image generation.");
        CheckDestination(plan.Destination);
        if (!File.Exists(plan.Backend))
            throw new FileNotFoundException($"Selected backend missing: {plan.Backend}. Run build.cmd.");
        assets.CheckModelFiles(plan.ModelsDirectory);
        if (plan.Options.Device == ComputeDevice.Gpu)
        {
            var devices = await BackendProcess.GetGpuDevicesAsync(plan.Backend, cancellationToken).ConfigureAwait(false);
            if (!devices.Any(device => device.Name.Equals(plan.Options.GpuDevice, StringComparison.OrdinalIgnoreCase)))
                throw new UsageException($"GPU {plan.Options.GpuDevice} is unavailable. Check the Vulkan graphics driver and detected devices, or explicitly select CPU. No CPU fallback was used.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(plan.WorkDirectory);
        try
        {
            File.WriteAllText(plan.PromptFile, plan.Options.Prompt, new UTF8Encoding(false, true));
            if (plan.Options.InitImage is not null)
            {
                InputImage.Prepare(plan.Options.InitImage, plan.PreparedImageFile!, plan.Options.Width, plan.Options.Height);
                output.WriteLine($"Image-to-image: {plan.Options.InitImage}; strength {plan.Options.Strength:G}; fitted with white padding.");
            }
            else
            {
                output.WriteLine("Text-to-image.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            output.WriteLine(plan.Options.Device == ComputeDevice.Cpu
                ? "CPU only: separate CPU runtime; compute and parameters on CPU."
                : $"GPU diffusion: {plan.Options.GpuDevice}; text encoding and VAE on CPU; parameters in RAM. No silent CPU fallback.");
            output.WriteLine("Qwen-Image-2.1-Turbo (AD-Q4_K), Qwen3-VL-8B (Q4_K_M), Qwen 2.1 VAE.");
            output.WriteLine($"{plan.Options.Width}x{plan.Options.Height}; {plan.Options.Threads} threads; seed {plan.Options.Seed}; Euler / 8-step Turbo schedule / CFG 1.");
            output.WriteLine("Qwen Research License: research/evaluation only. PNG metadata includes your prompt.");
            var timer = Stopwatch.StartNew();
            var code = await BackendProcess.RunAsync(BackendProcess.CreateStartInfo(plan.Backend, plan.Arguments),
                output, error, cancellationToken).ConfigureAwait(false);
            if (code != 0)
                throw new InferenceException(code);
            cancellationToken.ThrowIfCancellationRequested();
            PngValidator.Validate(plan.GeneratedFile, plan.Options.Width, plan.Options.Height);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(plan.Destination)!);
            File.Move(plan.GeneratedFile, plan.Destination, overwrite: false);
            return new GenerationResult(plan.Destination, timer.Elapsed);
        }
        finally
        {
            try
            {
                Directory.Delete(plan.WorkDirectory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"WARNING: could not clean this run's temporary directory '{plan.WorkDirectory}': {exception.Message}");
            }
        }
    }

    private static void CheckDestination(string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new UsageException($"Output already exists; choose a new path: {destination}");
    }

    private static void RequireAsciiPath(string path)
    {
        if (path.Any(character => character > 127))
            throw new UsageException("The pinned native backend requires ASCII project/model paths on Windows. Prompt files, input images and final PNG paths may contain Unicode.");
    }
}
