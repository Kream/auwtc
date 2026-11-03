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

  // 1. Advancing clock straddling a minute boundary: ALL rows must use the
  //    FIRST read (§10.3). The old per-city implementation re-read the clock
  //    per row and would show LAX 12:59 / JFK 15:59 / LHR 21:00 — mixed.
  [Fact]
  public void Refresh_AdvancingClockAcrossMinuteBoundary_AllRowsUseFirstRead()
  {
    DateTimeOffset start = Parse(BeforeBoundary);
    ProbeClock probe = new(start, TimeSpan.FromSeconds(1)); // crosses 21:00 on read 2
    ClockViewModel vm = ViewModel(probe.Next);

    vm.Refresh();

    Assert.Equal(1, probe.Reads); // exactly one clock read per snapshot
    for (int i = 0; i < vm.Cities.Count; i++)
      Assert.Equal(ExpectedMinute(vm.Cities[i], start), vm.Rows[i].Time); // all from the SAME instant
  }

  // 2. Constant clocks pinned at the :59.999 / :00.001 boundaries — the task's
  //    literal acceptance: each open shows the exact mutually-consistent triple.
  [Theory]
  [InlineData(BeforeBoundary, "12:59", "15:59", "20:59")]
  [InlineData(AfterBoundary, "13:00", "16:00", "21:00")]
  public void Refresh_ConstantClock_PinsBoundaryMinutes(string iso, string lax, string jfk, string lhr)
  {
    DateTimeOffset instant = Parse(iso);
    ClockViewModel vm = ViewModel(() => instant);

    vm.Refresh();

    Assert.Equal(new[] { lax, jfk, lhr }, vm.Rows.Select(r => r.Time).ToArray());
  }

  // 3. §10.3: each subsequent open takes a FRESH snapshot (no caching, no live updates).
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
  //    throws. Rows remain mutually consistent across the transition.
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

    Assert.Equal(expectedLax, vm.Rows[0].Time); // LAX row only; same instant feeds all rows
  }

  // 5. §12: an invalid zone degrades ONLY its own row; the other rows and the row
  //    count are unaffected (the popup still shows every row).
  [Fact]
  public void Refresh_InvalidZone_DegradesOnlyThatRow()
  {
    DateTimeOffset instant = Parse(AfterBoundary);
    ClockViewModel vm = ViewModel(() => instant);
    vm.Cities.Add(new City("BAD", "Not/AZone", 4));

    vm.Refresh();

    Assert.Equal(4, vm.Rows.Count);
    Assert.Equal("13:00", vm.Rows[0].Time);  // LAX unaffected
    Assert.Equal("16:00", vm.Rows[1].Time);  // JFK unaffected
    Assert.Equal("21:00", vm.Rows[2].Time);  // LHR unaffected
    Assert.Equal("--:--", vm.Rows[3].Time);  // the bad row degrades
  }
}