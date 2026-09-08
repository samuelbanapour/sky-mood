using Microsoft.Maui.ApplicationModel;

namespace SkyMood.App;

/// <summary>
/// Minimal About/Settings screen — app name + version and a "Join our Discord" link.
/// Opened modally from MainPage, mirroring the MapPage pattern.
/// </summary>
public partial class AboutPage : ContentPage
{
    const string DiscordUrl = "https://discord.gg/VyVWy9kmj9";

    public AboutPage()
    {
        InitializeComponent();

        var version = AppInfo.Current.VersionString;
        var build = AppInfo.Current.BuildString;
        VersionLabel.Text = string.IsNullOrEmpty(build) || build == version
            ? $"Version {version}"
            : $"Version {version} ({build})";

        CloseBtn.Clicked += async (_, _) => await Navigation.PopModalAsync();
    }

    async void OnDiscordTapped(object? sender, TappedEventArgs e)
    {
        try { await Browser.Default.OpenAsync(DiscordUrl, BrowserLaunchMode.SystemPreferred); }
        catch { /* no browser available on this device — nothing more we can do */ }
    }
}
