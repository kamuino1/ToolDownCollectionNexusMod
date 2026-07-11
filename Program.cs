using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Interactions;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
//135.0.3179.66
class Program
{
    // ====== CẤU HÌNH ======
    // Link collection trên NexusMods (dạng mới: https://next.nexusmods.com/<game>/collections/<slug>)
    const string CollectionUrl = "https://next.nexusmods.com/skyrimspecialedition/collections/xxxxxx";

    // Đường dẫn tới folder chứa msedgedriver.exe
    const string DriverPath = @"D:\1\Tool\ToolDownCollectionNexusMod\driver";

    // Đường dẫn tới IDM extension (.crx) - tương tự bản FitGirl
    const string IdmExtensionPath = @"C:\Program Files (x86)\Internet Download Manager\IDMGCExt.crx";

    // Bỏ qua bao nhiêu mod đầu tiên (dùng khi chạy lại giữa chừng)
    const int SkipIndex = 0;

    static void Main()
    {
        // game domain nằm ngay sau host trong CollectionUrl, ví dụ "skyrimspecialedition"
        string gameDomain = ParseGameDomain(CollectionUrl);
        Console.WriteLine($"Game domain: {gameDomain}");

        var options = new EdgeOptions();
        options.AddArgument("--start-maximized");
        options.AddExtension(IdmExtensionPath);

        // Thêm uBlock Extension (nếu cần)
        //options.AddExtension(@"D:\1\Tool\ToolDownCollectionNexusMod\extension\ublock.crx");

        var service = EdgeDriverService.CreateDefaultService(DriverPath);
        var driver = new EdgeDriver(service, options);

        // NexusMods yêu cầu đăng nhập mới tải được -> mở trang login rồi chờ user đăng nhập tay
        driver.Navigate().GoToUrl("https://users.nexusmods.com/auth/sign_in");
        Console.WriteLine("Hãy đăng nhập NexusMods trong trình duyệt, xong rồi ấn ENTER ở cửa sổ này để tiếp tục...");
        Console.ReadLine();

        // Mở trang collection
        driver.Navigate().GoToUrl(CollectionUrl);
        Thread.Sleep(4000);

        // ====== PHASE 1: Thu thập link mod ======
        var modLinks = CollectModLinks(driver, gameDomain)
            .Skip(SkipIndex)
            .ToList();

        Console.WriteLine($"Tìm thấy {modLinks.Count} mod trong collection.");
        if (modLinks.Count == 0)
        {
            Console.WriteLine("Không lấy được link mod nào. Kiểm tra lại CollectionUrl / đăng nhập / selector.");
            driver.Quit();
            return;
        }

        // ====== PHASE 2: Tải từng mod ======
        var mainTab = driver.WindowHandles.First();

        foreach (var modUrl in modLinks)
        {
            // Chuẩn hoá về tab Files của mod
            string filesUrl = modUrl.Contains("?") ? modUrl + "&tab=files" : modUrl + "?tab=files";

            // Mở tab mới
            ((IJavaScriptExecutor)driver).ExecuteScript("window.open(arguments[0], '_blank');", filesUrl);
            Thread.Sleep(1000);

            // Chuyển sang tab mới
            var tabs = driver.WindowHandles;
            driver.SwitchTo().Window(tabs.Last());

            // Đợi trang tải xong
            Thread.Sleep(4000);

            try
            {
                // 1) Bấm nút "Manual" (tải thủ công) trên tab Files
                var manualButton = driver.FindElement(By.CssSelector("a.btn[href*='file_id']"));
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", manualButton);
                Thread.Sleep(4000);

                // 2) Trang trung gian -> bấm "Slow download"
                try
                {
                    var slowButton = driver.FindElement(By.CssSelector("#slowDownloadButton"));
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", slowButton);
                }
                catch (NoSuchElementException)
                {
                    // Tài khoản Premium: tải trực tiếp, không có nút slow download
                    Console.WriteLine("Không thấy nút Slow download (có thể là tài khoản Premium): " + modUrl);
                }
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("Không tìm thấy nút tải trong mod: " + modUrl);
                driver.Close();
                driver.SwitchTo().Window(mainTab);
                continue;
            }

            // Gọi handler để xử lý IDM popup (Enter = Start, Ctrl+P = Pause)
            IDMHandler.HandleIDMWindow();

            Thread.Sleep(6000);

            driver.Close();                    // đóng tab mod
            driver.SwitchTo().Window(mainTab); // quay lại tab collection
        }

        driver.Quit();
        Console.WriteLine("Hoàn tất!");
    }

