using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
//135.0.3179.66
class Program
{
    // ====== CONFIG ======
    // NexusMods collection URL (mods tab), e.g. .../games/<game>/collections/<slug>/mods
    const string CollectionUrl = "https://www.nexusmods.com/games/stardewvalley/collections/tckf0m/mods";

    // Folder that contains msedgedriver.exe
    const string DriverPath = @"D:\1\Tool\ToolDownCollectionNexusMod\driver";

    // Path to the IDM browser extension (.crx) - same as the FitGirl build
    const string IdmExtensionPath = @"C:\Program Files (x86)\Internet Download Manager\IDMGCExt.crx";

    // How many mods to skip from the start (useful when resuming a partial run)
    const int SkipIndex = 0;

    // Edge profile used to KEEP the NexusMods login session (to get past the Cloudflare captcha).
    //
    // OPTION 1 (recommended - an EXISTING logged-in profile):
    //   UserDataDir      = the Edge "User Data" folder
    //   ProfileDirectory = the profile FOLDER name (not the display name); see edge://version -> "Profile Path"
    //   => Edge MUST be fully closed before running the tool (Edge won't share an open profile with Selenium).
    //
    // OPTION 2 (a dedicated profile for the tool): point UserDataDir at an empty folder, log in once with normal Edge.
    const string UserDataDir = @"D:\Temp";

    // Profile folder name: "Default" or "Profile 1" ... (see edge://version -> Profile Path)
    const string ProfileDirectory = "Profile 1";

