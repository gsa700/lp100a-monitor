// LP-100A Monitor - a desktop monitor for the TelePost LP-100A vector RF wattmeter
// Copyright (C) 2026  David Erickson (AB0R)
//
// This program is free software: you can redistribute it and/or modify it under the
// terms of the GNU General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE.  See the GNU General Public License for more details. You should
// have received a copy of the GNU General Public License along with this program (see
// the LICENSE file).  If not, see <https://www.gnu.org/licenses/>.

using Avalonia;

namespace Lp100a.App;

internal static class Program
{
    // Avalonia entry point. Don't use any Avalonia/UI types before AppMain is called.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
