using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Navigation;

namespace Semojum.Desktop.Dialogs;

public enum LegalDocumentKind { Terms, Privacy }

/// <summary>Offline, selectable Notion snapshots in Figma's login legal-document modal.</summary>
public sealed class LegalDocumentDialog : CompactDialog
{
    public LegalDocumentKind Kind { get; }
    public FlowDocumentScrollViewer Reader { get; }
    public LegalDocumentDialog(LegalDocumentKind kind)
        : base(kind == LegalDocumentKind.Terms ? "이용약관" : "개인정보 처리방침", 560, 536, popover: true)
    {
        Kind = kind;
        var layout = PublicDialogDesign.Frame(this, Title, "login-legal-close.svg", "LegalDocumentClose", out _, "Semojum.Feature.Account.Wpf");
        // The supplied modal has a close icon and no action/footer divider.
        layout.RowDefinitions[2].Height = new(24);
        foreach (var footer in layout.Children.OfType<Border>().Where(b => Grid.GetRow(b) == 2)) footer.Visibility = Visibility.Collapsed;
        Root.Children.Clear(); Root.Children.Add(layout);
        var name = kind == LegalDocumentKind.Terms ? "Terms" : "Privacy";
        var uri = new Uri($"pack://application:,,,/Semojum.Feature.Account.Wpf;component/Legal/{name}.xaml");
        using var stream = Application.GetResourceStream(uri)?.Stream ?? throw new InvalidOperationException("약관 문서를 읽을 수 없습니다.");
        var document = (FlowDocument)XamlReader.Load(stream);
        document.FontFamily = (FontFamily)FindResource("UiFont");
        Reader = new FlowDocumentScrollViewer
        {
            Document = document, IsToolBarVisible = false, Background = Brushes.White,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new(24, 18, 24, 16), Focusable = true
        };
        AutomationProperties.SetAutomationId(Reader, "LegalDocumentReader");
        AutomationProperties.SetName(Reader, Title + " 본문");
        Reader.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler((_, e) =>
        {
            if (e.Uri.IsAbsoluteUri && e.Uri.Scheme is "https" or "http" or "mailto")
            {
                try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                { Reader.ToolTip = "링크를 열지 못했습니다. 주소를 복사해 이용해 주세요."; }
            }
            e.Handled = true;
        }));
        Grid.SetRow(Reader, 1); layout.Children.Add(Reader);
        KeyboardNavigation.SetTabNavigation(layout, KeyboardNavigationMode.Cycle);
        Loaded += (_, _) => Reader.Focus();
    }
}