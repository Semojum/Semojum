namespace Semojum.Feature.Shell.Wpf;

/// <summary>Optional editor actions exposed to the help screen by its owner.</summary>
public interface IHelpCommandSource
{
    Task ExecuteHelpCommandAsync(string command);
}
