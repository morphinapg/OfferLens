using System;
using System.Collections.Generic;
using System.Text;

namespace OfferLens.Services
{
    public interface IAccessibilityPermissionService
    {
        bool IsServiceEnabled();
        void OpenAccessibilitySettings();
    }
}
