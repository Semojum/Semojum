using System.Windows;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Feature.Account.Wpf;

/// <summary>Operations supplied by the application shell to the account screen.</summary>
public interface IAccountScreenHost
{
    Version Version { get; }
    void ShowNotices(Window owner, SemojumApiClient api);
    void ShowInquiry(Window owner, SemojumApiClient api);
    NoticeDialog LoginRejected(SemojumApiException error);
}

public static class AccountScreenServices
{
    private static IAccountScreenHost? host;
    public static IAccountScreenHost Host => host ?? throw new InvalidOperationException("The account screen host has not been configured.");
    public static void Configure(IAccountScreenHost screenHost) => host = screenHost ?? throw new ArgumentNullException(nameof(screenHost));
}
