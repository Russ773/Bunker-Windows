namespace Bunker;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Bunker.exe --check-widget in.json out.json: judge a widget file without opening any window (used by the check on GitHub).
        if (args.Length >= 3 && args[0] == "--check-widget") return WidgetCheck.Run(args[1], args[2]);

        // Only one Bunker at a time: a second launch brings the running one forward and quits. A restart Bunker asked for itself waits for the old copy to finish.
        var restarting = Environment.GetEnvironmentVariable("BUNKER_RESTARTING") == "1";
        Environment.SetEnvironmentVariable("BUNKER_RESTARTING", null);
        using var single = SingleInstance.TryStart(AppSettings.Folder, restarting);
        if (single is null) return 0;

        ApplicationConfiguration.Initialize();
        Theme.Init(AppSettings.Load().LightTheme); // must happen before any window exists

        MainForm? main = null;
        using (var splash = new SplashForm())
            splash.RunFor(1800, () => main = new MainForm());

        main!.Shown += (_, _) => single.Listen(() => { try { main.BeginInvoke(main.BringForward); } catch (InvalidOperationException) { } });
        Application.Run(main);
        return 0;
    }
}
