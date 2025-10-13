using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SharpPad.Desktop.Services;

/// <summary>
/// Provides native file picker bridge for WebView on macOS and other platforms.
/// This bypasses WKWebView file input restrictions by using native Avalonia file dialogs.
/// </summary>
public class FilePickerBridge
{
    private readonly Window _window;

    public FilePickerBridge(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>
    /// Opens a native file picker dialog for JSON files and returns the file content.
    /// </summary>
    /// <returns>JSON string with file content or error message</returns>
    public async Task<string> PickJsonFileAsync()
    {
        try
        {
            var storageProvider = _window.StorageProvider;
            if (storageProvider == null)
            {
                return JsonSerializer.Serialize(new { success = false, error = "Storage provider not available" });
            }

            var fileTypes = new List<FilePickerFileType>
            {
                new("JSON Files") { Patterns = new[] { "*.json" } },
                new("All Files") { Patterns = new[] { "*.*" } }
            };

            var options = new FilePickerOpenOptions
            {
                Title = "Select JSON File to Import",
                AllowMultiple = false,
                FileTypeFilter = fileTypes
            };

            var files = await storageProvider.OpenFilePickerAsync(options);

            if (files == null || files.Count == 0)
            {
                return JsonSerializer.Serialize(new { success = false, error = "No file selected" });
            }

            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();

            return JsonSerializer.Serialize(new
            {
                success = true,
                content = content,
                fileName = file.Name
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Opens a native save file dialog and saves the provided content.
    /// </summary>
    /// <param name="content">The content to save</param>
    /// <param name="defaultFileName">Default file name</param>
    /// <returns>JSON string with success status</returns>
    public async Task<string> SaveJsonFileAsync(string content, string defaultFileName)
    {
        try
        {
            var storageProvider = _window.StorageProvider;
            if (storageProvider == null)
            {
                return JsonSerializer.Serialize(new { success = false, error = "Storage provider not available" });
            }

            var fileTypes = new List<FilePickerFileType>
            {
                new("JSON Files") { Patterns = new[] { "*.json" } }
            };

            var options = new FilePickerSaveOptions
            {
                Title = "Save JSON File",
                SuggestedFileName = defaultFileName,
                FileTypeChoices = fileTypes,
                ShowOverwritePrompt = true
            };

            var file = await storageProvider.SaveFilePickerAsync(options);

            if (file == null)
            {
                return JsonSerializer.Serialize(new { success = false, error = "No file selected" });
            }

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content);

            return JsonSerializer.Serialize(new
            {
                success = true,
                fileName = file.Name
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            });
        }
    }
}
