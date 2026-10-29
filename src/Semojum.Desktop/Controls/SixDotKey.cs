using System.Windows.Input;

namespace Semojum.Desktop.Controls;

/// <summary>Convert a WPF physical key to the printable key used by six-dot input.</summary>
internal static class SixDotKey
{
    public static char? FromEvent(KeyEventArgs e) => FromKey(e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key);

    public static char? FromKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return (char)('A' + (int)key - (int)Key.A);
        if (key is >= Key.D0 and <= Key.D9) return (char)('0' + (int)key - (int)Key.D0);
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return (char)('0' + (int)key - (int)Key.NumPad0);
        return key switch
        {
            Key.Oem1 => ';', Key.OemPlus => '=', Key.OemComma => ',', Key.OemMinus => '-',
            Key.OemPeriod => '.', Key.Oem2 => '/', Key.Oem3 => '`', Key.Oem4 => '[',
            Key.Oem5 or Key.Oem102 => '\\', Key.Oem6 => ']', Key.Oem7 => '\'',
            Key.Add => '=', Key.Subtract => '-', Key.Multiply => '8', Key.Divide => '/',
            Key.Decimal => '.', _ => null
        };
    }
}
