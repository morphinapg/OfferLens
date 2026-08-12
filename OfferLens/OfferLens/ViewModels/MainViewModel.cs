using CommunityToolkit.Mvvm.ComponentModel;

namespace OfferLens.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    //[ObservableProperty]
    //public partial string Greeting { get; set; } = "Welcome to Avalonia!";

    [ObservableProperty]
    public partial decimal? PerMileTargetGood { get; set; }

    [ObservableProperty]
    public partial decimal? PerMileTargetGreat { get; set; }

    [ObservableProperty]
    public partial decimal? PerHourTargetGood { get; set; }

    [ObservableProperty]
    public partial decimal? PerHourTargetGreat { get; set; }

    [ObservableProperty]
    public partial bool UseCustomGreat { get; set; } = false;
}
