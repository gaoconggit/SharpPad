using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace SharpPad.Controllers;

[ApiController]
[Route("api/filesystem")]
public class FileSystemController : ControllerBase
{
    private static readonly string[] IgnoredDirectories = { ".git", "bin", "obj", ".vs", "node_modules" };
    private const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB

    [HttpPost("open-folder")]
    public IActionResult OpenFolder([FromBody] OpenFolderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Path))
        {
            return BadRequest(new { message = "路径不能为空" });
        }

        var normalizedRoot = NormalizePath(request.Path);
        if (!Directory.Exists(normalizedRoot))
        {
            return NotFound(new { message = "文件夹不存在" });
        }

        try
        {
            var items = LoadWorkspaceItems(normalizedRoot, normalizedRoot);
            return Ok(new
            {
                success = true,
                rootPath = normalizedRoot,
                files = items
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"读取文件夹失败: {ex.Message}" });
        }
    }

    [HttpPost("sync")]
    public IActionResult SyncWorkspace([FromBody] SyncWorkspaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.RootPath))
        {
            return BadRequest(new { message = "缺少根目录路径" });
        }

        if (request.Files is null)
        {
            return BadRequest(new { message = "缺少文件列表" });
        }

        var normalizedRoot = NormalizePath(request.RootPath);
        try
        {
            if (!Directory.Exists(normalizedRoot))
            {
                Directory.CreateDirectory(normalizedRoot);
            }

            var touchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                normalizedRoot
            };

            WriteWorkspaceItems(normalizedRoot, request.Files, touchedPaths);

            if (request.PruneExtra)
            {
                PruneExtraEntries(normalizedRoot, touchedPaths);
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"同步文件失败: {ex.Message}" });
        }
    }

    private static List<WorkspaceItemDto> LoadWorkspaceItems(string currentPath, string rootPath)
    {
        var result = new List<WorkspaceItemDto>();
        var directoryInfo = new DirectoryInfo(currentPath);

        foreach (var dir in directoryInfo.EnumerateDirectories())
        {
            if (ShouldIgnoreDirectory(dir.Name))
            {
                continue;
            }

            var child = new WorkspaceItemDto
            {
                Id = CreateDeterministicId(Path.GetRelativePath(rootPath, dir.FullName)),
                Name = dir.Name,
                Type = "folder",
                Path = NormalizeRelativePath(Path.GetRelativePath(rootPath, dir.FullName)),
                Files = LoadWorkspaceItems(dir.FullName, rootPath)
            };
            result.Add(child);
        }

        foreach (var file in directoryInfo.EnumerateFiles())
        {
            if (file.Length > MaxFileSizeBytes)
            {
                continue;
            }

            string content;
            try
            {
                content = System.IO.File.ReadAllText(file.FullName);
            }
            catch
            {
                continue; // Skip unreadable files
            }

            var relativePath = Path.GetRelativePath(rootPath, file.FullName);
            result.Add(new WorkspaceItemDto
            {
                Id = CreateDeterministicId(relativePath),
                Name = file.Name,
                Type = "file",
                Path = NormalizeRelativePath(relativePath),
                Content = content,
                ProjectType = "console",
                NugetConfig = new NugetConfigDto
                {
                    Packages = new List<NugetPackageDto>()
                }
            });
        }

        return result;
    }

    private static void WriteWorkspaceItems(string rootPath, IEnumerable<WorkspaceItemDto> items, HashSet<string> touchedPaths)
    {
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            var relativePath = !string.IsNullOrWhiteSpace(item.Path)
                ? item.Path
                : item.Name;

            var targetPath = NormalizePath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!targetPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"非法路径: {targetPath}");
            }

            if (string.Equals(item.Type, "folder", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(targetPath);
                touchedPaths.Add(targetPath);
                WriteWorkspaceItems(targetPath, item.Files ?? Enumerable.Empty<WorkspaceItemDto>(), touchedPaths);
            }
            else
            {
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    touchedPaths.Add(directory);
                }

                var content = item.Content ?? string.Empty;
                System.IO.File.WriteAllText(targetPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                touchedPaths.Add(targetPath);
            }
        }
    }

    private static void PruneExtraEntries(string rootPath, HashSet<string> touchedPaths)
    {
        var normalizedTouched = new HashSet<string>(touchedPaths.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            if (ShouldIgnoreDirectory(new DirectoryInfo(Path.GetDirectoryName(file) ?? string.Empty).Name))
            {
                continue;
            }

            var normalizedFile = NormalizePath(file);
            if (!normalizedTouched.Contains(normalizedFile))
            {
                System.IO.File.Delete(file);
            }
        }

        var directories = Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length)
            .ToList();

        foreach (var dir in directories)
        {
            if (ShouldIgnoreDirectory(new DirectoryInfo(dir).Name))
            {
                continue;
            }

            var normalizedDir = NormalizePath(dir);
            if (!normalizedTouched.Contains(normalizedDir) && IsDirectoryEmpty(dir))
            {
                Directory.Delete(dir, recursive: false);
            }
        }
    }

    private static bool IsDirectoryEmpty(string path)
    {
        return !Directory.EnumerateFileSystemEntries(path).Any();
    }

    private static bool ShouldIgnoreDirectory(string name)
    {
        return IgnoredDirectories.Any(ignored =>
            string.Equals(ignored, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.TrimStart('/');
    }

    private static string CreateDeterministicId(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath ?? Guid.NewGuid().ToString("N"));
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

public sealed class OpenFolderRequest
{
    public string? Path { get; set; }
}

public sealed class SyncWorkspaceRequest
{
    public string? RootPath { get; set; }
    public List<WorkspaceItemDto>? Files { get; set; }
    public bool PruneExtra { get; set; } = true;
}

public sealed class WorkspaceItemDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Path { get; set; }
    public string? Content { get; set; }
    public List<WorkspaceItemDto>? Files { get; set; }
    public string? ProjectType { get; set; }
    public NugetConfigDto? NugetConfig { get; set; }
}

public sealed class NugetConfigDto
{
    public List<NugetPackageDto>? Packages { get; set; }
}

public sealed class NugetPackageDto
{
    public string? Id { get; set; }
    public string? Version { get; set; }
}
