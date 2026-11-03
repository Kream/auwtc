using System.Globalization;
using ActuallyUsableWorldTimeClock2;
using Xunit;

namespace WorldTimeZoneClock.Tests;

/// <summary>
/// Snapshot-consistency suite (design doc §10.3, App. B B10): every row of one
/// popup open derives from ONE identical instant. Two complementary proofs:
/// a PROBE clock (advancing per read + counting reads) proves Refresh performs
/// exactly one clock read and uses only its FIRST value — with the old per-city
/// reads this fails (later rows jump a minute); CONSTANT clocks pinned at the
/// :59.999 / :00.001 boundaries lock in the mutually-consistent contract.
/// ROSTER-AGNOSTIC (build #14, App. B B14): the documented trio (LAX/JFK/LHR)
/// is asserted BY NAME — §4 specifies those three exist, not that they are the
/// only ones. No case assumes the default roster's count, order, or exclusivity:
/// adding a City to ClockViewModel.Cities (the §13 growth path) keeps the suite
/// green. (Pre-#14, a user-added 4th city — Pacific/Auckland — legitimately
/// failed 3 cases while the app behaved correctly; the tests were the defect.)
/// </summary>
public sealed class ClockSnapshotTests
{
  // 2024-01-15: LAX on PST (UTC-8), JFK on EST (UTC-5), LHR on GMT (UTC+0) —
  // stable historical offsets, no DST transitions near the date.
  private const string BeforeBoundary = "2024-01-15T20:59:59.999Z"; // …59.999s
  private const string AfterBoundary = "2024-01-15T21:00:00.001Z"; // …00.001s

  /// <summary>Test double: returns first + step*Reads and counts reads.</summary>
  private sealed class ProbeClock
  {
    private readonly DateTimeOffset _first;
    private readonly TimeSpan _step;
    public int Reads { get; private set; }

    public ProbeClock(DateTimeOffset first, TimeSpan step)
    {
      _first = first;
      _step = step;
    }

    public DateTimeOffset Next()
    {
      DateTimeOffset value = _first.AddTicks(_step.Ticks * Reads);
      Reads++;
      return value;
    }
  }

