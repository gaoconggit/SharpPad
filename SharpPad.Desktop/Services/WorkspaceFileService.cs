using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SharpPad.Desktop.Services;

internal sealed class WorkspaceFileService
{
    private const string MetadataDirectoryName = ".sharppad";
    private const string MetadataFileName = "workspace.json";
    private const long MaxFileSizeBytes = 512 * 1024; // 512 KB per file to avoid loading large binaries

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        MetadataDirectoryName,
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csx", ".csproj",
        ".fs", ".fsproj",
        ".vb", ".vbproj",
        ".json", ".txt", ".md", ".xml", ".config", ".props", ".targets", ".sln"
    };

    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<WorkspaceLoadResult> PickFolderAsync(Window owner)
    {
        if (owner is null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        var storageProvider = owner.StorageProvider;
        if (storageProvider is null)
        {
            return WorkspaceLoadResult.CreateFailure("当前窗口未提供存储服务。");
        }

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择项目文件夹",
            AllowMultiple = false
        });

        var folder = folders is { Count: > 0 } ? folders[0] : null;
        if (folder is null)
        {
            return WorkspaceLoadResult.CreateCancelled();
        }

        var localPath = folder.Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return WorkspaceLoadResult.CreateFailure("无法解析所选文件夹路径。");
        }

        return await LoadWorkspaceAsync(localPath);
    }

    public async Task<WorkspaceLoadResult> LoadWorkspaceAsync(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return WorkspaceLoadResult.CreateFailure("工作区路径为空。");
        }

        if (!Directory.Exists(rootPath))
        {
            return WorkspaceLoadResult.CreateFailure("工作区文件夹不存在。");
        }

        try
        {
            var normalizedRoot = NormalizeRootPath(rootPath);
            var metadata = await ReadMetadataAsync(normalizedRoot);
            var items = await BuildItemsAsync(normalizedRoot, metadata);

            return WorkspaceLoadResult.CreateSuccess(normalizedRoot, items);
        }
        catch (Exception ex)
        {
            return WorkspaceLoadResult.CreateFailure($"读取工作区失败: {ex.Message}");
        }
    }

    public async Task<WorkspaceSaveResult> SaveWorkspaceAsync(string rootPath, IReadOnlyCollection<WorkspaceItem> items)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return WorkspaceSaveResult.CreateFailure("工作区路径为空。");
        }

        var normalizedRoot = NormalizeRootPath(rootPath);
        if (!Directory.Exists(normalizedRoot))
        {
            return WorkspaceSaveResult.CreateFailure("工作区文件夹不存在。");
        }

        try
        {
            var metadata = await ReadMetadataAsync(normalizedRoot);
            var updatedMetadata = new Dictionary<string, WorkspaceFileMetadata>(StringComparer.OrdinalIgnoreCase);

            var trackedPaths = new HashSet<string>(metadata.Files.Keys, StringComparer.OrdinalIgnoreCase);
            var incomingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await WriteItemsAsync(normalizedRoot, items, incomingPaths, updatedMetadata);

            var stalePaths = new HashSet<string>(trackedPaths, StringComparer.OrdinalIgnoreCase);
            stalePaths.ExceptWith(incomingPaths);
            await RemoveStaleFilesAsync(normalizedRoot, stalePaths);

            metadata.Files = updatedMetadata;
            await WriteMetadataAsync(normalizedRoot, metadata);

            return WorkspaceSaveResult.CreateSuccess(normalizedRoot);
        }
        catch (Exception ex)
        {
            return WorkspaceSaveResult.CreateFailure($"保存到磁盘失败: {ex.Message}");
        }
    }

    public async Task<WorkspaceSaveResult> SaveWorkspaceFileAsync(string rootPath, WorkspaceItem file)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return WorkspaceSaveResult.CreateFailure("工作区路径为空。");
        }

        if (!string.Equals(file.Type, "file", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceSaveResult.CreateFailure("仅支持保存文件内容。");
        }

        var normalizedRoot = NormalizeRootPath(rootPath);
        if (!Directory.Exists(normalizedRoot))
        {
            return WorkspaceSaveResult.CreateFailure("工作区文件夹不存在。");
        }

        try
        {
            var metadata = await ReadMetadataAsync(normalizedRoot);
            var relativePath = NormalizeRelativePath(file.RelativePath);
            var fullPath = EnsureTargetPath(normalizedRoot, relativePath);

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(fullPath, file.Content ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            metadata.Files[relativePath] = new WorkspaceFileMetadata
            {
                ProjectType = file.ProjectType,
                NugetConfig = file.NugetConfig
            };

            await WriteMetadataAsync(normalizedRoot, metadata);
            return WorkspaceSaveResult.CreateSuccess(fullPath);
        }
        catch (Exception ex)
        {
            return WorkspaceSaveResult.CreateFailure($"保存文件失败: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<WorkspaceItem>> BuildItemsAsync(string rootPath, WorkspaceMetadata metadata)
    {
        var directory = new DirectoryInfo(rootPath);
        if (!directory.Exists)
        {
            return Array.Empty<WorkspaceItem>();
        }

        var children = new List<WorkspaceItem>();

        foreach (var subDirectory in directory.EnumerateDirectories().Where(d => !IsIgnoredDirectory(d)))
        {
            children.Add(await BuildFolderAsync(rootPath, subDirectory, metadata));
        }

        foreach (var file in directory.EnumerateFiles())
        {
            if (ShouldIncludeFile(file))
            {
                children.Add(await BuildFileAsync(rootPath, file, metadata));
            }
        }

        return children;
    }

    private async Task<WorkspaceItem> BuildFolderAsync(string rootPath, DirectoryInfo directory, WorkspaceMetadata metadata)
    {
        var item = new WorkspaceItem
        {
            Name = directory.Name,
            Type = "folder",
            RelativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, directory.FullName)),
            Files = new List<WorkspaceItem>()
        };

        foreach (var subDirectory in directory.EnumerateDirectories().Where(d => !IsIgnoredDirectory(d)))
        {
            item.Files!.Add(await BuildFolderAsync(rootPath, subDirectory, metadata));
        }

        foreach (var file in directory.EnumerateFiles())
        {
            if (ShouldIncludeFile(file))
            {
                item.Files!.Add(await BuildFileAsync(rootPath, file, metadata));
            }
        }

        return item;
    }

    private async Task<WorkspaceItem> BuildFileAsync(string rootPath, FileInfo fileInfo, WorkspaceMetadata metadata)
    {
        var relativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, fileInfo.FullName));
        var content = string.Empty;

        try
        {
            if (fileInfo.Length <= MaxFileSizeBytes)
            {
                content = await File.ReadAllTextAsync(fileInfo.FullName);
            }
        }
        catch
        {
            // ignore read failures and return empty content
        }

        metadata.Files.TryGetValue(relativePath, out var meta);

        return new WorkspaceItem
        {
            Name = fileInfo.Name,
            Type = "file",
            RelativePath = relativePath,
            Content = content,
            ProjectType = meta?.ProjectType,
            NugetConfig = meta?.NugetConfig,
            Files = null
        };
    }

    private static string NormalizeRootPath(string rootPath)
    {
        var fullPath = Path.GetFullPath(rootPath);
        if (!fullPath.EndsWith(Path.DirectorySeparatorChar))
        {
            fullPath += Path.DirectorySeparatorChar;
        }
        return fullPath;
    }

    private static string NormalizeRelativePath(string? relativePath)
    {
        var normalized = relativePath ?? string.Empty;
        normalized = normalized.Replace('\\', '/');
        normalized = normalized.TrimStart('/');
        return normalized;
    }

    private static string EnsureTargetPath(string rootPath, string relativePath)
    {
        var combined = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!combined.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标路径不在工作区内。");
        }
        return combined;
    }

    private async Task WriteItemsAsync(string rootPath,
        IEnumerable<WorkspaceItem> items,
        ISet<string> incomingPaths,
        IDictionary<string, WorkspaceFileMetadata> metadata)
    {
        foreach (var item in items)
        {
            var relativePath = NormalizeRelativePath(item.RelativePath);
            var fullPath = EnsureTargetPath(rootPath, relativePath);

            if (string.Equals(item.Type, "folder", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(fullPath);
                incomingPaths.Add(relativePath);

                if (item.Files is not null)
                {
                    await WriteItemsAsync(rootPath, item.Files, incomingPaths, metadata);
                }
            }
            else
            {
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(fullPath, item.Content ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                incomingPaths.Add(relativePath);
                metadata[relativePath] = new WorkspaceFileMetadata
                {
                    ProjectType = item.ProjectType,
                    NugetConfig = item.NugetConfig
                };
            }
        }
    }

    private static async Task RemoveStaleFilesAsync(string rootPath, IEnumerable<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            try
            {
                var fullPath = EnsureTargetPath(rootPath, NormalizeRelativePath(relativePath));
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
                else if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, recursive: true);
                }
            }
            catch
            {
                // ignore deletion errors to avoid blocking the save operation
            }
        }
    }

    private static bool IsIgnoredDirectory(DirectoryInfo directory)
    {
        return IgnoredDirectories.Contains(directory.Name);
    }

    private static bool ShouldIncludeFile(FileInfo fileInfo)
    {
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            return false;
        }

        return AllowedExtensions.Contains(fileInfo.Extension);
    }

    private async Task<WorkspaceMetadata> ReadMetadataAsync(string rootPath)
    {
        try
        {
            var metadataPath = GetMetadataPath(rootPath);
            if (!File.Exists(metadataPath))
            {
                return new WorkspaceMetadata();
            }

            await using var stream = File.OpenRead(metadataPath);
            var metadata = await JsonSerializer.DeserializeAsync<WorkspaceMetadata>(stream, _serializerOptions);
            return metadata ?? new WorkspaceMetadata();
        }
        catch
        {
            return new WorkspaceMetadata();
        }
    }

    private async Task WriteMetadataAsync(string rootPath, WorkspaceMetadata metadata)
    {
        var directory = Path.Combine(rootPath, MetadataDirectoryName);
        Directory.CreateDirectory(directory);

        var metadataPath = GetMetadataPath(rootPath);
        await using var stream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(stream, metadata, _serializerOptions);
    }

    private static string GetMetadataPath(string rootPath)
    {
        return Path.Combine(rootPath, MetadataDirectoryName, MetadataFileName);
    }
}

