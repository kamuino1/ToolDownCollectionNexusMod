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
    const string CollectionUrl = "https://www.nexusmods.com/games/stardewvalley/collections/tckf0m/mods";

    // Đường dẫn tới folder chứa msedgedriver.exe
    const string DriverPath = @"D:\1\Tool\ToolDownCollectionNexusMod\driver";

    // Đường dẫn tới IDM extension (.crx) - tương tự bản FitGirl
    const string IdmExtensionPath = @"C:\Program Files (x86)\Internet Download Manager\IDMGCExt.crx";

    // Bỏ qua bao nhiêu mod đầu tiên (dùng khi chạy lại giữa chừng)
    const int SkipIndex = 0;

    // Profile Edge để GIỮ phiên đăng nhập NexusMods (vượt Cloudflare captcha).
    //
    // CÁCH 1 (khuyên dùng - profile SẴN CÓ đã đăng nhập):
    //   UserDataDir     = thư mục "User Data" của Edge
    //   ProfileDirectory= tên FOLDER profile (không phải tên hiển thị), xem tại edge://version -> "Profile Path"
    //   => PHẢI đóng hết Edge trước khi chạy tool (Edge không share profile đang mở cho Selenium).
    //
    // CÁCH 2 (profile riêng cho tool): UserDataDir trỏ tới folder trống, đăng nhập 1 lần bằng Edge thường.
    const string UserDataDir = @"D:\Temp";

    // Tên folder profile: "Default" hoặc "Profile 1" ... (xem edge://version -> Profile Path)
    const string ProfileDirectory = "Profile 1";

    static void Main()
    {
        // game domain nằm ngay sau host trong CollectionUrl, ví dụ "skyrimspecialedition"
        string gameDomain = ParseGameDomain(CollectionUrl);
        Console.WriteLine($"Game domain: {gameDomain}");

        // Edge phải đóng hoàn toàn thì Selenium mới dùng được profile (nếu không -> SessionNotCreated:
        // "cannot create default profile directory"). Tắt sạch tiến trình Edge còn sót lại.
        KillEdgeProcesses();

        var options = new EdgeOptions();
        options.AddArgument("--start-maximized");

        // Dùng lại profile đã đăng nhập -> bỏ qua trang login + Cloudflare captcha
        options.AddArgument($"user-data-dir={UserDataDir}");
        options.AddArgument($"profile-directory={ProfileDirectory}");

        // Giảm dấu hiệu "trình duyệt bị tự động hoá" để Cloudflare Turnstile không chặn
        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalEdgeOption("useAutomationExtension", false);

        //options.AddExtension(IdmExtensionPath);

        // Thêm uBlock Extension (nếu cần)
        //options.AddExtension(@"D:\1\Tool\ToolDownCollectionNexusMod\extension\ublock.crx");

        var service = EdgeDriverService.CreateDefaultService(DriverPath);
        var driver = new EdgeDriver(service, options);

        // Ẩn navigator.webdriver trước khi trang tải (belt-and-suspenders chống phát hiện automation)
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
            Console.WriteLine("Không set được CDP override (bỏ qua): " + ex.Message);
        }

        // Với profile đã đăng nhập, thường không cần login lại. Vẫn chừa 1 nhịp để
        // bạn tự xử lý nếu còn Cloudflare check / chưa đăng nhập.
        driver.Navigate().GoToUrl("https://www.nexusmods.com/");
        Console.WriteLine("Nếu còn Cloudflare check hoặc chưa đăng nhập: xử lý trong trình duyệt rồi ấn ENTER để tiếp tục...");
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

    // Tắt mọi tiến trình Edge/driver còn chạy để giải phóng khoá profile
    static void KillEdgeProcesses()
    {
        foreach (var name in new[] { "msedge", "msedgedriver" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    p.Kill(true);          // kill cả cây tiến trình con
                    p.WaitForExit(3000);
                }
                catch { /* đã thoát / không kill được -> bỏ qua */ }
            }
        }
        Thread.Sleep(1000); // chờ Windows giải phóng file lock (SingletonLock)
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

    // Chỉ khớp link trang mod thật: .../mods/<số> (loại /mods (không id) và /collections/...)
    static readonly Regex ModLinkRegex = new Regex(@"/mods/\d+(\b|$|[/?#])");

    // ====== PHASE 1: Thu thập tất cả link trang mod trong collection ======
    // Trang collection mới render dạng BẢNG: mỗi mod là 1 <tr class="collection-mod-row">,
    // tên mod chỉ là <span> (không có link). Link trang mod chỉ xuất hiện khi HOVER vào dòng
    // -> site sinh ra <div data-floating-ui-portal> chứa thẻ <a href=".../mods/<id>">.
    static List<string> CollectModLinks(IWebDriver driver, string gameDomain)
    {
        // Chờ bảng mod load xong
        WaitFor(() => driver.FindElements(By.CssSelector("tr.collection-mod-row")).Count > 0, 20000);

        var rows = driver.FindElements(By.CssSelector("tr.collection-mod-row"));
        int rowCount = rows.Count;
        Console.WriteLine($"[Mods] Tìm thấy {rowCount} dòng mod trong bảng.");

        var result = new List<string>();
        var actions = new Actions(driver);

        for (int i = 0; i < rowCount; i++)
        {
            // Re-fetch theo index vì DOM có thể thay đổi (portal/expand) làm element stale
            var freshRows = driver.FindElements(By.CssSelector("tr.collection-mod-row"));
            if (i >= freshRows.Count) break;
            var row = freshRows[i];

            try
            {
                // Phần tử hover: ô tên mod (fallback: cả dòng)
                var hoverTarget = row.FindElements(By.CssSelector(".collection-mod-row__mod-name-container"))
                                     .FirstOrDefault() ?? row;

                ((IJavaScriptExecutor)driver).ExecuteScript(
                    "arguments[0].scrollIntoView({block:'center'});", hoverTarget);
                actions.MoveToElement(hoverTarget).Perform();

                // Chờ portal (hoặc bất kỳ link /mods/<id> mới) xuất hiện
                string? href = null;
                WaitFor(() =>
                {
                    href = FindModHref(driver);
                    return href != null;
                }, timeoutMs: 2500);

                // Fallback: nếu hover không ra link, thử bấm nút mũi tên để mở rộng dòng
                if (href == null)
                {
                    var arrow = row.FindElements(By.CssSelector("button.collection-mod-row__arrow-button"))
                                   .FirstOrDefault();
                    if (arrow != null)
                    {
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", arrow);
                        WaitFor(() => { href = FindModHref(driver); return href != null; }, 2500);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", arrow); // thu gọn lại
                    }
                }

                if (!string.IsNullOrEmpty(href) && !result.Contains(href))
                {
                    result.Add(href);
                    Console.WriteLine($"  [{result.Count}/{rowCount}] {href}");
                }
                else if (href == null)
                {
                    Console.WriteLine($"  [--/{rowCount}] Không lấy được link ở dòng {i + 1}");
                }

                // Rời chuột để đóng portal trước khi sang dòng kế
                actions.MoveToElement(driver.FindElement(By.TagName("h1"))).Perform();
                Thread.Sleep(120);
            }
            catch (WebDriverException)
            {
                // dòng stale / không hover được -> bỏ qua
            }
        }

        return result.Distinct().ToList();
    }

    // Tìm 1 href trỏ tới trang mod (.../mods/<id>) trong portal hiện đang mở
    static string? FindModHref(IWebDriver driver)
    {
        return driver
            .FindElements(By.CssSelector("div[data-floating-ui-portal] a[href*='/mods/']"))
            .Select(a => a.GetAttribute("href"))
            .FirstOrDefault(h => !string.IsNullOrEmpty(h)
                                 && ModLinkRegex.IsMatch(h)
                                 && !h.Contains("/collections/"));
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
