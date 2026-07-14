using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using NexusShared;

// Collection tool: scrape all mod titles -> fill each link (Phase 1) -> download (shared Phase 2).
class Program
{
    // ====== CONFIG ======
    // NexusMods collection URL (mods tab), e.g. .../games/<game>/collections/<slug>/mods
    const string CollectionUrl = "https://www.nexusmods.com/games/stardewvalley/collections/tckf0m/mods";

    // Folder that contains msedgedriver.exe
    const string DriverPath = @"D:\1\Tool\ToolDownCollectionNexusMod\driver";

    // How many mods to skip from the start (useful when resuming a partial run)
    const int SkipIndex = 0;

    // Edge profile used to KEEP the NexusMods login session (past the Cloudflare captcha).
    // Edge MUST be fully closed before running (Edge won't share an open profile with Selenium).
    const string UserDataDir = @"D:\Temp";
    const string ProfileDirectory = "Profile 1";

    // Folder where the progress CSV is written
    const string OutputDir = @"D:\1\Tool\ToolDownCollectionNexusMod";

    // Set to an existing progress CSV to RESUME from it: those entries are loaded first and
    // Phase 1 ignores any collection mod already recorded (by name). Leave "" for a fresh run.
    const string ResumeFromCsv = "";

    // true when ResumeFromCsv points at an existing file
    static readonly bool Resuming = !string.IsNullOrWhiteSpace(ResumeFromCsv) && File.Exists(ResumeFromCsv);

    // Progress file: when resuming we write back to the same file; otherwise a fresh timestamped
    // name, e.g. collection_progress_2026-07-12_01-15-03.csv
    static readonly string OutputCsvPath = Resuming
        ? ResumeFromCsv
        : Path.Combine(OutputDir, $"collection_progress_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv");

    static void Main()
    {
        Logging.Setup(Path.Combine(OutputDir, "logs", "collection-.log"));
        string gameDomain = Collector.ParseGameDomain(CollectionUrl);
        Logging.Line($"Game domain: {gameDomain}");

        // Resume: load existing entries (if configured). The loaded list is our working list;
        // Phase 1 will not re-add / re-hover mods already recorded here.
        var mods = Resuming ? CsvStore.Load(OutputCsvPath) : new List<ModEntry>();
        if (Resuming) Logging.Line($"Resuming from {OutputCsvPath} ({mods.Count} existing entries).");
        var known = new HashSet<string>(
            mods.Select(m => (m.Name ?? "").Trim()), StringComparer.OrdinalIgnoreCase);

        EdgeFactory.KillEdgeProcesses();
        var driver = EdgeFactory.Create(DriverPath, UserDataDir, ProfileDirectory);

        // With a logged-in profile you usually don't need to log in again. Still pause once so you
        // can handle a Cloudflare check / login by hand if needed.
        driver.Navigate().GoToUrl("https://www.nexusmods.com/");
        Logging.Line("If there is still a Cloudflare check or you are not logged in: handle it in the browser, then press ENTER to continue...");
        Console.ReadLine();

        // Open the collection page
        driver.Navigate().GoToUrl(CollectionUrl);
        Thread.Sleep(4000);

        // ====== PHASE 1a: scrape titles, append only mods not already in the list ======
        var scraped = Collector.CollectTitles(driver)
            .Skip(SkipIndex)
            .ToList();
        Collector.AddNewTitles(mods, scraped, known);

        for (int i = 0; i < mods.Count; i++)
        {
            mods[i].Index = i + 1;
            if (string.IsNullOrEmpty(mods[i].UpdatedAt))
                mods[i].UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        void Persist() => CsvStore.Save(OutputCsvPath, mods);

        Logging.Line($"Total mods now: {mods.Count}.");
        Persist(); // write CSV up front (loaded + new titles)
        Logging.Line($"Progress file: {OutputCsvPath}");

        if (mods.Count == 0)
        {
            Logging.Line("No mods. Check CollectionUrl / login / selectors.");
            driver.Quit();
            Logging.Close();
            return;
        }

        // ====== PHASE 1b: fill the link for every mod (updates the CSV live) ======
        Collector.FillLinks(driver, mods, Persist);
        int linked = mods.Count(m => !string.IsNullOrWhiteSpace(m.Url));
        Logging.Line($"Links filled: {linked}/{mods.Count}. Starting downloads...");

        // ====== PHASE 2: download each mod (shared) ======
        Phase2.Run(driver, mods, Persist);

        driver.Quit();
        Persist();
        Logging.Line("Done! Progress saved to: " + OutputCsvPath);
        Logging.Close();
    }
}
