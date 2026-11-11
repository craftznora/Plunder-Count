using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using PlunderCount;

// --standby: the invisible process that waits for Fortnite to start ("open with Fortnite"). No console, nothing else.
if (args.Contains("--standby"))
{
    var dataIndex = Array.IndexOf(args, "--data");
    return OperatingSystem.IsWindows()
        ? FortniteWatch.RunStandby(dataIndex >= 0 && dataIndex + 1 < args.Length ? args[dataIndex + 1] : Store.DefaultFolder())
        : 1;
}

// Windows app: open a console window (or write into the terminal for command line options).
if (OperatingSystem.IsWindows())
{
    NativeConsole.Ensure(attachToParent: args.Any(a => a is "--once" or "--help" or "-h" or "--overlay-preview"));
}
Console.OutputEncoding = Encoding.UTF8;

// A crash must not close the window before the message can be read (the exe is usually double-clicked).
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    ErrorLog.Write("Crash", e.ExceptionObject);
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine();
    Console.WriteLine("Unexpected error: " + e.ExceptionObject);
    Console.ResetColor();
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("Press any key to close.");
        try { Console.ReadKey(intercept: true); } catch { }
    }
};

// ---- Arguments ---------------------------------------------------------------------------
string? dataDir = null, demosArg = null;
var once = false;
var reset = false;
var auto = false;          // opened by the standby when Fortnite started: closes itself after Fortnite
var afterUpdate = false;   // restarted by the updater: waits for the previous instance to quit
string? previewPath = null;
var demo = false;          // --demo: previews use made-up data (and also write the report)
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--data": dataDir = args[++i]; break;
        case "--demos": demosArg = args[++i]; break;
        case "--once": once = true; break;
        case "--reset-config": reset = true; break;
        case "--overlay-preview": previewPath = args[++i]; break;
        case "--demo": demo = true; break;
        case "--auto": auto = true; break;
        case "--after-update": afterUpdate = true; break;
        case "-h":
        case "--help":
            Console.WriteLine("""
                Plunder Count

                Options:
                  --demos <folder>    replays folder (default %LOCALAPPDATA%\FortniteGame\Saved\Demos)
                  --data <folder>     data folder (default %APPDATA%\PlunderCount)
                  --once              analyze the finished replays, then quit
                  --reset-config      ask the setup questions again
                  --overlay-preview <file.png>   save previews of the overlay, then quit
                """);
            return 0;
    }
}

dataDir ??= Store.DefaultFolder();
var store = new Store(dataDir);
ErrorLog.FilePath = store.ErrorLogPath;
var interactive = ConsoleUi.Interactive;
if (interactive && !once && previewPath == null)
{
    ConsoleUi.EnsureSize(110, 40);
}

// One instance at a time: two would count the same matches and fight over the shortcuts.
using var instance = new Mutex(false, AppInfo.InstanceMutexFor(dataDir));
if (previewPath == null && !AcquireInstance(instance, afterUpdate ? TimeSpan.FromSeconds(20) : TimeSpan.Zero))
{
    if (auto)
    {
        return 0; // opened with Fortnite while the player had already opened it: nothing to do
    }
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("  The chest counter is already open: look in the taskbar.");
    Console.ResetColor();
    if (interactive)
    {
        Console.WriteLine("  (this window closes in 10 seconds)");
        Thread.Sleep(10_000);
    }
    return 0;
}

// ---- Configuration -----------------------------------------------------------------------
var config = reset ? null : store.LoadConfig();
var firstRun = config == null;
config ??= new Config();

var appName = "Plunder Count";
ConsoleUi.SetTitle($"{appName} {AppInfo.VersionText}");

if (demosArg != null) config.DemosFolder = demosArg;
var defaultDemos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");
if (string.IsNullOrWhiteSpace(config.DemosFolder))
{
    config.DemosFolder = defaultDemos;
}

if (previewPath != null)
{
    // Development: draws the overlay off-screen and saves it as PNG (idle + match in progress), then quits.
    var previewData = demo ? Demo.Data() : Profiles.View(store.LoadData(), config);
    if (!demo)
    {
        // In memory only: the 3 latest matches count as "this session", to show a realistic session line.
        foreach (var m in previewData.Matches.OrderByDescending(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).Take(3))
        {
            m.MatchStartUtc = DateTime.UtcNow;
        }
    }
    var previewDir = Path.GetDirectoryName(Path.GetFullPath(previewPath)) ?? ".";
    if (demo)
    {
        File.WriteAllText(Path.Combine(previewDir, "report.html"), ReportWriter.Report(config, previewData, Profiles.List(previewData)));
        File.WriteAllText(Path.Combine(previewDir, "overlay.html"), ReportWriter.Overlay(OverlayModel.Build(previewData, null, false)));
        File.WriteAllText(Path.Combine(previewDir, "stream.html"), ReportWriter.Stream(OverlayModel.Build(previewData, null, true)));
        Console.WriteLine($"Preview: {Path.Combine(previewDir, "report.html")}");
    }
    if (!OperatingSystem.IsWindows())
    {
        return 1;
    }
    NativeOverlay.SetAppearance(config.OverlaySize, config.OverlayOpacity);
    // Locked, as in game. Each style in its three states: idle, match in progress, live count.
    NativeOverlay.Start(-20000, -20000, true, (_, _) => { }, _ => { });
    var fakeLive = new MatchRecord { ChestsOpened = 12, TreasureFinds = { new TreasureFind { Item = "Trésor" } } };
    foreach (var style in NativeOverlay.Styles)
    {
        NativeOverlay.SetStyle(style);
        foreach (var (suffix, live, inProgress) in new[] { ("-idle", (MatchRecord?)null, false), ("-inprogress", null, true), ("-live", fakeLive, true) })
        {
            NativeOverlay.Update(OverlayModel.Build(previewData, live, inProgress));
            Thread.Sleep(600);
            var name = Path.GetFileNameWithoutExtension(previewPath) + (style == "classic" ? "" : "-" + style) + suffix + ".png";
            var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(previewPath)) ?? ".", name);
            Console.WriteLine(NativeOverlay.Snapshot(file) ? $"Preview: {file}" : $"Capture failed: {file}");
        }
        // Unlocked (padlock and backdrop visible), idle.
        NativeOverlay.Update(OverlayModel.Build(previewData, null, false));
        NativeOverlay.ToggleLock();
        Thread.Sleep(600);
        var unlocked = Path.Combine(previewDir, Path.GetFileNameWithoutExtension(previewPath) + (style == "classic" ? "" : "-" + style) + "-unlocked.png");
        Console.WriteLine(NativeOverlay.Snapshot(unlocked) ? $"Preview: {unlocked}" : $"Capture failed: {unlocked}");
        NativeOverlay.ToggleLock();
        Thread.Sleep(300);
    }
    return 0;
}

