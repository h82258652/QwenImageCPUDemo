# Qwen-Image-2.1-Turbo WinForms demo

A Windows x64 **WinForms application for local text-to-image and image-to-image
generation**, with an explicit **CPU/GPU choice in both modes**. It runs the actual
Qwen-Image-2.1-Turbo weights through pinned CPU and Vulkan builds of
stable-diffusion.cpp. There is no cloud inference, model substitution, or
placeholder output. Quantization reduces memory usage and can reduce image quality.

**License:** the image model and VAE are under the Qwen Research License
(research/evaluation only). Commercial use requires a separate license.
Read [MODEL-LICENSES.md](MODEL-LICENSES.md) before use or redistribution.

## Build and run

In Command Prompt, or run the commands prefixed with `.\` in PowerShell:

```bat
cd /d C:\Repos\QwenImageCPUDemo2\QwenImageCPUDemo
build.cmd
prepare-models.cmd
bin\ImageCpuDemo.exe
```

`build.cmd` **compiles the C# source** and creates `bin\ImageCpuDemo.exe` plus its DLL
and runtime configuration. Double-click this executable to open the desktop UI.
The build also obtains and verifies the official CPU and Vulkan runtimes
(about 17.5 MB and 30.1 MB compressed). Use `build.cmd -CpuOnly` to prepare only the
CPU runtime; GPU generation then requires running the normal build once.
Neither build downloads models. No C++, Python, CUDA toolkit, git initialization,
or global PowerShell policy changes are needed.

Build prerequisite: [.NET 10 x64 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
Running the compiled application requires the **.NET 10 x64 Windows Desktop
Runtime**, not just the base .NET runtime. This is a framework-dependent executable:
keep all files in `bin` and keep `bin`, `backend`, and `models` in the layout below.
If the native backend cannot start because a runtime DLL is missing, install Microsoft's
[Visual C++ x64 redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe).
GPU mode also requires a Vulkan-compatible graphics card and vendor graphics driver.
The scripts report missing dependencies rather than silently installing them.

## Desktop workflow

1. Choose **text-to-image** or **image-to-image (redraw)**, then choose **CPU** or
   **GPU**. GPU devices are detected when the window opens; the GPU mode has a
   refresh button and an adapter selector. CPU is the default.
2. Enter a prompt. For image-to-image, select a **PNG, JPEG or BMP** and set the
   **redraw strength (0.01 to 1, default 0.75)**. Larger values change the image more.
3. Set dimensions, seed and CPU thread count. Optionally choose another model
   directory or a new PNG output path. Leaving the output path empty gives each
   result a unique name under `output`.
4. Start generation. The window stays responsive and displays the native log and
   sampling progress. A validated result is saved automatically and shown in the
   preview with a copyable file path. Cancel stops only this run's backend; closing
   a busy window cancels and waits for cleanup before exiting.

The source preview and generated preview are separate. Source images are never
modified. JPEG orientation is corrected, and the image is fitted to the requested
output size **without stretching or cropping**; uncovered areas and transparency
are composited on white. Input limits are 100 MiB and 64 million pixels.

This is **initial-image redraw**, not reference-image instruction editing or
inpainting. It uses the existing three models, without an additional vision
projector. Turbo has only eight sampling steps, so the native backend quantizes
strength into a shortened sigma schedule. Even the smallest strength is not a
pixel-preserving copy, and strengths close to 1 can discard the source's influence.

Width and height default to 512 and must be **multiples of 32, from 32 to 2048**.
The upper limit remains a deliberate safety limit. Start at 256 or 512; larger
images can require substantially more time and memory.

## CPU and GPU behavior

| Selection | Diffusion | Text encoder / VAE | Model weight storage |
| --- | --- | --- | --- |
| CPU | CPU-only runtime | CPU | System RAM |
| GPU | Selected Vulkan GPU | CPU | System RAM, staged to GPU as needed |

GPU mode explicitly passes `--backend all=cpu,diffusion=VulkanN` and
`--params-backend cpu`. CPU mode uses a separate CPU-only executable and passes
`--backend cpu --params-backend cpu`. `--rng cpu` controls random numbers, not
the inference device.

Keeping text encoding and the VAE on CPU makes GPU acceleration more practical
on small cards. It is **not** a claim that all computation runs on the GPU or that
every card is faster than CPU. The current Quadro P620 has only 2 GiB VRAM:
automatic graph segmentation can stage weights, but working buffers and each
segment must still fit. Low VRAM, unsupported operations or an unsuitable driver
can still cause a reported failure. No inference failure is silently retried on CPU.
Select CPU explicitly if the GPU cannot run the requested workload.

## Prompts and options

The existing command-line generation interface is retained. Since the executable
is now a Windows GUI application, use the DLL through `dotnet` in scripts to get
normal console waiting and redirected output:

```bat
dotnet bin\ImageCpuDemo.dll --prompt-file prompt.txt -W 256 -H 256 --device cpu --threads 4 --seed 42 -o output\small.png
dotnet bin\ImageCpuDemo.dll --prompt "watercolor cat" --init-image "C:\Images\cat.jpg" --strength 0.5 --device gpu --gpu-device Vulkan0 -o output\redraw.png
dotnet bin\ImageCpuDemo.dll --help
dotnet bin\ImageCpuDemo.dll --prompt "cat" -W 320 -H 192 --device gpu --dry-run
dotnet bin\ImageCpuDemo.dll --interactive
```

`--init-image` enables image-to-image; omitting it selects text-to-image.
`--strength` requires an input image. `--device` accepts only `cpu` or `gpu`;
`--gpu-device` is optional and defaults to `Vulkan0`. Use the UI's detected device
names to select another GPU. `--interactive` preserves the original console prompt,
width and height workflow (CPU text-to-image). No arguments now open WinForms.

Save `prompt.txt` as UTF-8. Chinese prompts, newlines, spaces and quotation marks
are supported. Direct `--prompt` input is also Unicode-safe, subject to the normal
quoting rules of your shell. Internally the prompt is sent as a BOM-free UTF-8 file,
not through the native CLI's Windows code-page conversion.

The pinned native CLI uses narrow Windows paths. Keep the **project and model
directory paths ASCII**, such as `C:\Repos\QwenImageCPUDemo2\QwenImageCPUDemo`.
Prompt-file paths, source-image paths and final PNG output paths can contain
Chinese/Unicode: the .NET front end reads, stages and publishes them itself.

`--models-dir DIR` selects another folder containing the same three pinned files.
Relative user paths resolve against the calling working directory; default model,
backend and output paths resolve against the project, not the calling directory.
Without `--output`, a uniquely named, timestamped PNG is written under `output`.
Existing output files are **never overwritten**.

`--dry-run` prints the exact argument array and prompt without loading weights,
probing GPUs, decoding input images or creating files. It is an inspection mode,
**not** an inference success or a check that the selected GPU/input is available.
During real CLI generation, native progress and errors stream to the console.
**Ctrl+C**, or Cancel in the GUI, terminates only this run's backend and publishes no partial image.
A successful exit requires a complete PNG with valid chunk checksums, decodable
RGB/RGBA scanlines, and exactly the requested dimensions.
Generated PNG metadata includes the prompt and generation settings.

Exit codes: `0` success/help/dry-run (explicitly identified), `2` invalid input,
`3` missing files/startup/I/O/invalid image, `4` native inference failure, `130` cancelled.

## Models and memory

`prepare-models.cmd` downloads **9,904,989,432 bytes (9.22 GiB)** in total:

| Component | Pinned file |
| --- | --- |
| Turbo diffusion generator | `Qwen-Image-2.1-Turbo-AD-Q4_K.gguf` |
| Qwen3-VL-8B text encoder | `Qwen3VL-8B-Instruct-Q4_K_M.gguf` |
| Qwen Image 2.1 VAE | `qwen_image_2.1_vae_bf16.safetensors` |

These are real model files, not Git LFS pointers. Neither text-to-image nor
initial-image redraw needs a vision `mmproj`.
Do not substitute the original Qwen Image/Wan VAE or a non-Turbo
generator. Both 4-bit components affect fidelity relative to BF16.

All repository revisions, URLs, byte counts and SHA256 hashes are fixed in
`assets.lock.json`. Model hashes come from the publishers' Hugging Face LFS metadata;
backend archive hashes come from their GitHub release asset digests.
Downloads use HTTPS, retain `.part` files for resume, and are promoted only after
size and SHA256 verification. Existing complete files are verified, not replaced.
If a hash fails, move/remove **that specific reported file** before retrying.

```bat
prepare-models.cmd -VerifyOnly
prepare-models.cmd -ModelsDirectory D:\AI\Models
```

Inference itself is offline. Startup checks model presence and size; use
`-VerifyOnly` for a full SHA256 recheck after copying files.
Allow more disk space than the download size and substantial free physical and
committed memory beyond the model weights. Start at **256x256 or 512x512**.
Large images can be extremely slow and require much more working memory;
there is no promise of a particular generation time or memory ceiling.

Both native runtimes come from the Windows x64 release
[`master-956-1b0ba10`](https://github.com/leejet/stable-diffusion.cpp/releases/tag/master-956-1b0ba10).
The CPU runtime includes runtime-selected instruction variants, including AVX2;
the GPU runtime adds Vulkan without replacing the CPU-only package.
Default threads are half the logical processor count, capped at 8. `--threads`
can reduce CPU load. Memory mapping, flash attention, and 256-pixel VAE tiling are
enabled. The program does not change paging/system settings or stop other processes.

Turbo's settings are fixed: **8 steps, Euler, CFG 1**, with these exact sigmas:

```text
1.0,0.978453,0.95418,0.926626,0.89508,0.845148,0.704534,0.414568,0.0
```

The first eight values are from the official
[`model_index.json`](https://huggingface.co/Qwen/Qwen-Image-2.1-Turbo/blob/d65dbc9a7e8f6b5479e33dee6030eaab2a906509/model_index.json);
the terminal zero is supplied to sd.cpp. The pinned backend includes working custom
sigma support. Image-to-image uses its native strength-dependent suffix of this
same schedule. Ordinary Qwen Image 2.1 defaults (such as CFG 6) are not used.

## Source and checks

```text
build.cmd                    Compile WinForms front end and prepare CPU/Vulkan runtimes
prepare-models.cmd           Download/resume and SHA256-check model files
assets.lock.json             Reproducible dependency/model manifest
src\ImageCpuDemo\            WinForms, shared inference service and CLI source
scripts\                    Build and download script source
scripts\PrepareBackends.ps1  Verify and extract matching CPU/Vulkan runtimes
tests\ImageCpuDemo.Tests\    Dependency-free targeted checks
bin\ImageCpuDemo.exe         Compiled application (requires accompanying bin files)
backend\stable-diffusion-1b0ba10\         Verified CPU runtime and license texts
backend\stable-diffusion-1b0ba10-vulkan\  Verified Vulkan runtime and license texts
models\                     Downloaded weights
output\                     Generated images
.cache\                     Backend archives and build/test cache
.work\                      Per-run UTF-8 prompt, staged input and output (cleaned on exit)
```

`build.cmd -Test` builds and runs argument/input validation, Unicode/quoting,
CPU/GPU placement, GPU discovery, exact Turbo schedule, image decoding/fitting,
WinForms option wiring, subprocess/error/cancellation, atomic output publication
and PNG checks. It also verifies the published executable's GUI subsystem and CLI.
These tests do not load large models. Their synthetic PNG fixtures are temporary
test data, not demo output. Real inference is a separate run with prepared weights.

The existing `output` folder includes a real 256x256 RGBA cat image and
`cpu-smoke.log` from the original console version. On the target i9-10900X,
that CPU-only run used 8 threads, seed 42,
and took about 113 seconds including loading and decoding. Its Chinese/English
prompt, exact Turbo sigma schedule, model filenames and dimensions are embedded
in the PNG metadata. This small-image timing is not a guarantee for larger images.

The native dependency's complete source is available at its
[pinned commit](https://github.com/leejet/stable-diffusion.cpp/tree/1b0ba10893f4e2e0656103011fc1c4645a02a734).
Its published CPU and Vulkan build recipes are in that commit's
`.github/workflows/build.yml`. The root build compiles our front end and reuses
those verified official native binaries; it does not claim to rebuild
stable-diffusion.cpp from source.
