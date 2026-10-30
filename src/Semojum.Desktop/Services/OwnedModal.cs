using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Media;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

/// <summary>Owner-scoped Figma backdrop and initial placement for the editor's modal dialogs.</summary>
internal static class OwnedModal
{
    private sealed class State
    {
        internal int Depth;
        internal AdornerLayer? Layer;
        internal Backdrop? Backdrop;
    }
    private sealed class Backdrop(UIElement adorned) : Adorner(adorned)
    {
        private static readonly Brush Fill = new SolidColorBrush(Color.FromArgb(115, 180, 180, 180));
        protected override void OnRender(DrawingContext drawingContext) => drawingContext.DrawRectangle(Fill, null, new Rect(RenderSize));
    }
    private static readonly ConditionalWeakTable<Window, State> Owners = new();

    private static bool Eligible(Window dialog) => dialog is NoticeDialog or OpenDocumentDialog or TranslationOptionsDialog or SaveAsDialog or ExportRangeDialog or
        GoToDialog or FindDialog or DefaultsDialog or LayoutDialog or SixDotDialog or FormDialog or FormEditDialog or DeleteFormDialog or
        HeaderInputDialog or FooterInputDialog or FooterDialog or VisualDescriptionDialog or MathDialog or AppliedRulesDialog or RuleDialog or
        NotificationsDialog or AboutDialog or PublicNoticesDialog or PublicInquiryDialog;

    internal static bool? Show(Window dialog, Func<bool?> show)
    {
        if (!Eligible(dialog) || dialog.Owner is not { IsVisible: true } owner || dialog.WindowStartupLocation != WindowStartupLocation.CenterOwner)
            return show();
        var state = Owners.GetValue(owner, static _ => new State()); state.Depth++;
        var moving = false; var ready = false; var manuallyMoved = false;
        void Position()
        {
            if (!owner.IsVisible || !dialog.IsVisible || manuallyMoved) return;
            var left = owner.Left + Math.Max(0, owner.ActualWidth - dialog.ActualWidth) / 2;
            var top = owner.Top + Math.Max(0, owner.ActualHeight - dialog.ActualHeight) * .45;
            moving = true;
            try { dialog.Left = left; dialog.Top = top; WindowBounds.Fit(dialog); }
            finally { moving = false; }
        }
        void Loaded(object? _, RoutedEventArgs __) { Position(); ready = true; }
        void OwnerMoved(object? _, EventArgs __) => Position();
        void OwnerResized(object? _, SizeChangedEventArgs __) => Position();
        void DialogMoved(object? _, EventArgs __) { if (ready && !moving) manuallyMoved = true; }
        try
        {
            if (state.Depth == 1 && owner.Content is UIElement target && AdornerLayer.GetAdornerLayer(target) is { } layer)
            {
                var backdrop = new Backdrop(target) { IsHitTestVisible = false };
                AutomationProperties.SetAutomationId(backdrop, "FigmaModalBackdrop"); layer.Add(backdrop); state.Layer = layer; state.Backdrop = backdrop;
            }
            dialog.Loaded += Loaded; dialog.LocationChanged += DialogMoved; owner.LocationChanged += OwnerMoved; owner.SizeChanged += OwnerResized;
            return show();
        }
        finally
        {
            dialog.Loaded -= Loaded; dialog.LocationChanged -= DialogMoved; owner.LocationChanged -= OwnerMoved; owner.SizeChanged -= OwnerResized;
            if (--state.Depth == 0)
            {
                if (state.Layer is not null && state.Backdrop is not null) state.Layer.Remove(state.Backdrop);
                state.Layer = null; state.Backdrop = null; Owners.Remove(owner);
            }
        }
    }
}
