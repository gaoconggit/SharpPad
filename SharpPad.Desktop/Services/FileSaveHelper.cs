using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SharpPad.Desktop.Services;

internal static class FileSaveHelper
{
    public static async Task<FileSaveResult> SaveTextAsync(Window owner, string? suggestedFileName, string content, string? mimeType)
    {
        if (owner is null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        var storageProvider = owner.StorageProvider;
        if (storageProvider is null)
        {
            return FileSaveResult.CreateFailure("当前窗口未提供存储服务。");
        }

        var fileName = NormalizeFileName(suggestedFileName, mimeType);
        var extension = Path.GetExtension(fileName);
        var defaultExtension = extension.TrimStart('.');
        var fileTypes = CreateFileTypes(extension);

        var options = new FilePickerSaveOptions
        {
            Title = "保存导出的文件",
            SuggestedFileName = fileName,
            FileTypeChoices = fileTypes
        };

        if (!string.IsNullOrEmpty(defaultExtension))
        {
            options.DefaultExtension = defaultExtension;
        }

        IStorageFile? targetFile;
        try
        {
            targetFile = await storageProvider.SaveFilePickerAsync(options);
        }
        catch (Exception ex)
        {
            return FileSaveResult.CreateFailure(ex.Message);
        }

        if (targetFile is null)
        {
            return FileSaveResult.CreateCancelled();
        }

        try
        {
            await using var stream = await targetFile.OpenWriteAsync();
            if (stream.CanSeek)
            {
                stream.SetLength(0);
            }

            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteAsync(content);
            await writer.FlushAsync();
        }
        catch (Exception ex)
        {
            return FileSaveResult.CreateFailure(ex.Message);
        }

        var savedPath = targetFile.Path?.LocalPath;
        return FileSaveResult.CreateSuccess(targetFile.Name, savedPath);
    }

    private static string NormalizeFileName(string? fileName, string? mimeType)
    {
        var trimmed = (fileName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            trimmed = "export.json";
        }

        var extension = Path.GetExtension(trimmed);
        if (!string.IsNullOrEmpty(extension))
        {
            return trimmed;
        }

        var inferredExtension = GuessExtensionFromMime(mimeType);
        if (!trimmed.EndsWith(inferredExtension, StringComparison.OrdinalIgnoreCase))
        {
            trimmed += inferredExtension;
        }

        return trimmed;
    }

    private static string GuessExtensionFromMime(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return ".json";
        }

        return mimeType.ToLowerInvariant() switch
        {
            "application/json" => ".json",
            "text/plain" => ".txt",
            _ => ".json"
        };
    }

    private static IReadOnlyList<FilePickerFileType>? CreateFileTypes(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        var description = $"{normalized.TrimStart('.').ToUpperInvariant()} 文件";

        return new[]
        {
            new FilePickerFileType(description)
            {
                Patterns = new[] { "*" + normalized }
            }
        };
    }
}

internal readonly record struct FileSaveResult(bool Success, bool Cancelled, string? FileName, string? FilePath, string? Error)
{
    public static FileSaveResult CreateSuccess(string? fileName, string? filePath) => new(true, false, fileName, filePath, null);
    public static FileSaveResult CreateCancelled() => new(false, true, null, null, null);
    public static FileSaveResult CreateFailure(string? error) => new(false, false, null, null, error);
}
