using Foundation;
using SkyMood;
using WatchConnectivity;

namespace SkyMood.App;

/// <summary>
/// Bridges MainPage's existing weather/places pipeline to the paired Apple Watch companion over
/// WatchConnectivity. Reuses SkyMood.Core's WeatherService/GeocodingService via the callbacks
/// passed to Configure — this class only knows how to serialize/deserialize and shuttle data,
/// it never fetches weather itself.
/// </summary>
public sealed class WatchConnectivityService : WCSessionDelegate
{
    public static readonly WatchConnectivityService Instance = new();

    Func<GeoLocation>? _getCurrentLocation;
    Func<GeoLocation, Task<WeatherResult>>? _refresh;
    Func<IReadOnlyList<WeatherResult>>? _getPlaces;
    Func<GeoLocation, Task>? _selectLocation;
    Func<bool>? _getIsFahrenheit;

    WatchConnectivityService() { }

    public void Configure(
        Func<GeoLocation> getCurrentLocation,
        Func<GeoLocation, Task<WeatherResult>> refresh,
        Func<IReadOnlyList<WeatherResult>> getPlaces,
        Func<GeoLocation, Task> selectLocation,
        Func<bool> getIsFahrenheit)
    {
        _getCurrentLocation = getCurrentLocation;
        _refresh = refresh;
        _getPlaces = getPlaces;
        _selectLocation = selectLocation;
        _getIsFahrenheit = getIsFahrenheit;

        if (!WCSession.IsSupported) return;
        WCSession.DefaultSession.Delegate = this;
        WCSession.DefaultSession.ActivateSession();
    }

    /// <summary>Pushes the phone's latest weather + saved places so the watch always has
    /// something to show on next launch/wake, even without a live request/reply round trip.</summary>
    public void Push(WeatherResult result)
    {
        if (!WCSession.IsSupported) return;
        var session = WCSession.DefaultSession;
        if (session.ActivationState != WCSessionActivationState.Activated) return;

        session.UpdateApplicationContext(BuildPayload(result), out _);
    }

    public override void ActivationDidComplete(WCSession session, WCSessionActivationState activationState, NSError? error) { }
    public override void DidBecomeInactive(WCSession session) { }
    public override void DidDeactivate(WCSession session) => session.ActivateSession();

    public override void DidReceiveMessage(WCSession session, NSDictionary<NSString, NSObject> message, WCSessionReplyHandler replyHandler)
        => _ = HandleMessageAsync(message, replyHandler);

    async Task HandleMessageAsync(NSDictionary<NSString, NSObject> message, WCSessionReplyHandler replyHandler)
    {
        try
        {
            if (_getCurrentLocation is null || _refresh is null)
            {
                replyHandler(new NSDictionary<NSString, NSObject>());
                return;
            }

            var action = (message[new NSString("action")] as NSString)?.ToString();
            WeatherResult result;

            if (action == "selectPlace"
                && message[new NSString("lat")] is NSNumber latN
                && message[new NSString("lon")] is NSNumber lonN
                && _selectLocation is not null)
            {
                var name = (message[new NSString("name")] as NSString)?.ToString() ?? "";
                var loc = new GeoLocation(name, "", "", latN.DoubleValue, lonN.DoubleValue);
                await _selectLocation(loc);
                result = await _refresh(loc);
            }
            else
            {
                result = await _refresh(_getCurrentLocation());
            }

            replyHandler(BuildPayload(result));
            Push(result); // covers the case where the reply itself doesn't reach the watch in time
        }
        catch
        {
            replyHandler(new NSDictionary<NSString, NSObject>());
        }
    }

    NSDictionary<NSString, NSObject> BuildPayload(WeatherResult result)
    {
        var places = _getPlaces?.Invoke() ?? Array.Empty<WeatherResult>();
        var placeDicts = places.Select(p => (NSObject)new NSDictionary<NSString, NSObject>(
            new[] { new NSString("name"), new NSString("lat"), new NSString("lon"), new NSString("tempC"), new NSString("code") },
            new NSObject[]
            {
                new NSString(p.Location.Display),
                NSNumber.FromDouble(p.Location.Latitude),
                NSNumber.FromDouble(p.Location.Longitude),
                NSNumber.FromDouble(p.Reading.TempC),
                NSNumber.FromInt32(p.Reading.WeatherCode),
            })).ToArray();

        var dailyDicts = result.Reading.Forecast.Select(d => (NSObject)new NSDictionary<NSString, NSObject>(
            new[] { new NSString("day"), new NSString("code"), new NSString("highC"), new NSString("lowC"), new NSString("uv") },
            new NSObject[]
            {
                new NSString(d.ShortDay),
                NSNumber.FromInt32(d.WeatherCode),
                NSNumber.FromDouble(d.HighC),
                NSNumber.FromDouble(d.LowC),
                NSNumber.FromDouble(d.UvIndexMax),
            })).ToArray();

        return new NSDictionary<NSString, NSObject>(
            new[]
            {
                new NSString("lat"), new NSString("lon"), new NSString("placeName"),
                new NSString("tempC"), new NSString("feelsLikeC"), new NSString("highC"), new NSString("lowC"),
                new NSString("code"), new NSString("isDay"), new NSString("fahrenheit"),
                new NSString("uvIndex"), new NSString("daily"), new NSString("places"),
            },
            new NSObject[]
            {
                NSNumber.FromDouble(result.Location.Latitude),
                NSNumber.FromDouble(result.Location.Longitude),
                new NSString(result.Location.Display),
                NSNumber.FromDouble(result.Reading.TempC),
                NSNumber.FromDouble(result.Reading.FeelsLikeC),
                NSNumber.FromDouble(result.Reading.HighC),
                NSNumber.FromDouble(result.Reading.LowC),
                NSNumber.FromInt32(result.Reading.WeatherCode),
                NSNumber.FromBoolean(result.Reading.IsDay),
                NSNumber.FromBoolean(_getIsFahrenheit?.Invoke() ?? false),
                NSNumber.FromDouble(result.Reading.UvIndex),
                NSArray.FromNSObjects(dailyDicts),
                NSArray.FromNSObjects(placeDicts),
            });
    }
}