LowerPriority();
var data = store.LoadData();
var tracker = new Tracker(store, config, data);
// Before any scan: otherwise replays analyzed during the setup would be analyzed a second time right after.
var reanalyzing = tracker.UpgradeIfNeeded();
var reportOpened = false;

// ---- First launch: setup screens ----------------------------------------------------------
if (firstRun && interactive)
{
    // Step 2: the replays folder.
    ConsoleUi.Screen(appName, "1/4");
    ConsoleUi.Text("Your replays", ConsoleColor.White);
    ConsoleUi.Blank();
    if (!Directory.Exists(config.DemosFolder))
    {
        ConsoleUi.Text("✖ Replays folder not found:", ConsoleColor.Red);
        ConsoleUi.Hint("  " + config.DemosFolder);
        ConsoleUi.Blank();
        ConsoleUi.Text("Fortnite creates it with the first recorded match. If you just turned replays on, that is expected.");
        var where = ConsoleUi.Menu(new[]
        {
            "Keep this folder",
            "Enter another folder",
        });
        if (where == 1)
        {
            config.DemosFolder = ConsoleUi.Input("Path of the replays folder: ", config.DemosFolder);
            ConsoleUi.Blank();
        }
    }
    else
    {
        ConsoleUi.Text("✔ Replays folder found:", ConsoleColor.Green);
        ConsoleUi.Hint("  " + config.DemosFolder);
    }
    ConsoleUi.Blank();
    ConsoleUi.Text("Important: in Fortnite, turn on  Settings → Game → Replays → Record Replays.", ConsoleColor.Yellow);
    ConsoleUi.Hint("Without it, Fortnite saves no replay and the tool has nothing to count.");
    ConsoleUi.Blank();

    // Replays already on the PC.
    var existing = tracker.UnprocessedFiles().Where(f => ReplayHeaderProbe.IsLive(f.FullName) != true).ToList();
    var analyzeNow = false;
    if (existing.Count > 0)
    {
        var present = L.Count(existing.Count, "replay is already on your PC", "replays are already on your PC");
        ConsoleUi.Text($"{present}. Count them too?");
        analyzeNow = ConsoleUi.Menu(new[]
        {
            "Yes, analyze them now",
            "No, only my next matches",
        }) == 0;
        if (!analyzeNow)
        {
            tracker.MarkSkipped(existing);
        }
    }
    else
    {
        ConsoleUi.WaitEnter("Enter to continue");
    }
    store.SaveConfig(config);

    if (analyzeNow)
    {
        ConsoleUi.Screen(appName, "analysis");
        ConsoleUi.Text("Analyzing your replays…", ConsoleColor.White);
        ConsoleUi.Hint("The report opens in your browser and fills in as the replays are read.");
        ConsoleUi.Blank();
        ReportWriter.Progress = new ScanProgress(0, existing.Count);
        ReportWriter.WriteAll(store, config, data);
        if (config.OpenReportOnStart)
        {
            Open(store.ReportPath);
            reportOpened = true;
        }
        tracker.Scan();
        tracker.NewProfilesFound = false;
        ConsoleUi.Blank();
        if (Profiles.List(data).Count < 2)
        {
            ConsoleUi.WaitEnter("Enter to continue");
        }
    }

    if (Profiles.List(data).Count >= 2)
    {
        ConsoleUi.Screen(appName, "profile");
        ChooseProfile(data, config, store);
    }

    // Step 3: the overlay (some players only want the report).
    if (OperatingSystem.IsWindows())
    {
        ConsoleUi.Screen(appName, "2/4");
        ConsoleUi.Text("Show an overlay in game?", ConsoleColor.White);
        ConsoleUi.Blank();
        ConsoleUi.Text("A small always-on-top window shows your chests while you play.");
        ConsoleUi.Hint("Without it, your statistics are in the report. Can be changed at any time in the settings.");
        config.ShowOverlay = ConsoleUi.Menu(new[]
        {
            "Yes, show the overlay",
            "No, only the report",
        }) == 0;
        store.SaveConfig(config);
    }

    // Step 4: open with Fortnite. Nothing is recommended: most players simply start it themselves.
    if (OperatingSystem.IsWindows() && AppInfo.ExePath != null)
    {
        ConsoleUi.Screen(appName, "3/4");
        ConsoleUi.Text("Open the counter automatically when Fortnite starts?", ConsoleColor.White);
        ConsoleUi.Blank();
        ConsoleUi.Text("A tiny invisible process waits for Fortnite to start, then opens the counter.\nThe counter closes by itself after Fortnite.");
        ConsoleUi.Hint("Can be changed at any time in the settings. Turn it off before deleting PlunderCount.exe.");
        var withFortnite = ConsoleUi.Menu(new[]
        {
            "No, I will start it myself",
            "Yes, open it with Fortnite",
        }) == 1;
        config.StartWithFortnite = withFortnite && FortniteWatch.Apply(true, dataDir);
        store.SaveConfig(config);
    }

    // Step 5: ready.
    ConsoleUi.Screen(appName, "4/4");
    ConsoleUi.Text("All set!", ConsoleColor.Green);
    ConsoleUi.Blank();
    ConsoleUi.Text("Keep this window open (you can minimize it) while you play:\nthe chests of each match are counted a few seconds after it ends.");
    ConsoleUi.Blank();
    ConsoleUi.Text("Everything works with the arrow keys and Enter. Settings and help are in the tabs at the top of the screen.");
    if (config.ShowOverlay)
    {
        ConsoleUi.Blank();
        ConsoleUi.Text("Overlay: Ctrl+Shift+O to show or hide it, Ctrl+Shift+L to lock it (even in game).");
        ConsoleUi.Hint("To see it in game, set Fortnite to \"Windowed Fullscreen\" (Settings → Video → Window Mode).");
    }
    ConsoleUi.WaitEnter("Press Enter to start");
}
else
{
    store.SaveConfig(config);
    if (interactive && !once && Profiles.List(data).Count >= 2)
    {
        ConsoleUi.Screen(appName, "profile");
        ChooseProfile(data, config, store);
    }
}

