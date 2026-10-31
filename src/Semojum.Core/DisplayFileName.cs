using System.Text;

namespace Semojum.Core;

/// <summary>Normalizes a name for display or a new filename; never alters an existing filesystem path.</summary>
public static class DisplayFileName
{
    public static string Normalize(string value)
    {
        try { return value.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return value; } // Preserve unusual legacy data instead of losing access to it.
    }
}
