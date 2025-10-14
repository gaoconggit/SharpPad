using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SharpPad.Desktop.Services;

/// <summary>
/// Native file picker bridge for macOS/Windows/Linux desktop app.
/// Provides safe file operations that work around WKWebView restrictions.
/// </summary>
public class FilePickerBridge
{
    private readonly Window _window;

    public FilePickerBridge(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>
    /// Opens native file picker to select a JSON file for import.
    /// Returns the file content as JSON string, or null if cancelled.
    /// </summary>
    public async Task<string?> PickJsonFileAsync()
    {
        try
        {
            var storageProvider = _window.StorageProvider;
            if (storageProvider == null)
                return null;

            var options = new FilePickerOpenOptions
            {
                Title = "选择要导入的JSON文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON Files")
                    {
                        Patterns = new[] { "*.json" }
                    },
                    FilePickerFileTypes.All
                }
            };

            var files = await storageProvider.OpenFilePickerAsync(options);

            if (files == null || files.Count == 0)
                return null;

            var file = files[0];

            // Read file content
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = await reader.ReadToEndAsync();

            return content;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error picking JSON file: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Opens native save file dialog to export JSON data.
    /// Returns true if file was saved successfully, false otherwise.
    /// </summary>
    public async Task<bool> SaveJsonFileAsync(string fileName, string jsonContent)
    {
        try
        {
            var storageProvider = _window.StorageProvider;
            if (storageProvider == null)
                return false;

            var options = new FilePickerSaveOptions
            {
                Title = "导出文件夹",
                SuggestedFileName = fileName,
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON Files")
                    {
                        Patterns = new[] { "*.json" }
                    }
                },
                ShowOverwritePrompt = true
            };

            var file = await storageProvider.SaveFilePickerAsync(options);

            if (file == null)
                return false;

            // Write file content
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(jsonContent);

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error saving JSON file: {ex.Message}");
            return false;
        }
    }
}
