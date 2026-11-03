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
	new City("CHC", "Pacific/Auckland", 4),
  };

  /// <summary>Computed rows (one per city), rebuilt by Refresh().</summary>
  public ObservableCollection<CityTimeRow> Rows { get; } = new();

  /// <summary>
  /// clock is optional and passes through to CityTimeProvider: omitted/null =
  /// the real clock (§6.1), so the App wiring is unchanged.
  /// </summary>
  public ClockViewModel(Func<DateTimeOffset>? clock = null)
  {
    _provider = new CityTimeProvider(clock);
  }

  /// <summary>
  /// Recomputes all rows from a single snapshot instant (§10.3): one clock
  /// read captured at the moment of the tray click, then every city converted
  /// from that SAME instant — the rows are mutually consistent by construction
  /// and can never straddle a minute boundary (App. B B10). The snapshot is
  /// not live: each subsequent open takes a fresh snapshot.
  /// </summary>
  public void Refresh()
  {
    DateTimeOffset instant = _provider.Now; // the one clock read per open
    IReadOnlyList<CityTimeRow> snapshot = _provider.GetLocalTimes(Cities, instant);

    Rows.Clear(); // computed first — no transient empty/partial window
    foreach (CityTimeRow row in snapshot)
      Rows.Add(row);
  }
}