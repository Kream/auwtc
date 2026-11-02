namespace ActuallyUsableWorldTimeClock2;

/// <summary>Static description of a displayed city (design doc §6.1).</summary>
public sealed class City
{
  /// <summary>Short display name, e.g. "LAX".</summary>
  public string Name { get; }

  /// <summary>IANA time zone ID, e.g. "America/Los_Angeles" (design doc §10).</summary>
  public string TimeZoneId { get; }

  /// <summary>Display order: 1 = LAX, 2 = JFK, 3 = LHR.</summary>
  public int Order { get; }

  public City(string name, string timeZoneId, int order)
  {
    Name = name;
    TimeZoneId = timeZoneId;
    Order = order;
  }
}