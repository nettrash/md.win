# Microsoft Store listing copy

The text that goes into Partner Center for **md** on the Microsoft Store,
kept next to the code it describes — the Windows counterpart of the Mac
repo's `appstore/` and the Android repo's `play/` folders. One file per
field, plain text, paste as-is. The three listings are written separately,
because the apps differ; this one never mentions the others.

| File | Partner Center field | Limit | Current |
| --- | --- | --- | --- |
| — | Product name | A reserved name; policy 10.1.1 forbids descriptive or marketing text in it, so reserve **md** (fallback if taken: **md by nettrash**, never "md — Markdown editor") | — |
| `description.txt` | Store listing ▸ Description | 10 000 chars, plain text — no HTML, code snippets or URLs (links go in their own fields) | 4939 |
| — | Store listing ▸ What's new in this version | 1 500 chars; **leave blank on the first submission** — Microsoft says so in the field's own help | blank |
| `features.txt` | Store listing ▸ Product features | up to 20 features, ≤ 200 chars each, one per line, **no bullets of our own** (the Store adds them) | 20 lines, longest 150 |
| `short-description.txt` | Supplemental ▸ Short description | 1 000 chars, but some views show only the first 270 — keep under 270 | 265 |
| `system-requirements.txt` | Supplemental ▸ Additional system requirements | up to 11 items each for Minimum and Recommended hardware, ≤ 200 chars each, no bullets | 3 minimum, 2 recommended, longest 91 |
| `search-terms.txt` | Properties ▸ Search terms | ≤ 7 unique terms or phrases (policy 10.1.3); the form has capped each at 30 chars and the set at 21 unique words | 7 terms |
| `copyright.txt` | Supplemental ▸ Copyright and trademark info | 200 chars | 30 |
| — | Supplemental ▸ Additional license terms | 10 000 chars; leave blank — md is MIT, and `LICENSE` ships at the root of the MSIX as well as in the repo (`Md.App.csproj` includes it as Content) | blank |
| — | Supplemental ▸ Short title / Sort title / Voice title | 50 / 255 / 255 — Xbox-facing, leave blank | blank |
| `privacy-policy-url.txt` | Properties ▸ Privacy policy URL | **Mandatory** for a `runFullTrust` (desktop) app — policy 10.5.1 | 43 |
| `support-url.txt` | Properties ▸ Website and Support contact info | a URL; the same page the app opens from Help ▸ md Help | 43 |
| `restricted-capabilities.txt` | Submission options ▸ Restricted capabilities ▸ "Why do you need the runFullTrust capability, and how will it be used in your product?" | no documented limit; keep it one screen and specific | 2204 |
| `certification-notes.txt` | Submission options ▸ Notes for certification | no documented limit; the form has capped it at 2 000 chars — stay under | 1993 |
| `screenshots/` (not yet captured) | Store listing ▸ Screenshots | at least 1; desktop up to 10; PNG, **1366 × 768 or larger** (4K allowed), ≤ 50 MB each; a caption ≤ 200 chars each | — |
| — | Store listing ▸ Store logos ▸ 1:1 app tile icon | 300 × 300 PNG, strongly recommended (otherwise the Store uses the package's logo) | — |

Limits verified against learn.microsoft.com on 2026-09-06 ("Add and edit
Store listing info", "App screenshots, images, and trailers", "Manage
submission options", all for MSIX). The two "the form has capped" notes are
observed behaviour, not documented — re-check them in the form.

## No angle brackets

App Store Connect rejects `<` and `>` in listing fields, and although
Partner Center accepts them the copy here keeps the same rule, so that any
sentence can be pasted into any of the family's listings unchanged. The
private-notes bullet therefore describes the syntax in words ("an HTML
comment on its own line whose text begins with the word note") instead of
showing the comment, and no Markdown or HTML sample appears anywhere in
`store/`. Bullets (•), em dashes (—), curly quotes and ″ are ordinary
Unicode and are accepted.

## Ground rules these texts follow

Every claim was checked against the design and the shipping build — Store
policy 10.1 (accurate, not misleading) is what a listing fails on, and the
listing must describe *this* version, not the roadmap.

- No references to other platforms (the iPhone / iPad, Mac, Android and
  VS Code siblings are deliberately not mentioned), no competitor
  comparisons, no pricing, promotions or "free", no unverifiable
  superlatives, no rating requests.
- Third-party names (Markdown, LaTeX, KaTeX, Mermaid, Graphviz, PlantUML,
  EPUB, TextBundle) are used descriptively, and Microsoft's (Windows,
  Windows 11, File Explorer, WebView2, Jump List, Share) without implying
  endorsement. Only the WebView2 Runtime is named as a dependency, and
  since it ships with Windows 11 policy 10.2.4's "disclose at the start of
  the description" does not apply — the description still says so in
  A REAL WINDOWS APP.
- Privacy claims match the code *and* `PRIVACY.md` — the policy the
  listing links to. Both say the same thing: nothing is collected, and the
  only network use is fetching an image a document itself points at.
- Shortcuts are written the Windows way (`Ctrl+1`, `Ctrl+Alt+Up`), never
  with Mac glyphs.

## Wording that must not drift back

The same corrections the other two listings had to make, plus two that are
Windows-specific:

- **Not** "no third-party dependencies" — the app bundles KaTeX, mhchem,
  Mermaid, Graphviz, PlantUML and highlight.js. Say "the only outside code
  is the open-source engines … bundled in and run on your PC". Say the
  engines are open source and bundled; do **not** claim their licence texts
  are published (that file is still pending family-wide). The `LICENSE` the
  package carries is **md's own MIT licence**, nothing more — it is not an
  engine notice file, and no listing sentence may imply that it is. (Two
  engine notices do ship, and only inside a document: an exported HTML page
  carries KaTeX's MIT and OFL notice when it embeds the KaTeX faces, and
  Mermaid's MIT notice when a Mermaid diagram brings its theme stylesheet with
  it. That is an export's content, not a listing claim.)
- **Not** "zero permissions", "no network", "no network access" or "makes
  no network connections" — the preview fetches an image a document names
  by URL, and WebView2 is Microsoft's runtime with its own diagnostics.
  Say "the only thing md fetches from the network is an image your own
  document points at by URL".
- Private notes are hidden from the preview / PDF / print **only when the
  comment is on its own line**; inline, it renders.
- Pagination is line-aware for *text*. A diagram taller than the page can
  still be split, so the copy does not promise otherwise.
- LaTeX export **keeps a plot's or diagram's source under a comment** —
  "no chart in the .tex". Never claim plots or diagrams are drawn in the
  `.tex`.
- **Windows:** the prose face is **Lucida Sans Typewriter**, a stand-in for
  the family's American Typewriter, which Windows does not ship and md does
  not bundle. Lucida Sans Typewriter ships with Windows, so it may be named;
  never name American Typewriter as something the Windows app has.
- **Windows:** `.textbundle` **folders** open through File ▸ Open
  TextBundle Folder… (Windows cannot associate a folder); `.textpack` opens
  by double-click. Never promise a folder association, Versions, or the
  title-bar proxy menu.
- **Windows:** the app requires **Windows 11**; never imply Windows 10.
  Don't name OneDrive — "your own cloud-synced folders" is the honest
  phrase for any provider the file dialog reaches.

## Search terms

Seven terms, one per line, each a generic description of what the app does
— never another product's title (policy 10.1.3 lists that as a violation),
no pricing terms, no superlatives. Phrases count as one term ("markdown
editor"). Terms already in the product name are wasted. Singular forms are
enough; the Store matches plurals.

## Screenshots to capture

Desktop screenshots, PNG, 1920 × 1080 (the Store's minimum is 1366 × 768),
light theme unless noted, in this order — captions in `screenshots/captions.txt`
when they exist, ≤ 200 chars each:

1. Split layout on `06-Math.md` — editor left, typeset chemistry right.
2. Preview of `07-Diagrams.md` — Mermaid, Graphviz and PlantUML on one page.
3. Split on `08-Plots.md` — the plot block's source and the chart.
4. The Book window on the unpacked Example Book — sidebar, article, footer.
5. Zen mode, dark theme, writing.
6. The in-window print preview, or File ▸ Export ▸ PDF Page Size open.

Recipe: a fresh local Windows account (nothing personal in the Recent list
or the taskbar), display 1920 × 1080 at **100 %** scaling (Settings ▸
System ▸ Display ▸ Scale), taskbar set to hide automatically (Settings ▸
Personalization ▸ Taskbar ▸ Taskbar behaviors) so a maximized window is
exactly 1920 × 1080; open the example, `Win+Up` to maximize, then
`Win+PrtScn` — Windows saves `Pictures\Screenshots\Screenshot (n).png` at
the display's resolution. Never capture the whole desktop with other
windows on it; the window is the shot. Keep text in the top two-thirds
(the Store may overlay captions on the bottom third) and add no marketing
text or logos to the images.

## Age rating (IARC)

Answered at submission through the IARC questionnaire (policy 11.11). For
md every content question is **No**: no violence, sexual content, language,
controlled substances, gambling or fear; no user-to-user interaction or
exchange of content (the Windows Share pane is the OS's, not an in-app
exchange); no sharing of location or personal information; no purchases;
no unrestricted internet browsing (the preview shows only the document).
Expected result: **ESRB Everyone / PEGI 3 / USK 0 / IARC 3+** across the
board. The wording of the questions changes; answer the substance.

## Properties and declarations

- Category **Productivity**; no subcategory needed.
- System requirements (the structured checkboxes): Keyboard and Mouse as
  required hardware; nothing else required. Touch is supported but not
  required.
- Product declarations: leave *"This app has been tested to meet
  accessibility guidelines"* unticked unless it has been; *"Customers can
  install this app to alternate drives"* can stay on; no drivers, no NT
  services, no pen-and-ink dependence.
- **Restricted capability:** `runFullTrust` is one, and Partner Center asks
  why. The justification is in `certification-notes.txt` (a WinUI 3 desktop
  app using the Windows file pickers, drag and drop, file associations and
  WebView2, reading and writing only the user's own documents).
- **Privacy policy URL** is mandatory for this app (policy 10.5.1: Desktop
  Bridge / full-trust products always need one). The site page must be
  live — `nettrash-me/frontend/assets/msstore/md/privacy.html`, served at
  the URL in `privacy-policy-url.txt` — *before* the submission is sent.
  The site now builds it: `frontend/index.html` carries
  `rel="copy-dir" href="assets/msstore"` beside the `appstore` and `play`
  lines, and `trunk build --release` writes `dist/msstore/md/{privacy,support}.html`
  (verified). What remains is the **deploy** — check both URLs answer 200.

## Before submitting: what can only be done on Windows or in Partner Center

Everything above is in the repo and reviewable here. The following cannot
be: each needs a Windows machine, or the Partner Center form itself. None
of them is done. Tick them off in order.

- [ ] **Screenshots.** At least one is required and none exists — there is
      no `store/screenshots/` folder yet. PNG, **1366 × 768 or larger**
      (shoot 1920 × 1080), ≤ 50 MB each, up to 10; capture the six shots
      listed under *Screenshots to capture* above, on a fresh local Windows
      account, following the recipe there. Also consider the optional
      300 × 300 PNG store logo.
- [ ] **IARC age-rating questionnaire.** Answered in the submission form,
      not in this repo (policy 11.11). The intended answers are written out
      under *Age rating (IARC)* above — answer the substance, since the
      wording of the questions changes.
- [ ] **Reserve the product name.** Partner Center ▸ *Create a new app* →
      reserve **md**; if it is taken, **md by nettrash** (never a
      descriptive name — policy 10.1.1).
- [ ] **Swap the `Identity` placeholders.** `src/Md.App/Package.appxmanifest`
      ships `Name="nettrash.md"` and `Publisher="CN=nettrash"` as
      placeholders, with a comment at the top of the file saying so. Replace
      both with the values Partner Center ▸ *Product identity* shows for the
      reserved name (or let Visual Studio ▸ *Associate App with the Store*
      rewrite them). The MSIX will not be accepted until they match.
- [ ] **Justify `runFullTrust`.** Partner Center asks why the restricted
      capability is declared. Paste the CAPABILITIES paragraph from
      `certification-notes.txt`: a WinUI 3 desktop app using the Windows file
      pickers, drag and drop, file associations and WebView2, reading and
      writing only the user's own documents.
- [ ] **Deploy the two site pages.** `https://nettrash.me/msstore/md/privacy.html`
      and `.../support.html` must answer 200 before the submission is sent;
      the build already writes them (see *Properties and declarations*).

## First submission

"What's new in this version" stays **blank**: Microsoft's guidance for a
first submission. From 1.1 on it carries the release's headline changes in
the family's shape (`Version 1.1 — …`, then ALL-CAPS groups of • bullets),
within 1 500 characters. The MSIX `Version` must be strictly greater than
the previous submission's and its Revision must be 0.

The Build component is bumped **automatically on every build** by the
`BumpPackageVersion` target in `src/Md.App/Md.App.csproj`, which rewrites only
the digits in `Package.appxmanifest`'s `<Identity Version="…">` and always
writes Revision back as 0. So a resubmission can never be rejected for a
version that is not strictly greater. Consequences: the manifest shows as
modified after any build, CI bumps it too, and two builds of identical source
produce different packages. `-p:BumpPackageVersion=false` opts out for a build,
and Visual Studio's design-time builds are excluded so the version does not
churn while you type.
