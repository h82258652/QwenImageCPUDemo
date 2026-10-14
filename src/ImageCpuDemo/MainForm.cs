using System.ComponentModel;
using System.Text;

namespace ImageCpuDemo;

internal sealed class MainForm : Form
{
    private readonly ImageGenerator generator;
    private readonly ComboBox mode = Choice("generationMode");
    private readonly ComboBox device = Choice("computeDevice");
    private readonly ComboBox gpuDevice = Choice("gpuDevice");
    private readonly Button refreshDevices = new() { Text = "重新检测", AutoSize = true, Name = "refreshDevices" };
    private readonly TextBox prompt = new()
    {
        Name = "prompt",
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical,
        Height = 130,
        Text = "A cute orange cat sitting in a sunny garden, watercolor illustration"
    };
    private readonly TextBox inputPath = new() { Name = "inputImagePath", ReadOnly = true };
    private readonly Button browseInput = new() { Name = "browseInput", Text = "选择图片", AutoSize = true };
    private readonly NumericUpDown strength = new()
    {
        Name = "strength",
        Minimum = (decimal)Options.MinimumStrength,
        Maximum = 1,
        DecimalPlaces = 2,
        Increment = 0.05m,
        Value = 0.75m
    };
    private readonly NumericUpDown width = Dimension("imageWidth");
    private readonly NumericUpDown height = Dimension("imageHeight");
    private readonly NumericUpDown threads = new()
    {
        Name = "threads",
        Minimum = 1,
        Maximum = Environment.ProcessorCount,
        Value = Options.DefaultThreads
    };
    private readonly NumericUpDown seed = new() { Name = "seed", Minimum = 0, Maximum = long.MaxValue, Value = 42 };
    private readonly TextBox modelsDirectory = new() { Name = "modelsDirectory" };
    private readonly TextBox outputPath = new()
    {
        Name = "outputPath",
        PlaceholderText = "留空：自动保存到 output 文件夹"
    };
    private readonly Label modeHint = Hint("");
    private readonly Label deviceHint = Hint("");
    private readonly GroupBox inputGroup = new() { Text = "生成参数", Dock = DockStyle.Fill, Padding = new Padding(10) };
    private readonly GroupBox sourceGroup = new() { Text = "原图", Dock = DockStyle.Fill };
    private readonly GroupBox resultGroup = new() { Text = "生成结果", Dock = DockStyle.Fill };
    private readonly PictureBox sourcePreview = Preview("sourcePreview", "原图预览");
    private readonly PictureBox resultPreview = Preview("resultPreview", "生成图片预览");
    private readonly TableLayoutPanel previews = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AccessibleName = "图像预览" };
    private readonly RichTextBox log = new()
    {
        Name = "generationLog",
        AccessibleName = "运行日志",
        Dock = DockStyle.Fill,
        ReadOnly = true,
        DetectUrls = false,
        BackColor = Color.FromArgb(247, 249, 252),
        BorderStyle = BorderStyle.None
    };
    private readonly Button generate = new() { Name = "generate", Text = "开始生成", AutoSize = true, MinimumSize = new Size(120, 34) };
    private readonly Button cancel = new() { Name = "cancelGeneration", Text = "取消", AutoSize = true, Enabled = false, MinimumSize = new Size(90, 34) };
    private readonly ProgressBar progressBar = new() { Width = 200, Height = 22, Margin = new Padding(16, 8, 3, 3) };
    private readonly Label status = new() { Name = "status", Text = "就绪", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
    private readonly TextBox lastOutput = new()
    {
        Name = "lastOutput",
        AccessibleName = "已保存的 PNG 路径",
        ReadOnly = true,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        PlaceholderText = "生成成功后将在此显示保存路径，可选中复制。"
    };
    private readonly ToolTip tips = new();
    private readonly Label inputLabel;
    private readonly Label strengthLabel;
    private readonly Label gpuLabel;
    private Task operation = Task.CompletedTask;
    private CancellationTokenSource? operationCancellation;
    private bool closeRequested;

    internal MainForm(ImageGenerator generator)
    {
        this.generator = generator;
        Text = "Qwen Image Demo";
        Name = "mainForm";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1220, 860);
        MinimumSize = new Size(1060, 760);
        Padding = new Padding(14);

        mode.Items.AddRange(["文生图", "图生图（原图重绘）"]);
        device.Items.AddRange(["CPU", "GPU（Vulkan 扩散加速）"]);
        mode.SelectedIndex = 0;
        device.SelectedIndex = 0;
        modelsDirectory.Text = generator.DefaultModelsDirectory;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, AccessibleName = "主界面布局" };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = "Qwen Image Demo · 本地图像生成",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Text = "Qwen-Image-2.1-Turbo  ·  完全本地推理  ·  Qwen Research License：仅研究 / 评估用途",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 1);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, AccessibleName = "生成工作区" };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(inputGroup, 0, 0);
        root.Controls.Add(body, 0, 2);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AccessibleName = "生成参数内容" };
        var inputs = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, AccessibleName = "参数输入" };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        scroll.Controls.Add(inputs);
        inputGroup.Controls.Add(scroll);
        AddRow(inputs, "生成模式", mode);
        AddRow(inputs, "计算设备", device);
        gpuLabel = AddRow(inputs, "GPU 设备", gpuDevice, refreshDevices);
        AddHint(inputs, deviceHint);
        AddRow(inputs, "提示词", prompt);
        inputLabel = AddRow(inputs, "原图", inputPath, browseInput);
        strengthLabel = AddRow(inputs, "重绘强度", strength);
        AddHint(inputs, modeHint);
        AddRow(inputs, "宽度", width);
        AddRow(inputs, "高度", height);
        AddRow(inputs, "随机种子", seed);
        AddRow(inputs, "CPU 线程", threads);
        var browseModels = new Button { Text = "选择目录", AutoSize = true, Name = "browseModels" };
        var browseOutput = new Button { Text = "保存位置", AutoSize = true, Name = "browseOutput" };
        AddRow(inputs, "模型目录", modelsDirectory, browseModels);
        AddRow(inputs, "输出 PNG", outputPath, browseOutput);
        AddHint(inputs, Hint("尺寸必须为 32 的倍数（32–2048）。建议从 256 或 512 开始；大图可能很慢或内存不足。现有图片不会被覆盖。"));

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AccessibleName = "预览和日志" };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previews.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sourceGroup.Controls.Add(sourcePreview);
        resultGroup.Controls.Add(resultPreview);
        previews.Controls.Add(sourceGroup, 0, 0);
        previews.Controls.Add(resultGroup, 1, 0);
        right.Controls.Add(previews, 0, 0);
        right.Controls.Add(lastOutput, 0, 1);
        var logGroup = new GroupBox { Text = "运行日志 / 原生推理进度", Dock = DockStyle.Fill, Padding = new Padding(8) };
        logGroup.Controls.Add(log);
        right.Controls.Add(logGroup, 0, 2);
        body.Controls.Add(right, 1, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(0, 8, 0, 0), AccessibleName = "生成操作" };
        buttons.Controls.AddRange([generate, cancel, progressBar]);
        root.Controls.Add(buttons, 0, 3);
        root.Controls.Add(status, 0, 4);
        root.Controls.Add(new Label
        {
            Text = "GPU 模式仅加速扩散计算；文本编码 / VAE 在 CPU，权重保留在内存。显存不足会报错，不会自动改用 CPU。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 8, 0, 0)
        }, 0, 5);
        Controls.Add(root);

        tips.SetToolTip(strength, "数值越小越接近原图，越大改动越明显。Turbo 只有 8 个采样步，强度按步数量化；接近 1 时原图影响可能消失。");
        tips.SetToolTip(browseInput, "支持 PNG / JPEG / BMP，最多 100 MiB、6400 万像素。自动纠正照片方向，等比缩放并补白到输出尺寸。");
        tips.SetToolTip(outputPath, "留空自动命名；也可指定新的 PNG 路径。不会覆盖现有文件。");
        tips.SetToolTip(prompt, "支持中文、英文和多行提示词。提示词会写入生成 PNG 的元数据。");

        mode.SelectedIndexChanged += (_, _) => UpdateMode();
        device.SelectedIndexChanged += (_, _) => UpdateDevice();
        browseInput.Click += (_, _) => SelectInput();
        browseModels.Click += (_, _) => SelectModels();
        browseOutput.Click += (_, _) => SelectOutput();
        refreshDevices.Click += (_, _) => operation = RefreshDevicesAsync();
        generate.Click += (_, _) => operation = GenerateAsync();
        cancel.Click += (_, _) => CancelOperation();
        Shown += (_, _) => operation = RefreshDevicesAsync();
        UpdateMode();
        UpdateDevice();
    }

    internal Options ReadOptions()
    {
        if (mode.SelectedIndex == 1 && string.IsNullOrWhiteSpace(inputPath.Text))
            throw new UsageException("请先选择一张用于重绘的原图。");
        var selectedGpu = gpuDevice.SelectedItem as GpuDevice;
        if (device.SelectedIndex == 1 && selectedGpu is null)
            throw new UsageException("没有可用的 Vulkan GPU。请查看日志、重新检测设备，或明确选择 CPU。");
        var options = new Options
        {
            Prompt = prompt.Text,
            Width = (int)width.Value,
            Height = (int)height.Value,
            Seed = (long)seed.Value,
            Threads = (int)threads.Value,
            Device = device.SelectedIndex == 1 ? ComputeDevice.Gpu : ComputeDevice.Cpu,
            GpuDevice = selectedGpu?.Name ?? "Vulkan0",
            InitImage = mode.SelectedIndex == 1 ? inputPath.Text : null,
            Strength = (double)strength.Value,
            ModelsDirectory = string.IsNullOrWhiteSpace(modelsDirectory.Text) ? null : modelsDirectory.Text.Trim(),
            Output = string.IsNullOrWhiteSpace(outputPath.Text) ? null : outputPath.Text.Trim()
        };
        options.Validate();
        return options;
    }

    private async Task RefreshDevicesAsync()
    {
        using var cancellation = new CancellationTokenSource();
        operationCancellation = cancellation;
        SetBusy(true);
        SetStatus("正在检测 Vulkan GPU…", Color.DimGray);
        var selectedName = (gpuDevice.SelectedItem as GpuDevice)?.Name;
        gpuDevice.Items.Clear();
        try
        {
            var devices = await generator.GetGpuDevicesAsync(cancellation.Token);
            gpuDevice.Items.AddRange(devices);
            if (devices.Length > 0)
            {
                gpuDevice.SelectedItem = devices.FirstOrDefault(item => item.Name == selectedName) ?? devices[0];
                SetStatus($"就绪。检测到 {devices.Length} 个 Vulkan GPU，可在“计算设备”中选择 CPU 或 GPU。", Color.DarkGreen);
                AppendLog("检测到 GPU：" + string.Join(", ", devices.Select(item => item.ToString())) + Environment.NewLine);
            }
            else
            {
                SetStatus("未检测到 Vulkan GPU。请检查显卡驱动；仍可选择 CPU。", Color.DarkOrange);
                AppendLog(status.Text + Environment.NewLine);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("设备检测已取消。", Color.DimGray);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            SetStatus("GPU 检测失败；CPU 仍可选择。详情见日志。", Color.Firebrick);
            AppendLog(exception.Message + Environment.NewLine);
        }
        finally
        {
            operationCancellation = null;
            SetBusy(false);
        }
    }

    private async Task GenerateAsync()
    {
        GenerationPlan plan;
        try
        {
            plan = generator.CreatePlan(ReadOptions());
        }
        catch (Exception exception) when (exception is UsageException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            ShowFailure(exception);
            return;
        }
        using var cancellation = new CancellationTokenSource();
        operationCancellation = cancellation;
        SetBusy(true);
        log.Clear();
        SetStatus("正在生成… 模型加载期间可能暂时没有采样进度。", Color.DarkBlue);
        using var writer = new ProgressWriter(new Progress<string>(AppendLog));
        try
        {
            var result = await Task.Run(() => generator.GenerateAsync(plan, writer, writer, cancellation.Token));
            lastOutput.Text = result.Path;
            resultGroup.Text = $"生成结果 · {plan.Options.Width} × {plan.Options.Height}";
            resultPreview.AccessibleDescription = $"{plan.Options.Width} × {plan.Options.Height}; {result.Path}";
            SetStatus($"已保存 PNG，用时 {result.Elapsed:hh\\:mm\\:ss}。", Color.DarkGreen);
            AppendLog($"Saved: {result.Path}{Environment.NewLine}");
            try
            {
                ReplaceImage(resultPreview, InputImage.Load(result.Path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SetStatus("PNG 已保存，但无法加载预览。保存路径见右侧，详情见日志。", Color.DarkOrange);
                AppendLog(exception.Message + Environment.NewLine);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消。后端进程已停止，未发布部分图片。", Color.DimGray);
            AppendLog(status.Text + Environment.NewLine);
        }
        catch (Exception exception) when (exception is UsageException or InferenceException or IOException or
            UnauthorizedAccessException or Win32Exception or ArgumentException or InvalidOperationException)
        {
            ShowFailure(exception);
        }
        finally
        {
            operationCancellation = null;
            SetBusy(false);
        }
    }

    private void SelectInput()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择重绘原图",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            var image = InputImage.Load(dialog.FileName);
            ReplaceImage(sourcePreview, image);
            sourceGroup.Text = $"原图 · {image.Width} × {image.Height}";
            sourcePreview.AccessibleDescription = $"{image.Width} × {image.Height}; {dialog.FileName}";
            inputPath.Text = dialog.FileName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowFailure(exception);
        }
    }

    private void SelectModels()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择包含三个 Qwen 模型文件的目录", UseDescriptionForTitle = true };
        if (Directory.Exists(modelsDirectory.Text))
            dialog.InitialDirectory = modelsDirectory.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            modelsDirectory.Text = dialog.SelectedPath;
    }

    private void SelectOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "选择新的 PNG 保存路径（不覆盖已有文件）",
            Filter = "PNG 图片 (*.png)|*.png",
            DefaultExt = "png",
            AddExtension = true,
            FileName = $"image-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png",
            InitialDirectory = Directory.Exists(Path.Combine(generator.Root, "output")) ? Path.Combine(generator.Root, "output") : generator.Root
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            outputPath.Text = dialog.FileName;
    }

    private void UpdateMode()
    {
        var imageMode = mode.SelectedIndex == 1;
        inputLabel.Visible = inputPath.Visible = browseInput.Visible = imageMode;
        strengthLabel.Visible = strength.Visible = imageMode;
        sourceGroup.Visible = imageMode;
        previews.SetColumn(resultGroup, imageMode ? 1 : 0);
        previews.SetColumnSpan(resultGroup, imageMode ? 1 : 2);
        modeHint.Text = imageMode
            ? "上传原图 + 提示词进行重绘。强度越大改动越明显；Turbo 只有 8 步，接近 1 时原图影响可能消失。原图会等比缩放并补白，透明区域按白色处理。"
            : "输入提示词生成新图片。支持中文和多行文本；提示词将嵌入 PNG 元数据。";
    }

    private void UpdateDevice()
    {
        var gpu = device.SelectedIndex == 1;
        gpuLabel.Visible = gpuDevice.Visible = refreshDevices.Visible = gpu;
        deviceHint.Text = gpu
            ? "GPU：Vulkan 扩散加速，文本编码和 VAE 在 CPU。小显存显卡不保证提速或能运行全部尺寸。"
            : "CPU：使用独立 CPU 后端，无需显卡。线程数可调。";
    }

    private void SetBusy(bool busy)
    {
        inputGroup.Enabled = !busy && !closeRequested;
        generate.Enabled = !busy && !closeRequested;
        cancel.Enabled = busy && !closeRequested;
        progressBar.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        if (!busy)
            progressBar.Value = 0;
    }

    private void CancelOperation()
    {
        if (operationCancellation is null)
            return;
        cancel.Enabled = false;
        SetStatus("正在取消，请等待后端退出并清理临时文件…", Color.DimGray);
        operationCancellation.Cancel();
    }

    private void ShowFailure(Exception exception)
    {
        SetStatus("操作失败，详情见日志。", Color.Firebrick);
        AppendLog("ERROR: " + exception.Message + Environment.NewLine);
        if (!closeRequested)
            MessageBox.Show(this, exception.Message, "Qwen Image Demo", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SetStatus(string text, Color color)
    {
        status.Text = text;
        status.ForeColor = color;
    }

    private void AppendLog(string text)
    {
        if (IsDisposed)
            return;
        if (log.TextLength > 200_000)
        {
            log.Select(0, log.TextLength - 150_000);
            log.SelectedText = "";
        }
        log.AppendText(text.Replace("\r\n", "\n").Replace('\r', '\n'));
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (operation.IsCompleted)
        {
            base.OnFormClosing(e);
            return;
        }
        e.Cancel = true;
        base.OnFormClosing(e);
        if (closeRequested)
            return;
        closeRequested = true;
        CancelOperation();
        await operation;
        BeginInvoke(Close);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tips.Dispose();
            sourcePreview.Image?.Dispose();
            resultPreview.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static void ReplaceImage(PictureBox preview, Image image)
    {
        var previous = preview.Image;
        preview.Image = image;
        previous?.Dispose();
    }

    private static ComboBox Choice(string name) => new() { Name = name, DropDownStyle = ComboBoxStyle.DropDownList };
    private static NumericUpDown Dimension(string name) => new() { Name = name, Minimum = 32, Maximum = Options.MaxDimension, Increment = 32, Value = 512 };
    private static PictureBox Preview(string name, string accessibleName) => new()
    {
        Name = name,
        AccessibleName = accessibleName,
        AccessibleRole = AccessibleRole.Graphic,
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(238, 241, 246)
    };
    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(3, 3, 3, 10)
    };

    private static Label AddRow(TableLayoutPanel table, string text, Control input, Button? button = null)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label { Text = text, AutoSize = true, Margin = new Padding(3, 8, 3, 4) };
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(3, 4, 3, 4);
        table.Controls.Add(label, 0, row);
        table.Controls.Add(input, 1, row);
        if (button is null)
            table.SetColumnSpan(input, 2);
        else
        {
            button.Dock = DockStyle.Fill;
            table.Controls.Add(button, 2, row);
        }
        return label;
    }

    private static void AddHint(TableLayoutPanel table, Label label)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(label, 0, row);
        table.SetColumnSpan(label, 3);
    }

    private sealed class ProgressWriter(IProgress<string> progress) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => progress.Report(value.ToString());
        public override void Write(string? value)
        {
            if (value is not null)
                progress.Report(value);
        }
        public override void WriteLine(string? value) => progress.Report(value + Environment.NewLine);
        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(buffer.ToString());
            return Task.CompletedTask;
        }
    }
}
