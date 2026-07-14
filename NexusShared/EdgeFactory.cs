using System.Diagnostics;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;

namespace NexusShared;

// Builds a logged-in, anti-detection Edge session and frees the profile lock
public static class EdgeFactory
{
    // Kill any running Edge/driver processes so Selenium can use the profile
    public static void KillEdgeProcesses()
    {
        foreach (var name in new[] { "msedge", "msedgedriver" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(true); p.WaitForExit(3000); }
                catch { /* already exited / cannot kill -> ignore */ }
            }
        }
        Thread.Sleep(1000); // wait for Windows to release the file lock (SingletonLock)
    }

    // Create an EdgeDriver using an existing profile + anti-automation-detection flags
    public static EdgeDriver Create(string driverPath, string userDataDir, string profileDirectory)
    {
        var options = new EdgeOptions();
        options.AddArgument("--start-maximized");
        options.AddArgument($"user-data-dir={userDataDir}");
        options.AddArgument($"profile-directory={profileDirectory}");
        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalEdgeOption("useAutomationExtension", false);

        var service = EdgeDriverService.CreateDefaultService(driverPath);
        var driver = new EdgeDriver(service, options);

        // Hide navigator.webdriver before the page loads
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
            Logging.Line("Could not set CDP override (skipping): " + ex.Message);
        }

        return driver;
    }
}
