namespace NtfsRescue.Core;

/// <summary>Stav aplikácie zdieľaný medzi UI a jadrom.</summary>
public static class AppState
{
    private static int _surfaceBusy;
    private static volatile bool _diskBusy;

    /// <summary>Prebieha aspoň jedna operácia, ktorá nesmie byť prerušená resetom USB.</summary>
    public static bool Busy => Volatile.Read(ref _surfaceBusy) > 0 || _diskBusy;

    public static void SurfaceBusy(bool busy)
    {
        if (busy) Interlocked.Increment(ref _surfaceBusy);
        else if (Interlocked.Decrement(ref _surfaceBusy) < 0)
            Interlocked.Exchange(ref _surfaceBusy, 0);
    }

    public static void DiskBusy(bool busy) => _diskBusy = busy;
}
