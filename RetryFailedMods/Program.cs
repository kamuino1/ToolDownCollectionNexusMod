using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using NexusShared;

// Retry tool: load the CSV produced by the collection tool into ModEntry objects, then feed
// them straight into the shared Phase 2. Phase2.Run skips rows that are blank/"done".
class Program
{
    // ====== CONFIG ======
    // The progress CSV to read + rewrite in place. Point this at the file the collection tool produced.
    const string CsvPath = @"D:\1\Tool\ToolDownCollectionNexusMod\collection_progress.csv";

    // Reuse the collection tool's driver + Edge profile
    const string DriverPath = @"D:\1\Tool\ToolDownCollectionNexusMod\driver";
    const string UserDataDir = @"D:\Temp";
    const string ProfileDirectory = "Profile 1";

    static void Main()
    {
        Logging.Setup(Path.Combine(Path.GetDirectoryName(CsvPath) ?? ".", "logs", "retry-.log"));

        if (!File.Exists(CsvPath))
        {
            Logging.Line("CSV not found: " + CsvPath);
            Logging.Close();
            return;
        }

        // Load every row; the ModEntry list IS the input to Phase 2
        var mods = CsvStore.Load(CsvPath);
        Logging.Line($"Loaded {mods.Count} rows from {CsvPath}");

        int toRetry = mods.Count(m =>
            !string.IsNullOrWhiteSpace(m.Url) &&
            !string.Equals(m.Status?.Trim(), "done", StringComparison.OrdinalIgnoreCase));
        Logging.Line($"{toRetry} mod(s) to retry (has link and status != done).");
        if (toRetry == 0)
        {
            Logging.Line("Nothing to retry. Done.");
            Logging.Close();
            return;
        }

        EdgeFactory.KillEdgeProcesses();
        var driver = EdgeFactory.Create(DriverPath, UserDataDir, ProfileDirectory);

        // Let the user handle any Cloudflare check / login before we start
        driver.Navigate().GoToUrl("https://www.nexusmods.com/");
        Logging.Line("If there is still a Cloudflare check or you are not logged in: handle it in the browser, then press ENTER to continue...");
        Console.ReadLine();

        // Shared Phase 2 (skips blank-Url and already-"done" rows itself)
        Phase2.Run(driver, mods, () => CsvStore.Save(CsvPath, mods));

        driver.Quit();
        CsvStore.Save(CsvPath, mods);
        int remaining = mods.Count(m => !string.Equals(m.Status?.Trim(), "done", StringComparison.OrdinalIgnoreCase));
        Logging.Line($"Done! {mods.Count - remaining}/{mods.Count} mods now done. CSV: {CsvPath}");
        Logging.Close();
    }
}
