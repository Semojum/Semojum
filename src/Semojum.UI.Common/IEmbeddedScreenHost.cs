using System.Windows;

namespace Semojum.UI.Common;

/// <summary>Hosts a full-window feature screen while preserving the application's native window.</summary>
public interface IEmbeddedScreenHost
{
    Window Window { get; }
    Task<T> ShowScreenAsync<T>(FrameworkElement screen, string title, Task<T> completion);
}
