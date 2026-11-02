using System.Globalization;

namespace ActuallyUsableWorldTimeClock2;

/// <summary>Computes the current time for a city (design doc §6.1).</summary>
public sealed class CityTimeProvider
{
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
      DateTime cityNow = TimeZoneInfo.ConvertTime(DateTime.Now, zone);
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