// ---- Command line: analyze once and quit ---------------------------------------------------
if (once)
{
    ConsoleUi.Screen(appName, $"v{AppInfo.VersionText}");
    tracker.Scan(ignoreQuietPeriod: true);
    ReportWriter.WriteAll(store, config, data);
    tracker.PrintSummary();
    Console.WriteLine($"  Report: {store.ReportPath}");
    return 0;
}

// ---- Main screen ---------------------------------------------------------------------------
// One screen, redrawn in place: tabs (home / settings / help), a menu driven by the arrow keys, and an activity
// area that shows what the counter prints (captured from Console.Out).
var quit = false;
var restart = false;
var wake = new AutoResetEvent(false);
var keys = new BlockingCollection<ConsoleKeyInfo>();
var log = new ActivityLog();
var mainThread = Environment.CurrentManagedThreadId;
var dashboard = false;
var lastDraw = DateTime.MinValue;
var tab = 0;
var selection = new int[3];
UpdateInfo? update = null;
var updateAnnounced = false;
var notesHidden = false;
var showWhatsNew = !firstRun && config.LastVersion != AppInfo.VersionText;
if (!showWhatsNew && config.LastVersion != AppInfo.VersionText)
{
    config.LastVersion = AppInfo.VersionText;
    store.SaveConfig(config);
}
DateTime? autoCloseAt = null;
DateTime? fortniteClosedSince = null;
FileSystemWatcher? watcher = null;
var watcherFired = false;
var nextScan = DateTime.UtcNow.AddSeconds(2);

if (interactive)
{
    log.Changed += () =>
    {
        if (!dashboard)
        {
            return;
        }
        if (Environment.CurrentManagedThreadId == mainThread)
        {
            // While a scan runs (progress bar), redraw at most ~12 times a second.
            if ((DateTime.UtcNow - lastDraw).TotalMilliseconds >= 80) Draw();
        }
        else
        {
            wake.Set(); // written by another thread (overlay): the main loop redraws
        }
    };
    Console.SetOut(log);
    dashboard = true;
    try { Console.Clear(); } catch { }
    Frames.Invalidate();
    Draw();
}

if (reanalyzing)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("New analysis version: the replays still on disk will be analyzed again (no duplicates).");
    Console.ResetColor();
}

if (config.ShowOverlay)
{
    StartOverlay();
}

if (OperatingSystem.IsWindows())
{
    Updater.CleanupOld();
    if (config.StartWithFortnite)
    {
        FortniteWatch.Apply(true, dataDir); // follows the exe if it was moved or updated, and restarts the standby
    }
    if (Process.GetProcessesByName("ChestTracker") is { Length: > 0 } oldTool)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("The old version (ChestTracker.exe) is still open: close it, otherwise both count at the same time.");
        Console.ResetColor();
        foreach (var p in oldTool) p.Dispose();
    }
}

// Nothing to read yet: explain why, so that a new user is not left wondering.
if (!tracker.ReplayFiles().Any())
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine(Directory.Exists(config.DemosFolder)
        ? "No replay yet: that is expected if you have not played since replays were turned on."
        : "The replays folder does not exist yet: Fortnite creates it with the first recorded match.");
    Console.WriteLine("Check in Fortnite: Settings → Game → Replays → Record Replays.");
    Console.ResetColor();
}

// Replays left from earlier: the report opens with a loading card, the screen shows a progress bar.
var backlog = tracker.BacklogCount();
if (backlog > 1)
{
    ReportWriter.Progress = new ScanProgress(0, backlog);
}
ReportWriter.WriteAll(store, config, data);
if (config.OpenReportOnStart && !reportOpened && !auto)
{
    Open(store.ReportPath);
}
if (backlog > 0)
{
    tracker.Scan();
    ReportWriter.Progress = null;
    ReportWriter.WriteAll(store, config, data);
}
AnnounceNewProfiles();
UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);

if (auto && OperatingSystem.IsWindows() && !(config.MinimizeToTray && interactive))
{
    NativeConsole.Minimize(); // opened with Fortnite: the game keeps the screen
}

// New version on GitHub (checked in the background; the only network access of the tool).
if (config.UpdateMode != "off")
{
    _ = Task.Run(async () =>
    {
        var found = await Updater.CheckAsync();
        if (found != null)
        {
            Volatile.Write(ref update, found);
            wake.Set();
        }
    });
}

Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit = true; wake.Set(); };

// Minimized: into the notification area (classic console window only).
if (interactive && OperatingSystem.IsWindows())
{
    Tray.Enabled = config.MinimizeToTray;
    Tray.OpenReport = () => Open(store.ReportPath);
    Tray.Quit = () => { quit = true; wake.Set(); };
    Tray.Start();
    if (auto && config.MinimizeToTray && Tray.Available)
    {
        Tray.Hide(notify: false); // opened with Fortnite: straight to the tray, the game keeps the screen
    }
    else if (auto && config.MinimizeToTray)
    {
        NativeConsole.Minimize(); // no tray for this window (terminal): minimized as before
    }
}

// Keyboard: one thread blocked on ReadKey (no polling). Menus opened from the main loop read the same queue.
if (interactive)
{
    new Thread(() =>
    {
        while (true)
        {
            try { keys.Add(Console.ReadKey(intercept: true)); wake.Set(); }
            catch { return; }
        }
    }) { IsBackground = true, Name = "Keyboard" }.Start();
    ConsoleUi.ReadKey = () => keys.Take();
}

WatchReplays();

