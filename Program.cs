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

    // Progress file; the date/time is stamped into the name when the program starts,
    // e.g. collection_progress_2026-07-12_01-15-03.csv
    static readonly string OutputCsvPath =
        Path.Combine(OutputDir, $"collection_progress_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv");

    static void Main()
    {
        string gameDomain = Collector.ParseGameDomain(CollectionUrl);
        Console.WriteLine($"Game domain: {gameDomain}");

        EdgeFactory.KillEdgeProcesses();
        var driver = EdgeFactory.Create(DriverPath, UserDataDir, ProfileDirectory);

        // With a logged-in profile you usually don't need to log in again. Still pause once so you
        // can handle a Cloudflare check / login by hand if needed.
        driver.Navigate().GoToUrl("https://www.nexusmods.com/");
        Console.WriteLine("If there is still a Cloudflare check or you are not logged in: handle it in the browser, then press ENTER to continue...");
        Console.ReadLine();

        // Open the collection page
        driver.Navigate().GoToUrl(CollectionUrl);
        Thread.Sleep(4000);

        // ====== PHASE 1a: collect all mod titles (no hover) and write the CSV up front ======
        var mods = Collector.CollectTitles(driver)
            .Skip(SkipIndex)
            .ToList();

        for (int i = 0; i < mods.Count; i++)
        {
            mods[i].Index = i + 1;
            mods[i].Status = "pending";
            mods[i].UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        void Persist() => CsvStore.Save(OutputCsvPath, mods);

        Console.WriteLine($"Found {mods.Count} mods in the collection.");
        Persist(); // titles CSV (Url still blank)
        Console.WriteLine($"Progress file: {OutputCsvPath}");

        if (mods.Count == 0)
        {
            Console.WriteLine("No mod titles collected. Check CollectionUrl / login / selectors.");
            driver.Quit();
            return;
        }

        // ====== PHASE 1b: fill the link for every mod (updates the CSV live) ======
        Collector.FillLinks(driver, mods, Persist);
        int linked = mods.Count(m => !string.IsNullOrWhiteSpace(m.Url));
        Console.WriteLine($"Links filled: {linked}/{mods.Count}. Starting downloads...");

        // ====== PHASE 2: download each mod (shared) ======
        Phase2.Run(driver, mods, Persist);

        driver.Quit();
        Persist();
        Console.WriteLine("Done! Progress saved to: " + OutputCsvPath);
    }
}
