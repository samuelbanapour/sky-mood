using SkyMood;

// Sky Mood — console head. Exercises the whole Core data layer (geocode → satellite → live → cache)
// and prints a little ANSI weather card. Mostly for development/verification; the real fun is the
// MAUI app, but this proves the offline + satellite plumbing without the heavy mobile toolchain.
//
// Usage:
//   dotnet run --project SkyMood.Console -- "Tokyo"
//   dotnet run --project SkyMood.Console -- --inbox /path/to/groundstation "London"
//   dotnet run --project SkyMood.Console -- --list        (show everything cached offline)

string? inbox = null;
bool listCache = false;
var words = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--inbox" when i + 1 < args.Length: inbox = args[++i]; break;
        case "--list": listCache = true; break;
        default: words.Add(args[i]); break;
    }
}

var cacheDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyMood", "cache");
var inboxDir = inbox ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyMood", "groundstation");

var service = WeatherService.CreateDefault(cacheDir, inboxDir);

if (listCache)
{
    var places = service.CachedPlaces();
    if (places.Count == 0) { Console.WriteLine("Nothing cached yet. Look up a place first."); return; }
    Console.WriteLine($"\n  {places.Count} place(s) saved for offline use:\n");
    foreach (var p in places)
        Console.WriteLine($"   • {p.Location.Display,-34} {p.Reading.TempC,5:0}°C  {p.Reading.Visual.Label}");
    Console.WriteLine();
    return;
}

var query = words.Count > 0 ? string.Join(' ', words) : "London";

Console.WriteLine($"\n  Looking up “{query}”…");
var geocoder = new GeocodingService();
var matches = await geocoder.SearchAsync(query);
var location = matches.Count > 0 ? matches[0] : GeocodingService.Default;

WeatherResult result;
try { result = await service.GetAsync(location); }
catch (WeatherUnavailableException ex) { Console.WriteLine($"\n  {ex.Message}\n"); return; }

Render(result);

static void Render(WeatherResult r)
{
    string esc = ((char)27).ToString(); // ANSI escape, built from its code point (no raw bytes in source)
    var v = r.Reading.Visual;
    string badge = r.Channel switch
    {
        DataChannel.Live      => $"{esc}[42;30m LIVE {esc}[0m",
        DataChannel.Satellite => $"{esc}[46;30m SATELLITE {esc}[0m",
        _                     => $"{esc}[43;30m OFFLINE {esc}[0m",
    };

    Console.WriteLine();
    Console.WriteLine("  ┌────────────────────────────────────────────┐");
    foreach (var line in Scene(v.Kind, r.Reading.IsDay))
        Console.WriteLine($"  │  {line,-40}  │");
    Console.WriteLine("  ├────────────────────────────────────────────┤");
    Console.WriteLine($"  │  {v.Emoji}  {r.Reading.TempC,4:0}°C   {v.Label,-26} │");
    Console.WriteLine($"  │  {r.Location.Display,-42} │");
    Console.WriteLine("  ├────────────────────────────────────────────┤");
    Console.WriteLine($"  │  Feels {r.Reading.FeelsLikeC,3:0}°   Humidity {r.Reading.Humidity,3}%          │");
    Console.WriteLine($"  │  Wind {r.Reading.WindKph,4:0} km/h   High {r.Reading.HighC,3:0}° / Low {r.Reading.LowC,3:0}°  │");
    Console.WriteLine("  └────────────────────────────────────────────┘");
    Console.WriteLine($"     {badge}  {r.Note}   (source: {r.SourceName})");
    Console.WriteLine();
}

// A tiny ASCII sky so even the console build is a little bit fun.
static string[] Scene(WeatherKind kind, bool day) => kind switch
{
    WeatherKind.Clear     => day
        ? new[] { "        \\   |   /        ", "         .-\"\"\"-.         ", "    --- (  sun  ) ---    ", "         '-...-'         " }
        : new[] { "     *      .     *      ", "         .-\"\"\"-.    *    ", "    *   (  moon )        ", "      .     '-...-'   *  " },
    WeatherKind.FewClouds => new[] { "     \\  |  /   .--.      ", "      .-.    .'    '.    ", "   --(   )--(  cloud )   ", "      '-'    '.____.'    " },
    WeatherKind.Cloudy    => new[] { "      .--.      .--.     ", "   .-(    ).--.(    ).   ", "  (    cloudy      )    ", "   '--'----'--'----'    " },
    WeatherKind.Fog       => new[] { "   _ _ _ _ _ _ _ _ _    ", "    _ _ _ _ _ _ _ _ _   ", "   _ _ _  fog  _ _ _    ", "    _ _ _ _ _ _ _ _ _   " },
    WeatherKind.Rain      => new[] { "      .--.    .--.       ", "   .-(    ).-(    ).     ", "  (   rain clouds   )   ", "   ' ' ' ' ' ' ' ' '    " },
    WeatherKind.Snow      => new[] { "      .--.    .--.       ", "   .-(    ).-(    ).     ", "  (   snow  clouds  )   ", "   *  *  *  *  *  *  *   " },
    WeatherKind.Storm     => new[] { "      .--.    .--.       ", "   .-(    ).-(    ).     ", "  (   storm clouds  )   ", "   ' /_ ' ' /_ ' ' '    " },
    _                     => new[] { "                        ", "       (  weather )      ", "                        ", "                        " },
};
