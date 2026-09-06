# Verified facts for the md.win port (fetched 2026-09-05)

## Toolchain (nuget.org / learn.microsoft.com)
- Windows App SDK stable channel: **2.4.0** (released 2026-08-13; 2.x supported to 2027-04-29). 1.8 is in maintenance and leaves support 2026-09-09. Metapackage `Microsoft.WindowsAppSDK` 2.4.0 is what a WinUI app references (it pulls `Microsoft.WindowsAppSDK.WinUI` 2.3.6 etc. transitively).
- Current `dotnet new winui` template (Microsoft.WindowsAppSDK.WinUI.CSharp.Templates 0.0.6-alpha): TFM `net10.0-windows10.0.26100.0`, `UseWinUI=true`, `WinUISDKReferences=false`, `EnableMsixTooling=true`, packages `Microsoft.Windows.SDK.BuildTools` 10.0.26100.7705 + `Microsoft.Windows.SDK.BuildTools.WinApp` 0.3.1 (gives `dotnet run` package identity), Platforms x86;x64;ARM64, publish profiles SelfContained=true. The template's App.xaml.cs relies on the XAML-generated `Program.Main`.
- Local .NET SDKs on this Mac: 10.0.102 and 8.0.403. A `UseWinUI` LIBRARY builds on macOS with `-p:EnableWindowsTargeting=true`; an APP fails only at XamlCompiler.exe (net472). So code-behind can be type-checked here via a shadow Library project (tools/xamlcheck).
- WinUI 3 WebView2 control: `Microsoft.UI.Xaml.Controls.WebView2`; WebView2 SDK comes transitively (Microsoft.Web.WebView2 latest stable 1.0.4191.47). Windows 11 ships the Evergreen WebView2 runtime in-box.
- Decision: TargetPlatformMinVersion / manifest MinVersion **10.0.22000.0 (Windows 11)**; Platforms x64;ARM64 (no x86: Windows 11 has no 32-bit edition).

## Microsoft Store listing (Partner Center, MSIX)
- Description: required, ≤ **10,000** chars plain text; no HTML, code snippets or URLs in it (links go in their own fields).
- What's new in this version: ≤ **1500** chars; leave BLANK on a first submission.
- Product features: up to **20**, ≤ **200** chars each, no bullets of our own.
- Short description: ≤ **1000** chars, only the first **270** shown in some views — keep under 270.
- Short title ≤ 50; Sort title ≤ 255; Voice title ≤ 255 (Xbox-only uses).
- Additional system requirements: up to 11 items each for Minimum and Recommended hardware, ≤ 200 chars each.
- Screenshots: at least **1** required (recommend ≥ 4 per device family); Store logos optional.
- Search terms (policy 10.1.3): ≤ **7** unique terms/phrases, relevant, no pricing terms, no other products' titles.
- Policy 10.5.1: "Product types that inherently have access to Personal Information must always have privacy policies. These include ... Desktop Bridge and Win32 products." A runFullTrust packaged desktop app is one → a **privacy policy URL is mandatory** in Partner Center.
- Policy 10.2.2: no dynamic inclusion of remote code — our engines are bundled in the package (fine); remote images in documents are content, not code.
- Policy 10.2.4: dependency on non-integrated software must be disclosed at the START of the description — WebView2 runtime is in-box on Windows 11 so no disclosure needed at min 22000; mention offline engines are bundled.
- Policy 11.11: IARC age-rating questionnaire at submission (a Markdown editor → "Everyone"/PEGI 3 answers).
- Policy 10.1.1: title must not contain descriptive/marketing text → product name "md" (reserve it in Partner Center; if taken, "md — Markdown editor" is NOT allowed as a name with descriptive text... a fallback like "md by nettrash" may be needed).

## Family conventions (from memory + repos)
- Author: nettrash <nettrash@nettrash.me>; MIT © 2026 nettrash; CODEOWNERS `* @nettrash`.
- Never claim "no third-party dependencies" or "zero permissions"; never use '<' or '>' in store copy (App Store rule; keep the habit); LaTeX export keeps plot source under a comment (don't overclaim); bundled engines are "open source and bundled" — do not claim licence texts are published.
- New port starts at version **1.0** (md.vscode precedent) with feature parity to md 1.4.
- md-init.js and rich/ are byte-identical across the app repos; Examples/ byte-identical; 15 of 16 testdata fixtures byte-identical; test.md is per-platform (already localised for Windows in tests/Md.Core.Tests/Fixtures/testdata/test.md).
- CSS font stack is `"American Typewriter", "Courier New", serif` → on Windows the preview prose falls to Courier New (the stylesheet is shared and must stay byte-identical). Editor font on Windows: Courier New to match. No font bundling (nettrash would have to name a specific font/licence first).

## Typography decision for Windows (settled 2026-09-05 after reading export.md/html.md)
- The shared stylesheet bytes stay `"American Typewriter", "Courier New", serif` (golden parity). Exports (HTML/EPUB/SVG/LaTeX) are produced from an OFFSCREEN WebView2 that loads the pure Core HTML, exactly as macOS's offscreen WebRenderer does — so nothing Windows-specific can leak into an export.
- The on-screen preview and the print/PDF renderer get a Windows-only `<style id="md-win-fonts">` appended by the APP (never by Md.Core, never in the export renderer): prose `Georgia, "Courier New", serif` (the family's stand-in — md.vscode uses Georgia where American Typewriter is absent; md.Android's own CSS and the shared EPUB CSS use Georgia too), code stays `"Courier New", monospace`.
- The TextBox editor face is Georgia (prose) at the macOS editor size; README states the stand-in plainly, as md.vscode's README does.
