using System;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Content;
using AndroidX.Core.Util;
using AndroidX.Window.Java.Layout;
using AndroidX.Window.Layout;

namespace SyncDroidAndroid.Services;

public enum WindowWidthSizeClass
{
    Compact,  // < 600dp (Phone in portrait)
    Medium,   // 600dp .. < 840dp (Tablet portrait, Foldable unfolded, Large phone landscape)
    Expanded  // >= 840dp (Tablet landscape, Desktop / Freeform window)
}

public enum WindowHeightSizeClass
{
    Compact,  // < 480dp (Phone in landscape)
    Medium,   // 480dp .. < 900dp
    Expanded  // >= 900dp
}

public enum FoldablePosture
{
    Normal,
    Tabletop, // Half-opened horizontal hinge
    Book,     // Half-opened vertical hinge
    FlatSeparatingVertical // Flat unfolded with separating vertical hinge
}

public sealed record AdaptiveWindowState(
    int WidthPx,
    int HeightPx,
    float WidthDp,
    float HeightDp,
    WindowWidthSizeClass WidthSizeClass,
    WindowHeightSizeClass HeightSizeClass,
    FoldablePosture Posture,
    Rect? HingeBoundsPx)
{
    public bool UseNavigationRail =>
        WidthSizeClass != WindowWidthSizeClass.Compact ||
        HeightSizeClass == WindowHeightSizeClass.Compact ||
        Posture == FoldablePosture.Book;

    public bool UseTwoPaneLayout =>
        WidthSizeClass != WindowWidthSizeClass.Compact ||
        Posture == FoldablePosture.Book ||
        Posture == FoldablePosture.FlatSeparatingVertical;

    public int ContentPaddingDp => WidthSizeClass switch
    {
        WindowWidthSizeClass.Expanded => 20,
        WindowWidthSizeClass.Medium => 16,
        _ => 12
    };

    public int PaneGapDp => WidthSizeClass switch
    {
        WindowWidthSizeClass.Expanded => 16,
        _ => 12
    };
}

/// <summary>
/// Integrates Jetpack WindowManager (androidx.window.layout) to compute WindowMetrics,
/// Material 3 WindowSizeClasses, and foldable display postures for responsive UI adaptation.
/// </summary>
public sealed class ResponsiveWindowService
{
    public static ResponsiveWindowService Instance { get; } = new();

    private WindowInfoTrackerCallbackAdapter? _callbackAdapter;
    private LayoutStateConsumer? _layoutConsumer;
    private WindowLayoutInfo? _latestLayoutInfo;

    public AdaptiveWindowState CurrentState { get; private set; } = new(
        WidthPx: 1080,
        HeightPx: 2400,
        WidthDp: 360f,
        HeightDp: 800f,
        WidthSizeClass: WindowWidthSizeClass.Compact,
        HeightSizeClass: WindowHeightSizeClass.Medium,
        Posture: FoldablePosture.Normal,
        HingeBoundsPx: null);

    public event Action<AdaptiveWindowState>? WindowStateChanged;

    private ResponsiveWindowService() { }

    public void StartTracking(Activity activity)
    {
        StopTracking();

        try
        {
            var tracker = WindowInfoTracker.GetOrCreate(activity);
            _callbackAdapter = new WindowInfoTrackerCallbackAdapter(tracker);
            _layoutConsumer = new LayoutStateConsumer(info =>
            {
                _latestLayoutInfo = info;
                RecomputeWindowMetrics(activity);
            });

            var executor = ContextCompat.GetMainExecutor(activity);
            _callbackAdapter.AddWindowLayoutInfoListener(activity, executor, _layoutConsumer);
        }
        catch
        {
            // Fallback to metrics calculation if device ROM lacks window extensions
        }

        RecomputeWindowMetrics(activity);
    }

    public void StopTracking()
    {
        try
        {
            if (_callbackAdapter != null && _layoutConsumer != null)
            {
                _callbackAdapter.RemoveWindowLayoutInfoListener(_layoutConsumer);
            }
        }
        catch { }
        finally
        {
            _layoutConsumer = null;
            _callbackAdapter = null;
        }
    }

    public AdaptiveWindowState RecomputeWindowMetrics(Activity activity)
    {
        try
        {
            var metrics = WindowMetricsCalculator.Companion.OrCreate.ComputeCurrentWindowMetrics(activity);
            var bounds = metrics.Bounds;
            float density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
            if (density <= 0f) density = 1f;

            float widthDp = bounds.Width() / density;
            float heightDp = bounds.Height() / density;

            var widthClass = widthDp switch
            {
                < 600f => WindowWidthSizeClass.Compact,
                < 840f => WindowWidthSizeClass.Medium,
                _ => WindowWidthSizeClass.Expanded
            };

            var heightClass = heightDp switch
            {
                < 480f => WindowHeightSizeClass.Compact,
                < 900f => WindowHeightSizeClass.Medium,
                _ => WindowHeightSizeClass.Expanded
            };

            var (posture, hingeRect) = EvaluateFoldablePosture(_latestLayoutInfo);

            var nextState = new AdaptiveWindowState(
                WidthPx: bounds.Width(),
                HeightPx: bounds.Height(),
                WidthDp: widthDp,
                HeightDp: heightDp,
                WidthSizeClass: widthClass,
                HeightSizeClass: heightClass,
                Posture: posture,
                HingeBoundsPx: hingeRect);

            if (!Equals(CurrentState, nextState))
            {
                CurrentState = nextState;
                WindowStateChanged?.Invoke(nextState);
            }
            else
            {
                CurrentState = nextState;
            }
        }
        catch { }

        return CurrentState;
    }