internal sealed class WorkspaceItem
{
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Type { get; set; } = "file";
    public string? Content { get; set; }
    public string? ProjectType { get; set; }
    public NugetConfig? NugetConfig { get; set; }
    public List<WorkspaceItem>? Files { get; set; }
}

internal sealed class WorkspaceMetadata
{
    public int Version { get; set; } = 1;
    public Dictionary<string, WorkspaceFileMetadata> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class WorkspaceFileMetadata
{
    public string? ProjectType { get; set; }
    public NugetConfig? NugetConfig { get; set; }
}

internal sealed class NugetConfig
{
    public List<NugetPackage> Packages { get; set; } = new();
}

internal sealed class NugetPackage
{
    public string? Id { get; set; }
    public string? Version { get; set; }
}

internal readonly record struct WorkspaceLoadResult(bool Success, bool Cancelled, string? RootPath, IReadOnlyList<WorkspaceItem>? Files, string? Error)
{
    public static WorkspaceLoadResult CreateSuccess(string rootPath, IReadOnlyList<WorkspaceItem> files) => new(true, false, rootPath, files, null);
    public static WorkspaceLoadResult CreateCancelled() => new(false, true, null, null, null);
    public static WorkspaceLoadResult CreateFailure(string message) => new(false, false, null, null, message);
}

internal readonly record struct WorkspaceSaveResult(bool Success, string? Message, string? RootPath)
{
    public static WorkspaceSaveResult CreateSuccess(string? rootPath) => new(true, null, rootPath);
    public static WorkspaceSaveResult CreateFailure(string message) => new(false, message, null);
}
