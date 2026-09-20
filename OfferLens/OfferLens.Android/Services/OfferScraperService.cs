using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Runtime;
using Android.Views;
using Android.Views.Accessibility;
using Android.Widget;
using Avalonia.SimplePreferences;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OfferLens.Services
{
    [Service(Label = "OfferLens Tracker", Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Exported = true)]
    [IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
    [MetaData("android.accessibilityservice", Resource = "@xml/accessibility_service_config")]
    public class OfferScraperService : AccessibilityService
    {
        private string _lastMileText = "";
        private string _lastHourText = "";
        private int _lastMileY = -1;
        private int _lastHourY = -1;

        protected override void OnServiceConnected()
        {
            base.OnServiceConnected();
            // This fires the moment the user flips the toggle to "ON" in the Android settings
        }

        public override void OnAccessibilityEvent(AccessibilityEvent? e)
        {
            bool isActive = Avalonia.SimplePreferences.Preferences.Get<bool>("IsActive", false);

            if (!isActive || e == null)
            {
                ClearOverlays();
                return;
            }

            var targetRoots = new List<AccessibilityNodeInfo>();

            // Search ALL visible windows and grab every single window belonging to Dasher
            if (Windows != null)
            {
                foreach (var window in Windows)
                {
                    var root = window.Root;
                    var pkg = root?.PackageName?.ToString();

                    if (pkg == "com.doordash.driverapp" || pkg == "com.CompanyName.OfferTestUI")
                    {
                        if (root != null) targetRoots.Add(root);
                        // The "break;" is removed so it finds BOTH the notification and the main app
                    }
                }
            }

            // If no Dasher windows exist on screen, abort
            if (targetRoots.Count == 0)
            {
                ClearOverlays();
                return;
            }

            // Only process Window Content or Window State changes
            if (e.EventType != EventTypes.WindowContentChanged &&
                e.EventType != EventTypes.WindowStateChanged) return;

            ExtractOfferData(targetRoots); // Pass the list instead of a single root
        }

        public override void OnInterrupt()
        {
            // Required override by the OS, handles service interruptions

            ClearOverlays();
        }

        public override bool OnUnbind(Intent? intent)
        {
            ClearOverlays();
            return base.OnUnbind(intent);
        }

        private void ExtractOfferData(List<AccessibilityNodeInfo> roots)
        {
            var textNodes = new List<AccessibilityNodeInfo>();
            var screenText = new System.Text.StringBuilder();

            // Single pass through ALL active Dasher windows to combine their text
            foreach (var root in roots)
            {
                ExtractTextAndNodes(root, textNodes, screenText);
            }

            string fullText = screenText.ToString();
            string lowerText = fullText.ToLower();

            // 2. Strict Screen Validation
            if (lowerText.Contains("active hr") ||
                // Kills the overlays on the popup
                lowerText.Contains("are you sure you want to decline") || 
                // Check for "incl." OR "guaranteed" OR "total will be higher"
                (!lowerText.Contains("incl.") && !lowerText.Contains("guaranteed") && !lowerText.Contains("total will be higher")) ||
                !lowerText.Contains("decline") ||
                // The screen MUST contain either "deliver by" OR "est." for the time
                (!lowerText.Contains("deliver by") && !lowerText.Contains("est.") && !lowerText.Contains("min")) ||
                !lowerText.Contains("mi") ||
                !lowerText.Contains("$")
                )
            {
                // Not a standard Earn By Offer screen. Abort.
                ClearOverlays();
                return;
            }

            // 3. Find the specific nodes for positioning
            AccessibilityNodeInfo? payoutNode = null;
            AccessibilityNodeInfo? timeNode = null;

            double payout = 0, miles = 0, hours = 0;
            DateTime deliverBy = DateTime.MinValue;

            foreach (var node in textNodes)
            {
                var text = node.Text;
                if (string.IsNullOrEmpty(text)) continue;

                // Find Payout Node
                var payoutMatch = Regex.Match(text, @"\$(\d+\.\d{2})");
                if (payoutMatch.Success)
                {
                    payout = double.Parse(payoutMatch.Groups[1].Value);
                    payoutNode = node;
                }

                // Find Miles (Doesn't need a node, just the value)
                var milesMatch = Regex.Match(text, @"(\d+(\.\d+)?)\s*mi");
                if (milesMatch.Success)
                {
                    miles = double.Parse(milesMatch.Groups[1].Value);
                }

                // Find Time Node (Look for both formats)
                var deliverByMatch = Regex.Match(text, @"Deliver by\s+(\d{1,2}:\d{2}\s*[AP]M)", RegexOptions.IgnoreCase);
                var estMinMatch = Regex.Match(text, @"(?:est\.\s*)?(\d+)\s*min", RegexOptions.IgnoreCase);

                if (deliverByMatch.Success)
                {
                    deliverBy = DateTime.Parse(deliverByMatch.Groups[1].Value);
                    timeNode = node;

                    // Truncate DateTime.Now to the current minute to stop the rate from creeping
                    var now = DateTime.Now;
                    var roundedNow = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

                    // Calculate hours from the clock time using the stabilized time
                    if (deliverBy < roundedNow) deliverBy = deliverBy.AddDays(1);
                    hours = (deliverBy - roundedNow).TotalHours;
                }
                else if (estMinMatch.Success)
                {
                    // Calculate hours directly from the estimated minutes
                    double minutes = double.Parse(estMinMatch.Groups[1].Value);
                    hours = minutes / 60.0;
                    timeNode = node;
                }
            }

            // 4. If we successfully scraped the data, run the math and draw
            if (payoutNode != null && timeNode != null && miles > 0 && hours > 0)
            {
                double perMile = payout / miles;
                double perHour = payout / hours;

                string mileText = $"${perMile:F2} / mi";
                string hourText = $"${perHour:F2} / hr";

                // Get current Y positions
                Rect payoutBounds = new Rect();
                payoutNode.GetBoundsInScreen(payoutBounds);
                Rect timeBounds = new Rect();
                timeNode.GetBoundsInScreen(timeBounds);

                // If the text and positions haven't changed, DO NOTHING.
                if (_activeOverlays.Count > 0 &&
                    _lastMileText == mileText && _lastHourText == hourText &&
                    _lastMileY == payoutBounds.CenterY() && _lastHourY == timeBounds.CenterY())
                {
                    return;
                }

                // Fetch targets & determine colors
                double? targetGoodMile = (double?)Preferences.Get<decimal?>("PerMileTargetGood", null)!;
                double? targetGreatMile = (double?)Preferences.Get<decimal?>("PerMileTargetGreat", null)!;
                double? targetGoodHour = (double?)Preferences.Get<decimal?>("PerHourTargetGood", null)!;
                double? targetGreatHour = (double?)Preferences.Get<decimal?>("PerHourTargetGreat", null)!;
                bool useCustomGreat = Preferences.Get<bool>("UseCustomGreat", false);

                string mileColor = GetOverlayColor(perMile, targetGoodMile, targetGreatMile, useCustomGreat && targetGreatMile != null);
                string hourColor = GetOverlayColor(perHour, targetGoodHour, targetGreatHour, useCustomGreat && targetGreatHour != null);

                // Wipe and redraw only when something actually changes
                ClearOverlays();

                DrawTargetedOverlay(payoutNode, mileText, mileColor);
                DrawTargetedOverlay(timeNode, hourText, hourColor);

                // Cache the state
                _lastMileText = mileText;
                _lastHourText = hourText;
                _lastMileY = payoutBounds.CenterY();
                _lastHourY = timeBounds.CenterY();
            }
        }

        private string GetOverlayColor(double actual, double? good, double? great, bool useGreat)
        {
            if (useGreat)
            {
                if (good is not null)
                {
                    if (actual >= great) return "#DF40DF40"; // Green (Great)
                    if (actual >= good) return "#DFDFDF40"; // Yellow (Good)
                    return "#DFDF4040"; // Red (Bad)
                }
                else
                    return "#DF404040"; // Gray (No Good target set)

            }
            else
            {
                if (good is not null)
                {
                    // If "Great" is disabled, "Good" becomes Green
                    if (actual >= good) return "#DF40DF40"; // Green (Good)
                    return "#DFDF4040"; // Red (Bad)
                }
                else
                    return "#DF404040"; // Gray (No Good target set)
            }
        }

        // The updated recursive function that does dual-duty
        private void ExtractTextAndNodes(AccessibilityNodeInfo? node, List<AccessibilityNodeInfo> nodes, System.Text.StringBuilder sb)
        {
            if (node == null) return;

            if (!string.IsNullOrEmpty(node.Text))
            {
                nodes.Add(node);
                sb.AppendLine(node.Text);
            }

            for (int i = 0; i < node.ChildCount; i++)
            {
                ExtractTextAndNodes(node.GetChild(i), nodes, sb);
            }
        }

        private IWindowManager? _windowManager;
        private List<View> _activeOverlays = new(); // Keep track so we can remove them later

        private void DrawTargetedOverlay(AccessibilityNodeInfo targetNode, string text, string hexColor)
        {
            if (_windowManager == null)
            {
                var wmObject = GetSystemService(Context.WindowService);
                _windowManager = wmObject?.JavaCast<IWindowManager>();
            }

            // 1. Get the physical screen coordinates of the Dasher text
            Rect bounds = new Rect();
            targetNode.GetBoundsInScreen(bounds);

            // 2. Create your visual border/box
            var overlayView = new TextView(this)
            {
                Text = text,
                TextSize = 16,
                Gravity = GravityFlags.Center
            };
            overlayView.SetTypeface(null, TypefaceStyle.Bold);
            overlayView.SetTextColor(Color.Black);

            // Create a new rectangle shape
            var backgroundShape = new GradientDrawable();
            backgroundShape.SetShape(ShapeType.Rectangle);

            // Set your rounded corners (adjust the float value to make it more or less round)
            backgroundShape.SetCornerRadius(15f);

            // Set the color using your hex string
            backgroundShape.SetColor(Color.ParseColor(hexColor));

            // Apply the shape to the TextView
            overlayView.Background = backgroundShape;

            // 3. Configure exact placement on the right side of the screen
            int overlayHeight = DpToPx(35); // The height you specified from your XAML
            int overlayWidth = DpToPx(100); // Adjust width as needed to fit your text

            var layoutParams = new WindowManagerLayoutParams(
                overlayWidth,
                overlayHeight,
                WindowManagerTypes.AccessibilityOverlay,
                // Add LayoutInScreen and LayoutNoLimits here
                WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal | WindowManagerFlags.LayoutInScreen | WindowManagerFlags.LayoutNoLimits,
                Format.Translucent)
            {
                Gravity = GravityFlags.Top | GravityFlags.Right,
                X = DpToPx(10),
                Y = bounds.CenterY() - (overlayHeight / 2)
            };

            _windowManager?.AddView(overlayView, layoutParams);
            _activeOverlays.Add(overlayView);
        }

        private int DpToPx(int dp)
        {
            float density = Resources?.DisplayMetrics?.Density ?? 1f;
            return (int)(dp * density + 0.5f); // + 0.5f ensures correct rounding
        }


        private void ClearOverlays()
        {
            if (_activeOverlays.Count == 0 || _windowManager == null) return;

            foreach (var view in _activeOverlays)
            {
                try { _windowManager.RemoveView(view); } catch { }
            }

            _activeOverlays.Clear();
            _lastMileText = "";
            _lastHourText = "";
            _lastMileY = -1;
            _lastHourY = -1;
        }
    }
}
