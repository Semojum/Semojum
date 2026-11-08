using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;

public sealed class MergeBlocksDialog : NoticeDialog
{
    public Semojum.Core.ManualEditDecision Decision => Choice == 0 ? Semojum.Core.ManualEditDecision.Overwrite : Semojum.Core.ManualEditDecision.Cancel;
    /// <param name="spots">Manual-braille spots that merging discards; one block with manual braille counts as one spot.</param>
    public MergeBlocksDialog(int spots) : base("두 블록을 합치면 전체를 다시 점역합니다.",
        $"직접 고친 점자 {spots}군데가 사라집니다.", ["합치기", "취소"], width: 440) { }
}
