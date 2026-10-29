using System.IO;
using System.Windows;
using System.Windows.Controls;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsMemoRestoreChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true); window.Show();
        var path = Path.Combine(directory, "memo-restoration-" + Guid.NewGuid() + ".smj");
        try
        {
            var block = window.Session.ActiveBlock!; block.ServerId = "memo-fixture";
            var at = new DateTimeOffset(2026, 10, 1, 12, 30, 0, TimeSpan.FromHours(9));
            var memo = new LineMemo("restore-fixture", 42, "TEXT", 0, "원문", "원 메모", at,
                Resolved: true, Author: "원 작성자", ResolvedAt: at.AddHours(1), Replies: [
                    new("첫 답글 작성자", "첫 답글", at.AddMinutes(1)), new(null, "이전 답글", at.AddMinutes(2))]);
            block.Memos.Add(memo);
            var dialog = new MemoThreadDialog(block, "TEXT", 0, "원문", window.EditStructure, window.Session, showResolved: true) { Owner = window };
            dialog.Show(); dialog.ResolveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var restored = block.Memos.Single(m => m.LocalId == memo.LocalId);
            check(dialog.IsVisible && !restored.Resolved && restored.ResolvedAt is null && restored.Author == memo.Author
                && restored.Replies!.SequenceEqual(memo.Replies!), "Reopening a resolved memo retains the original author and replies");
            dialog.Close();
            window.ReplayHistory(); var undone = block.Memos.Single(m => m.LocalId == memo.LocalId);
            check(undone.Resolved && undone.ResolvedAt == memo.ResolvedAt && undone.Replies!.SequenceEqual(memo.Replies!),
                "Undo of reopening restores resolution time and replies");
            window.ReplayHistory(true); var redone = block.Memos.Single(m => m.LocalId == memo.LocalId);
            check(!redone.Resolved && redone.ResolvedAt is null && redone.Replies!.SequenceEqual(memo.Replies!),
                "Redo reopens the same memo without altering replies");
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(window.Session));
            var loaded = await SmjFile.LoadAsync(path);
            var saved = loaded.Blocks.SelectMany(b => b.Memos).Single(m => m.LocalId == memo.LocalId);
            check(!saved.Resolved && saved.ResolvedAt is null && saved.ServerId == 42 && saved.Replies!.SequenceEqual(memo.Replies!),
                "Saved and reopened memo retains its identity and replies after restoration");
            var sent = JobClient.PlanSave(SmjFile.Snapshot(loaded).Manifest).Pages.SelectMany(p => p.Memos ?? [])
                .Single(m => m.MemoId == 42);
            check(!sent.Resolved && sent.ResolvedAt is null && sent.Replies is [{ Author: "첫 답글 작성자", Body: "첫 답글" }, { Author: null, Body: "이전 답글" }],
                "Server save snapshot sends the restored state and unchanged reply authors");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); if (File.Exists(path)) File.Delete(path); }
    }
}
