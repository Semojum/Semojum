using System.Windows;
using System.Windows.Controls;

namespace Semojum.Desktop.Dialogs;

public sealed class AboutDialog : CompactDialog
{
    public TextBlock EngineVersion { get; } = new() { Text = "확인 중…" };
    public AboutDialog(string version, Func<Task<string>> engineVersion) : base("누리닷 정보", 440, 278)
    {
        var body = new StackPanel { Margin = new(16, 7, 16, 0) }; Body(body);
        void Info(string label, FrameworkElement value)
        {
            var row = new DockPanel { Height = 33 };
            row.Children.Add(new TextBlock { Text = label, Width = 114, FontSize = 14, Height = 20, Margin = new(0, 9, 0, 4) });
            value.Height = 20; value.Margin = new(0, 9, 0, 4); row.Children.Add(value);
            body.Children.Add(new Border { BorderBrush = (System.Windows.Media.Brush)FindResource("Line"), BorderThickness = new(0, 0, 0, 1), Child = row });
        }
        Info("버전", new TextBlock { Text = "V" + version.TrimStart('V', 'v'), FontSize = 14, FontWeight = FontWeights.SemiBold });
        var engine = new StackPanel { Orientation = Orientation.Horizontal };
        engine.Children.Add(new TextBlock { Text = "Semojum V", FontSize = 14 }); EngineVersion.FontSize = 14; engine.Children.Add(EngineVersion);
        Info("점역 엔진", engine);
        Info("운영 기관", new TextBlock { Text = "누리닷", FontSize = 14 });
        Info("연락처", new TextBlock { Text = "contact@semo-jum.com", FontSize = 14 });
        OnLoad(async () =>
        {
            try { EngineVersion.Text = await engineVersion(); }
            catch { EngineVersion.Text = "확인 불가 · 엔진 연결을 확인해 주세요."; }
        });
        Action("확인", Close, true);
        EditorModalDesign.Frame(this, Root, Actions);
    }
}
