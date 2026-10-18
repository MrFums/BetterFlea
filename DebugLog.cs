namespace BetterFlea;

// Extra logging for testing, switched on by debugLogging. Off by default so the server console stays quiet.
//  - Summary: one line per startup step (filter counts, offer counts...). Live as soon as the config is read.
//  - Write:   every single price floor, drift and flip decision. Only bound once the server has
//             finished loading, so the thousands of startup offers don't flood the log.
internal static class DebugLog
{
    private static Action<string>? _summary;
    private static Action<string>? _write;

    public static void BindSummaries(ConfigManager configManager, Action<string> write)
    {
        _summary = configManager.Mod.DebugLogging ? write : null;
    }

    public static void Bind(ConfigManager configManager, Action<string> write)
    {
        _write = configManager.Mod.DebugLogging ? write : null;
    }

    public static void Summary(Func<string> message)
    {
        _summary?.Invoke("[BetterFlea] " + message());
    }

    public static void Write(Func<string> message)
    {
        _write?.Invoke("[BetterFlea] " + message());
    }
}
