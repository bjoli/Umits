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

using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

namespace Umits;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}