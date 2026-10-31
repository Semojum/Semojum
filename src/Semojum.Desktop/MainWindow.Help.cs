using Semojum.Feature.Shell.Wpf;

namespace Semojum.Desktop;

public partial class MainWindow : IHelpCommandSource
{
    Task IHelpCommandSource.ExecuteHelpCommandAsync(string command) => ExecuteAsync(command);
}
