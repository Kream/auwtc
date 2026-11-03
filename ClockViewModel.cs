using System.Collections.ObjectModel;

namespace ActuallyUsableWorldTimeClock2;

/// <summary>
/// Holds the city list and the snapshot rows shown by the popup (design doc §6.2).
/// Refresh() recomputes all rows — called when the popup opens (snapshot at click).
/// </summary>
public sealed class ClockViewModel
{
  private readonly CityTimeProvider _provider;

  /// <summary>Cities in display order: LAX, JFK, LHR.</summary>
  public ObservableCollection<City> Cities { get; } = new()
  {
    new City("LAX", "America/Los_Angeles", 1),
    new City("JFK", "America/New_York", 2),
    new City("LHR", "Europe/London", 3),
  };

  /// <summary>Computed rows (one per city), rebuilt by Refresh().</summary>
  public ObservableCollection<CityTimeRow> Rows { get; } = new();

  /// <summary>
  /// clock is optional and passes through to CityTimeProvider: omitted/null =
  /// the real clock (§6.1), so the App wiring is unchanged. An injected clock
  /// delegate is consulted once per row inside Refresh(), which makes the
  /// per-city snapshot race deterministically testable (App. B B8).
  /// </summary>
  public ClockViewModel(Func<DateTimeOffset>? clock = null)
  {
    _provider = new CityTimeProvider(clock);
  }

  /// <summary>
  /// Recomputes all rows from the current time. Call once per popup open;
  /// the popup does not live-refresh while open.
  /// </summary>
  public void Refresh()
  {
    Rows.Clear();
    foreach (City city in Cities)
      Rows.Add(new CityTimeRow(city, _provider.GetLocalTime(city)));
  }
}