using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace Semojum.Desktop.Services;

/// <summary>Finish native IME text before AvalonEdit cancels it on focus loss.</summary>
internal static class NativeIme
{
    public static void Attach(TextEditor editor, Action? completeForTest = null)
    {
        void Commit()
        {
            if (!editor.TextArea.IsKeyboardFocused) return;
            if (completeForTest is not null) completeForTest(); else Complete(editor);
        }
        editor.PreviewLostKeyboardFocus += (_, e) =>
        {
            if (!e.Handled) Commit();
        };
        // WPF/IMM can retire the old input context before PreviewLostKeyboardFocus.
        // A click anywhere in the owning window must finish text at the old caret
        // while that context still exists. Reattach when a pane is detached/rejoined.
        Window? owner = null;
        HwndSource? source = null;
        void BeforeMouseDown(object sender, MouseButtonEventArgs e) => Commit();
        nint BeforeDeactivate(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            // Catch native deactivation before WPF/IMM tears down the focused
            // editor's context. PreviewLostKeyboardFocus alone is too late.
            if ((message is 0x001C or 0x0086 && wParam == 0)
                || (message == 0x0006 && (wParam.ToInt64() & 0xFFFF) == 0)) Commit();
            if (message is 0x010D or 0x010F) UpdateCompositionFont(editor);
            return 0;
        }
        void Detach()
        {
            if (owner is not null) owner.PreviewMouseDown -= BeforeMouseDown;
            source?.RemoveHook(BeforeDeactivate); source = null;
            owner = null;
        }
        editor.Loaded += (_, _) =>
        {
            Detach(); owner = Window.GetWindow(editor);
            if (owner is not null) owner.PreviewMouseDown += BeforeMouseDown;
            source = PresentationSource.FromVisual(editor) as HwndSource;
            source?.AddHook(BeforeDeactivate);
        };
        editor.Unloaded += (_, _) => Detach();
        editor.AddHandler(CommandManager.PreviewExecutedEvent,
            new ExecutedRoutedEventHandler((_, _) => Commit()));
    }

    // AvalonEdit uses the visual line height as a positive LOGFONT cell height.
    // Our composite font deliberately has extra line spacing, so that makes the
    // composition glyph oversized. Negative height specifies the actual em size.
    private static void UpdateCompositionFont(TextEditor editor)
    {
        if (!editor.TextArea.IsKeyboardFocused || editor.IsReadOnly || !editor.Options.EnableImeSupport
            || PresentationSource.FromVisual(editor) is not HwndSource source) return;
        var context = ImmGetContext(source.Handle);
        if (context == 0) return;
        try
        {
            var family = editor.FontFamily.Source;
            if (string.IsNullOrWhiteSpace(family))
                family = editor.FontFamily.FamilyMaps.FirstOrDefault()?.Target;
            // WPF packaged fonts use a URI followed by #Family; IMM takes only the family name.
            family = family?.Split(',')[0].Split('#').Last().Trim();
            if (string.IsNullOrWhiteSpace(family)) family = "Malgun Gothic";
            var font = new CompositionFont
            {
                Height = -(int)Math.Max(1, Math.Round(editor.FontSize * source.CompositionTarget.TransformToDevice.M22)),
                Weight = editor.FontWeight.ToOpenTypeWeight(),
                Italic = editor.FontStyle == FontStyles.Italic ? (byte)1 : (byte)0,
                CharacterSet = 1,
                Face = family[..Math.Min(31, family.Length)]
            };
            ImmSetCompositionFontW(context, ref font);
        }
        finally { ImmReleaseContext(source.Handle, context); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CompositionFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharacterSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Face;
    }
    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmSetCompositionFontW(nint context, ref CompositionFont font);
    public static void Complete(TextEditor editor)
    {
        if (!editor.TextArea.IsKeyboardFocused || editor.IsReadOnly || !editor.Options.EnableImeSupport
            || PresentationSource.FromVisual(editor) is not HwndSource source) return;
        var context = ImmGetContext(source.Handle);
        if (context == 0) return;
        try
        {
            if (ImmGetCompositionStringW(context, 0x0008, 0, 0) > 0)
                ImmNotifyIME(context, 0x0015, 0x0001, 0); // NI_COMPOSITIONSTR / CPS_COMPLETE, not CPS_CANCEL
        }
        finally { ImmReleaseContext(source.Handle, context); }
    }

    [DllImport("imm32.dll")] private static extern nint ImmGetContext(nint hwnd);
    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmReleaseContext(nint hwnd, nint context);
    [DllImport("imm32.dll")] private static extern int ImmGetCompositionStringW(nint context, uint index, nint buffer, uint length);
    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmNotifyIME(nint context, uint action, uint index, uint value);
}
