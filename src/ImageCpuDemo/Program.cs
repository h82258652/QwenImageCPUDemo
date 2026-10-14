using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ImageCpuDemo;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0)
            return RunConsoleAsync(args).GetAwaiter().GetResult();

        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new MainForm(ImageGenerator.FromPublishedApp()));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(exception.Message, "Qwen Image Demo - startup error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 3;
        }
    }

    private static async Task<int> RunConsoleAsync(string[] args)
    {
        try
        {
            // WinExe has no console of its own. Inherited redirected handles remain usable.
            if (AttachConsole(uint.MaxValue) == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error is not (5 or 6)) // Already attached, or parent has no console.
                    throw new Win32Exception(error);
            }
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);
            var options = Options.Parse(args);
            if (options.Help)
            {
                Console.WriteLine(Options.HelpText);
                return 0;
            }
            if (options.Interactive)
                options = Options.ReadInteractive(Console.In, Console.Out);
            var generator = ImageGenerator.FromPublishedApp();
            var plan = generator.CreatePlan(options);
            if (options.DryRun)
            {
                Console.WriteLine("DRY RUN: no model is loaded, no file is written, and no image is generated.");
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    backend = plan.Backend,
                    arguments = plan.Arguments,
                    prompt = options.Prompt,
                    input = options.InitImage,
                    output = plan.Destination
                }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                Console.WriteLine("Local inference can take a long time. Ctrl+C cancels only this run's backend.");
                var result = await generator.GenerateAsync(plan, Console.Out, Console.Error, cancellation.Token);
                Console.WriteLine($"\nSaved: {result.Path}");
                Console.WriteLine($"PNG: {options.Width}x{options.Height}; elapsed {result.Elapsed:g}.");
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
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
        catch (InferenceException exception)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return 4;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException or Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return 3;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int AttachConsole(uint processId);
}
