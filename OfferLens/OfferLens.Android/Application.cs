using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using OfferLens.Services;

namespace OfferLens.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // Initialize the accessibility permission service
            AppServices.PermissionService = new AndroidAccessibilityService();

            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}
