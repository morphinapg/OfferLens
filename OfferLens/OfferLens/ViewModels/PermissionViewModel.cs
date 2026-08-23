using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OfferLens.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;

namespace OfferLens.ViewModels
{
    public partial class PermissionViewModel : ObservableObject
    {
        private readonly IAccessibilityPermissionService _permissionService;

        public PermissionViewModel(IAccessibilityPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        [RelayCommand]
        private void OpenSettings()
        {
            _permissionService.OpenAccessibilitySettings();
        }
    }
}
