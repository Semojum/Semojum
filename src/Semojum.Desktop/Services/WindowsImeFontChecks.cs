using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Semojum.Core;

namespace Semojum.Desktop.Services;

internal static class WindowsImeFontChecks
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharacterSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Face;
    }
    [DllImport("imm32.dll")] private static extern nint ImmGetContext(nint hwnd);
    [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(nint hwnd, nint context);
    [DllImport("imm32.dll", CharSet = CharSet.Unicode)] private static extern bool ImmGetCompositionFontW(nint context, out LogFont font);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, int message, nint wParam, nint lParam);

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true);
        window.Show(); window.Activate();
        try
        {
            var editor = window.PageEditor.Editor;
            var failures = new List<string>();
            var original = editor.Text;
            foreach (var (size, spacing, bold) in new[] { (14d, 1.3d, false), (20d, 1.3d, false), (14d, 2.6d, true) })
            {
                window.ApplyAppearance(new EditorAppearance(TextSize: size, LineSpacing: spacing, Bold: bold));
                editor.TextArea.Focus(); Keyboard.Focus(editor.TextArea);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                editor.TextArea.TextView.EnsureVisualLines();
                check(editor.TextArea.IsKeyboardFocused, "IME size probe focuses the actual text panel");
                var source = (HwndSource)PresentationSource.FromVisual(editor);
                var context = ImmGetContext(source.Handle);
                check(context != 0, "IME size probe obtains a native input context");
                try
                {
                    // Exercise the same Win32 notification used while Korean composition updates.
                    SendMessageW(source.Handle, 0x010F, 0, 0);
                    check(ImmGetCompositionFontW(context, out var font), "IME size probe reads the native composition font");
                    var expected = -(int)Math.Round(size * source.CompositionTarget.TransformToDevice.M22);
                    var message = $"IME font follows {size}px text rather than line spacing (height={font.Height}, expected={expected}, face={font.Face})";
                    if (font.Height != expected || font.Face != "Malgun Gothic" || font.Weight != (bold ? 700 : 400)) failures.Add(message);
                    else check(true, message);
                }
                finally { ImmReleaseContext(source.Handle, context); }
            }
            check(editor.Text == original, "IME font notifications do not alter document text");
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
}
