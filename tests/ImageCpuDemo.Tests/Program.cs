using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
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
        if (args.FirstOrDefault() == "--fake-echo")
        {
            Console.Error.Write(new string('e', 131072) + "\rprogress 8/8\n");
            Console.WriteLine(JsonSerializer.Serialize(args[1..]));
            return 17;
        }
        if (args.FirstOrDefault() == "--fake-sleep")
        {
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

            var help = await RunApp(app, ["--help"], scratch);
            Check(help.Code == 0 && help.Out.Contains("Qwen-Image-2.1-Turbo"), "Published EXE help from another working directory");
            var invalid = await RunApp(app, ["-p", "cat", "--width", "513"], scratch);
            Check(invalid.Code == 2 && invalid.Error.Contains("divisible by 32"), "Published EXE invalid-input exit");
            var missing = await RunApp(app, ["-p", "cat", "--models-dir", Path.Combine(scratch, "missing")], scratch);
            Check(missing.Code == 3 && missing.Error.Contains("Model missing"), "Missing models fail, never fake an image");
            var piped = await RunApp(app, [], scratch, "cat\n65\n96\n");
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
