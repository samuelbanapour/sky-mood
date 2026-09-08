using System.Diagnostics;
using Avalonia.Controls;

namespace SkyMood.Desktop;

/// <summary>
/// Minimal About/Settings screen — app name + version and a "Join our Discord" link.
/// Opened as a dialog from MainWindow, mirroring the MapWindow pattern.
/// </summary>
public partial class AboutWindow : Window
{
    const string DiscordUrl = "https://discord.gg/VyVWy9kmj9";

    // SkyMood.Desktop.csproj sets no <Version>, so the assembly stays at .NET's implicit 1.0.0.0.
    // Hardcoded here to match SkyMood.App.csproj's <ApplicationDisplayVersion>1.0</...> so all
    // heads of this app show the same version string; bump both together on a release.
    const string AppVersion = "1.0";

    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppVersion}";
        CloseBtn.Click += (_, _) => Close();
        DiscordBtn.Click += (_, _) => OpenUrl(DiscordUrl);
    }

    static void OpenUrl(string url)
    {
        try
        {
            // UseShellExecute=true is required on .NET Core/5+ — without it Process.Start treats
            // the URL as an executable path instead of handing it to the OS's default handler.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Fallback for environments where shell-execute of a bare URL doesn't resolve
            // (e.g. some Linux distros) — hand it to the platform opener directly.
            try
            {
                if (OperatingSystem.IsLinux())
                    Process.Start("xdg-open", url);
                else if (OperatingSystem.IsMacOS())
                    Process.Start("open", url);
            }
            catch { /* no browser available — nothing more we can do */ }
        }
    }
}