    static void Main()
    {
        // The game domain is the path segment right after the host, e.g. "stardewvalley"
        string gameDomain = ParseGameDomain(CollectionUrl);
        Console.WriteLine($"Game domain: {gameDomain}");

        // Edge must be fully closed for Selenium to use the profile (otherwise -> SessionNotCreated:
        // "cannot create default profile directory"). Kill any leftover Edge processes.
        KillEdgeProcesses();

        var options = new EdgeOptions();
        options.AddArgument("--start-maximized");

        // Reuse the logged-in profile -> skip the login page + Cloudflare captcha
        options.AddArgument($"user-data-dir={UserDataDir}");
        options.AddArgument($"profile-directory={ProfileDirectory}");

        // Reduce "automation-controlled browser" signals so Cloudflare Turnstile doesn't block us
        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalEdgeOption("useAutomationExtension", false);

        //options.AddExtension(IdmExtensionPath);

        // Add the uBlock extension (if needed)
        //options.AddExtension(@"D:\1\Tool\ToolDownCollectionNexusMod\extension\ublock.crx");

        var service = EdgeDriverService.CreateDefaultService(DriverPath);
        var driver = new EdgeDriver(service, options);

        // Hide navigator.webdriver before the page loads (belt-and-suspenders anti-automation-detection)
        try
        {
            driver.ExecuteCdpCommand("Page.addScriptToEvaluateOnNewDocument",
                new Dictionary<string, object>
                {
                    ["source"] = "Object.defineProperty(navigator, 'webdriver', {get: () => undefined});"
                });
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not set CDP override (skipping): " + ex.Message);
        }

        // With a logged-in profile you usually don't need to log in again. Still pause once so you
        // can handle a Cloudflare check / login by hand if needed.
        driver.Navigate().GoToUrl("https://www.nexusmods.com/");
        Console.WriteLine("If there is still a Cloudflare check or you are not logged in: handle it in the browser, then press ENTER to continue...");
        Console.ReadLine();

        // Open the collection page
        driver.Navigate().GoToUrl(CollectionUrl);
        Thread.Sleep(4000);

        // ====== PHASE 1: Collect mod links ======
        var modLinks = CollectModLinks(driver, gameDomain)
            .Skip(SkipIndex)
            .ToList();

        Console.WriteLine($"Found {modLinks.Count} mods in the collection.");
        if (modLinks.Count == 0)
        {
            Console.WriteLine("No mod links collected. Check CollectionUrl / login / selectors.");
            driver.Quit();
            return;
        }

        // ====== PHASE 2: Download each mod (open in a new tab) ======
        string mainTab = driver.WindowHandles.First();

        int index = 0;
        foreach (var modUrl in modLinks)
        {
            index++;

            // Normalize to the mod's Files tab
            string filesUrl = modUrl.Contains("?") ? modUrl + "&tab=files" : modUrl + "?tab=files";
            Console.WriteLine($"[{index}/{modLinks.Count}] {filesUrl}");

            try
            {
                // Open the mod's Files tab in a NEW browser tab and switch to it
                ((IJavaScriptExecutor)driver).ExecuteScript("window.open(arguments[0], '_blank');", filesUrl);
                Thread.Sleep(1000);
                driver.SwitchTo().Window(driver.WindowHandles.Last());
                Thread.Sleep(4000);

                // 1) Click the "Manual" button on the file card -> a popup appears
                IWebElement? manualBtn = null;
                WaitFor(() => { manualBtn = FindByText(driver, "button", "Manual"); return manualBtn != null; }, 8000);
                if (manualBtn == null)
                {
                    Console.WriteLine("  'Manual' button not found -> skip.");
                }
                else
                {
                    ClickJs(driver, manualBtn);

                    // 2) In the popup, click the "Manual download" link (href .../api/files/<id>/download)
                    IWebElement? manualDl = null;
                    WaitFor(() =>
                    {
                        manualDl = driver.FindElements(By.CssSelector("a[href*='/api/files/']")).FirstOrDefault()
                                   ?? FindByText(driver, "a", "Manual download");
                        return manualDl != null;
                    }, 6000);

                    if (manualDl != null)
                        ClickJs(driver, manualDl);
                    else
                        Console.WriteLine("  'Manual download' link not found.");

                    // 3) Click the "Slow download" button (free tier)
                    IWebElement? slowBtn = null;
                    WaitFor(() => { slowBtn = FindByText(driver, "button", "Slow download"); return slowBtn != null; }, 8000);

                    if (slowBtn != null)
                        ClickJs(driver, slowBtn);
                    else
                        Console.WriteLine("  'Slow download' button not found (maybe Premium / already downloading).");
                }

                // Wait ~6s for the download to start
                Thread.Sleep(6000);
            }
            catch (WebDriverException ex)
            {
                Console.WriteLine("  Error while processing mod: " + ex.Message);
            }
            finally
            {
                // Close the mod tab and go back to the collection tab
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

        driver.Quit();
        Console.WriteLine("Done!");
    }

    // Kill any running Edge/driver processes to release the profile lock
    static void KillEdgeProcesses()
    {
        foreach (var name in new[] { "msedge", "msedgedriver" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    p.Kill(true);          // kill the whole process tree
                    p.WaitForExit(3000);
                }
                catch { /* already exited / cannot kill -> ignore */ }
            }
        }
        Thread.Sleep(1000); // wait for Windows to release the file lock (SingletonLock)
    }

    // Get the game domain (the path segment right after the host) from CollectionUrl
    static string ParseGameDomain(string collectionUrl)
    {
        try
        {
            var uri = new Uri(collectionUrl);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return "";
            // New URL format: /games/<domain>/collections/... -> drop the "games" prefix
            if (segments[0].Equals("games", StringComparison.OrdinalIgnoreCase) && segments.Length > 1)
                return segments[1];
            return segments[0];
        }
        catch
        {
            return "";
        }
    }

    // Matches real mod-page links only: .../mods/<number> (excludes /mods without id and /collections/...)
    static readonly Regex ModLinkRegex = new Regex(@"/mods/\d+(\b|$|[/?#])");

    // ====== PHASE 1: Collect every mod-page link in the collection ======
    // The new collection page renders a TABLE: each mod is a <tr class="collection-mod-row">,
    // and the mod name is just a <span> (no link). The mod link only appears when you HOVER a row
    // -> the site creates a <div data-floating-ui-portal> containing an <a href=".../mods/<id>">.
    static List<string> CollectModLinks(EdgeDriver driver, string gameDomain)
    {
        // Wait for the mod table to load
        WaitFor(() => driver.FindElements(By.CssSelector("tr.collection-mod-row")).Count > 0, 20000);

        var rows = driver.FindElements(By.CssSelector("tr.collection-mod-row"));
        int rowCount = rows.Count;
        Console.WriteLine($"[Mods] Found {rowCount} mod rows in the table.");

        var result = new List<string>();

        for (int i = 0; i < rowCount; i++)
        {
            // Re-fetch by index because the DOM can change (portal) and make the element stale
            var freshRows = driver.FindElements(By.CssSelector("tr.collection-mod-row"));
            if (i >= freshRows.Count) break;
            var row = freshRows[i];

            try
            {
                // Hover target: the mod-name cell (fallback: the whole row)
                var hoverTarget = row.FindElements(By.CssSelector(".collection-mod-row__mod-name-container"))
                                     .FirstOrDefault() ?? row;

                ((IJavaScriptExecutor)driver).ExecuteScript(
                    "arguments[0].scrollIntoView({block:'center'});", hoverTarget);
                Thread.Sleep(150);

                // Real hover via CDP -> floating-ui builds the tooltip portal.
                // (Actions.MoveToElement does not trigger the tooltip, it only expands the row.)
                HoverElementCdp(driver, hoverTarget);

                string? href = null;
                WaitFor(() =>
                {
                    href = FindModHref(driver);
                    return href != null;
                }, timeoutMs: 3000);

                if (!string.IsNullOrEmpty(href) && !result.Contains(href))
                {
                    result.Add(href);
                    Console.WriteLine($"  [{result.Count}/{rowCount}] {href}");
                }
                else if (href == null)
                {
                    Console.WriteLine($"  [--/{rowCount}] Could not get a link for row {i + 1}");
                }

                // Move the cursor to the top-left corner to close the tooltip before the next row
                DispatchMouseMove(driver, 2, 2);
                Thread.Sleep(120);
            }
            catch (WebDriverException)
            {
                // stale row / cannot hover -> skip
            }
        }

        return result.Distinct().ToList();
    }

    // Find one href that points to a mod page (.../mods/<id>) inside the currently open portal
    static string? FindModHref(IWebDriver driver)
    {
        return driver
            .FindElements(By.CssSelector("div[data-floating-ui-portal] a[href*='/mods/']"))
            .Select(a => a.GetAttribute("href"))
            .FirstOrDefault(h => !string.IsNullOrEmpty(h)
                                 && ModLinkRegex.IsMatch(h)
                                 && !h.Contains("/collections/"));
    }

    // Real hover over the center of an element via CDP Input.dispatchMouseEvent
    static void HoverElementCdp(EdgeDriver driver, IWebElement el)
    {
        var coords = (System.Collections.IList)((IJavaScriptExecutor)driver).ExecuteScript(
            "var r = arguments[0].getBoundingClientRect();" +
            "return [r.left + r.width / 2, r.top + r.height / 2];", el);

        double x = Convert.ToDouble(coords[0]);
        double y = Convert.ToDouble(coords[1]);
        DispatchMouseMove(driver, x, y);
    }

    // Send a real mouse-move event to viewport coordinates (x, y)
    static void DispatchMouseMove(EdgeDriver driver, double x, double y)
    {
        try
        {
            driver.ExecuteCdpCommand("Input.dispatchMouseEvent", new Dictionary<string, object>
            {
                ["type"] = "mouseMoved",
                ["x"] = x,
                ["y"] = y
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("CDP mouseMoved error: " + ex.Message);
        }
    }

    // Find the first element matching a CSS selector whose visible text equals `text`
    static IWebElement? FindByText(IWebDriver driver, string css, string text)
    {
        return driver.FindElements(By.CssSelector(css))
            .FirstOrDefault(e =>
            {
                try { return string.Equals((e.Text ?? "").Trim(), text, StringComparison.OrdinalIgnoreCase); }
                catch (WebDriverException) { return false; }
            });
    }

    // Click an element via JavaScript (robust against overlays / interception)
    static void ClickJs(IWebDriver driver, IWebElement el)
    {
        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", el);
    }

    // Helper: wait until cond() is true, polling every stepMs (no extra NuGet needed)
    static bool WaitFor(Func<bool> cond, int timeoutMs, int stepMs = 300)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            try
            {
                if (cond()) return true;
            }
            catch (WebDriverException)
            {
                // element not ready yet -> retry
            }
            Thread.Sleep(stepMs);
            waited += stepMs;
        }
        return false;
    }
}
