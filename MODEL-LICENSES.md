# Model and runtime licenses

This is a local research/evaluation demo, not a grant of rights for commercial use.
Keep the upstream licenses and attribution notices when redistributing applicable
weights or binaries. No model license is replaced by a license for this front end.

| Material | License and source |
| --- | --- |
| Qwen-Image-2.1-Turbo and its community AD-Q4_K quantization | **Qwen Research License**, non-commercial research/evaluation only. [Official license](https://huggingface.co/Qwen/Qwen-Image-2.1-Turbo/blob/d65dbc9a7e8f6b5479e33dee6030eaab2a906509/LICENSE); local copy: `licenses\Qwen-Research-LICENSE.txt`. |
| Qwen Image 2.1 VAE (Comfy-Org packaging) | The Qwen Image 2.1 model's **Qwen Research License**; do not treat repackaging as a different license. |
| Official Qwen3-VL-8B-Instruct GGUF text encoder | **Apache-2.0**, as declared in the [pinned official model card](https://huggingface.co/Qwen/Qwen3-VL-8B-Instruct-GGUF/blob/f982a07559d4a2f6c8744d840bf6fccab30eea96/README.md). Local copy: `licenses\Qwen3-VL-LICENSE.txt`, from the [Qwen3-VL upstream project](https://github.com/QwenLM/Qwen3-VL/blob/96588727e44c78b25ba03ea03b8e12f7e64fd0da/LICENSE). |
| stable-diffusion.cpp and GGML | MIT; their license texts are included in the verified backend package as `stable-diffusion.cpp.txt` and `ggml.txt`. The official native package also contains third-party image libraries; see the pinned upstream source for their notices before redistribution. |

The Qwen Research License includes restrictions on commercial use, redistribution,
attribution, naming, and certain uses of outputs. For commercial use, obtain the
separate license described in the official agreement. Read the complete license;
this brief description is not legal advice.

`licenses\NOTICE.txt` preserves the required Qwen attribution. The model filenames
and download revisions are in `assets.lock.json`. The image generator is explicitly
the **Turbo** checkpoint, not ordinary Qwen Image 2.1; its default Q4 quantization is
published by AtomicChat, not directly by the Qwen authors.
