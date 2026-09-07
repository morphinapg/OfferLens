using Avalonia;
using Avalonia.SimplePreferences;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OfferLens.Services;
using OfferLens.Views;
using System.Runtime.Serialization.DataContracts;
using System.Timers;

namespace OfferLens.ViewModels;

public partial class MainViewModel : ViewModelBase
{

    string? _perMileTargetGood_Text;
    decimal? PerMileTargetGood;
    public string? PerMileTargetGood_Text
    {
        get => _perMileTargetGood_Text;
        set
        {
            // Parse the text to decimal and update the corresponding property
            if (decimal.TryParse(value, out decimal parsedValue))
            {
                PerMileTargetGood = parsedValue;
            }
            else
            {
                PerMileTargetGood = null; // or handle invalid input as needed
            }

            _perMileTargetGood_Text = value;
            OnPropertyChanged(nameof(PerMileTargetGood_Text));
            Preferences.Set("PerMileTargetGood", PerMileTargetGood); // Save the value to preferences
        }
    }

    string? _perMileTargetGreat_Text;
    decimal? PerMileTargetGreat;
    public string? PerMileTargetGreat_Text
    {
        get => UseCustomGreat ? _perMileTargetGreat_Text : null;
        set
        {
            // Parse the text to decimal and update the corresponding property
            if (decimal.TryParse(value, out decimal parsedValue))
            {
                PerMileTargetGreat = parsedValue;
            }
            else
            {
                PerMileTargetGreat = null; // or handle invalid input as needed
            }

            _perMileTargetGreat_Text = value;
            OnPropertyChanged(nameof(PerMileTargetGreat_Text));
            Preferences.Set("PerMileTargetGreat", PerMileTargetGreat); // Save the value to preferences
        }
    }

    string? _perHourTargetGood_Text;
    decimal? PerHourTargetGood;
    public string? PerHourTargetGood_Text
    {
        get => _perHourTargetGood_Text;
        set
        {
            // Parse the text to decimal and update the corresponding property
            if (decimal.TryParse(value, out decimal parsedValue))
            {
                PerHourTargetGood = parsedValue;
            }
            else
            {
                PerHourTargetGood = null; // or handle invalid input as needed
            }

            _perHourTargetGood_Text = value;
            OnPropertyChanged(nameof(PerHourTargetGood_Text));
            Preferences.Set("PerHourTargetGood", PerHourTargetGood); // Save the value to preferences
        }
    }

    string? _perHourTargetGreat_Text;
    decimal? PerHourTargetGreat;
    public string? PerHourTargetGreat_Text
    {
        get => UseCustomGreat ? _perHourTargetGreat_Text : null;
        set
        {
            // Parse the text to decimal and update the corresponding property
            if (decimal.TryParse(value, out decimal parsedValue))
            {
                PerHourTargetGreat = parsedValue;
            }
            else
            {
                PerHourTargetGreat = null; // or handle invalid input as needed
            }

            _perHourTargetGreat_Text = value;
            OnPropertyChanged(nameof(PerHourTargetGreat_Text));
            Preferences.Set("PerHourTargetGreat", PerHourTargetGreat); // Save the value to preferences
        }
    }

    bool _useCustomGreat = false;
    public bool UseCustomGreat
    {
        get => _useCustomGreat;
        set
        {
            if (_useCustomGreat != value)
            {
                _useCustomGreat = value;
                OnPropertyChanged(nameof(UseCustomGreat));
                // Notify that the related properties have changed
                OnPropertyChanged(nameof(PerMileTargetGreat_Text));
                OnPropertyChanged(nameof(PerHourTargetGreat_Text));

                Preferences.Set("UseCustomGreat", value); // Save the value to preferences
            }
        }
    }

    bool _isActive = false;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged(nameof(IsActive));

                Preferences.Set("IsActive", value); // Save the value to preferences
            }
        }
    }

    [ObservableProperty]
    PermissionPage? _permissionPage;

    [ObservableProperty]
    bool _permissionVisible = false;

    public double GridOpacity => PermissionVisible ? 0 : 1;

    Timer PermissionCheckTimer = new(1000);

    public MainViewModel()
    {
        var service = AppServices.PermissionService;

        if (service is not null)
        {
            if (!service.IsServiceEnabled())
            {
                LoadPermissionPage(service);
            }

            PermissionCheckTimer.Elapsed += (s, e) =>
            {
                //Every 1 second, we need to check if the permission has been granted or revoked, and adjust the display of the PermissionPage to accomodate

                var hasPermission = service.IsServiceEnabled();

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (PermissionVisible)
                    {
                        if (hasPermission)
                            UnloadPermissionPage();
                    }
                    else if (!hasPermission)
                    {
                        LoadPermissionPage(service);
                    }
                });                
            };

            PermissionCheckTimer.Start();
        }

        if (Preferences.ContainsKey("PerMileTargetGood"))
        {
            var permilegood = Preferences.Get<decimal?>("PerMileTargetGood", null);
            PerMileTargetGood_Text = permilegood.ToString();
        }
        if (Preferences.ContainsKey("PerHourTargetGood"))
        {
            var perhourgood = Preferences.Get<decimal?>("PerHourTargetGood", null);
            PerHourTargetGood_Text = perhourgood.ToString();
        }
        if (Preferences.ContainsKey("PerMileTargetGreat"))
        {
            var permilegreat = Preferences.Get<decimal?>("PerMileTargetGreat", null);
            PerMileTargetGreat_Text = permilegreat.ToString();
        }
        if (Preferences.ContainsKey("PerHourTargetGreat"))
        {
            var perhourgreat = Preferences.Get<decimal?>("PerHourTargetGreat", null);
            PerHourTargetGreat_Text = perhourgreat.ToString();
        }
        if (Preferences.ContainsKey("UseCustomGreat"))
        {
            UseCustomGreat = Preferences.Get<bool>("UseCustomGreat", false);
        }
        if (Preferences.ContainsKey("IsActive"))
        {
            IsActive = Preferences.Get<bool>("IsActive", false);
        }
    }

    void LoadPermissionPage(IAccessibilityPermissionService service)
    {
        var model = new PermissionViewModel(service);

        PermissionPage = new PermissionPage() { DataContext = model };
        PermissionVisible = true;
        OnPropertyChanged(nameof(GridOpacity));
    }

    void UnloadPermissionPage()
    {
        PermissionVisible = false;
        PermissionPage = null;
        OnPropertyChanged(nameof(GridOpacity));
    }
}