nextScan = DateTime.UtcNow.AddSeconds(2);
while (!quit)
{
    if (DateTime.UtcNow >= nextScan)
    {
        try
        {
            tracker.Scan();
            AnnounceNewProfiles();
            // Match in progress: re-read the replay being recorded (throttled inside).
            tracker.UpdateLive();
            UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while watching: {ex.Message}");
        }
        // A replay being written: check again soon. Otherwise only a safety scan once a minute.
        nextScan = DateTime.UtcNow.AddSeconds(tracker.IsFinishing ? 2 : tracker.HasPending ? 15 : (watcher != null && !auto ? 60 : 10));
    }

    if (auto && OperatingSystem.IsWindows())
    {
        CheckAutoClose();
    }

    var pendingUpdate = Volatile.Read(ref update);
    if (pendingUpdate != null && !updateAnnounced)
    {
        var idle = !tracker.IsLive && !tracker.HasPending && !tracker.IsFinishing;
        if (config.UpdateMode == "auto")
        {
            if (idle && AppInfo.ExePath != null)
            {
                updateAnnounced = true;
                InstallUpdate(pendingUpdate);
            }
        }
        else
        {
            updateAnnounced = true;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"New version {pendingUpdate.Version.ToString(3)} available: what's new is shown on the home tab.");
            Console.ResetColor();
        }
    }

    if (quit)
    {
        break;
    }
    Draw();

    // Sleep until something happens (file change, key press) or the next scheduled scan. At most one second, so that
    // a resized window or the auto-close countdown are redrawn.
    var wait = nextScan - DateTime.UtcNow;
    if (interactive && wait > TimeSpan.FromSeconds(1))
    {
        wait = TimeSpan.FromSeconds(1);
    }
    if (wait > TimeSpan.Zero && wake.WaitOne(wait))
    {
        // A file change: check again within 2 s (the readiness checks decide when a replay is complete).
        if (keys.Count == 0 && !quit && watcherFired)
        {
            watcherFired = false;
            nextScan = DateTime.UtcNow.AddSeconds(2) < nextScan ? DateTime.UtcNow.AddSeconds(2) : nextScan;
        }
    }

    while (!quit && keys.TryTake(out var key))
    {
        if (autoCloseAt != null)
        {
            // Any key keeps the counter open after Fortnite.
            autoCloseAt = null;
            auto = false;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("The counter stays open.");
            Console.ResetColor();
            continue;
        }
        HandleKey(key);
    }
}

dashboard = false;
watcher?.Dispose();
if (OperatingSystem.IsWindows())
{
    NativeOverlay.Stop();
    Tray.Stop();
}
Console.SetOut(ConsoleUi.Out);
if (interactive)
{
    try { Console.Clear(); } catch { }
    ConsoleUi.RestoreCursor(true);
}

if (restart && AppInfo.ExePath is { } newExe)
{
    try { instance.ReleaseMutex(); } catch { }
    var restartArgs = string.Join(" ", args.Where(a => a is not ("--after-update" or "--reset-config")).Select(a => a.Contains(' ') ? $"\"{a}\"" : a).Append("--after-update"));
    Process.Start(new ProcessStartInfo(newExe, restartArgs) { UseShellExecute = true })?.Dispose();
    return 0;
}
if (restart)
{
    Console.WriteLine("  Start the counter again to continue.");
}

Console.WriteLine("  See you soon!");
return 0;

// ---- Main screen: drawing ---------------------------------------------------------------------

// A menu entry of the main screen: its label, what Enter does, and a line that explains it.
static (string Label, Action Run, string? About) Item(string label, Action run, string? about = null) => (label, run, about);

