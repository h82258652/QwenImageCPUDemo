# CPU image demo using Qwen-Image-2.1-Turbo

A Windows x64 console application for **local, CPU-only text-to-image generation**.
It runs the actual Qwen-Image-2.1-Turbo weights through a pinned CPU build of
stable-diffusion.cpp. There is no cloud inference, model substitution, GUI, or
placeholder image. Quantization reduces memory usage and can reduce image quality.

**License:** the image model and VAE are under the Qwen Research License
(research/evaluation only). Commercial use requires a separate license.
Read [MODEL-LICENSES.md](MODEL-LICENSES.md) before use or redistribution.

## Build and run

In Command Prompt, or run the commands prefixed with `.\` in PowerShell:

```bat
cd /d C:\Repos\QwenImageCPUDemo
build.cmd
prepare-models.cmd
bin\ImageCpuDemo.exe --prompt "A cute orange cat sitting in a sunny garden, watercolor illustration" --width 512 --height 512 --output output\cat.png
```

`build.cmd` **compiles the C# source** and creates
`C:\Repos\QwenImageCPUDemo\bin\ImageCpuDemo.exe` plus its DLL and runtime configuration.
It also obtains and verifies the official CPU backend (about 17.5 MB compressed).
It does **not** download models. No C++, Python, CUDA, git initialization, or global
PowerShell policy changes are needed.

Build prerequisite: [.NET 10 x64 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
Running the compiled application requires the .NET 10 x64 runtime. This is a
framework-dependent executable: keep all files in `bin` and keep `bin`, `backend`,
and `models` in the layout below. If the native backend cannot start because a
runtime DLL is missing, install Microsoft's
[Visual C++ x64 redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe).
The scripts report missing dependencies rather than silently installing them.

Run `bin\ImageCpuDemo.exe` without arguments to enter prompt, width, and height
interactively. Empty interactive dimensions default to 512. Command-line dimensions
also default to 512. Width and height must be **multiples of 32, from 32 to 2048**;
the upper limit is a deliberate safety limit for this CPU demo.

## Prompts and options

```bat
bin\ImageCpuDemo.exe --prompt-file prompt.txt -W 256 -H 256 --threads 4 --seed 42 -o output\small.png
bin\ImageCpuDemo.exe --help
bin\ImageCpuDemo.exe --prompt "cat" -W 320 -H 192 --dry-run
```

Save `prompt.txt` as UTF-8. Chinese prompts, newlines, spaces and quotation marks
are supported. Direct `--prompt` input is also Unicode-safe, subject to the normal
quoting rules of your shell. Internally the prompt is sent as a BOM-free UTF-8 file,
not through the native CLI's Windows code-page conversion.

The pinned native CLI uses narrow Windows paths. Keep the **project and model
directory paths ASCII**, for example the requested `C:\Repos\QwenImageCPUDemo`.
Prompt-file input paths and final PNG output paths can contain Chinese/Unicode:
the .NET front end reads/moves them itself.

`--models-dir DIR` selects another folder containing the same three pinned files.
Relative user paths resolve against the calling working directory; default model,
backend and output paths resolve against the project, not the calling directory.
Without `--output`, a timestamped PNG is written under `output`.
Existing output files are **never overwritten**.

`--dry-run` prints the exact argument array and prompt without loading weights or
creating any image. It is an inspection mode, **not** an inference success.
During real generation, native progress and errors stream to the console.
**Ctrl+C** terminates only this run's backend and publishes no partial image.
A successful exit requires a complete PNG with valid chunk checksums, decodable
RGB/RGBA scanlines, and exactly the requested dimensions.
Generated PNG metadata includes the prompt and generation settings.

Exit codes: `0` success/help/dry-run (explicitly identified), `2` invalid input,
`3` missing files/startup/I/O/invalid image, `4` native inference failure, `130` cancelled.

## Models, memory and CPU settings

`prepare-models.cmd` downloads **9,904,989,432 bytes (9.22 GiB)** in total:

| Component | Pinned file |
| --- | --- |
| Turbo diffusion generator | `Qwen-Image-2.1-Turbo-AD-Q4_K.gguf` |
| Qwen3-VL-8B text encoder | `Qwen3VL-8B-Instruct-Q4_K_M.gguf` |
| Qwen Image 2.1 VAE | `qwen_image_2.1_vae_bf16.safetensors` |

These are real model files, not Git LFS pointers. Text-to-image does not need a
vision `mmproj`. Do not substitute the original Qwen Image/Wan VAE or a non-Turbo
generator. Both 4-bit components affect fidelity relative to BF16.

All repository revisions, URLs, byte counts and SHA256 hashes are fixed in
`assets.lock.json`. Model hashes come from the publishers' Hugging Face LFS metadata;
the backend archive hash comes from its GitHub release asset digest.
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

The backend archive is the CPU-only Windows x64 release
[`master-956-1b0ba10`](https://github.com/leejet/stable-diffusion.cpp/releases/tag/master-956-1b0ba10).
It includes runtime-selected CPU instruction variants, including AVX2.
The application additionally passes **`--backend cpu --params-backend cpu`**.
`--rng cpu` controls random numbers; it is **not** what enforces CPU computation.
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
sigma support. Ordinary Qwen Image 2.1 defaults (such as CFG 6) are not used.

## Source and checks

```text
build.cmd                    Compile front end and prepare pinned CPU backend
prepare-models.cmd           Download/resume and SHA256-check model files
assets.lock.json             Reproducible dependency/model manifest
src\ImageCpuDemo\            Complete console application source
scripts\                    Build and download script source
tests\ImageCpuDemo.Tests\    Dependency-free targeted checks
bin\ImageCpuDemo.exe         Compiled application (requires accompanying bin files)
backend\stable-diffusion-1b0ba10\  Verified native runtime and its license texts
models\                     Downloaded weights
output\                     Generated images
.cache\                     Backend archive and build/test cache
.work\                      Per-run UTF-8 prompt and temporary PNG (cleaned on exit)
```

`build.cmd -Test` builds and runs argument/input validation, Unicode/quoting, CPU
placement, exact Turbo schedule, subprocess/error/cancellation, and PNG checks.
These tests do not load large models. Their synthetic PNG fixtures are temporary
test data, not demo output. Real inference is a separate run with prepared weights.

The delivered `output` folder includes a real 256x256 RGBA cat image and
`cpu-smoke.log`. On the target i9-10900X, the CPU-only run used 8 threads, seed 42,
and took about 113 seconds including loading and decoding. Its Chinese/English
prompt, exact Turbo sigma schedule, model filenames and dimensions are embedded
in the PNG metadata. This small-image timing is not a guarantee for larger images.

The native dependency's complete source is available at its
[pinned commit](https://github.com/leejet/stable-diffusion.cpp/tree/1b0ba10893f4e2e0656103011fc1c4645a02a734).
Its published CPU build recipe is in that commit's `.github/workflows/build.yml`.
The root build compiles our front end and reuses that verified official native
binary; it does not claim to rebuild stable-diffusion.cpp from source.
