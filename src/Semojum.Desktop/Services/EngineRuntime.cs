using System.IO;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Services;

internal static class EngineRuntime
{
    public static BrailleSidecar Create()
    {
        var python = Environment.GetEnvironmentVariable("SEMOJUM_ENGINE_PYTHON");
        return new BrailleSidecar(!string.IsNullOrWhiteSpace(python)
            ? new SidecarCommand(python, ["-u", "-m", "semojum_braille.sidecar"])
            : new SidecarCommand(Path.Combine(AppContext.BaseDirectory, "engine", "semojum-engine.exe"), []));
    }
}
