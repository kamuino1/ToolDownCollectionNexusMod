using OpenQA.Selenium;
using OpenQA.Selenium.Edge;

namespace NexusShared;

// PHASE 2 (shared): download every mod in `mods` that has a Url and isn't already "done".
// Each mod's ModEntry is the unit of work; `persist` is called after every status change so
// the caller can save its own CSV. Used by both the collection tool and the retry tool.
public static class Phase2
{
    public static void Run(EdgeDriver driver, List<ModEntry> mods, Action persist)
    {
        string mainTab = driver.WindowHandles.First();
        int total = mods.Count;
        int n = 0;

        void Set(ModEntry mod, string status)
        {
            mod.Status = status;
            mod.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($"    -> {status}");
            persist();
        }

        foreach (var mod in mods)
        {
            n++;

            if (string.IsNullOrWhiteSpace(mod.Url))
            {
                Console.WriteLine($"[{n}/{total}] {mod.Name} -> no link, skipping.");
                continue;
            }
            if (string.Equals(mod.Status?.Trim(), "done", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[{n}/{total}] {mod.Name} -> already done, skipping.");
                continue;
            }

            // Normalize to the mod's Files tab
            string filesUrl = mod.Url.Contains("?") ? mod.Url + "&tab=files" : mod.Url + "?tab=files";
            Console.WriteLine($"[{n}/{total}] {mod.Name} -> {filesUrl}");

            try
            {
                // Open the mod's Files tab in a NEW browser tab and switch to it
                ((IJavaScriptExecutor)driver).ExecuteScript("window.open(arguments[0], '_blank');", filesUrl);
                Thread.Sleep(1000);
                driver.SwitchTo().Window(driver.WindowHandles.Last());
                Thread.Sleep(4000);
                Set(mod, "opened");

                // The download UI lives inside a Web Component (<mod-download-modal>) Shadow DOM,
                // so we click via JS that pierces shadow roots (Dom.DeepClick).

                // 1) Click the "Manual" button -> the download popup opens
                bool manualClicked = false;
                Dom.WaitFor(() => { manualClicked = Dom.DeepClick(driver, "button", "Manual"); return manualClicked; }, 8000);
                if (!manualClicked)
                {
                    Console.WriteLine("  'Manual' button not found -> skip.");
                    Set(mod, "manual-not-found");
                }
                else
                {
                    Set(mod, "manual-clicked");

                    // 2) In the popup: click the "Manual download" link (href .../api/files/<id>/download)
                    bool manualDlClicked = false;
                    Dom.WaitFor(() => { manualDlClicked = Dom.DeepClick(driver, "a[href*='/api/files/']", ""); return manualDlClicked; }, 6000);
                    if (manualDlClicked)
                    {
                        Set(mod, "manualdl-clicked");
                    }
                    else
                    {
                        Console.WriteLine("  'Manual download' link not found.");
                        Set(mod, "manualdl-not-found");
                    }

                    // 3) Click the "Slow download" button (free tier)
                    bool slowClicked = false;
                    Dom.WaitFor(() => { slowClicked = Dom.DeepClick(driver, "button", "Slow download"); return slowClicked; }, 8000);
                    if (slowClicked)
                    {
                        Set(mod, "slow-clicked");
                    }
                    else
                    {
                        Console.WriteLine("  'Slow download' button not found (maybe Premium / already downloading).");
                        Set(mod, "slow-not-found");
                    }
                }

                // Wait ~6s for the download to start
                Thread.Sleep(6000);

                if (mod.Status == "slow-clicked" || mod.Status == "manualdl-clicked")
                    Set(mod, "done");
            }
            catch (WebDriverException ex)
            {
                Console.WriteLine("  Error while processing mod: " + ex.Message);
                Set(mod, "error: " + ex.Message.Replace("\r", " ").Replace("\n", " "));
            }
            finally
            {
                try
                {
                    if (driver.WindowHandles.Count > 1)
                    {
                        driver.Close();
                        driver.SwitchTo().Window(mainTab);
                    }
                }
                catch (WebDriverException) { }
            }
        }
    }
}
