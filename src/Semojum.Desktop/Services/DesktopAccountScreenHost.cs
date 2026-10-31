using System.Windows;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using Semojum.Feature.Account.Wpf;

namespace Semojum.Desktop.Services;

internal sealed class DesktopAccountScreenHost : IAccountScreenHost
{
    public Version Version => MainWindow.CurrentVersion;
    public void ShowNotices(Window owner, SemojumApiClient api) => new PublicNoticesDialog(api) { Owner = owner }.ShowDialog();
    public void ShowInquiry(Window owner, SemojumApiClient api) => new PublicInquiryDialog(api) { Owner = owner }.ShowDialog();
    public NoticeDialog LoginRejected(SemojumApiException error) => FileNotices.LoginRejected(error);
}
