using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;

public sealed class MergeBlocksDialog : NoticeDialog
{
    public Semojum.Core.ManualEditDecision Decision => Choice switch
    {
        0 => Semojum.Core.ManualEditDecision.Overwrite,
        1 => Semojum.Core.ManualEditDecision.Keep,
        _ => Semojum.Core.ManualEditDecision.Cancel
    };
    public MergeBlocksDialog() : base("두 블록을 합치면 전체를 다시 점역합니다.",
        "직접 고친 점자가 사라집니다.", ["합치기", "고친 점자 유지", "취소"], width: 440) { }
}
