namespace ActuallyUsableWorldTimeClock2;

/// <summary>One display row: a city plus its snapshot time (design doc §6.1).</summary>
public sealed class CityTimeRow
{
  public City City { get; }

  /// <summary>Formatted time, e.g. "17:13" (24-hour HH:mm).</summary>
  public string Time { get; }

  public CityTimeRow(City city, string time)
  {
    City = city;
    Time = time;
  }
}