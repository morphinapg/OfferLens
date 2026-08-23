using Android.AccessibilityServices;
using Android.App;
using Android.Views.Accessibility;
using System;
using System.Collections.Generic;
using System.Text;

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
            // This is where your Dasher UI scraping logic will go
        }

        public override void OnInterrupt()
        {
            // Required override by the OS, handles service interruptions
        }
    }
}
