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
        protected override void OnServiceConnected()
        {
            base.OnServiceConnected();
            // This fires the moment the user flips the toggle to "ON" in the Android settings
        }

        public override void OnAccessibilityEvent(AccessibilityEvent? e)
        {
            bool isActive = Avalonia.SimplePreferences.Preferences.Get<bool>("IsActive", false);

            // 1. If turned off, clear everything and stop listening
            if (!isActive || e == null)
            {
                ClearOverlays();
                return;
            }

            // 2. Ignore background system updates (clock ticking, wifi signal changing, etc.)
            // If we don't return here, these events will trigger a false-positive screen wipe
            if (e.PackageName == "com.android.systemui") return;

            // 3. Grab the root node of whatever is currently occupying the screen
            var rootNode = RootInActiveWindow;
            if (rootNode == null) return;

            // 4. Only listen to the DoorDash Dasher app, or my test app
            var activePackage = rootNode.PackageName?.ToString();
            if (activePackage != "com.doordash.driverapp" && activePackage != "com.CompanyName.OfferTestUI")
            {
                ClearOverlays();
                return;
            }

            // 5. Only process Window Content or Window State changes
            if (e.EventType != EventTypes.WindowContentChanged &&
                e.EventType != EventTypes.WindowStateChanged) return;

            ExtractOfferData(rootNode);
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

        private void ExtractOfferData(AccessibilityNodeInfo rootNode)
        {
            var textNodes = new List<AccessibilityNodeInfo>();
            var screenText = new System.Text.StringBuilder();

            // 1. Single pass to get both the full text and the list of UI nodes
            ExtractTextAndNodes(rootNode, textNodes, screenText);
            string fullText = screenText.ToString();

            // 2. Strict Screen Validation (Your exact criteria)
            if (!fullText.Contains("$") ||
                !fullText.Contains("mi") ||
                !fullText.Contains("incl. tips") ||
                !fullText.Contains("Accept") ||
                !fullText.Contains("Deliver by"))
            {
                // Not a standard Earn By Offer screen. Abort.
                ClearOverlays();
                return;
            }

            // 3. Find the specific nodes for positioning
            AccessibilityNodeInfo? payoutNode = null;
            AccessibilityNodeInfo? timeNode = null;

            double payout = 0, miles = 0;
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

                // Find Time Node
                var timeMatch = Regex.Match(text, @"Deliver by\s+(\d{1,2}:\d{2}\s*[AP]M)");
                if (timeMatch.Success)
                {
                    deliverBy = DateTime.Parse(timeMatch.Groups[1].Value);
                    timeNode = node;
                }
            }

            // 4. If we successfully scraped the data, run the math and draw
            if (payoutNode != null && timeNode != null && miles > 0 && deliverBy != DateTime.MinValue)
            {
                if (deliverBy < DateTime.Now) deliverBy = deliverBy.AddDays(1);
                double hours = (deliverBy - DateTime.Now).TotalHours;

                double perMile = payout / miles;
                double perHour = payout / hours;

                // Fetch all targets
                double? targetGoodMile = (double?)Preferences.Get<decimal?>("PerMileTargetGood", null)!;
                double? targetGreatMile = (double?)Preferences.Get<decimal?>("PerMileTargetGreat", null)!;
                double? targetGoodHour = (double?)Preferences.Get<decimal?>("PerHourTargetGood", null)!;
                double? targetGreatHour = (double?)Preferences.Get<decimal?>("PerHourTargetGreat", null)!;
                bool useCustomGreat = Preferences.Get<bool>("UseCustomGreat", false);

                // Determine Colors
                string mileColor = GetOverlayColor(perMile, targetGoodMile, targetGreatMile, useCustomGreat && targetGreatMile != null);
                string hourColor = GetOverlayColor(perHour, targetGoodHour, targetGreatHour, useCustomGreat && targetGreatHour != null);

                // WIPE the old overlays right before drawing the new ones 
                // to prevent stacking from the countdown timer updating the screen
                ClearOverlays();

                // Draw the overlays perfectly aligned with the nodes we saved
                DrawTargetedOverlay(payoutNode, $"${perMile:F2} / mi", mileColor);
                DrawTargetedOverlay(timeNode, $"${perHour:F2} / hr", hourColor);
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
            backgroundShape.SetCornerRadius(5f);

            // Set the color using your hex string
            backgroundShape.SetColor(Color.ParseColor(hexColor));

            // Apply the shape to the TextView
            overlayView.Background = backgroundShape;

            // 3. Configure exact placement on the right side of the screen
            int overlayHeight = 80; // The height you specified from your XAML
            int overlayWidth = 250; // Adjust width as needed to fit your text

            var layoutParams = new WindowManagerLayoutParams(
                overlayWidth,
                overlayHeight,
                WindowManagerTypes.AccessibilityOverlay,
                WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal,
                Format.Translucent)
            {
                // Anchor to the top-right corner of the phone screen
                Gravity = GravityFlags.Top | GravityFlags.Right,

                X = 40, // A small 40px margin from the right edge

                // Push it down to perfectly center with the Dasher text
                Y = bounds.CenterY() - (overlayHeight / 2)
            };

            _windowManager?.AddView(overlayView, layoutParams);
            _activeOverlays.Add(overlayView);
        }

        private void ClearOverlays()
        {
            if (_activeOverlays.Count == 0 || _windowManager == null) return;

            foreach (var view in _activeOverlays)
            {
                try
                {
                    _windowManager.RemoveView(view);
                }
                catch
                {
                    // Ignore if the view was already somehow destroyed by the OS
                }
            }

            _activeOverlays.Clear();
        }
    }
}
