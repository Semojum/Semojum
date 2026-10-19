using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;

public sealed class WorkFolderDialog : Window
{
    private readonly ListView entries = new();
    private readonly TextBlock location = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick };
    public string? CurrentFolder { get; private set; }
    public string? SelectedFile { get; private set; }

    public WorkFolderDialog(string? folder)
    {
        Title = "작업 폴더"; Width = 900; Height = 540; MinWidth = 620; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; header.Children.Add(actions);
        AddButton(actions, "다른 폴더 선택…", ChooseFolder);
        AddButton(actions, "상위 폴더", () => { if (CurrentFolder is not null && Directory.GetParent(CurrentFolder) is { } parent) Navigate(parent.FullName); });
        AddButton(actions, "새로 고침", () => { if (CurrentFolder is not null) Navigate(CurrentFolder); });
        header.Children.Add(location); header.Children.Add(error);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        AddButton(footer, "열기", OpenSelected);
        var cancel = new Button { Content = "닫기", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) }; footer.Children.Add(cancel);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var columns = new GridView(); entries.View = columns;
        foreach (var (label, property, width) in new[] { ("이름", "Name", 410d), ("수정 시각", "ModifiedLabel", 180d), ("크기", "SizeLabel", 120d) })
            columns.Columns.Add(new GridViewColumn { Header = label, DisplayMemberBinding = new Binding(property), Width = width });
        entries.MouseDoubleClick += (_, _) => OpenSelected();
        entries.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; OpenSelected(); } };
        root.Children.Add(entries);
        if (folder is not null) Navigate(folder);
        else location.Text = "작업 파일이 있는 폴더를 선택해 주세요.";
    }

    public void Navigate(string folder)
    {
        try
        {
            var listed = LocalWorkspace.ListFolder(folder);
            CurrentFolder = Path.GetFullPath(folder); entries.ItemsSource = listed; location.Text = CurrentFolder;
            error.Text = listed.Length == 0 ? "이 폴더에는 세모점 작업 파일이나 하위 폴더가 없습니다." : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { error.Text = "폴더를 읽지 못했습니다. 다른 폴더를 선택해 주세요.\n" + ex.Message; }
    }

    private void ChooseFolder()
    {
        var picker = new OpenFolderDialog { Title = "작업 폴더 선택", InitialDirectory = CurrentFolder ?? "" };
        if (picker.ShowDialog(this) == true) Navigate(picker.FolderName);
    }
    private void OpenSelected()
    {
        if (entries.SelectedItem is not WorkspaceEntry entry) return;
        if (entry.IsDirectory) Navigate(entry.Path);
        else if (!File.Exists(entry.Path)) { Navigate(CurrentFolder!); error.Text = "파일이 이동되거나 삭제되었습니다."; }
        else { SelectedFile = entry.Path; DialogResult = true; }
    }
    private static void AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
}
