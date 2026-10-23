using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Bunker;

/// <summary>
/// Lets only one Bunker run at a time (per Windows user and data folder). Starting it again brings the running one to the front,
/// even if it is hidden in the tray, and the new copy quits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private RegisteredWaitHandle? _listener;

    private SingleInstance(Mutex mutex, EventWaitHandle show) { _mutex = mutex; _show = show; }

    /// <summary>
    /// Names are made from the data folder, so a copy pointed at a different folder (the test hooks do this) is a separate Bunker.
    /// </summary>
    internal static string Name(string dataFolder) =>
        "Bunker-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataFolder.TrimEnd('\\', '/').ToLowerInvariant())))[..16];

    /// <summary>
    /// Returns null when Bunker is already running (after asking that copy to come to the front). With <paramref name="waitForPrevious"/>
    /// the old copy is given a few seconds to finish first, for a restart that Bunker started itself.
    /// </summary>
    public static SingleInstance? TryStart(string dataFolder, bool waitForPrevious = false)
    {
        var name = Name(dataFolder);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\" + name + "-show"); // created first so a signal sent while the first copy is still starting is not lost
        var mutex = new Mutex(false, "Local\\" + name);
        bool owned;
        try { owned = mutex.WaitOne(waitForPrevious ? TimeSpan.FromSeconds(10) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { owned = true; } // the previous copy was killed, which is fine
        if (owned) return new SingleInstance(mutex, show);

        AllowSetForegroundWindow(-1); // this launch has the user's click behind it, so let the running copy take the focus
        show.Set();
        show.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>Calls <paramref name="onSecondLaunch"/> (on a pool thread) each time Bunker is started again.</summary>
    public void Listen(Action onSecondLaunch) =>
        _listener = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => onSecondLaunch(), null, Timeout.Infinite, false);

    public void Dispose()
    {
        _listener?.Unregister(null);
        _show.Dispose();
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
