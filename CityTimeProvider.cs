using System.Globalization;

namespace ActuallyUsableWorldTimeClock2;

/// <summary>Computes the current time for a city (design doc §6.1).</summary>
public sealed class CityTimeProvider
{
  /// <summary>Default clock: the real system time with the local offset (§11).</summary>
  private static readonly Func<DateTimeOffset> DefaultClock = () => DateTimeOffset.Now;

  private readonly Func<DateTimeOffset> _clock;

  /// <summary>
  /// clock is optional: null/omitted = the real clock, so production wiring
  /// is unchanged. An injected clock is read once per GetLocalTime call, which
  /// makes every per-city snapshot read — and any minute-boundary straddle
  /// across the three rows — deterministically drivable in tests (App. B B8).
  /// </summary>
  public CityTimeProvider(Func<DateTimeOffset>? clock = null)
  {
    _clock = clock ?? DefaultClock;
  }

  /// <summary>
  /// Returns the city's current local time formatted as 24-hour "HH:mm".
  /// Uses IANA zone IDs with TimeZoneInfo, which handles DST automatically.
  /// </summary>
  public string GetLocalTime(City city)
  {
    // §12: an unknown/invalid zone id degrades to "--:--" so the popup
    // still shows all three rows.
    try
    {
      TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId);
      // DateTimeOffset overload: converts the reading's instant (via its
      // offset) to the zone's wall clock — DST handling as before.
      DateTimeOffset cityNow = TimeZoneInfo.ConvertTime(_clock(), zone);
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
}