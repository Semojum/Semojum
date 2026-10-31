using System.Windows.Controls;

namespace Semojum.Desktop.Dialogs;

public sealed class TextValueDialog : EditorDialog
{
    private readonly TextBox input;
    public string Value => input.Text;
    public TextValueDialog(string title, string label, string value = "", int maxLength = 0) : base(title)
    {
        input = new TextBox { Text = value, MaxLength = maxLength }; Row(label, input);
    }
    protected override bool Apply() => true;
}
