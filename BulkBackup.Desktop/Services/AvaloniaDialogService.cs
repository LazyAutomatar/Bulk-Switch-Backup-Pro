using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;

namespace BulkBackup.Desktop.Services;

public class AvaloniaDialogService : IDialogService
{
    private TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    public async Task<string?> ShowOpenFolderDialogAsync(string title)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider == null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> ShowOpenFileDialogAsync(string title, string filterName, params string[] extensions)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider == null) return null;

        var patterns = extensions.Select(e => $"*.{e.TrimStart('.')}").ToArray();
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(filterName)
                {
                    Patterns = patterns
                },
                FilePickerFileTypes.All
            }
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<IReadOnlyList<string>> ShowOpenFilesDialogAsync(string title, string filterName, params string[] extensions)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider == null) return Array.Empty<string>();

        var patterns = extensions.Select(e => $"*.{e.TrimStart('.')}").ToArray();
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(filterName)
                {
                    Patterns = patterns
                },
                FilePickerFileTypes.All
            }
        });

        return files.Select(f => f.Path.LocalPath).ToList();
    }

    public async Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string extension, string filterName)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider == null) return null;

        var ext = extension.TrimStart('.');
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            DefaultExtension = ext,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(filterName)
                {
                    Patterns = new[] { $"*.{ext}" }
                },
                FilePickerFileTypes.All
            }
        });

        return file?.Path.LocalPath;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Yes",
            CloseButtonText = "No"
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public async Task SetClipboardTextAsync(string text)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.Clipboard != null)
        {
            await topLevel.Clipboard.SetTextAsync(text);
        }
    }
}
