using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using ImageCpuDemo;

internal static class Program
{
    private static readonly string UnicodePrompt = "\u4e00\u53ea\u732b says \"hello\"; %PATH% & | < > \uD83D\uDC31\nsecond line";
    private static int passed;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.FirstOrDefault() == "--list-devices")
        {
            Console.WriteLine("Vulkan0\tTest GPU\nCPU\tTest CPU");
            return 0;
        }
        if (args.FirstOrDefault() is "--fake-generate" or "--fake-corrupt" or "--fake-fail" or "--fake-race")
        {
            var generated = Value(args, "--output");
            var width = int.Parse(Value(args, "--width"), CultureInfo.InvariantCulture);
            var height = int.Parse(Value(args, "--height"), CultureInfo.InvariantCulture);
            Check(File.ReadAllText(Value(args, "--prompt-file"), Encoding.UTF8) == UnicodePrompt, "Fake backend reads staged Unicode prompt");
            if (args.Contains("--init-img"))
                PngValidator.Validate(Value(args, "--init-img"), width, height);
            if (args[0] == "--fake-corrupt")
                File.WriteAllText(generated, "not a PNG");
            else
                WriteTestPng(generated, width, height, 3);
            if (args[0] == "--fake-race")
                File.WriteAllText(args[1], "preserve competing output");
            if (args[0] == "--fake-fail")
            {
                Console.Error.WriteLine("Test inference failure after writing a partial result.");
                return 17;
            }
            return 0;
        }
        if (args.FirstOrDefault() == "--fake-echo")
        {
            Console.Error.Write(new string('e', 131072) + "\rprogress 8/8\n");
            Console.WriteLine(JsonSerializer.Serialize(args[1..]));
            return 17;
        }
        if (args.FirstOrDefault() == "--fake-sleep")
        {
            if (args.Contains("--output"))
                WriteTestPng(Value(args, "--output"), 64, 96, 3);
            Console.WriteLine(Environment.ProcessId);
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 0;
        }

        var root = Directory.GetCurrentDirectory();
        var app = Path.Combine(root, "bin", "ImageCpuDemo.exe");
        var scratch = Path.Combine(root, ".cache", "tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            Check(Options.Parse(["--prompt", UnicodePrompt, "-W", "320", "-H", "192"]).Prompt == UnicodePrompt, "Unicode prompt parsing");
            foreach (var value in new[] { "0", "-32", "31", "33", "513", "2049", "2147483648", "abc", "32.0" })
                Expect<UsageException>(() => Options.Parse(["-p", "cat", "-W", value]), "Reject width " + value);
            Check(Options.Parse(["-p", "cat", "-W", "32", "-H", "2048"]).Height == 2048, "Dimension boundaries");
            Expect<UsageException>(() => Options.Parse(["-p", "cat", "--prompt", "dog"]), "Reject duplicate aliases");
            Expect<UsageException>(() => Options.Parse(["-p"]), "Reject missing value");
            Expect<UsageException>(() => Options.Parse(["--width", "64"]), "Reject missing prompt");
            Expect<UsageException>(() => Options.Parse(["-p", " "]), "Reject whitespace prompt");
            Expect<UsageException>(() => Options.Parse(["-p", "cat", "--output", "out.jpg"]), "Reject non-PNG output");
            Expect<UsageException>(() => Options.Parse(["-p", "cat", "--threads", "0"]), "Reject zero threads");
            Expect<UsageException>(() => Options.Parse(["-p", "cat", "--threads", (Environment.ProcessorCount + 1).ToString()]), "Reject excessive threads");
            Expect<UsageException>(() => Options.Parse(["-p", "cat", "--seed", "-1"]), "Reject negative seed");
            Expect<UsageException>(() => Options.Parse(["-p", "cat", "--unused"]), "Reject unknown option");
            Check(Options.Parse(["--help"]).Help, "Help without prompt");
            Check(Options.Parse(["--interactive"]).Interactive, "Explicit console interactive mode");
            Expect<UsageException>(() => Options.Parse(["--interactive", "-p", "cat"]), "Reject ignored interactive arguments");
            Check(Options.Parse(["-p", "cat"]).Device == ComputeDevice.Cpu, "CPU remains the default");
            Check(Options.Parse(["-p", "cat", "--device", "GPU", "--gpu-device", "Vulkan1"]).GpuDevice == "Vulkan1",
                "Select a specific GPU");
            Expect<UsageException>(() => Options.Parse(["-p", "cat", "--device", "auto"]), "Reject implicit device fallback");
            Expect<UsageException>(() => Options.Parse(["-p", "cat", "--gpu-device", "Vulkan0"]), "Reject GPU selection in CPU mode");
            foreach (var name in new[] { "cpu", "gpu", "Vulkan", "Vulkan0,all=cpu", "Vulkan-1", "Vulkan0&Vulkan1" })
                Expect<UsageException>(() => Options.Parse(["-p", "cat", "--device", "gpu", "--gpu-device", name]), "Reject GPU name " + name);
            foreach (var value in new[] { "0", "-1", "0.009", "1.01", "NaN", "Infinity", "0,5", "abc" })
                Expect<UsageException>(() => Options.Parse(["-p", "cat", "--init-image", "image.png", "--strength", value]), "Reject strength " + value);
            Check(Options.Parse(["-p", "cat", "--init-image", "image.png", "--strength", "0.01"]).Strength == 0.01 &&
                Options.Parse(["-p", "cat", "--init-image", "image.png", "--strength", "1"]).Strength == 1, "Strength boundaries");
            Expect<UsageException>(() => Options.Parse(["-p", "cat", "--strength", "0.5"]), "Reject strength without image");
            Expect<UsageException>(() => (new Options { Prompt = "cat", Width = 33 }).Validate(), "UI and CLI share dimension validation");
            Expect<UsageException>(() => (new Options { Prompt = "cat", Strength = double.NaN }).Validate(), "UI and CLI share strength validation");

            var promptFile = Path.Combine(scratch, "\u63d0\u793a\u8bcd.txt");
            File.WriteAllText(promptFile, UnicodePrompt, new UTF8Encoding(false, true));
            Check(Options.Parse(["--prompt-file", promptFile]).Prompt == UnicodePrompt, "UTF-8 prompt file with Unicode path");
            Expect<UsageException>(() => Options.Parse(["-p", "cat", "--prompt-file", promptFile]), "Reject conflicting prompt sources");
            File.WriteAllBytes(promptFile, [0xff, 0xff]);
            Expect<DecoderFallbackException>(() => Options.Parse(["--prompt-file", promptFile]), "Reject malformed UTF-8");
            var interactive = Options.ReadInteractive(new StringReader("cat\n320\n192\n"), TextWriter.Null);
            Check(interactive.Width == 320 && interactive.Height == 192, "Interactive dimensions");
            Check(Options.ReadInteractive(new StringReader("cat\n\n\n"), TextWriter.Null).Width == 512, "Interactive defaults");
            Expect<UsageException>(() => Options.ReadInteractive(new StringReader("cat\n"), TextWriter.Null), "Interactive EOF");

            var assets = Assets.Load(Path.Combine(root, "assets.lock.json"));
            var backendArgs = BackendProcess.BuildArguments(interactive, assets, @"C:\model folder", @"C:\work\prompt.txt", @"C:\work\result.png");
            Check(Value(backendArgs, "--backend") == "cpu" && Value(backendArgs, "--params-backend") == "cpu", "Force CPU compute and parameter placement");
            Check(Value(backendArgs, "--steps") == "8" && Value(backendArgs, "--cfg-scale") == "1.0" &&
                Value(backendArgs, "--sampling-method") == "euler" &&
                Value(backendArgs, "--sigmas") == "1.0,0.978453,0.95418,0.926626,0.89508,0.845148,0.704534,0.414568,0.0",
                "Exact Turbo sampling schedule");
            Check(Value(backendArgs, "--width") == "320" && Value(backendArgs, "--height") == "192" &&
                Value(backendArgs, "--batch-count") == "1", "Exact requested dimensions and batch");
            Check(Value(backendArgs, "--diffusion-model").EndsWith("Qwen-Image-2.1-Turbo-AD-Q4_K.gguf") &&
                Value(backendArgs, "--vae").EndsWith("qwen_image_2.1_vae_bf16.safetensors") &&
                Value(backendArgs, "--llm").EndsWith("Qwen3VL-8B-Instruct-Q4_K_M.gguf"), "Correct three model components");
            Check(!backendArgs.Contains("--init-img") && !backendArgs.Contains("--strength"), "Text mode has no image initialization");
            Check(assets.Backend.Revision == assets.GpuBackend.Revision &&
                assets.BackendPath(root, ComputeDevice.Cpu) != assets.BackendPath(root, ComputeDevice.Gpu), "Separate CPU/GPU runtimes at the same revision");
            var redraw = interactive with { InitImage = @"C:\original image.png", Strength = 0.5 };
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                foreach (var compute in new[] { ComputeDevice.Cpu, ComputeDevice.Gpu })
                {
                    var arguments = BackendProcess.BuildArguments(redraw with { Device = compute, GpuDevice = "Vulkan1" },
                        assets, @"C:\models", @"C:\work\prompt.txt", @"C:\work\result.png", @"C:\work\input.png");
                    Check(Value(arguments, "--init-img") == @"C:\work\input.png" && Value(arguments, "--strength") == "0.5",
                        $"Image initialization and invariant strength on {compute}");
                    Check(Value(arguments, "--backend") == (compute == ComputeDevice.Cpu ? "cpu" : "all=cpu,diffusion=Vulkan1") &&
                        Value(arguments, "--params-backend") == "cpu", $"Explicit compute and weight placement on {compute}");
                    Check(Value(arguments, "--sigmas") == BackendProcess.TurboSigmas, $"Keep the Turbo schedule on {compute}");
                }
            }
            finally { CultureInfo.CurrentCulture = culture; }
            Expect<InvalidOperationException>(() => BackendProcess.BuildArguments(redraw, assets, @"C:\models", "prompt.txt", "out.png"),
                "Image mode requires a staged image path");
            var devices = BackendProcess.ParseGpuDevices("log message\nCPU\tCPU\nVulkan0\tFirst GPU\r\nvulkan1\tSecond GPU\nVulkan0\tDuplicate\nVulkan\tInvalid\nVulkan2,\tInvalid");
            Check(devices.Select(item => item.Name).SequenceEqual(["Vulkan0", "vulkan1"]), "GPU discovery parses only actual Vulkan devices");
            Check(BackendProcess.ParseGpuDevices("CPU\tCPU\nno graphics device").Length == 0, "CPU is never advertised as a GPU");
            using (var pe = new PEReader(File.OpenRead(app)))
                Check(pe.PEHeaders.PEHeader?.Subsystem == Subsystem.WindowsGui, "Published executable is a Windows GUI application");

            var help = await RunApp(app, ["--help"], scratch);
            Check(help.Code == 0 && help.Out.Contains("Qwen-Image-2.1-Turbo"), "Published EXE help from another working directory");
            var invalid = await RunApp(app, ["-p", "cat", "--width", "513"], scratch);
            Check(invalid.Code == 2 && invalid.Error.Contains("divisible by 32"), "Published EXE invalid-input exit");
            var missing = await RunApp(app, ["-p", "cat", "--models-dir", Path.Combine(scratch, "missing")], scratch);
            Check(missing.Code == 3 && missing.Error.Contains("Model missing"), "Missing models fail, never fake an image");
            var piped = await RunApp(app, ["--interactive"], scratch, "cat\n65\n96\n");
            Check(piped.Code == 2 && piped.Error.Contains("width"), "Published EXE interactive input validation");
            var existing = Path.Combine(scratch, "existing.png");
            File.WriteAllText(existing, "do not overwrite");
            var conflict = await RunApp(app, ["-p", "cat", "-o", existing], scratch);
            Check(conflict.Code == 2 && File.ReadAllText(existing) == "do not overwrite", "Preserve existing output");
            var finalPath = Path.Combine(scratch, "\u6d4b\u8bd5 output.png");
            var dry = await RunApp(app, ["-p", UnicodePrompt, "-W", "64", "-H", "96", "-o", finalPath, "--dry-run"], scratch);
            using (var json = JsonDocument.Parse(dry.Out[dry.Out.IndexOf('{')..]))
            {
                var planned = json.RootElement.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()!).ToArray();
                Check(dry.Code == 0 && json.RootElement.GetProperty("prompt").GetString() == UnicodePrompt &&
                    json.RootElement.GetProperty("output").GetString() == finalPath &&
                    Value(planned, "--width") == "64" && Value(planned, "--height") == "96" &&
                    Value(planned, "--sigmas") == BackendProcess.TurboSigmas &&
                    !File.Exists(Value(planned, "--prompt-file")) && !File.Exists(finalPath),
                    "Published EXE dry-run argument array and zero output side effects");
            }
            var gpuDry = await RunApp(app, ["-p", UnicodePrompt, "--device", "gpu", "--gpu-device", "Vulkan1",
                "--init-image", promptFile, "--strength", "0.5", "--dry-run"], scratch);
            using (var json = JsonDocument.Parse(gpuDry.Out[gpuDry.Out.IndexOf('{')..]))
            {
                var planned = json.RootElement.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()!).ToArray();
                Check(gpuDry.Code == 0 && json.RootElement.GetProperty("backend").GetString() == assets.BackendPath(root, ComputeDevice.Gpu) &&
                    json.RootElement.GetProperty("input").GetString() == promptFile &&
                    Value(planned, "--backend") == "all=cpu,diffusion=Vulkan1" && Value(planned, "--strength") == "0.5" &&
                    !File.Exists(Value(planned, "--init-img")), "Published GPU img2img dry-run stages nothing and selects the correct runtime");
            }

            var fake = Path.Combine(AppContext.BaseDirectory, "ImageCpuDemo.Tests.exe");
            string[] forwarded = [UnicodePrompt, @"C:\folder with spaces\trailing\", "--literal", "\"quoted\""];
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var code = await BackendProcess.RunAsync(BackendProcess.CreateStartInfo(fake, ["--fake-echo", .. forwarded]),
                stdout, stderr, CancellationToken.None);
            Check(code == 17 && JsonSerializer.Deserialize<string[]>(stdout.ToString())!.SequenceEqual(forwarded) &&
                stderr.ToString().EndsWith("\rprogress 8/8\n"), "Native argument escaping, concurrent output draining and error exit");
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                var timer = Stopwatch.StartNew();
                stdout = new StringWriter();
                try
                {
                    await BackendProcess.RunAsync(BackendProcess.CreateStartInfo(fake, ["--fake-sleep"]),
                        stdout, TextWriter.Null, cancellation.Token);
                    throw new InvalidOperationException("Cancellation did not stop the child.");
                }
                catch (OperationCanceledException)
                {
                    Check(timer.Elapsed < TimeSpan.FromSeconds(10), "Cancellation stops the owned child promptly");
                    if (int.TryParse(stdout.ToString().Trim(), out var pid))
                        Expect<ArgumentException>(() => Process.GetProcessById(pid), "Cancelled child no longer running");
                }
            }

            var sourceImage = Path.Combine(scratch, "\u539f\u56fe portrait.png");
            WriteTestPng(sourceImage, 32, 64, 3);
            var originalBytes = File.ReadAllBytes(sourceImage);
            var stagedImage = Path.Combine(scratch, "staged.png");
            InputImage.Prepare(sourceImage, stagedImage, 64, 64);
            PngValidator.Validate(stagedImage, 64, 64);
            using (var bitmap = InputImage.Load(stagedImage))
            {
                Check(bitmap.GetPixel(0, 32).ToArgb() == Color.White.ToArgb() &&
                    bitmap.GetPixel(32, 32).ToArgb() == Color.Black.ToArgb(), "Fit input without stretching or cropping, with white padding");
            }
            Check(File.ReadAllBytes(sourceImage).SequenceEqual(originalBytes), "Unicode source image is never modified");
            using (var preview = InputImage.Load(sourceImage))
            using (var exclusive = new FileStream(sourceImage, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(preview.Width == 32 && exclusive.Length > 0, "Preview does not lock its source file");
            foreach (var format in new[] { ImageFormat.Jpeg, ImageFormat.Bmp })
            {
                var path = Path.Combine(scratch, "input." + format.ToString().ToLowerInvariant());
                using (var bitmap = new Bitmap(40, 24))
                    bitmap.Save(path, format);
                using var decoded = InputImage.Load(path);
                Check(decoded.Width == 40 && decoded.Height == 24, "Decode " + format);
            }
            var transparent = Path.Combine(scratch, "transparent.png");
            WriteTestPng(transparent, 32, 32, 4);
            var flattened = Path.Combine(scratch, "flattened.png");
            InputImage.Prepare(transparent, flattened, 32, 32);
            using (var bitmap = InputImage.Load(flattened))
                Check(bitmap.GetPixel(16, 16).ToArgb() == Color.White.ToArgb(), "Transparent input is composited on white");
            var corruptImage = Path.Combine(scratch, "corrupt.png");
            File.WriteAllText(corruptImage, "not an image");
            Expect<InvalidDataException>(() => InputImage.Load(corruptImage), "Reject corrupt source images");

            var serviceRoot = Path.Combine(scratch, "service");
            var modelFolder = Path.Combine(serviceRoot, "models");
            Directory.CreateDirectory(modelFolder);
            var smallAssets = assets with { Models = assets.Models.Select(model => model with { Size = 3 }).ToArray() };
            foreach (var model in smallAssets.Models)
                File.WriteAllBytes(Path.Combine(modelFolder, model.File), [1, 2, 3]);
            var generator = new ImageGenerator(serviceRoot, smallAssets);
            GenerationPlan Plan(string name, string fakeMode, ComputeDevice compute = ComputeDevice.Cpu, string? image = null)
            {
                var plan = generator.CreatePlan(new Options
                {
                    Prompt = UnicodePrompt,
                    Width = 64,
                    Height = 96,
                    Device = compute,
                    InitImage = image,
                    Output = Path.Combine(scratch, name + ".png")
                });
                return plan with { Backend = fake, Arguments = [fakeMode, .. plan.Arguments] };
            }
            foreach (var compute in new[] { ComputeDevice.Cpu, ComputeDevice.Gpu })
                foreach (var image in new string?[] { null, sourceImage })
                {
                    var plan = Plan($"success-{compute}-{(image is null ? "text" : "image")}", "--fake-generate", compute, image);
                    var result = await generator.GenerateAsync(plan, TextWriter.Null, TextWriter.Null, CancellationToken.None);
                    PngValidator.Validate(result.Path, 64, 96);
                    Check(result.Path == plan.Destination && !Directory.Exists(plan.WorkDirectory), $"Publish and clean {compute} {(image is null ? "text" : "image")} generation");
                }
            var corruptPlan = Plan("bad-output", "--fake-corrupt");
            await ExpectAsync<InvalidDataException>(() => generator.GenerateAsync(corruptPlan, TextWriter.Null, TextWriter.Null, CancellationToken.None),
                "Reject native success with corrupt PNG");
            Check(!File.Exists(corruptPlan.Destination) && !Directory.Exists(corruptPlan.WorkDirectory), "Corrupt result is never published");
            var failedPlan = Plan("failed-output", "--fake-fail");
            await ExpectAsync<InferenceException>(() => generator.GenerateAsync(failedPlan, TextWriter.Null, TextWriter.Null, CancellationToken.None),
                "Native nonzero exit is an inference error");
            Check(!File.Exists(failedPlan.Destination) && !Directory.Exists(failedPlan.WorkDirectory), "Native failure cleans partial output");
            var badSourcePlan = Plan("bad-source", "--fake-generate", image: corruptImage);
            await ExpectAsync<InvalidDataException>(() => generator.GenerateAsync(badSourcePlan, TextWriter.Null, TextWriter.Null, CancellationToken.None),
                "Reject bad source before invoking inference");
            Check(!File.Exists(badSourcePlan.Destination) && !Directory.Exists(badSourcePlan.WorkDirectory), "Bad-source work is cleaned");
            var racePlan = Plan("competing-output", "--fake-race");
            racePlan = racePlan with { Arguments = ["--fake-race", racePlan.Destination, .. racePlan.Arguments[1..]] };
            await ExpectAsync<IOException>(() => generator.GenerateAsync(racePlan, TextWriter.Null, TextWriter.Null, CancellationToken.None),
                "Atomic publication rejects a destination created during inference");
            Check(File.ReadAllText(racePlan.Destination) == "preserve competing output" && !Directory.Exists(racePlan.WorkDirectory),
                "Preserve competing output and clean work");
            var unavailable = Plan("unavailable-gpu", "--fake-generate", ComputeDevice.Gpu);
            unavailable = unavailable with { Options = unavailable.Options with { GpuDevice = "Vulkan9" } };
            await ExpectAsync<UsageException>(() => generator.GenerateAsync(unavailable, TextWriter.Null, TextWriter.Null, CancellationToken.None),
                "Unavailable GPU is an error, not CPU fallback");
            Check(!Directory.Exists(unavailable.WorkDirectory) && !File.Exists(unavailable.Destination), "GPU preflight fails before writing input/output");
            var cancelPlan = Plan("cancelled-output", "--fake-sleep", image: sourceImage);
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                stdout = new StringWriter();
                await ExpectAsync<OperationCanceledException>(() => generator.GenerateAsync(cancelPlan, stdout, TextWriter.Null, cancellation.Token),
                    "Cancel generation after a child writes a result");
                Check(!File.Exists(cancelPlan.Destination) && !Directory.Exists(cancelPlan.WorkDirectory), "Cancellation publishes no partial image and cleans input");
                var pidLine = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim();
                Check(int.TryParse(pidLine, out var pid), "Cancelled inference reported its process ID");
                Expect<ArgumentException>(() => Process.GetProcessById(pid), "Cancelled inference child is gone");
            }
            CheckForm(generator, sourceImage);

            Check(PngValidator.Crc32("IEND"u8, []) == 0xae426082, "PNG standard CRC32 vector");
            foreach (var channels in new[] { 3, 4 })
            {
                var png = Path.Combine(scratch, $"fixture-{channels}.png");
                WriteTestPng(png, 32, 64, channels);
                PngValidator.Validate(png, 32, 64);
                Check(true, $"Validate RGB/RGBA pixels ({channels} channels)");
                Expect<InvalidDataException>(() => PngValidator.Validate(png, 64, 32), "Reject wrong output dimensions");
                var bytes = File.ReadAllBytes(png);
                bytes[^1] ^= 1;
                File.WriteAllBytes(png, bytes);
                Expect<InvalidDataException>(() => PngValidator.Validate(png, 32, 64), "Reject PNG corruption");
                File.WriteAllBytes(png, bytes[..^12]);
                Expect<InvalidDataException>(() => PngValidator.Validate(png, 32, 64), "Reject missing PNG end");
            }
            Console.WriteLine($"Passed {passed} checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static string Value(string[] arguments, string option) => arguments[Array.IndexOf(arguments, option) + 1];

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException("FAILED: " + name);
        passed++;
    }

    private static void Expect<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, name); return; }
        throw new InvalidOperationException("FAILED (no expected exception): " + name);
    }

    private static async Task ExpectAsync<T>(Func<Task> action, string name) where T : Exception
    {
        try { await action(); }
        catch (T) { Check(true, name); return; }
        throw new InvalidOperationException("FAILED (no expected exception): " + name);
    }

    private static void CheckForm(ImageGenerator generator, string sourceImage)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm(generator);
                Check(form.ReadOptions().Device == ComputeDevice.Cpu && form.ReadOptions().InitImage is null,
                    "WinForms defaults to CPU text-to-image");
                T Control<T>(string name) where T : Control => (T)form.Controls.Find(name, searchAllChildren: true).Single();
                Check(!string.IsNullOrEmpty(Control<PictureBox>("sourcePreview").AccessibleName) &&
                    !string.IsNullOrEmpty(Control<PictureBox>("resultPreview").AccessibleName), "Both previews have accessible image labels");
                Check(!string.IsNullOrEmpty(Control<TextBox>("lastOutput").AccessibleName) &&
                    !string.IsNullOrEmpty(Control<RichTextBox>("generationLog").AccessibleName), "Output path and progress log have accessible labels");
                Control<ComboBox>("generationMode").SelectedIndex = 1;
                Expect<UsageException>(() => form.ReadOptions(), "WinForms requires a source in image mode");
                Control<TextBox>("inputImagePath").Text = sourceImage;
                Control<NumericUpDown>("strength").Value = 0.35m;
                Control<NumericUpDown>("imageWidth").Value = 320;
                Control<NumericUpDown>("imageHeight").Value = 192;
                Check(form.ReadOptions().InitImage == sourceImage && form.ReadOptions().Strength == 0.35 &&
                    form.ReadOptions().Width == 320 && form.ReadOptions().Height == 192, "WinForms forwards image and generation parameters");
                Control<ComboBox>("computeDevice").SelectedIndex = 1;
                Expect<UsageException>(() => form.ReadOptions(), "WinForms never substitutes CPU for missing GPU");
                var gpu = Control<ComboBox>("gpuDevice");
                gpu.Items.Add(new GpuDevice("Vulkan1", "Test GPU"));
                gpu.SelectedIndex = 0;
                Check(form.ReadOptions().Device == ComputeDevice.Gpu && form.ReadOptions().GpuDevice == "Vulkan1" &&
                    form.ReadOptions().InitImage == sourceImage, "WinForms image mode forwards the selected GPU");
                Control<ComboBox>("generationMode").SelectedIndex = 0;
                Check(form.ReadOptions().InitImage is null && form.ReadOptions().Device == ComputeDevice.Gpu,
                    "WinForms text mode omits stale source images and retains GPU selection");
                Control<NumericUpDown>("imageWidth").Value = 33;
                Expect<UsageException>(() => form.ReadOptions(), "WinForms rejects manually entered non-multiple dimensions");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15)))
            throw new InvalidOperationException("WinForms checks did not complete.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static async Task<(int Code, string Out, string Error)> RunApp(string app, string[] arguments,
        string directory, string? input = null)
    {
        var start = BackendProcess.CreateStartInfo(app, arguments);
        start.WorkingDirectory = directory;
        start.RedirectStandardInput = input is not null;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start app.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            await child.StandardInput.WriteAsync(input);
            child.StandardInput.Close();
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await child.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            throw;
        }
        return (child.ExitCode, await output, await error);
    }

    // Synthetic fixtures exist only in the test scratch directory, never in the demo output.
    private static void WriteTestPng(string path, int width, int height, int channels)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = channels == 3 ? (byte)2 : (byte)6;
        Chunk("IHDR", header);
        using var data = new MemoryStream();
        using (var zip = new ZLibStream(data, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + width * channels];
            for (var y = 0; y < height; y++)
                zip.Write(row);
        }
        Chunk("IDAT", data.ToArray());
        Chunk("IEND", []);

        void Chunk(string name, byte[] bytes)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length);
            file.Write(number);
            var type = Encoding.ASCII.GetBytes(name);
            file.Write(type);
            file.Write(bytes);
            BinaryPrimitives.WriteUInt32BigEndian(number, PngValidator.Crc32(type, bytes));
            file.Write(number);
        }
    }
}
