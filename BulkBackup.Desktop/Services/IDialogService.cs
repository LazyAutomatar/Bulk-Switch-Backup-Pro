using System.Collections.Generic;
using System.Threading.Tasks;

namespace BulkBackup.Desktop.Services;

public interface IDialogService
{
    Task<string?> ShowOpenFolderDialogAsync(string title);
    Task<string?> ShowOpenFileDialogAsync(string title, string filterName, params string[] extensions);
    Task<IReadOnlyList<string>> ShowOpenFilesDialogAsync(string title, string filterName, params string[] extensions);
    Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string extension, string filterName);
    Task ShowMessageAsync(string title, string message);
    Task<bool> ShowConfirmationAsync(string title, string message);
    Task SetClipboardTextAsync(string text);
}
