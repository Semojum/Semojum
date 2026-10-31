using System.Runtime.CompilerServices;
using System.Windows;
using Semojum.Desktop.Dialogs;
using Semojum.Feature.Account.Wpf;

namespace Semojum.Desktop.Services;

/// <summary>Connects common dialog behavior to the desktop application's actual windows.</summary>
internal static class DesktopWindowServices
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        CommonWindowServices.Configure(UsesBackdrop, (owner, title) => new HelpWindow(title) { Owner = owner }.Show());
        AccountScreenServices.Configure(new DesktopAccountScreenHost());
    }

    private static bool UsesBackdrop(Window dialog) => dialog is NoticeDialog or OpenDocumentDialog or TranslationOptionsDialog or SaveAsDialog or ExportRangeDialog or
        GoToDialog or FindDialog or DefaultsDialog or LayoutDialog or SixDotDialog or FormDialog or FormEditDialog or DeleteFormDialog or
        HeaderInputDialog or FooterInputDialog or FooterDialog or VisualDescriptionDialog or MathDialog or AppliedRulesDialog or RuleDialog or
        NotificationsDialog or AboutDialog or PublicNoticesDialog or PublicInquiryDialog;
}