List<(string Label, Action Run, string? About)> Items()
{
    var list = new List<(string Label, Action Run, string? About)>();
    switch (tab)
    {
        case 0:
            list.Add(Item("Open the report (detailed statistics)",
                () => Open(store.ReportPath)));
            if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
            {
                list.Add(Item(NativeOverlay.IsVisible ? "Hide the overlay" : "Show the overlay",
                    NativeOverlay.Toggle));
                list.Add(Item(NativeOverlay.IsLocked ? "Unlock the overlay (to move it)"
                        : "Lock the overlay (the mouse goes through it)",
                    NativeOverlay.ToggleLock));
            }
            if (Profiles.List(data).Count >= 2)
            {
                list.Add(Item("Switch profile", () =>
                {
                    ConsoleUi.Screen(appName, "profile");
                    ChooseProfile(data, config, store);
                    ReportWriter.WriteAll(store, config, data);
                    UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
                }));
            }
            if (Volatile.Read(ref update) is { } u)
            {
                list.Add(Item($"Install version {u.Version.ToString(3)}",
                    () => InstallUpdate(u)));
                list.Add(notesHidden
                    ? Item("See what's new", () => notesHidden = false)
                    : Item("Later (hide what's new)", () => notesHidden = true));
            }
            else if (config.UpdateMode != "off")
            {
                list.Add(Item("Check for updates", CheckForUpdate));
            }
            if (showWhatsNew && Volatile.Read(ref update) == null)
            {
                list.Add(Item("OK, hide what's new", () =>
                {
                    showWhatsNew = false;
                    config.LastVersion = AppInfo.VersionText;
                    store.SaveConfig(config);
                }));
            }
            list.Add(Item("Quit", () => quit = true));
            break;

        case 1:
            string YesNo(bool b) => b ? "yes" : "no";
            string Percent(int p) => $"{p}%";
            if (OperatingSystem.IsWindows())
            {
                list.Add(Item($"In-game overlay : {(config.ShowOverlay ? "on" : "off")}",
                    ToggleOverlay,
                    "Small always-on-top window that shows your chests in game. Off: no window and no shortcut, everything stays in the report."));
                if (config.ShowOverlay)
                {
                    string StyleName(string s) => s == "classic"
                        ? "card"
                        : "compact (figures only)";
                    list.Add(Item($"Overlay style : {StyleName(config.OverlayStyle)}", () =>
                    {
                        var styles = NativeOverlay.Styles;
                        config.OverlayStyle = styles[(Array.IndexOf(styles, config.OverlayStyle) + 1) % styles.Length];
                        NativeOverlay.SetStyle(config.OverlayStyle);
                    }, "Card: the glass card with the match bar. Compact: only the figures, with a soft shadow (a dashed frame shows while it is unlocked, to move it). Enter to switch, applied at once."));
                    list.Add(Item($"Overlay size : {Percent(config.OverlaySize)}", () =>
                    {
                        config.OverlaySize = OverlayAppearance.Next(OverlayAppearance.Sizes, config.OverlaySize);
                        ApplyOverlayAppearance();
                    }, "Enter for the next size (applied at once)."));
                    list.Add(Item($"Overlay opacity : {Percent(config.OverlayOpacity)}", () =>
                    {
                        config.OverlayOpacity = OverlayAppearance.Next(OverlayAppearance.Opacities, config.OverlayOpacity);
                        ApplyOverlayAppearance();
                    }, "Enter for the next opacity (applied at once)."));
                }
            }
            if (OperatingSystem.IsWindows())
            {
                list.Add(Item($"Minimize to the notification area : {YesNo(config.MinimizeToTray)}",
                    () => Tray.Enabled = config.MinimizeToTray = !config.MinimizeToTray,
                    "Yes: minimizing the window puts it next to the clock instead of the taskbar. A click on the icon opens it again."));
            }
            list.Add(Item($"Open the report at startup : {YesNo(config.OpenReportOnStart)}",
                () => config.OpenReportOnStart = !config.OpenReportOnStart,
                "Opens the report in your browser each time the counter starts."));
            if (OperatingSystem.IsWindows())
            {
                list.Add(Item($"Open with Fortnite : {YesNo(config.StartWithFortnite)}", ToggleStartWithFortnite,
                    "A tiny invisible process waits for Fortnite and opens the counter, which closes afterwards. It keeps running when you quit the counter: set this to \"no\" before deleting or moving PlunderCount.exe."));
            }
            var updates = config.UpdateMode switch
            {
                "auto" => "installed automatically",
                "off" => "off",
                _ => "offered",
            };
            list.Add(Item($"Updates : {updates}",
                () => config.UpdateMode = config.UpdateMode switch { "prompt" => "auto", "auto" => "off", _ => "prompt" },
                "Checked on GitHub at startup (the only network access of the tool). Offered: what's new is shown and you choose. Automatic: installed between two matches."));
            list.Add(Item($"Replays folder : {config.DemosFolder}", ChangeDemosFolder,
                "Where Fortnite saves its replays. Only change it if you moved it. Enter to edit."));
            list.Add(Item("Start the setup again", ResetConfiguration,
                "Clears your settings and asks the first-launch questions again. Your match history is kept."));
            break;

        case 2:
            list.Add(Item("Open the data folder", () => Open(store.Folder),
                $"History, settings and report: {store.Folder}"));
            if (File.Exists(store.ErrorLogPath))
            {
                list.Add(Item("Open the error log", () => Open(store.ErrorLogPath),
                    "Attach it when you report a problem."));
            }
            list.Add(Item("Report a problem (GitHub)", () => Open(AppInfo.IssuesUrl), AppInfo.IssuesUrl));
            list.Add(Item("Project page (GitHub)", () => Open(AppInfo.RepoUrl), AppInfo.RepoUrl));
            break;
    }
    return list;
}

string[] TabNames() => new[]
{
    "Home",
    "Settings",
    "Help",
};

void Draw()
{
    if (!dashboard || !interactive)
    {
        return;
    }
    lastDraw = DateTime.UtcNow;
    var height = ConsoleUi.Height;
    var width = ConsoleUi.Width;
    var f = new Frame();
    f.Blank();
    f.Add(new Seg($"  ♦  {string.Join(" ", appName.ToUpperInvariant().ToCharArray())}", ConsoleColor.Yellow), new Seg($"  v{AppInfo.VersionText}", ConsoleColor.DarkGray));
    f.Add("  " + new string('─', Math.Min(width - 4, 76)), ConsoleColor.DarkGray);

    // Tabs.
    var names = TabNames();
    var tabLine = new List<Seg> { new("  ") };
    for (var i = 0; i < names.Length; i++)
    {
        tabLine.Add(i == tab ? new Seg($" {names[i].ToUpperInvariant()} ", ConsoleColor.Black, ConsoleColor.Yellow) : new Seg($" {names[i].ToUpperInvariant()} ", ConsoleColor.DarkGray));
        tabLine.Add(new Seg("  "));
    }
    f.Add(tabLine.ToArray());
    f.Blank();

    var items = Items();
    selection[tab] = Math.Clamp(selection[tab], 0, items.Count - 1);

    // Footer (kept at the bottom of the window).
    var footer = new List<Seg[]>();
    if (items[selection[tab]].About is { Length: > 0 } about)
    {
        footer.Add(Array.Empty<Seg>());
        footer.AddRange(ConsoleUi.Wrap(about, width - 6).Select(l => new[] { new Seg("  " + l, ConsoleColor.Gray) }));
    }
    footer.Add(Array.Empty<Seg>());
    footer.Add(new[] { new Seg("  " + "↑ ↓ choose   Enter confirm   ← → tabs", ConsoleColor.DarkGray) });
    if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
    {
        var o = "Ctrl+Shift+O" + (NativeOverlay.HotkeyRegistered ? "" : " (unavailable)");
        var l = "Ctrl+Shift+L" + (NativeOverlay.LockHotkeyRegistered ? "" : " (unavailable)");
        footer.Add(new[] { new Seg("  " + $"Overlay, even in game: {o} show / hide   {l} lock", ConsoleColor.DarkGray) });
    }

    // What's new gets the room left once the menu, the footer and a few lines of activity are placed.
    var noteRoom = height - 1 - f.Lines.Count - 10 - items.Count - footer.Count - 4;

    switch (tab)
    {
        case 0: DrawHome(f, noteRoom); break;
        case 2: DrawHelp(f); break;
    }

    // Menu.
    for (var i = 0; i < items.Count; i++)
    {
        f.Add(i == selection[tab]
            ? new Seg($"  ▌ {items[i].Label}", ConsoleColor.Yellow)
            : new Seg($"    {items[i].Label}", ConsoleColor.Gray));
    }

    // Activity (home tab): what the counter did lately, in the space left.
    var room = height - 1 - f.Lines.Count - footer.Count;
    if (tab == 0 && room >= 3)
    {
        f.Blank();
        f.Add("  ACTIVITY", ConsoleColor.DarkGray);
        foreach (var line in log.Last(room - 2))
        {
            f.Add(new[] { new Seg("  ") }.Concat(line).ToArray());
        }
    }
    while (f.Lines.Count < height - 1 - footer.Count)
    {
        f.Blank();
    }
    foreach (var line in footer)
    {
        f.Add(line);
    }
    Frames.Draw(f);
}

