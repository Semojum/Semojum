using System.Windows;
using Semojum.Feature.Board;

namespace Semojum.Feature.Usage.Wpf;

/// <summary>Desktop navigation actions used by the usage screen. OpenInstitution brings the administrator's open 기관 관리 window forward.</summary>
public sealed record UsageActions(Action<Window> Purchase, Func<Window, bool> NewInquiry,
    Action<Window, BoardPost> OpenPost, Action<Window> OpenBoard, Action<Window>? OpenInstitution = null);
