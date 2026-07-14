# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A pair of **Windows-only .NET 9 console tools** that drive **Microsoft Edge via Selenium** to batch-download the mods in a NexusMods *collection* through the free-tier "slow download" flow, tracking every mod's progress in a CSV. It is personal browser-automation, not a service or library for others.

- `ToolDownCollectionNexusMod` — the main tool: scrape a collection → resolve each mod's link → download.
- `RetryFailedMods` — reloads a progress CSV and re-runs only the download phase for rows that aren't `done`.
- `NexusShared` — class library holding everything both tools share.

## Commands

```powershell
# Build the whole solution (3 projects)
dotnet build ToolDownCollectionNexusMod.sln

# Run the main collection tool
dotnet run --project ToolDownCollectionNexusMod

# Run the retry tool
dotnet run --project RetryFailedMods
```

There are **no tests** and no lint config. Both exes target `net9.0-windows` and only build/run on Windows (Edge, Win32 `SendKeys` in `IDMHander.cs`, and hardcoded `C:\`/`D:\` paths).

There is no config file: **all settings are `const`s at the top of each `Program.cs`** (collection URL, driver path, Edge profile, CSV/resume path, `SkipIndex`). Editing behavior = editing those consts.

## Architecture

Two thin exe "orchestrators" over one shared library. The entire download step (`Phase2`) lives in `NexusShared` so both tools run it identically — the only difference is how the `List<ModEntry>` is produced (scrape the page vs. load a CSV).

- **`NexusShared/`** (namespace `NexusShared`)
  - `ModEntry.cs` — one CSV row: `Index, Name, Url, Status, UpdatedAt`.
  - `CsvStore.cs` — `Load`/`Save` + tolerant quoted-CSV parsing (UTF-8 BOM). The CSV is the source of truth and the resume mechanism.
  - `EdgeFactory.cs` — `KillEdgeProcesses()` + `Create(driverPath, userDataDir, profileDir)`: builds the `EdgeDriver` with anti-detection flags and a CDP script hiding `navigator.webdriver`.
  - `Dom.cs` — `WaitFor` (polling helper) and `DeepClick` (clicks by CSS/text **piercing shadow DOM**).
  - `Phase2.cs` — `Run(driver, mods, persist)`: **the shared download phase**. Per mod (skips blank-`Url`/`done`): open `?tab=files` in a new tab → DeepClick `Manual` → `Manual download` → `Slow download` → set status → close tab.
  - `Logging.cs` — Serilog setup (`Setup`/`Line`/`Close`); console + rolling daily file under `logs/`.
- **`ToolDownCollectionNexusMod/`** (main tool)
  - `Program.cs` — orchestrates: resume-load → launch Edge → login gate → Phase 1a/1b → `Phase2.Run`.
  - `Collector.cs` — **Phase 1, main-tool only**: `CollectTitles` (scrape all titles), `AddNewTitles` (dedup vs. resume list), `FillLinks` (hover each row → resolve link), plus the hover/link helpers.
  - `IDMHander.cs` — legacy IDM (Internet Download Manager) window handler; **currently unused** by the download flow (kept in case IDM integration is re-enabled).
- **`RetryFailedMods/`** — `Program.cs` only: `CsvStore.Load` → filter → `Phase2.Run`.
- `flows.html` — a rendered diagram of both flows + the shared Phase 2 + the status lifecycle. Update it when the flow changes.

### The two-phase pipeline (main tool)
- **Phase 1a — titles:** `CollectTitles` reads every mod title from the collection table in one JS call (no hover), so 100% of titles are captured up front and written to the CSV.
- **Phase 1b — links:** `FillLinks` hovers each row to resolve the mod link (see below), setting `link-ok` / `link-failed` / `duplicate`. Rows that already have a `Url` are skipped (this is what makes resume cheap).
- **Phase 2 — download:** shared `Phase2.Run`.

Status lifecycle per mod: `pending → link-ok → opened → manual-clicked → manualdl-clicked → slow-clicked → done`, with off-ramps `link-failed` / `duplicate` (blank Url, skipped) and `*-not-found` / `error: …`. **`persist` (a callback that rewrites the whole CSV) is invoked after every status change**, so the CSV is always a live snapshot and any run is resumable.

## Non-obvious constraints that dictate the design

Read these before changing the browser/scraping code — they explain why it looks the way it does:

- **Non-default Edge profile is mandatory.** Recent Edge/Chromium refuses automation on the *default* `User Data` dir ("DevTools remote debugging requires a non-default data directory"). `EdgeFactory` must point `--user-data-dir` at a separate, logged-in profile. Cross-machine profile copies also fail (cookies are DPAPI/App-Bound-encrypted to the machine) — the profile must be logged in on the same PC.
- **Edge must be closed first.** A running Edge holds the profile lock → `SessionNotCreated`. `KillEdgeProcesses()` force-kills `msedge`/`msedgedriver` at startup (this closes the user's open Edge windows — by design).
- **Cloudflare / login:** both tools navigate to nexusmods.com and `Console.ReadLine()`-pause so the user can clear any Cloudflare check / sign in manually before proceeding. The logged-in profile + anti-detection flags minimize challenges.
- **Collection mod links only exist on hover.** The mods tab is a table whose rows have *no* link; hovering a row makes a `data-floating-ui-portal` tooltip appear containing the `<a href>`. Selenium's synthetic hover doesn't trigger it, so `Collector.HoverElementCdp` uses a real CDP mouse move + synthetic pointer events, and `FindModHrefValidated` matches the tooltip's `<a title>` to the mod name before taking the href.
- **Download UI is inside a Web Component Shadow DOM** (`<mod-download-modal>`). Normal `FindElements` can't see it — that's the entire reason `Dom.DeepClick` recurses through shadow roots.
- **`msedgedriver.exe` in `driver/` must match the installed Edge version.** If Edge updates and the driver doesn't, the driver won't start.

## Layout

Each project has its own folder under the solution root (`<sln>\ToolDownCollectionNexusMod\`, `\NexusShared\`, `\RetryFailedMods\`); the solution root itself holds only the `.sln` plus shared runtime assets (`driver\`, the progress CSVs, `logs\`, `flows.html`). The config consts in each `Program.cs` reference the **solution-root** folder by absolute path (e.g. `DriverPath = …\ToolDownCollectionNexusMod\driver`), not the project folder.