void DrawHome(Frame f, int noteRoom)
{
    var view = Profiles.View(data, config);
    var matches = view.Matches;
    var profile = Profiles.Current(data, config);
    var profiles = Profiles.List(data);
    // ---- Status line: what the counter is doing right now, then the profile.
    Seg[] status;
    if (autoCloseAt is { } closeAt)
    {
        var left = Math.Max(0, (int)Math.Ceiling((closeAt - DateTime.UtcNow).TotalSeconds));
        status = new[] { new Seg("  ● ", ConsoleColor.Yellow), new Seg($"FORTNITE IS CLOSED", ConsoleColor.Yellow), new Seg($"  ·  closing in {left} s, press any key to keep it open", ConsoleColor.Gray) };
    }
    else if (tracker.IsLive)
    {
        var live = tracker.LiveMatch;
        status = live != null
            ? new[] { new Seg("  ● ", ConsoleColor.Green), new Seg("LIVE", ConsoleColor.Green), new Seg($"  ·  {L.Count(live.ChestsOpened, "chest")} so far", ConsoleColor.Gray) }
            : new[] { new Seg("  ● ", ConsoleColor.Yellow), new Seg("IN MATCH", ConsoleColor.Yellow), new Seg("  ·  its chests are counted when it ends", ConsoleColor.Gray) };
    }
    else if (tracker.IsFinishing || tracker.HasPending)
    {
        status = new[] { new Seg("  ● ", ConsoleColor.Cyan), new Seg("MATCH OVER", ConsoleColor.Cyan), new Seg("  ·  counting the chests…", ConsoleColor.Gray) };
    }
    else if (Directory.Exists(config.DemosFolder))
    {
        status = new[] { new Seg("  ● ", ConsoleColor.Gray), new Seg("WAITING FOR A MATCH", ConsoleColor.Gray) };
    }
    else
    {
        status = new[] { new Seg("  ● ", ConsoleColor.Red), new Seg("REPLAYS FOLDER NOT FOUND", ConsoleColor.Red), new Seg("  ·  see Help", ConsoleColor.Gray) };
    }
    f.Add(status.Concat(new[]
    {
        new Seg($"      {profile?.Name ?? "profile created with your first match"}", ConsoleColor.DarkGray),
        new Seg(profiles.Count >= 2 ? $" ({profiles.Count} accounts on this PC)" : "", ConsoleColor.DarkGray),
    }).ToArray());
    f.Blank();

    // ---- Four figures side by side: label, value, one or two lines of detail.
    var chests = matches.Sum(m => m.ChestsOpened);
    var treasures = matches.Sum(m => m.TreasureFinds.Count);
    var avg = matches.Count > 0 ? ((double)chests / matches.Count).ToString("F1", L.Culture) : "0";
    var session = matches.Where(ReportWriter.IsThisSession).ToList();
    var last = matches.OrderByDescending(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).FirstOrDefault();
    var colW = Math.Clamp((ConsoleUi.Width - 4) / 4, 16, 24);
    string Cell(string text) => text.Length >= colW ? text[..(colW - 2)] + "… " : text.PadRight(colW);
    void Cols(ConsoleColor[] colors, params string[] cells) =>
        f.Add(new[] { new Seg("  ") }.Concat(cells.Select((c, i) => new Seg(Cell(c), colors[i]))).ToArray());
    var dim = new[] { ConsoleColor.DarkGray, ConsoleColor.DarkGray, ConsoleColor.DarkGray, ConsoleColor.DarkGray };

    Cols(dim, "TOTAL CHESTS", "SESSION", "LAST MATCH", "TREASURES");
    Cols(new[] { ConsoleColor.Yellow, ConsoleColor.White, ConsoleColor.White, ConsoleColor.Magenta },
        L.N(chests), L.N(session.Sum(m => m.ChestsOpened)), last != null ? L.N(last.ChestsOpened) : "—", "★ " + L.N(treasures));
    Cols(dim,
        $"{L.Count(matches.Count, "match", "matches")} · {avg} avg",
        L.Count(session.Count, "match", "matches"),
        last != null ? Pretty.Playlist(last.Playlist) : "no match yet",
        treasures > 0 ? $"1 every {L.N((int)Math.Round((double)chests / treasures))} chests" : "none yet");
    Cols(dim, "", "", last != null ? Pretty.Placement(last) : "", "");
    f.Blank();

    // ---- Road to the next milestone, then the latest matches as a tiny chart (best in gold, newest in white).
    var barW = Math.Clamp(ConsoleUi.Width - 40, 20, 48);
    var next = Milestones.Next(chests);
    var prev = Milestones.Previous(chests);
    var filled = (int)Math.Round(barW * Math.Clamp((chests - prev) / (double)Math.Max(1, next - prev), 0, 1));
    f.Add(new Seg("  NEXT  ", ConsoleColor.DarkGray), new Seg(L.N(next).PadRight(7), ConsoleColor.White),
        new Seg(new string('═', filled), ConsoleColor.Yellow), new Seg(new string('─', barW - filled), ConsoleColor.DarkGray),
        new Seg($"  {L.N(next - chests)} TO GO", ConsoleColor.Yellow));
    var recent = matches.OrderBy(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).Select(m => m.ChestsOpened).ToList();
    recent = recent.TakeLast(Math.Min(barW, 32)).ToList();
    if (recent.Count > 1)
    {
        const string blocks = "▁▂▃▄▅▆▇█";
        var max = Math.Max(1, recent.Max());
        var bestIndex = recent.LastIndexOf(recent.Max());
        var segs = new List<Seg> { new("  LAST  ", ConsoleColor.DarkGray), new(recent.Count.ToString().PadRight(7), ConsoleColor.White) };
        for (var i = 0; i < recent.Count; i++)
        {
            var level = Math.Clamp((int)Math.Round(7.0 * recent[i] / max), 0, 7);
            segs.Add(new Seg(blocks[level].ToString(), i == recent.Count - 1 ? ConsoleColor.White : i == bestIndex ? ConsoleColor.Yellow : ConsoleColor.Gray));
        }
        segs.Add(new Seg($"  best {max}", ConsoleColor.DarkGray));
        f.Add(segs.ToArray());
    }
    f.Blank();

    // New version: what's new, in the chosen language.
    if (Volatile.Read(ref update) is { } u && !notesHidden)
    {
        NotesBox(f, noteRoom, $"New version {u.Version.ToString(3)} available",
            $"  (you have {AppInfo.VersionText}, nothing is lost when installing it)",
            u.WhatsNew, u.PageUrl);
    }
    else if (showWhatsNew && ReleaseNotes.WhatsNew(ReleaseNotes.Embedded, L.Code) is { Count: > 0 } current)
    {
        // First launch after an update: what this version brings (the notes are built into the exe).
        NotesBox(f, noteRoom, $"What's new in {AppInfo.VersionText}",
            "  (update installed)", current, null);
    }
}