  private static DateTimeOffset Parse(string iso) =>
      DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);

  private static ClockViewModel ViewModel(Func<DateTimeOffset> clock) => new(clock);

  /// <summary>Independent expected minute for a city at an instant (tz-db-derived).</summary>
  private static string ExpectedMinute(City city, DateTimeOffset instant)
  {
    TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId);
    return TimeZoneInfo.ConvertTime(instant, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
  }

  /// <summary>Row time for a city BY NAME — roster position/count is never assumed (build #14).</summary>
  private static string TimeOf(ClockViewModel vm, string name) =>
      vm.Rows.Single(r => r.City.Name == name).Time;

  // 1. Advancing clock straddling a minute boundary: ALL rows must use the
  //    FIRST read (§10.3). Already roster-agnostic — expectations derive from
  //    vm.Cities itself, so any roster (and any count) is checked wholesale.
  [Fact]
  public void Refresh_AdvancingClockAcrossMinuteBoundary_AllRowsUseFirstRead()
  {
    DateTimeOffset start = Parse(BeforeBoundary);
    ProbeClock probe = new(start, TimeSpan.FromSeconds(1)); // crosses 21:00 on read 2
    ClockViewModel vm = ViewModel(probe.Next);

    vm.Refresh();

    Assert.Equal(1, probe.Reads); // exactly one clock read per snapshot
    Assert.Equal(vm.Cities.Count, vm.Rows.Count); // §13: one row per city, any roster
    for (int i = 0; i < vm.Cities.Count; i++)
      Assert.Equal(ExpectedMinute(vm.Cities[i], start), vm.Rows[i].Time); // all from the SAME instant
  }

  // 2. Constant clocks pinned at the :59.999 / :00.001 boundaries — the task's
  //    literal acceptance: each open shows the exact mutually-consistent set.
  //    Build #14: pins asserted BY NAME (added cities neither break nor shift
  //    them) + one-row-per-city count invariant + whole-roster same-instant check.
  [Theory]
  [InlineData(BeforeBoundary, "12:59", "15:59", "20:59")]
  [InlineData(AfterBoundary, "13:00", "16:00", "21:00")]
  public void Refresh_ConstantClock_PinsBoundaryMinutes(string iso, string lax, string jfk, string lhr)
  {
    DateTimeOffset instant = Parse(iso);
    ClockViewModel vm = ViewModel(() => instant);

    vm.Refresh();

    Assert.Equal(vm.Cities.Count, vm.Rows.Count); // §13: one row per city, any roster
    Assert.Equal(lax, TimeOf(vm, "LAX"));          // the documented trio, by name
    Assert.Equal(jfk, TimeOf(vm, "JFK"));
    Assert.Equal(lhr, TimeOf(vm, "LHR"));
    for (int i = 0; i < vm.Cities.Count; i++)      // every row — documented or added —
      Assert.Equal(ExpectedMinute(vm.Cities[i], instant), vm.Rows[i].Time); // …from the SAME instant
  }

  // 3. §10.3: each subsequent open takes a FRESH snapshot (no caching, no live updates).
  //    Already roster-agnostic — compares whole snapshots, no per-city expectations.
  [Fact]
  public void Refresh_SecondOpen_TakesFreshSnapshot()
  {
    DateTimeOffset start = Parse(BeforeBoundary);
    ProbeClock probe = new(start, TimeSpan.FromMinutes(90));
    ClockViewModel vm = ViewModel(probe.Next);

    vm.Refresh();
    string[] first = vm.Rows.Select(r => r.Time).ToArray();

    vm.Refresh();
    string[] second = vm.Rows.Select(r => r.Time).ToArray();

    Assert.Equal(2, probe.Reads);          // one read per open
    Assert.NotEqual(first, second);        // the second open reflects its own instant
  }

  // 4. DST edges: ConvertTime(DateTimeOffset, tz) is instant-based — it maps the
  //    spring-forward gap and the fall-back ambiguity deterministically and never
  //    throws. Build #14: LAX asserted BY NAME (was Rows[0] — index assumed LAX first).
  [Theory]
  [InlineData("2024-03-10T09:59:59Z", "01:59")] // just before the 02:00 gap (PST)
  [InlineData("2024-03-10T10:00:00Z", "03:00")] // gap: 02:00 does not exist -> 03:00 (PDT)
  [InlineData("2024-11-03T08:59:59Z", "01:59")] // fall-back: first 01:59 (PDT)
  [InlineData("2024-11-03T09:00:00Z", "01:00")] // fall-back: second 01:00 (PST)
  public void Refresh_DstEdges_ConvertDeterministically(string iso, string expectedLax)
  {
    DateTimeOffset instant = Parse(iso);
    ClockViewModel vm = ViewModel(() => instant);

    vm.Refresh();

    Assert.Equal(expectedLax, TimeOf(vm, "LAX")); // LAX row by name; same instant feeds all rows
  }

  // 5. §12: an invalid zone degrades ONLY its own row. Build #14: roster-agnostic —
  //    the healthy baseline is the app's OWN first snapshot (same constant clock),
  //    so the case neither knows nor cares what the default roster contains; the
  //    BAD city is appended last, so indices 0..baseline-1 are the healthy rows.
  [Fact]
  public void Refresh_InvalidZone_DegradesOnlyThatRow()
  {
    DateTimeOffset instant = Parse(AfterBoundary);
    ClockViewModel vm = ViewModel(() => instant);

    vm.Refresh();
    string[] healthyBaseline = vm.Rows.Select(r => r.Time).ToArray(); // pre-addition rendered values

    vm.Cities.Add(new City("BAD", "Not/AZone", int.MaxValue));

    vm.Refresh();

    Assert.Equal(vm.Cities.Count, vm.Rows.Count);          // healthy roster + the bad row
    for (int i = 0; i < healthyBaseline.Length; i++)        // §12: ONLY the bad row degrades
      Assert.Equal(healthyBaseline[i], vm.Rows[i].Time);     // (healthy rows byte-identical)
    Assert.Equal("--:--", TimeOf(vm, "BAD"));               // the bad row degrades
  }
}