    private static (FoldablePosture Posture, Rect? HingeBounds) EvaluateFoldablePosture(WindowLayoutInfo? layoutInfo)
    {
        if (layoutInfo?.DisplayFeatures == null)
        {
            return (FoldablePosture.Normal, null);
        }

        foreach (var feature in layoutInfo.DisplayFeatures)
        {
            if (feature is IFoldingFeature foldingFeature)
            {
                var state = foldingFeature.State;
                var orientation = foldingFeature.Orientation;
                var bounds = foldingFeature.Bounds;

                if (Equals(state, FoldingFeatureState.HalfOpened))
                {
                    if (Equals(orientation, FoldingFeatureOrientation.Horizontal))
                    {
                        return (FoldablePosture.Tabletop, bounds);
                    }
                    if (Equals(orientation, FoldingFeatureOrientation.Vertical))
                    {
                        return (FoldablePosture.Book, bounds);
                    }
                }
                else if (foldingFeature.IsSeparating && Equals(orientation, FoldingFeatureOrientation.Vertical))
                {
                    return (FoldablePosture.FlatSeparatingVertical, bounds);
                }
            }
        }

        return (FoldablePosture.Normal, null);
    }

    /// <summary>
    /// Applies responsive single-column vs two-pane layout to a fragment's root container,
    /// left pane, and right pane based on the current Jetpack WindowManager state.
    /// </summary>
    public static void ApplyTwoPaneLayout(
        Context? context,
        LinearLayout? rootPaddingContainer,
        LinearLayout? columnsContainer,
        LinearLayout? leftPane,
        LinearLayout? rightPane)
    {
        if (context == null || columnsContainer == null || leftPane == null || rightPane == null)
            return;

        var state = Instance.CurrentState;
        float density = context.Resources?.DisplayMetrics?.Density ?? 1f;
        int padPx = (int)Math.Round(state.ContentPaddingDp * density);
        int gapPx = (int)Math.Round(state.PaneGapDp * density);

        rootPaddingContainer?.SetPadding(padPx, padPx, padPx, padPx);

        if (state.UseTwoPaneLayout)
        {
            columnsContainer.Orientation = Orientation.Horizontal;

            var leftLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
            {
                MarginEnd = gapPx / 2
            };
            leftPane.LayoutParameters = leftLp;

            var rightLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
            {
                MarginStart = gapPx / 2
            };
            rightPane.LayoutParameters = rightLp;
        }
        else
        {
            columnsContainer.Orientation = Orientation.Vertical;

            var leftLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                MarginEnd = 0
            };
            leftPane.LayoutParameters = leftLp;

            var rightLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                MarginStart = 0
            };
            rightPane.LayoutParameters = rightLp;
        }
    }

    /// <summary>
    /// Sizes a dialog responsively using Jetpack WindowMetricsCalculator.
    /// </summary>
    public static void ApplyAdaptiveDialogSizing(Dialog dialog, Context context)
    {
        try
        {
            var window = dialog.Window;
            if (window == null) return;

            var state = Instance.CurrentState;
            float density = context.Resources?.DisplayMetrics?.Density ?? 1f;

            if (state.WidthSizeClass == WindowWidthSizeClass.Expanded)
            {
                int maxWidthPx = (int)Math.Min(state.WidthPx * 0.72f, 760 * density);
                window.SetLayout(maxWidthPx, ViewGroup.LayoutParams.WrapContent);
            }
            else if (state.WidthSizeClass == WindowWidthSizeClass.Medium)
            {
                int maxWidthPx = (int)Math.Min(state.WidthPx * 0.86f, 640 * density);
                window.SetLayout(maxWidthPx, ViewGroup.LayoutParams.WrapContent);
            }
            else
            {
                int widthPx = (int)(state.WidthPx * 0.95f);
                window.SetLayout(widthPx, ViewGroup.LayoutParams.WrapContent);
            }
        }
        catch { }
    }

    private sealed class LayoutStateConsumer : Java.Lang.Object, IConsumer
    {
        private readonly Action<WindowLayoutInfo> _onAccept;

        public LayoutStateConsumer(Action<WindowLayoutInfo> onAccept)
        {
            _onAccept = onAccept;
        }

        public void Accept(Java.Lang.Object? value)
        {
            if (value is WindowLayoutInfo info)
            {
                _onAccept(info);
            }
        }
    }
}