void NotesBox(Frame f, int noteRoom, string title, string subtitle, IReadOnlyList<string> notes, string? pageUrl)
{
    {
        f.Add(new Seg("  ┌ ", ConsoleColor.Green), new Seg(title, ConsoleColor.Green), new Seg(subtitle, ConsoleColor.DarkGray));
        var shown = 0;
        foreach (var note in notes)
        {
            var wrapped = ConsoleUi.Wrap(note, ConsoleUi.Width - 10).ToList();
            if (shown + wrapped.Count > Math.Clamp(noteRoom - 3, 1, 12))
            {
                f.Add(new Seg("  │ ", ConsoleColor.Green), new Seg(pageUrl != null ? $"… and more: {pageUrl}" : "…", ConsoleColor.DarkGray));
                break;
            }
            for (var i = 0; i < wrapped.Count; i++)
            {
                f.Add(new Seg("  │ ", ConsoleColor.Green), new Seg((i == 0 ? "• " : "  ") + wrapped[i], ConsoleColor.Gray));
            }
            shown += wrapped.Count;
        }
        if (notes.Count == 0 && pageUrl != null)
        {
            f.Add(new Seg("  │ ", ConsoleColor.Green), new Seg($"What's new: {pageUrl}", ConsoleColor.Gray));
        }
        f.Add("  └", ConsoleColor.Green);
        f.Blank();
    }
}

void DrawHelp(Frame f)
{
    void Para(string text, ConsoleColor color = ConsoleColor.Gray)
    {
        foreach (var l in ConsoleUi.Wrap(text, ConsoleUi.Width - 6))
        {
            f.Add("  " + l, color);
        }
    }
    Para("The counter reads the replays Fortnite saves and counts your chests a few seconds after each match ends. Keep this window open while you play (you can minimize it).");
    f.Blank();
    Para("Nothing is counted? In Fortnite, turn on  Settings → Game → Replays → Record Replays.", ConsoleColor.Yellow);
    if (config.ShowOverlay && OperatingSystem.IsWindows())
    {
        f.Blank();
        f.Add("  " + "Overlay", ConsoleColor.White);
        Para("Ctrl+Shift+O: show / hide.  Ctrl+Shift+L: lock / unlock. These shortcuts work anywhere, even in game.");
        Para("Locked, the mouse goes through it. Unlocked, hold the left button to move it, right-click to hide it.");
        Para("To see it in game, set Fortnite to \"Windowed Fullscreen\" (Settings → Video → Window Mode).", ConsoleColor.DarkGray);
    }
    f.Blank();
}

// ---- Main screen: keys and actions ----------------------------------------------------------

void HandleKey(ConsoleKeyInfo key)
{
    var items = Items();
    switch (key.Key)
    {
        case ConsoleKey.LeftArrow:
            tab = (tab + 2) % 3;
            break;
        case ConsoleKey.RightArrow:
        case ConsoleKey.Tab:
            tab = (tab + 1) % 3;
            break;
        case ConsoleKey.Escape:
            tab = 0;
            break;
        case ConsoleKey.UpArrow:
            selection[tab] = (selection[tab] + items.Count - 1) % items.Count;
            break;
        case ConsoleKey.DownArrow:
            selection[tab] = (selection[tab] + 1) % items.Count;
            break;
        case ConsoleKey.Home:
            selection[tab] = 0;
            break;
        case ConsoleKey.End:
            selection[tab] = items.Count - 1;
            break;
        case ConsoleKey.Enter:
            var index = Math.Clamp(selection[tab], 0, items.Count - 1);
            items[index].Run();
            if (tab == 1)
            {
                // A setting changed: save it and show it everywhere at once.
                store.SaveConfig(config);
                ReportWriter.WriteAll(store, config, data);
                UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
            }
            break;
    }
}

void StartOverlay()
{
    if (!OperatingSystem.IsWindows() || NativeOverlay.IsRunning)
    {
        return;
    }
    NativeOverlay.SetAppearance(config.OverlaySize, config.OverlayOpacity);
    if (Array.IndexOf(NativeOverlay.Styles, config.OverlayStyle) < 0)
    {
        config.OverlayStyle = "classic"; // unknown style: back to the glass panel
    }
    NativeOverlay.SetStyle(config.OverlayStyle);
    NativeOverlay.Start(config.OverlayX, config.OverlayY, config.OverlayLocked,
        (x, y) =>
        {
            config.OverlayX = x;
            config.OverlayY = y;
            try { store.SaveConfig(config); } catch { /* ignore */ }
        },
        locked =>
        {
            config.OverlayLocked = locked;
            try { store.SaveConfig(config); } catch { /* ignore */ }
        });
    UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
}

void ToggleOverlay()
{
    config.ShowOverlay = !config.ShowOverlay;
    if (config.ShowOverlay)
    {
        StartOverlay();
    }
    else if (OperatingSystem.IsWindows())
    {
        NativeOverlay.Stop();
    }
}

void ToggleStartWithFortnite()
{
    var on = !config.StartWithFortnite;
    if (OperatingSystem.IsWindows() && FortniteWatch.Apply(on, dataDir))
    {
        config.StartWithFortnite = on;
        return;
    }
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Open with Fortnite: not possible here, run the counter from PlunderCount.exe.");
    Console.ResetColor();
}

