using Android.Content;
using OfferLens.Android;
using Android.Provider;
using System;
using System.Collections.Generic;
using System.Text;

namespace OfferLens.Services
{
    public class AndroidAccessibilityService : IAccessibilityPermissionService
    {
        public bool IsServiceEnabled()
        {
            var context = Application.Context;
            string? enabledServices = Settings.Secure.GetString(
                context.ContentResolver,
                Settings.Secure.EnabledAccessibilityServices);

            // Checks if your app's package name is in the list of enabled services
            return enabledServices != null && enabledServices.Contains(context.PackageName ?? string.Empty);
        }

        public void OpenAccessibilitySettings()
        {
            var intent = new Intent(Settings.ActionAccessibilitySettings);
            intent.AddFlags(ActivityFlags.NewTask);
            Application.Context.StartActivity(intent);
        }
    }
}
