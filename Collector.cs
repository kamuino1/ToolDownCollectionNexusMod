using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using NexusShared;

// PHASE 1 (collection tool only): scrape mod titles, then fill each mod's link by hovering.
static class Collector
{
    // Get the game domain (the path segment right after the host) from CollectionUrl
    public static string ParseGameDomain(string collectionUrl)
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

    // ====== PHASE 1a: collect every mod TITLE (plain text, no hover) ======
    public static List<ModEntry> CollectTitles(EdgeDriver driver)
    {
        Dom.WaitFor(() => driver.FindElements(By.CssSelector("tr.collection-mod-row")).Count > 0, 20000);

        var raw = (System.Collections.IList)((IJavaScriptExecutor)driver).ExecuteScript(
            "return Array.from(document.querySelectorAll(" +
            "'tr.collection-mod-row .collection-mod-row__mod-name-container'))" +
            ".map(e => (e.textContent || '').trim());");

        var result = new List<ModEntry>();
        for (int i = 0; i < raw.Count; i++)
        {
            string name = raw[i]?.ToString()?.Trim() ?? "";
            if (name.Length == 0) name = $"row-{i + 1}";
            result.Add(new ModEntry { Name = name, Url = "", Status = "pending" });
        }

        Console.WriteLine($"[Titles] Collected {result.Count} mod titles.");
        return result;
    }

    // ====== PHASE 1b: fill the link for every mod (hover each row) ======
    public static void FillLinks(EdgeDriver driver, List<ModEntry> mods, Action persist)
    {
        void Set(ModEntry mod, string status)
        {
            mod.Status = status;
            mod.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($"    -> {status}");
            persist();
        }

        for (int i = 0; i < mods.Count; i++)
        {
            var mod = mods[i];
            if (!string.IsNullOrEmpty(mod.Url)) continue; // already has a link (e.g. resumed run)

            try
            {
                // Re-fetch rows each iteration (portal changes can make elements stale)
                var rows = driver.FindElements(By.CssSelector("tr.collection-mod-row"));
                if (i >= rows.Count)
                {
                    Console.WriteLine($"  [{i + 1}/{mods.Count}] row missing -> link-failed ({mod.Name})");
                    Set(mod, "link-failed");
                    continue;
                }

                var nameEl = rows[i].FindElements(By.CssSelector(".collection-mod-row__mod-name-container"))
                                    .FirstOrDefault() ?? rows[i];

                ((IJavaScriptExecutor)driver).ExecuteScript(
                    "arguments[0].scrollIntoView({block:'center'});", nameEl);
                Thread.Sleep(150);

                // Re-trigger the hover on every poll so a transient miss just retries.
                string? href = null;
                Dom.WaitFor(() =>
                {
                    HoverElementCdp(driver, nameEl);
                    href = FindModHrefValidated(driver, nameEl);
                    return href != null;
                }, timeoutMs: 4000, stepMs: 400);

                if (!string.IsNullOrEmpty(href))
                {
                    mod.Url = href;
                    Console.WriteLine($"  [{i + 1}/{mods.Count}] {mod.Name} -> {href}");
                    Set(mod, "link-ok");
                }
                else
                {
                    Console.WriteLine($"  [{i + 1}/{mods.Count}] link-failed ({mod.Name})");
                    Set(mod, "link-failed");
                }

                // Move the cursor to the corner to close the tooltip before the next row
                DispatchMouseMove(driver, 2, 2);
                Thread.Sleep(120);
            }
            catch (WebDriverException)
            {
                Set(mod, "link-failed");
            }
        }
    }

    // JS run against a hovered mod-name element:
    //   1) read the span's aria-describedby (floating-ui sets it on hover)
    //   2) find the portal div whose id === that value
    //   3) only if the portal's <a title> equals the mod name, return the <a href>
    const string FindHrefJs = @"
const el = arguments[0];
let span = el.hasAttribute('aria-describedby') ? el : el.querySelector('[aria-describedby]');
if (!span) return null;
const id = span.getAttribute('aria-describedby');
if (!id) return null;
const portal = document.getElementById(id);
if (!portal) return null;
const a = portal.querySelector('a[href]');
if (!a) return null;
const expected = (span.textContent || '').trim();
const title = (a.getAttribute('title') || '').trim();
if (expected !== '' && title === expected) return a.href;
return null;
";

    static string? FindModHrefValidated(EdgeDriver driver, IWebElement nameEl)
    {
        try
        {
            var href = ((IJavaScriptExecutor)driver).ExecuteScript(FindHrefJs, nameEl) as string;
            if (!string.IsNullOrEmpty(href) && ModLinkRegex.IsMatch(href) && !href.Contains("/collections/"))
                return href;
            return null;
        }
        catch (WebDriverException)
        {
            return null;
        }
    }

    // JS that fires real pointer/mouse hover events on the element (and its inner span),
    // so floating-ui's React hover handlers flip even when a raw CDP move doesn't register.
    const string SyntheticHoverJs = @"
const el = arguments[0];
const r = el.getBoundingClientRect();
const x = r.left + r.width / 2, y = r.top + r.height / 2;
const targets = [el];
const span = el.querySelector('span, [aria-describedby]');
if (span) targets.push(span);
for (const t of targets){
  const base = { bubbles: true, cancelable: true, view: window, clientX: x, clientY: y };
  const pbase = Object.assign({ pointerType: 'mouse', pointerId: 1, isPrimary: true }, base);
  t.dispatchEvent(new PointerEvent('pointerover', pbase));
  t.dispatchEvent(new PointerEvent('pointerenter', pbase));
  t.dispatchEvent(new MouseEvent('mouseover', base));
  t.dispatchEvent(new MouseEvent('mouseenter', base));
  t.dispatchEvent(new PointerEvent('pointermove', pbase));
  t.dispatchEvent(new MouseEvent('mousemove', base));
}
";

    // Hover the center of an element. Uses a two-step CDP move (to force a real mouseenter
    // transition) plus synthetic pointer/mouse events as a reliable fallback.
    static void HoverElementCdp(EdgeDriver driver, IWebElement el)
    {
        var coords = (System.Collections.IList)((IJavaScriptExecutor)driver).ExecuteScript(
            "var r = arguments[0].getBoundingClientRect();" +
            "return [r.left + r.width / 2, r.top + r.height / 2];", el);

        double x = Convert.ToDouble(coords[0]);
        double y = Convert.ToDouble(coords[1]);

        // Two-step move: a nearby offset point, then the center -> creates an enter transition
        DispatchMouseMove(driver, x - 20, y - 20);
        Thread.Sleep(40);
        DispatchMouseMove(driver, x, y);

        // Belt-and-suspenders: also dispatch synthetic hover events on the element
        try { ((IJavaScriptExecutor)driver).ExecuteScript(SyntheticHoverJs, el); }
        catch (WebDriverException) { }
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
}