void ChangeDemosFolder()
{
    ConsoleUi.Screen(appName, "replays folder");
    ConsoleUi.Text("Current folder:");
    ConsoleUi.Hint("  " + config.DemosFolder);
    ConsoleUi.Blank();
    ConsoleUi.Hint("Type the new path, then Enter. Esc to cancel.");
    ConsoleUi.Blank();
    var folder = ConsoleUi.Input("New folder: ", config.DemosFolder);
    if (folder != config.DemosFolder)
    {
        config.DemosFolder = folder;
        WatchReplays();
        Console.WriteLine($"Replays folder: {folder}");
        nextScan = DateTime.UtcNow;
    }
}

void ResetConfiguration()
{
    ConsoleUi.Screen(appName, "start the setup again");
    ConsoleUi.Text("Your settings will be cleared and the counter will restart with the first-launch questions.");
    ConsoleUi.Hint("Your match history and statistics are kept.");
    if (!ConsoleUi.Confirm("Yes, start the setup again", "Cancel"))
    {
        return;
    }
    if (OperatingSystem.IsWindows())
    {
        if (config.StartWithFortnite)
        {
            FortniteWatch.Apply(false, dataDir);
        }
        NativeOverlay.Stop();
    }
    try { File.Delete(store.ConfigPath); } catch { /* the setup will overwrite it */ }
    restart = true;
    quit = true;
}

// Manual check: asks GitHub now (a version may have come out since the counter was opened).
void CheckForUpdate()
{
    Console.WriteLine("Checking GitHub for a new version…");
    Draw();
    var (found, reached) = Updater.CheckDetailedAsync().GetAwaiter().GetResult();
    if (found == null)
    {
        Console.WriteLine(reached
            ? $"You already have the latest version ({AppInfo.VersionText})."
            : "GitHub cannot be reached right now (no connection?): try again later.");
        return;
    }
    Volatile.Write(ref update, found);
    updateAnnounced = true;
    notesHidden = false;
}

void InstallUpdate(UpdateInfo u)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Downloading version {u.Version.ToString(3)}…");
    Console.ResetColor();
    Draw();
    var error = Updater.Install(u);
    if (error != null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Update failed: {error}");
        Console.ResetColor();
        Console.WriteLine($"You can download it manually: {u.PageUrl}");
        return;
    }
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Update installed: restarting the counter…");
    Console.ResetColor();
    Draw();
    restart = true;
    quit = true;
}

void AnnounceNewProfiles()
{
    if (!tracker.NewProfilesFound)
    {
        return;
    }
    tracker.NewProfilesFound = false;
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine("Several Fortnite accounts recorded replays on this PC: each one has its own profile. \"Switch profile\" on the home tab to choose yours.");
    Console.ResetColor();
}

// Replays folder: Windows tells us when a file changes, instead of us re-reading the folder all the time.
void WatchReplays()
{
    watcher?.Dispose();
    watcher = null;
    try
    {
        if (Directory.Exists(config.DemosFolder))
        {
            var w = new FileSystemWatcher(config.DemosFolder, "*.replay")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            w.Created += (_, _) => { watcherFired = true; wake.Set(); };
            w.Changed += (_, _) => { watcherFired = true; wake.Set(); };
            w.Renamed += (_, _) => { watcherFired = true; wake.Set(); };
            watcher = w;
        }
    }
    catch
    {
        watcher = null; // fall back to the periodic scan
    }
}

void CheckAutoClose()
{
    var now = DateTime.UtcNow;
    if (FortniteWatch.IsFortniteRunning())
    {
        fortniteClosedSince = null;
        autoCloseAt = null; // Fortnite started again
        return;
    }
    fortniteClosedSince ??= now;
    // Wait for the last match to be counted (at most 5 minutes, in case a replay was left unfinished by a crash).
    var lastMatchDone = !tracker.IsLive && !tracker.HasPending && !tracker.IsFinishing;
    if (!lastMatchDone && now - fortniteClosedSince < TimeSpan.FromMinutes(5))
    {
        return;
    }
    if (autoCloseAt == null)
    {
        autoCloseAt = now.AddSeconds(30);
    }
    else if (now >= autoCloseAt)
    {
        quit = true;
    }
}

// Shows the new size / opacity on the overlay right away (it stays visible above the console).
void ApplyOverlayAppearance()
{
    if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
    {
        NativeOverlay.SetAppearance(config.OverlaySize, config.OverlayOpacity);
    }
}

static bool AcquireInstance(Mutex m, TimeSpan wait)
{
    try
    {
        return m.WaitOne(wait);
    }
    catch (AbandonedMutexException)
    {
        return true; // the previous instance was killed: it is ours now
    }
}

// Several accounts recorded replays on this PC: which one is played now (the overlay and the report show it).
static void ChooseProfile(TrackerData d, Config c, Store s)
{
    var list = Profiles.List(d);
    var current = Math.Max(0, list.FindIndex(p => p.Key == Profiles.Current(d, c)?.Key));
    ConsoleUi.Text("Several Fortnite accounts recorded replays on this PC.");
    ConsoleUi.Text("Which account are you playing on?", ConsoleColor.White);
    ConsoleUi.Hint("Each account keeps its own statistics. You can switch at any time from the home tab (\"Switch profile\").");
    var choice = ConsoleUi.Menu(list.Select(Profiles.Describe).ToList(), current, escapeIndex: current);
    c.Profile = list[choice].Key;
    try { s.SaveConfig(c); } catch { /* ignore */ }
}

static void UpdateOverlay(Store store, Config config, TrackerData d, MatchRecord? live, bool inProgress)
{
    var view = OverlayModel.Build(Profiles.View(d, config), live, inProgress);
    ReportWriter.WriteOverlay(store, view);
    if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
    {
        NativeOverlay.Update(view);
    }
}

// Below-normal priority + Windows 11 "efficiency mode" (EcoQoS): the game always comes first.
static void LowerPriority()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }
    try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
    try { EcoQos.Enable(); } catch { }
}

static void Open(string path)
{
    try
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    catch
    {
        Console.WriteLine($"Open this file in your browser: {path}");
    }
}
