/*
Umits - A unit converter
Copyright (C) 2026 Linus Björnstam

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.

---

App Store Distribution Exception
(Additional Permission under Section 7 of the GNU General Public
License, version 3)

As a special exception to the GPLv3, the copyright holders grant you
permission to compile and publish this software to a digital
marketplace (such as the Apple App Store) whose Terms of Service or
Digital Rights Management (DRM) requirements would otherwise conflict
with the conditions of the GPLv3.

This exception applies strictly under the following conditions:

* Permitted Modifications: You may only make the technical
  modifications strictly necessary to comply with the digital
  marketplace’s submission requirements (e.g., modifying bundle IDs,
  API keys, or signing certificates).
* Restrictions: You may not modify the software's core functionality
  or create derivative works for other purposes under this exception.
  Any such modifications immediately void this exception, and the
  resulting work becomes subject entirely to the standard terms of
  the GPLv3, including the requirement to release the complete
  corresponding source code.
* No Sublicensing: You may not sublicense the specific rights
  granted by this exception to any third party.

If you modify this program, you may extend this exception to your
version of the program, but you are not obligated to do so. If you
do not wish to do so, delete this exception statement from your
version.
*/

namespace Umits;

using System;

#if ANDROID
using AndroidX.Core.View;
using Microsoft.Maui.Devices;
using View = Android.Views.View;
#endif

/* This is just a workaround for main view resizing not working on android and maui.
 * I banged my head to a brick wall for days trying to get the standard resizing
 * ways to work, but in the end I gave up. 
*/

public static class KeyboardService
{
    public static event EventHandler<double> KeyboardHeightChanged = null!;

#if ANDROID

    public static void StartListening(Android.App.Activity activity)
    {
        var decorView = activity.Window?.DecorView;

        if (decorView == null) return;

        ViewCompat.SetOnApplyWindowInsetsListener(decorView, new KeyboardInsetsListener());
    }

    private class KeyboardInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {

        
        public WindowInsetsCompat? OnApplyWindowInsets(View? v, WindowInsetsCompat? insets)
        {
            if (insets is null || v is null)
                throw new NullReferenceException();
            
            // Get keyboard height
            var imeInsets = insets.GetInsets(WindowInsetsCompat.Type.Ime());
            
            if (imeInsets is null)
                throw new NullReferenceException();
            
            double density = DeviceDisplay.MainDisplayInfo.Density;
            double keyboardHeight = imeInsets.Bottom / density;

            // Trigger the event
            KeyboardHeightChanged.Invoke(null, keyboardHeight);

            // Keep the UI from drawing under the status bar and navigation bar
            var systemBarInsets = insets.GetInsets(WindowInsetsCompat.Type.SystemBars());
            if (systemBarInsets is null)
                throw new NullReferenceException();
            v.SetPadding(systemBarInsets.Left, systemBarInsets.Top, systemBarInsets.Right, systemBarInsets.Bottom);

            return WindowInsetsCompat.Consumed;
        }
    }
    
    
#endif
}