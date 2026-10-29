using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
    internal Controls.OriginalUploadView OriginalUpload { get; private set; } = null!;
    private void InitializeOriginalUpload()
    {
        OriginalUpload = new Controls.OriginalUploadView { Visibility = Visibility.Collapsed };
        Grid.SetRow(OriginalUpload, 1);
        ((Grid)OriginalPanel.Child).Children.Add(OriginalUpload);
        OriginalUpload.UploadRequested += async paths =>
        {
            try
            {
                if (fileBusy || uploadFlowBusy) return;
                var selected = paths is { Length: > 0 } ? paths : FilePicker is null ? OpenDocumentDialog.SelectPaths(this) : FilePicker(this);
                if (selected is not { Length: > 0 }) return;
                if (!(selected.Length == 1 && Path.GetExtension(selected[0]).Equals(".smj", StringComparison.OrdinalIgnoreCase)))
                {
                    long total = 0;
                    foreach (var path in selected)
                    {
                        var length = new FileInfo(path).Length;
                        total = length > long.MaxValue - total ? long.MaxValue : total + length;
                    }
                    if (total > JobClient.MaximumUploadBytes) { OriginalUpload.ShowLimitWarning(selected, total); return; }
                }
                OriginalUpload.ClearLimitWarning();
                await UploadAsync(selected);
            }
            catch (Exception ex) { ShowError(ex); }
        };
    }
    private readonly LocalWorkspace workspace = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Semojum", "preferences.json"));
    private EditorPreferences preferences = new();
    private AccountPreferences accountPreferences = new();
    private readonly AccountPreferenceStore profiles = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Semojum", "accounts"));

    private void LoadAccountPreferences()
    {
        if (smoke || api.Account is null) return;
        try { accountPreferences = profiles.Load(api.Account.LoginId); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or ArgumentException) { accountPreferences = new(); session.Status = "계정 설정을 읽지 못했습니다: " + ex.Message; }
        sixDotKeys = accountPreferences.SixDotKeys; Braille.ConfigureKeys(sixDotKeys);
        Braille.SpaceEnabled = accountPreferences.SpaceEnabled; Braille.EnterEnabled = accountPreferences.EnterEnabled; CopyBrailleSettings(); ApplyAppearance(accountPreferences.Appearance ?? new());
    }
    private Task PersistAccountPreferencesAsync() => smoke || api.Account is null ? Task.CompletedTask : profiles.SaveAsync(api.Account.LoginId, accountPreferences);

    private void RestorePreferences()
    {
        if (smoke) return;
        preferences = workspace.Load();
        AutoUpdateToggle.IsChecked = preferences.AutoCheckUpdates;
        sixDotKeys = preferences.SixDotKeys; Braille.ConfigureKeys(sixDotKeys);
        Braille.SpaceEnabled = preferences.SpaceEnabled; Braille.EnterEnabled = preferences.EnterEnabled;
        CopyBrailleSettings();
        if (workspace.LoadWarning is { } warning) session.Status = warning;
    }

    private async Task PersistPreferencesAsync()
    {
        if (smoke) return;
        try { await workspace.SaveAsync(preferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { session.Status = "편집 내용은 유지됩니다. 최근 파일·입력 설정을 기록하지 못했습니다: " + ex.Message; }
    }

    private async Task RememberFileAsync(string path)
    {
        preferences = LocalWorkspace.Remember(preferences, path);
        await PersistPreferencesAsync();
    }

    private async void RecentFiles_Opened(object sender, RoutedEventArgs e)
    {
        if (sender != e.Source) return;
        RecentFilesMenu.Items.Clear();
        var missing = false;
        foreach (var path in (preferences.RecentFiles ?? []).Take(appearance.RecentCount))
        {
            if (!File.Exists(path)) { preferences = LocalWorkspace.Forget(preferences, path); missing = true; continue; }
            var file = new FileInfo(path);
            var item = new MenuItem { Header = $"{file.Name}    {file.LastWriteTime:yyyy-MM-dd HH:mm}", ToolTip = path };
            item.Click += async (_, _) =>
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        preferences = LocalWorkspace.Forget(preferences, path); await PersistPreferencesAsync();
                        MessageBox.Show(this, "파일이 이동되거나 삭제되어 최근 목록에서 제외했습니다.", "최근 문서"); return;
                    }
                    await OpenAsync(path);
                }
                catch (Exception ex) { ShowError(ex); }
            };
            RecentFilesMenu.Items.Add(item);
        }
        if (RecentFilesMenu.Items.Count == 0) RecentFilesMenu.Items.Add(new MenuItem { Header = "최근 작업 파일이 없습니다", IsEnabled = false });
        if (missing) await PersistPreferencesAsync();
    }

    private async Task OpenWorkFolderAsync(bool choose)
    {
        if (fileBusy) return;
        var folder = preferences.WorkFolder;
        if (choose || folder is null)
        {
            var picker = new OpenFolderDialog { Title = "작업 폴더 선택", InitialDirectory = folder ?? "" };
            if (picker.ShowDialog(this) != true) return;
            folder = picker.FolderName;
        }
        var browser = new WorkFolderDialog(folder) { Owner = this };
        var open = browser.ShowDialog() == true;
        if (browser.CurrentFolder is { } current)
        {
            preferences = preferences with { WorkFolder = current }; await PersistPreferencesAsync();
        }
        if (open && browser.SelectedFile is { } path) await OpenAsync(path);
    }
}