    // Lấy game domain (segment ngay sau host) từ CollectionUrl
    static string ParseGameDomain(string collectionUrl)
    {
        try
        {
            var uri = new Uri(collectionUrl);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[0] : "";
        }
        catch
        {
            return "";
        }
    }

    // ====== PHASE 1: Thu thập tất cả link trang mod trong collection ======
    static List<string> CollectModLinks(IWebDriver driver, string gameDomain)
    {
        // Bấm sang tab "Mods" để dữ liệu mod được render ra document
        ClickModsTab(driver);
        Thread.Sleep(2000);

        // --- Layer A: đọc dữ liệu nhúng trong page source ---
        var links = ExtractLinksFromPageSource(driver, gameDomain);
        if (links.Count > 0)
        {
            Console.WriteLine($"[Layer A] Lấy link từ page source: {links.Count} mod.");
            return links;
        }

        // --- Layer B: hover từng tile để lấy link trong floating-ui-portal ---
        Console.WriteLine("[Layer A] Không thấy link -> chuyển sang [Layer B] hover từng tile...");
        links = ExtractLinksByHover(driver);
        Console.WriteLine($"[Layer B] Lấy link bằng hover: {links.Count} mod.");
        return links;
    }

    // Layer A: regex quét page source tìm các tham chiếu /<game>/mods/<id>
    static List<string> ExtractLinksFromPageSource(IWebDriver driver, string gameDomain)
    {
        if (string.IsNullOrEmpty(gameDomain))
            return new List<string>();

        string html = driver.PageSource;
        var pattern = $@"/{Regex.Escape(gameDomain)}/mods/(\d+)";
        var ids = Regex.Matches(html, pattern)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        return ids
            .Select(id => $"https://www.nexusmods.com/{gameDomain}/mods/{id}")
            .ToList();
    }

    // Layer B: hover từng tile mod, đọc <a> sinh ra trong div[data-floating-ui-portal]
    static List<string> ExtractLinksByHover(IWebDriver driver)
    {
        var result = new List<string>();
        var actions = new Actions(driver);

        // Selector tile mod - có thể cần chỉnh theo DOM thực tế
        var tiles = driver.FindElements(By.CssSelector("[data-e2eid='mod-tile'], article, li"));
        Console.WriteLine($"[Layer B] Tìm thấy {tiles.Count} tile ứng viên.");

        foreach (var tile in tiles)
        {
            try
            {
                actions.MoveToElement(tile).Perform();

                // Chờ portal + <a> xuất hiện
                IWebElement? anchor = null;
                WaitFor(() =>
                {
                    var found = driver.FindElements(
                        By.CssSelector("div[data-floating-ui-portal] a[href*='/mods/']"));
                    if (found.Count > 0)
                    {
                        anchor = found[0];
                        return true;
                    }
                    return false;
                }, timeoutMs: 2000);

                if (anchor != null)
                {
                    string href = anchor.GetAttribute("href");
                    if (!string.IsNullOrEmpty(href) && !result.Contains(href))
                        result.Add(href);
                }

                // Rời chuột để portal đóng lại trước khi hover tile kế
                actions.MoveToElement(driver.FindElement(By.TagName("body")), 0, 0).Perform();
                Thread.Sleep(200);
            }
            catch (WebDriverException)
            {
                // tile không hover được / stale -> bỏ qua
            }
        }

        return result.Distinct().ToList();
    }

    // Bấm tab "Mods" trong collection (bỏ qua nếu không tìm thấy)
    static void ClickModsTab(IWebDriver driver)
    {
        try
        {
            var tab = driver.FindElements(By.CssSelector("a, button, [role='tab']"))
                .FirstOrDefault(e =>
                {
                    string text = (e.Text ?? "").Trim();
                    return text.Equals("Mods", StringComparison.OrdinalIgnoreCase)
                        || text.StartsWith("Mods", StringComparison.OrdinalIgnoreCase);
                });

            if (tab != null)
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", tab);
                Console.WriteLine("Đã bấm tab Mods.");
            }
            else
            {
                Console.WriteLine("Không tìm thấy tab Mods (có thể mặc định đã ở tab Mods).");
            }
        }
        catch (WebDriverException)
        {
            Console.WriteLine("Lỗi khi bấm tab Mods, tiếp tục...");
        }
    }

    // Helper: chờ điều kiện cond đúng, poll mỗi stepMs (không cần thêm NuGet)
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
                // element chưa sẵn sàng -> thử lại
            }
            Thread.Sleep(stepMs);
            waited += stepMs;
        }
        return false;
    }
}
