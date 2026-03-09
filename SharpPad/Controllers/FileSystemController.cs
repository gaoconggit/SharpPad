using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SharpPad.Controllers;

[ApiController]
[Route("api/fs")]
public class FileSystemController : ControllerBase
{
    private static string _workspaceRoot = GetDefaultWorkspaceRoot();

    private static string GetDefaultWorkspaceRoot()
    {
        var root = Environment.GetEnvironmentVariable("SHARPPAD_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            return Path.GetFullPath(root);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultDir = Path.Combine(home, "SharpPadProjects");
        Directory.CreateDirectory(defaultDir);
        return defaultDir;
    }

    private string ResolvePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return _workspaceRoot;

        var full = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath));
        if (!full.StartsWith(_workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal detected.");
        return full;
    }

    /// <summary>
    /// Get current workspace root path
    /// </summary>
    [HttpGet("workspace")]
    public IActionResult GetWorkspace()
    {
        return Ok(new { path = _workspaceRoot });
    }

    /// <summary>
    /// Set workspace root path (open folder)
    /// </summary>
    [HttpPost("workspace")]
    public IActionResult SetWorkspace([FromBody] SetWorkspaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return BadRequest(new { message = "路径不能为空" });

        var fullPath = Path.GetFullPath(request.Path);
        if (!Directory.Exists(fullPath))
        {
            try
            {
                Directory.CreateDirectory(fullPath);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"无法创建目录: {ex.Message}" });
            }
        }

        _workspaceRoot = fullPath;
        return Ok(new { path = _workspaceRoot });
    }

    /// <summary>
    /// Get file tree of current workspace
    /// </summary>
    [HttpGet("tree")]
    public IActionResult GetTree([FromQuery] string? path)
    {
        try
        {
            var dirPath = ResolvePath(path);
            if (!Directory.Exists(dirPath))
                return NotFound(new { message = "目录不存在" });

            var tree = BuildTree(dirPath, _workspaceRoot);
            return Ok(tree);
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new { message = "非法路径" });
        }
    }

    /// <summary>
    /// Read file content
    /// </summary>
    [HttpGet("file")]
    public IActionResult ReadFile([FromQuery] string path)
    {
        try
        {
            var fullPath = ResolvePath(path);
            if (!System.IO.File.Exists(fullPath))
                return NotFound(new { message = "文件不存在" });

            var content = System.IO.File.ReadAllText(fullPath);
            return Ok(new { path, content });
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new { message = "非法路径" });
        }
    }

    /// <summary>
    /// Save file content
    /// </summary>
    [HttpPut("file")]
    public IActionResult SaveFile([FromBody] SaveFileRequest request)
    {
        try
        {
            var fullPath = ResolvePath(request.Path);
            var dir = Path.GetDirectoryName(fullPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            System.IO.File.WriteAllText(fullPath, request.Content ?? "");
            return Ok(new { path = request.Path });
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new { message = "非法路径" });
        }
    }

    /// <summary>
    /// Create new file
    /// </summary>
    [HttpPost("file")]
    public IActionResult CreateFile([FromBody] CreateItemRequest request)
    {
        try
        {
            var fullPath = ResolvePath(request.Path);
            if (System.IO.File.Exists(fullPath))
                return Conflict(new { message = "文件已存在" });

            var dir = Path.GetDirectoryName(fullPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            System.IO.File.WriteAllText(fullPath, request.Content ?? "");
            return Ok(new { path = request.Path });
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new { message = "非法路径" });
        }
    }

    /// <summary>
    /// Create new folder
    /// </summary>
    [HttpPost("folder")]
    public IActionResult CreateFolder([FromBody] CreateItemRequest request)
    {
        try
        {
            var fullPath = ResolvePath(request.Path);
            if (Directory.Exists(fullPath))
                return Conflict(new { message = "文件夹已存在" });

            Directory.CreateDirectory(fullPath);
            return Ok(new { path = request.Path });
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new { message = "非法路径" });
        }
    }

    /// <summary>
    /// Delete file or folder
    /// </summary>
    [HttpDelete("item")]
    public IActionResult DeleteItem([FromQuery] string path)
    {
        try
        {
            var fullPath = ResolvePath(path);

            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
                return Ok(new { message = "已删除" });
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
                return Ok(new { message = "已删除" });
            }

            return NotFound(new { message = "路径不存在" });
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new { message = "非法路径" });
        }
    }

    /// <summary>
    /// Rename file or folder
    /// </summary>
    [HttpPost("rename")]
    public IActionResult Rename([FromBody] RenameRequest request)
    {
        try
        {
            var oldPath = ResolvePath(request.OldPath);
            var newPath = ResolvePath(request.NewPath);

            if (System.IO.File.Exists(oldPath))
            {
                var dir = Path.GetDirectoryName(newPath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                System.IO.File.Move(oldPath, newPath);
                return Ok(new { path = request.NewPath });
            }

            if (Directory.Exists(oldPath))
            {
                Directory.Move(oldPath, newPath);
                return Ok(new { path = request.NewPath });
            }

            return NotFound(new { message = "路径不存在" });
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest(new { message = "非法路径" });
        }
    }

    private static List<FileTreeNode> BuildTree(string dirPath, string rootPath)
    {
        var result = new List<FileTreeNode>();

        try
        {
            var dirs = Directory.GetDirectories(dirPath)
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                // Skip hidden folders
                if (name.StartsWith('.'))
                    continue;

                var relativePath = Path.GetRelativePath(rootPath, dir).Replace('\\', '/');
                result.Add(new FileTreeNode
                {
                    Name = name,
                    Path = relativePath,
                    Type = "folder",
                    Children = BuildTree(dir, rootPath)
                });
            }

            var files = Directory.GetFiles(dirPath)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith('.'))
                    continue;

                var relativePath = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
                result.Add(new FileTreeNode
                {
                    Name = name,
                    Path = relativePath,
                    Type = "file"
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }

        return result;
    }
}

public class SetWorkspaceRequest
{
    public string Path { get; set; } = "";
}

public class SaveFileRequest
{
    public string Path { get; set; } = "";
    public string? Content { get; set; }
}

public class CreateItemRequest
{
    public string Path { get; set; } = "";
    public string? Content { get; set; }
}

public class RenameRequest
{
    public string OldPath { get; set; } = "";
    public string NewPath { get; set; } = "";
}

public class FileTreeNode
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Type { get; set; } = "file";
    public List<FileTreeNode>? Children { get; set; }
}
