using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OfferLens.Services;
using OfferLens.Views;
using System.Runtime.Serialization.DataContracts;

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
            }
        }
    }

    [ObservableProperty]
    public PermissionPage? _permissionPage;

    [ObservableProperty]
    public bool _permissionVisible = false;

    public bool GridVisible => !PermissionVisible;

    public MainViewModel()
    {
        if (AppServices.PermissionService is not null && !AppServices.PermissionService.IsServiceEnabled())
        {
            var model = new PermissionViewModel(AppServices.PermissionService);
            model.PermissionGranted += Model_PermissionGranted;

            PermissionPage = new PermissionPage() { DataContext = model };
            PermissionVisible = true;   
            OnPropertyChanged(nameof(GridVisible));
        }
    }

    private void Model_PermissionGranted(object? sender, System.EventArgs e)
    {
        PermissionVisible = false;
        PermissionPage = null;
        OnPropertyChanged(nameof(GridVisible));
    }
}
