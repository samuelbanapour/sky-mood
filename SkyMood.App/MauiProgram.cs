using SkiaSharp.Views.Maui.Controls.Hosting;

namespace SkyMood.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            // Mapsui's MapControl (satellite/weather map) renders through SkiaSharp's SKGLView.
            // Without this call, Android crashes on first use with HandlerNotFoundException:
            // "Handler not found for view SkiaSharp.Views.Maui.Controls.SKGLView" — confirmed by
            // an on-device crash. (SkiaSharp.Views.Maui's own AppHostBuilderExtensions.cs has
            // only this parameterless overload — an older Mapsui sample's comment mentions a
            // `bool` argument, but that overload doesn't exist in the current source.)
            .UseSkiaSharp();
        return builder.Build();
    }
}
