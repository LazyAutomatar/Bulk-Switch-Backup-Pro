using System;
using Avalonia.Controls;

namespace BulkBackup.Desktop.Services;

public interface INavigationService
{
    bool Navigate(Type pageType);
    bool Navigate<T>() where T : Control;
    event Action<Type>? Navigated;
}

public class NavigationService : INavigationService
{
    public event Action<Type>? Navigated;

    public bool Navigate(Type pageType)
    {
        if (pageType == null) return false;
        Navigated?.Invoke(pageType);
        return true;
    }

    public bool Navigate<T>() where T : Control
    {
        return Navigate(typeof(T));
    }
}
