using System.Windows;
using Semojum.Feature.Board;

namespace Semojum.Feature.Usage.Wpf;

/// <summary>Desktop navigation actions used by the usage screen.</summary>
public sealed record UsageActions(Action<Window> Purchase, Func<Window, bool> NewInquiry,
    Action<Window, BoardPost> OpenPost, Action<Window> OpenBoard);
