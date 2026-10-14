using System.Text.Json;

namespace ImageCpuDemo;

internal sealed record BackendAsset(string Directory, string Revision);
internal sealed record ModelAsset(string Role, string File, long Size, string Sha256);
internal sealed record Assets(int SchemaVersion, BackendAsset Backend, BackendAsset GpuBackend, ModelAsset[] Models)
{
    internal static Assets Load(string path)
    {
        var assets = JsonSerializer.Deserialize<Assets>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The asset manifest is empty.");
        if (assets.SchemaVersion != 2 || assets.Backend is null || assets.GpuBackend is null || assets.Models is null ||
            assets.Models.Length != 3 || !IsFileName(assets.Backend.Directory) ||
            !IsFileName(assets.GpuBackend.Directory) ||
            assets.Backend.Directory.Equals(assets.GpuBackend.Directory, StringComparison.OrdinalIgnoreCase) ||
            assets.Backend.Revision is not { Length: 40 } ||
            assets.Backend.Revision != assets.GpuBackend.Revision)
            throw new InvalidDataException("Invalid asset manifest. Restore assets.lock.json and run build.cmd.");
        foreach (var role in new[] { "diffusion", "llm", "vae" })
        {
            var matching = assets.Models.Where(model => model is not null && model.Role == role).ToArray();
            if (matching.Length != 1 || !IsFileName(matching[0].File) || matching[0].Size <= 0 ||
                matching[0].Sha256 is not { Length: 64 })
                throw new InvalidDataException($"Invalid model entry: {role}.");
        }
        return assets;
    }

    internal string ModelPath(string directory, string role) =>
        Path.Combine(directory, Models.Single(model => model.Role == role).File);

    internal string BackendPath(string root, ComputeDevice device) =>
        Path.Combine(root, "backend", device == ComputeDevice.Gpu ? GpuBackend.Directory : Backend.Directory, "sd-cli.exe");

    internal void CheckModelFiles(string directory)
    {
        foreach (var model in Models)
        {
            var file = new FileInfo(Path.Combine(directory, model.File));
            if (!file.Exists)
                throw new FileNotFoundException($"Model missing: {file.FullName}. Run prepare-models.cmd first.");
            if (file.Length != model.Size)
                throw new InvalidDataException($"Model size mismatch: {file.FullName}. Run prepare-models.cmd -VerifyOnly; do not use partial or different weights.");
        }
    }

    private static bool IsFileName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value is not ("." or "..") &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Path.GetFileName(value) == value;
}
