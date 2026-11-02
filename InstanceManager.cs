namespace ActuallyUsableWorldTimeClock2;

/// <summary>
/// Single-instance enforcement via a named mutex (design doc §12).
/// A second launch sees the mutex already held and exits immediately.
/// </summary>
public sealed class InstanceManager : IDisposable
{
  private const string MutexName = @"Local\WorldTimeZoneClock";

  private readonly Mutex _mutex;
  private readonly bool _isPrimary;

  public InstanceManager()
  {
    _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
    _isPrimary = createdNew;
  }

  /// <summary>True if this process is the one allowed to run (first launch).</summary>
  public bool IsPrimaryInstance => _isPrimary;

  public void Dispose()
  {
    if (_isPrimary)
    {
      try { _mutex.ReleaseMutex(); }
      catch (ApplicationException) { /* already released */ }
    }
    _mutex.Dispose();
  }
}