using OpenQA.Selenium;
using OpenQA.Selenium.Edge;

namespace NexusShared;

// Small browser/DOM helpers shared by both tools
public static class Dom
{
    // Wait until cond() is true, polling every stepMs (no extra NuGet needed)
    public static bool WaitFor(Func<bool> cond, int timeoutMs, int stepMs = 300)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            try { if (cond()) return true; }
            catch (WebDriverException) { }
            Thread.Sleep(stepMs);
            waited += stepMs;
        }
        return false;
    }

    // JS: search light DOM + every shadow root (Web Components) for the first element
    // matching `selector` (and, if `text` is non-empty, whose trimmed text equals it), then click it.
    const string DeepClickJs = @"
const selector = arguments[0];
const text = (arguments[1] || '').trim().toLowerCase();
function search(root){
  let nodes = [];
  try { nodes = root.querySelectorAll(selector); } catch (e) {}
  for (const el of nodes){
    if (text === '' || (el.textContent || '').trim().toLowerCase() === text){
      el.click();
      return true;
    }
  }
  const all = root.querySelectorAll('*');
  for (const el of all){
    if (el.shadowRoot && search(el.shadowRoot)) return true;
  }
  return false;
}
return search(document);
";

    // Click an element by CSS selector (and optional exact text), piercing shadow DOM
    public static bool DeepClick(EdgeDriver driver, string selector, string text)
    {
        try
        {
            var r = ((IJavaScriptExecutor)driver).ExecuteScript(DeepClickJs, selector, text ?? "");
            return r is bool b && b;
        }
        catch (WebDriverException)
        {
            return false;
        }
    }
}
