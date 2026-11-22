namespace NtfsRescue.Core;

/// <summary>Stav aplikácie zdieľaný medzi UI a jadrom.</summary>
public static class AppState
{
    /// <summary>Prebieha kontrola povrchu, oprava alebo vymazanie disku.</summary>
    public static volatile bool Busy;
}
