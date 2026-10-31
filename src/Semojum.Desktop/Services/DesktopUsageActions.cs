using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using Semojum.Feature.Board;
using Semojum.Feature.Usage.Wpf;

namespace Semojum.Desktop.Services;

/// <summary>Connects usage-screen navigation to the desktop's actual inquiry and board windows.</summary>
internal static class DesktopUsageActions
{
    internal static UsageActions Create(SemojumApiClient api, string workInfo)
    {
        var board = new BoardClient(api);
        return new(
            owner => { new PublicInquiryDialog(api) { Owner = owner }.ShowDialog(); },
            owner => new PostEditorDialog(board, null, workInfo) { Owner = owner }.ShowDialog() == true,
            (owner, post) => { new PostDialog(board, post, workInfo) { Owner = owner }.ShowDialog(); },
            owner => { new BoardDialog(board, workInfo, initiallyMine: true) { Owner = owner }.ShowDialog(); });
    }
}
