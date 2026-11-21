using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using Avalonia.SimplePreferences;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OfferLens.Services
{
    [Service(Label = "OfferLens Tracker", Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
    [IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
    [MetaData("android.accessibilityservice", Resource = "@xml/accessibility_service_config")]
    public class OfferScraperService : AccessibilityService
    {
        private const string DasherPackage = "com.doordash.driverapp";
        private const string TestPackage = "com.CompanyName.OfferTestUI";
        private const long ThrottleIntervalMs = 200;

        private static readonly Regex InvisibleCharsRegex = new(@"[\p{Cf}\u00A0]+", RegexOptions.Compiled);
        private static readonly Regex PayoutRegex = new(@"\$(\d+\.\d{2})", RegexOptions.Compiled);
        private static readonly Regex MilesRegex = new(@"(\d+(?:\.\d+)?)\s*mi\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DeliverByRegex = new(@"Deliver by\s+(\d{1,2}:\d{2}\s*[AP]M)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Handles "1 hr 1 min", "1 hr, 40 min", "2 hrs", "45 min"
        private static readonly Regex DurationRegex = new(@"(?:(\d+)\s*hrs?\b(?:[\s,]*(\d+)\s*mins?\b)?|(\d+)\s*mins?\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LeadingMinutesRegex = new(@"^\s*(\d+)\s*mins?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private Handler? _mainHandler;
        private Action? _scanAction;
        private bool _isScanScheduled;
        private long _lastScanTimeMs;

        private string _lastMileText = "";
        private string _lastHourText = "";
        private int _lastMileY = -1;
        private int _lastHourY = -1;

        private IWindowManager? _windowManager;
        private TextView? _mileOverlay;
        private TextView? _hourOverlay;

        private readonly struct ScrapedNode
        {
            public readonly string Text;
            public readonly int CenterY;

            public ScrapedNode(string text, int centerY)
            {
                Text = text;
                CenterY = centerY;
            }
        }

        protected override void OnServiceConnected()
        {
            base.OnServiceConnected();
            _mainHandler = new Handler(Looper.MainLooper!);
            _scanAction = RunThrottledScan;
        }

        public override void OnAccessibilityEvent(AccessibilityEvent? e)
        {
            if (e == null) return;

            // 1. Filter event types FIRST (zero Binder IPC cost for irrelevant events)
            var eventType = e.EventType;
            if (eventType != EventTypes.WindowContentChanged &&
                eventType != EventTypes.WindowStateChanged &&
                eventType != EventTypes.WindowsChanged)
            {
                return;
            }

            // 2. Ignore events triggered by our own overlays to prevent feedback loops
            string? eventPkg = e.PackageName?.ToString();
            if (eventPkg == PackageName)
                return;

            // 3. If switching to another app/launcher, scan immediately so overlays vanish instantly
            bool isWindowSwitch = eventType == EventTypes.WindowStateChanged || eventType == EventTypes.WindowsChanged;
            if (isWindowSwitch && eventPkg != null && eventPkg != DasherPackage && eventPkg != TestPackage)
            {
                ScheduleScan(immediate: true);
                return;
            }

            // 4. Throttle rapid WindowContentChanged bursts (live map + countdown bar)
            ScheduleScan(immediate: false);
        }

        private void ScheduleScan(bool immediate)
        {
            _mainHandler ??= new Handler(Looper.MainLooper!);
            _scanAction ??= RunThrottledScan;

            if (immediate)
            {
                _mainHandler.RemoveCallbacks(_scanAction);
                _isScanScheduled = false;
                RunThrottledScan();
                return;
            }

            if (_isScanScheduled)
                return; // Coalesce all events arriving during the cooldown window

            long now = SystemClock.UptimeMillis();
            long elapsed = now - _lastScanTimeMs;

            if (elapsed >= ThrottleIntervalMs)
            {
                RunThrottledScan();
            }
            else
            {
                _isScanScheduled = true;
                _mainHandler.PostDelayed(_scanAction, ThrottleIntervalMs - elapsed);
            }
        }

        private void RunThrottledScan()
        {
            _isScanScheduled = false;
            _lastScanTimeMs = SystemClock.UptimeMillis();

            bool isActive = Preferences.Get<bool>("IsActive", false);
            if (!isActive)
            {
                ClearOverlays();
                return;
            }

            var scrapedNodes = new List<ScrapedNode>(64);
            var screenText = new StringBuilder(512);
            var tempRect = new Rect();
            bool foundTargetWindow = false;

            var windows = Windows;
            if (windows != null)
            {
                foreach (var window in windows)
                {
                    using (window)
                    {
                        // Ignore status bar, notifications, keyboard, and floating system widgets
                        if (window.Type != AccessibilityWindowType.Application)
                            continue;

                        using var root = window.Root;
                        if (root == null) continue;

                        string? pkg = root.PackageName?.ToString();
                        if (pkg == DasherPackage || pkg == TestPackage)
                        {
                            foundTargetWindow = true;
                            ExtractTextAndNodes(root, scrapedNodes, screenText, tempRect);
                        }
                    }
                }
            }

            if (!foundTargetWindow || scrapedNodes.Count == 0)
            {
                ClearOverlays();
                return;
            }

            ExtractOfferData(scrapedNodes, screenText.ToString());
        }

        private void CancelPendingScan()
        {
            if (_mainHandler != null && _scanAction != null)
            {
                _mainHandler.RemoveCallbacks(_scanAction);
            }
            _isScanScheduled = false;
        }

        public override void OnInterrupt()
        {
            CancelPendingScan();
            ClearOverlays();
        }

        public override bool OnUnbind(Intent? intent)
        {
            CancelPendingScan();
            ClearOverlays();
            return base.OnUnbind(intent);
        }

        public override void OnDestroy()
        {
            CancelPendingScan();
            ClearOverlays();
            base.OnDestroy();
        }

        private void ExtractOfferData(List<ScrapedNode> scrapedNodes, string fullText)
        {
            string lowerText = fullText.ToLowerInvariant();

            // 1. Strict Screen Validation (added "hr" for exact-hour offers)
            if (lowerText.Contains("active hr") ||
                lowerText.Contains("are you sure you want to decline") ||
                (!lowerText.Contains("incl.") && !lowerText.Contains("guaranteed") && !lowerText.Contains("total will be higher")) ||
                !lowerText.Contains("decline") ||
                (!lowerText.Contains("deliver by") && !lowerText.Contains("est.") && !lowerText.Contains("min") && !lowerText.Contains("hr")) ||
                !lowerText.Contains("mi") ||
                !lowerText.Contains("$"))
            {
                ClearOverlays();
                return;
            }

            // 2. Parse values and Y positions from scraped nodes
            int payoutCenterY = -1;
            int timeCenterY = -1;
            double payout = 0, miles = 0, hours = 0;

            for (int i = 0; i < scrapedNodes.Count; i++)
            {
                string cleanText = InvisibleCharsRegex.Replace(scrapedNodes[i].Text, " ");

                // Payout (first valid match)
                if (payoutCenterY < 0)
                {
                    var payoutMatch = PayoutRegex.Match(cleanText);
                    if (payoutMatch.Success &&
                        double.TryParse(payoutMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out payout))
                    {
                        payoutCenterY = scrapedNodes[i].CenterY;
                    }
                }

                // Miles (first valid match)
                if (miles <= 0)
                {
                    var milesMatch = MilesRegex.Match(cleanText);
                    if (milesMatch.Success)
                    {
                        double.TryParse(milesMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out miles);
                    }
                }

                // Time (lock onto first valid match)
                if (timeCenterY < 0)
                {
                    var deliverByMatch = DeliverByRegex.Match(cleanText);
                    if (deliverByMatch.Success &&
                        DateTime.TryParse(deliverByMatch.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var deliverBy))
                    {
                        var now = DateTime.Now;
                        var roundedNow = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
                        if (deliverBy < roundedNow) deliverBy = deliverBy.AddDays(1);

                        hours = (deliverBy - roundedNow).TotalHours;
                        if (hours > 0)
                        {
                            timeCenterY = scrapedNodes[i].CenterY;
                        }
                    }
                    else
                    {
                        var durationMatch = DurationRegex.Match(cleanText);
                        if (durationMatch.Success)
                        {
                            double hoursPart = 0;
                            double minutesPart = 0;

                            if (durationMatch.Groups[1].Success)
                            {
                                double.TryParse(durationMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out hoursPart);
                                if (durationMatch.Groups[2].Success)
                                {
                                    double.TryParse(durationMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutesPart);
                                }
                                else
                                {
                                    // Handle case where DoorDash splits "1 hr" and "1 min" into adjacent same-row nodes
                                    for (int j = i + 1; j < scrapedNodes.Count && j <= i + 2; j++)
                                    {
                                        if (Math.Abs(scrapedNodes[j].CenterY - scrapedNodes[i].CenterY) > DpToPx(15))
                                            break;

                                        string nextClean = InvisibleCharsRegex.Replace(scrapedNodes[j].Text, " ");
                                        var nextMinMatch = LeadingMinutesRegex.Match(nextClean);
                                        if (nextMinMatch.Success &&
                                            double.TryParse(nextMinMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double extraMins))
                                        {
                                            minutesPart = extraMins;
                                            break;
                                        }
                                    }
                                }
                            }
                            else if (durationMatch.Groups[3].Success)
                            {
                                double.TryParse(durationMatch.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutesPart);
                            }

                            double totalMinutes = (hoursPart * 60) + minutesPart;
                            if (totalMinutes > 0)
                            {
                                hours = totalMinutes / 60.0;
                                timeCenterY = scrapedNodes[i].CenterY;
                            }
                        }
                    }
                }

                // Short-circuit once all three pieces are found
                if (payoutCenterY >= 0 && timeCenterY >= 0 && miles > 0)
                    break;
            }

            // 3. Draw or clear
            if (payoutCenterY >= 0 && timeCenterY >= 0 && miles > 0 && hours > 0)
            {
                double perMile = payout / miles;
                double perHour = payout / hours;

                string mileText = $"${perMile:F2} / mi";
                string hourText = $"${perHour:F2} / hr";

                if (_mileOverlay != null && _hourOverlay != null &&
                    _lastMileText == mileText && _lastHourText == hourText &&
                    _lastMileY == payoutCenterY && _lastHourY == timeCenterY)
                {
                    return;
                }

                double? targetGoodMile = (double?)Preferences.Get<decimal?>("PerMileTargetGood", null);
                double? targetGreatMile = (double?)Preferences.Get<decimal?>("PerMileTargetGreat", null);
                double? targetGoodHour = (double?)Preferences.Get<decimal?>("PerHourTargetGood", null);
                double? targetGreatHour = (double?)Preferences.Get<decimal?>("PerHourTargetGreat", null);
                bool useCustomGreat = Preferences.Get<bool>("UseCustomGreat", false);

                string mileColor = GetOverlayColor(perMile, targetGoodMile, targetGreatMile, useCustomGreat && targetGreatMile != null);
                string hourColor = GetOverlayColor(perHour, targetGoodHour, targetGreatHour, useCustomGreat && targetGreatHour != null);

                DrawTargetedOverlay(payoutCenterY, mileText, mileColor, ref _mileOverlay);
                DrawTargetedOverlay(timeCenterY, hourText, hourColor, ref _hourOverlay);

                _lastMileText = mileText;
                _lastHourText = hourText;
                _lastMileY = payoutCenterY;
                _lastHourY = timeCenterY;
            }
            else
            {
                ClearOverlays();
            }
        }

        private string GetOverlayColor(double actual, double? good, double? great, bool useGreat)
        {
            if (useGreat)
            {
                if (good is not null)
                {
                    if (actual >= great) return "#DF40DF40"; // Green (Great)
                    if (actual >= good) return "#DFDFDF40";  // Yellow (Good)
                    return "#DFDF4040";                      // Red (Bad)
                }
                return "#DF404040";
            }
            else
            {
                if (good is not null)
                {
                    if (actual >= good) return "#DF40DF40";  // Green (Good)
                    return "#DFDF4040";                      // Red (Bad)
                }
                return "#DF404040";
            }
        }

        private void ExtractTextAndNodes(AccessibilityNodeInfo node, List<ScrapedNode> nodes, StringBuilder sb, Rect tempRect)
        {
            // Skip heavy map/rendering containers to cut IPC calls dramatically
            string? className = node.ClassName?.ToString();
            if (className != null && (
                className.Contains("MapView", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("SurfaceView", StringComparison.OrdinalIgnoreCase) ||
                className.Contains("TextureView", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            string? text = node.Text;
            if (!string.IsNullOrEmpty(text))
            {
                node.GetBoundsInScreen(tempRect);
                if (tempRect.Width() > 0 && tempRect.Height() > 0)
                {
                    nodes.Add(new ScrapedNode(text, tempRect.CenterY()));
                    sb.AppendLine(text);
                }
            }

            int childCount = node.ChildCount;
            for (int i = 0; i < childCount; i++)
            {
                using var child = node.GetChild(i);
                if (child != null)
                {
                    ExtractTextAndNodes(child, nodes, sb, tempRect);
                }
            }
        }

        private void DrawTargetedOverlay(int targetCenterY, string text, string hexColor, ref TextView? overlayView)
        {
            if (_windowManager == null)
            {
                var wmObject = GetSystemService(Context.WindowService);
                _windowManager = wmObject?.JavaCast<IWindowManager>();
            }

            int overlayHeight = DpToPx(35);
            int overlayWidth = DpToPx(100);

            var layoutParams = new WindowManagerLayoutParams(
                overlayWidth,
                overlayHeight,
                WindowManagerTypes.AccessibilityOverlay,
                WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal | WindowManagerFlags.LayoutInScreen | WindowManagerFlags.LayoutNoLimits,
                Format.Translucent)
            {
                Gravity = GravityFlags.Top | GravityFlags.Right,
                X = DpToPx(10),
                Y = targetCenterY - (overlayHeight / 2)
            };

            if (overlayView == null)
            {
                overlayView = new TextView(this)
                {
                    Text = text,
                    TextSize = 16,
                    Gravity = GravityFlags.Center
                };
                overlayView.SetTypeface(null, TypefaceStyle.Bold);
                overlayView.SetTextColor(Color.Black);

                var backgroundShape = new GradientDrawable();
                backgroundShape.SetShape(ShapeType.Rectangle);
                backgroundShape.SetCornerRadius(15f);
                backgroundShape.SetColor(Color.ParseColor(hexColor));
                overlayView.Background = backgroundShape;

                _windowManager?.AddView(overlayView, layoutParams);
            }
            else
            {
                overlayView.Text = text;
                if (overlayView.Background is GradientDrawable backgroundShape)
                {
                    backgroundShape.SetColor(Color.ParseColor(hexColor));
                }

                if (overlayView.IsAttachedToWindow)
                {
                    try
                    {
                        _windowManager?.UpdateViewLayout(overlayView, layoutParams);
                    }
                    catch
                    {
                        try { _windowManager?.RemoveView(overlayView); } catch { }
                        _windowManager?.AddView(overlayView, layoutParams);
                    }
                }
                else
                {
                    _windowManager?.AddView(overlayView, layoutParams);
                }
            }
        }

        private int DpToPx(int dp)
        {
            float density = Resources?.DisplayMetrics?.Density ?? 1f;
            return (int)(dp * density + 0.5f);
        }

        private void ClearOverlays()
        {
            _lastMileText = "";
            _lastHourText = "";
            _lastMileY = -1;
            _lastHourY = -1;

            if (_windowManager != null)
            {
                if (_mileOverlay != null)
                {
                    try { _windowManager.RemoveView(_mileOverlay); } catch { }
                    _mileOverlay = null;
                }
                if (_hourOverlay != null)
                {
                    try { _windowManager.RemoveView(_hourOverlay); } catch { }
                    _hourOverlay = null;
                }
            }
        }
    }
}