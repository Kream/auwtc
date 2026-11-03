using System.Globalization;

namespace ActuallyUsableWorldTimeClock2;

/// <summary>Converts one snapshot instant into per-city wall-clock times (design doc §6.1).</summary>
public sealed class CityTimeProvider
{
  /// <summary>Default clock: the real system time with the local offset (§11).</summary>
  private static readonly Func<DateTimeOffset> DefaultClock = () => DateTimeOffset.Now;

  private readonly Func<DateTimeOffset> _clock;

  /// <summary>
  /// clock is optional: null/omitted = the real clock, so production wiring
  /// is unchanged. The clock is only ever read through Now — once per
  /// snapshot (§10.3): a read per city could straddle a minute boundary and
  /// display mutually inconsistent rows (App. B B10).
  /// </summary>
  public CityTimeProvider(Func<DateTimeOffset>? clock = null)
  {
    _clock = clock ?? DefaultClock;
  }

  /// <summary>
  /// One clock read: the snapshot instant. Refresh() captures this once per
  /// popup open and converts every city from that single instant (§10.3).
  /// </summary>
  public DateTimeOffset Now => _clock();

  /// <summary>
  /// Returns the city's wall-clock time at the given instant, formatted as
  /// 24-hour "HH:mm". The DateTimeOffset overload of ConvertTime converts
  /// the instant (via its embedded offset) to the zone's wall clock — DST
  /// handled automatically, no gap/ambiguity issues (§11/§14).
  /// </summary>
  public string GetLocalTime(City city, DateTimeOffset instant)
  {
    // §12: an unknown/invalid zone id degrades to "--:--" so the popup
    // still shows all three rows.
    try
    {
      TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId);
      DateTimeOffset cityNow = TimeZoneInfo.ConvertTime(instant, zone);
      return cityNow.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
    catch (TimeZoneNotFoundException)
    {
      return "--:--";
    }
    catch (InvalidTimeZoneException)
    {
      return "--:--";
    }
  }

  /// <summary>
  /// Converts every city from ONE shared instant, preserving city order
  /// (§10.1: LAX → JFK → LHR). A bad zone degrades only its own row (§12).
  /// </summary>
  public IReadOnlyList<CityTimeRow> GetLocalTimes(IEnumerable<City> cities, DateTimeOffset instant)
  {
    List<CityTimeRow> rows = new();
    foreach (City city in cities)
      rows.Add(new CityTimeRow(city, GetLocalTime(city, instant)));
    return rows;
  }
}