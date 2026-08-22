using System.Globalization;
using SkyMood;

namespace SkyMood.App;

public partial class MapPage : ContentPage
{
    public MapPage(GeoLocation loc)
    {
        InitializeComponent();

        // InvariantCulture matters here — some locales render doubles with a comma decimal
        // separator, which would silently corrupt the lat/lon query string.
        string lat = loc.Latitude.ToString(CultureInfo.InvariantCulture);
        string lon = loc.Longitude.ToString(CultureInfo.InvariantCulture);
        string name = Uri.EscapeDataString(loc.Name);
        Map.Source = $"https://soloappsstudio.com/play/sky-mood/?lat={lat}&lon={lon}&name={name}&openMap=1";

        CloseBtn.Clicked += async (_, _) => await Navigation.PopModalAsync();
    }
}
