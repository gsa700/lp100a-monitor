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
using Lp100a.App.Services;
using Lp100a.Core;

namespace Lp100a.App;

internal static class Program
{
    /// <summary>
    /// An interactive <c>--uninstall</c> was asked for, so the UI starts solely to ask what to do
    /// about settings and the transmission log. Read by <see cref="App"/> once the window is up.
    /// </summary>
    public static bool PendingUninstall { get; private set; }

    // Avalonia entry point. Don't use any Avalonia/UI types before AppMain is called.
    [STAThread]
    public static void Main(string[] args)
    {
        var request = InstallCommandLine.Parse(args);

        // These run without a UI, so an exception here would surface as a crash dump rather than
        // as anything a user could act on. Report through the exit code and go quietly instead.
        try
        {
            switch (request.Action)
            {
                // Installing needs no UI: copy, register, and hand off to the installed copy.
                case InstallAction.Install:
                    var installed = InstallService.Install();
                    if (!request.Quiet) InstallService.LaunchDetached(installed);
                    return;

                // An unattended uninstall has nobody to ask, so it takes only the program. Note
                // there is deliberately no switch that deletes the transmission log: operating
                // history can only be removed by someone answering a prompt about it, never by a
                // command line that a shortcut or the installed-apps entry could carry.
                case InstallAction.Uninstall when request.Quiet:
                    InstallService.Uninstall(new UninstallOptions(RemoveSettings: false, RemoveLogs: false));
                    return;

                case InstallAction.Uninstall:
                    PendingUninstall = true;
                    break;
            }
        }
        catch (Exception)
        {
            Environment.ExitCode = 1;
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
