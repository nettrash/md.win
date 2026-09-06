# Privacy Policy

**Effective date:** 6 September 2026
**Applies to:** md for Windows — the Windows 11 Markdown editor published
by nettrash on the Microsoft Store. This policy is versioned alongside the
app's source code; the most recent commit on `main` is authoritative.

## TL;DR

md **does not collect, transmit, sell, or share any data.** It contains
no analytics, no advertising SDKs, no third-party trackers, and no
servers operated by us. The documents you open and edit stay where you
put them — on your PC, or in your own cloud-synced folders.

If that already answers your question, you don't need to read the rest.

## What we collect

**Nothing.** md has no account to create, no email to register, and no
telemetry. It contacts no servers of ours — there are none. The one time
the app touches the network at all is when a document you opened points at
an image by remote URL, and the renderer fetches that image so it can be
shown and printed (see **Permissions** below).

Two things around the app are Microsoft's, and it would be dishonest to let
the sentence above stand for them. First, md draws its preview and its
printed pages in the **WebView2 Runtime**, the Chromium-based component
that ships with Windows 11 and is updated by Microsoft. That runtime
collects its own diagnostic data — about its health, its crashes and how
its APIs are used — governed by Microsoft's privacy statement and by your
own **Settings ▸ Privacy & security ▸ Diagnostics & feedback ▸ Diagnostic
data** setting, not by this policy; Microsoft documents that a required
minimum is collected regardless of that setting, and that a crash inside
the runtime sends a crash dump to Microsoft. The runtime also includes
**Microsoft Defender SmartScreen**, which md leaves enabled, and Microsoft
requires every such app to give this notice: the software includes
Microsoft Defender SmartScreen, which collects and sends information to
Microsoft as described in Microsoft's privacy statement (its *SmartScreen*
section). In md's case the preview only ever loads the page md itself
builds, on your PC — a link you click opens in your browser, not in the
preview — so there is little for SmartScreen to look at, but the component
is there and the notice stands. md **adds nothing to that stream and reads
nothing from it.**
Second, because the app is distributed through the Microsoft Store,
Microsoft reports acquisition counts, ratings and — for a packaged app —
aggregated crash and hang counts that Windows itself collects under that
same diagnostic-data setting, to us as numbers on a publisher dashboard;
nothing in them identifies anybody, and the app sends nothing in order to
produce them.

## Your documents

md is a document editor. The files you open, create and save are handled
through the standard Windows file dialogs, file associations and drag and
drop, and are stored wherever you choose — locally or in a folder your own
cloud provider syncs. We never see them. If you keep a document in a synced
folder, it syncs through *your* account under *that* provider's privacy
terms, not ours.

The app stores a few small settings on your PC, in the package's own
settings store (`ApplicationData.Current.LocalSettings`). They are, in
full:

- `md.viewModeMemory` — which layout (Edit / Split / Preview) you last had
  each of your 200 most recently opened documents in. Each entry is a short
  hash of the file's path, not the path itself, and a mode.
- `md.pdfPageSize` — the page size chosen for PDF export (A4 unless you
  pick another).
- `md.bookBookmark` — the path of the book folder you have open in writer
  mode, if any. **Book ▸ Close Book** removes it.
- `md.bookLastArticle` — which article in that book you had open last, as a
  path relative to the book, so the book reopens where you left it.
- `md.bookOpensInSeparateWindows` — whether a book opens its articles in
  separate windows.
- `md.bookViewMode` — the book window's one layout.
- `md.win.windowSize.document` and `md.win.windowSize.book` — the last size
  of a document window and of the book window, so new windows open at it.

Beside those settings the app keeps, in its package folders, exactly the
following:

- `session.json` — the saved documents you had open when md last closed,
  with each one's layout, Zen state and window position, and whether the
  book window was open and where, so a plain launch reopens them. Untitled
  drafts are not in it. It holds file paths and window geometry, nothing
  else.
- A **WebView2 user-data folder** — the runtime's own cache and state for
  the preview, including the choices you last made in the print dialog. It
  lives under the package's local cache and can hold only what the preview
  loaded, which is md's own page.
- **Temporary files** for Share — the PDF or Markdown copy handed to the
  Windows Share pane, and the PDF an export renders before it is copied to
  the file you named — in the package's temporary folder. It holds nothing
  but those copies; Windows may clear it at any time, and removing the app
  removes it.
- `md.log` — an error log, written **only** when md hits an unhandled
  error, beside `session.json`. A line is a timestamp and the error's own
  text, which can name the file md was reading or writing when it failed.
  It is written for you, on your PC: nothing is sent anywhere, no crash
  report of ours exists to send it to, you can read or delete the file at
  any time, and removing the app removes it.

Two more things are held for md by Windows itself: the **future-access
grant** that lets md reopen your book folder without asking again (removed
by Close Book), and Windows' own **recent-items list**, which is what
**File ▸ Open Recent** and the taskbar Jump List show; **Clear Menu**
empties md's list.

One file may be written beside *your* document rather than in md's folders:
if a save fails while a window is closing — a full disk, a locked file — md
keeps your text next to the original as `<name> (rescued)` with the
original extension — `Notes (rescued).md` — and tells you so. It is your document, in your folder, and md never touches it again.

None of these settings and files leave your PC, and none of them contain
personal information. If a future version remembers anything else, it is
added to this list in the same commit that adds the setting.

## Permissions

md requests no special permissions — no camera, microphone, contacts,
location, or tracking prompt. Its package declares a single capability,
`runFullTrust`, which is what a classic Windows desktop app is: it runs with
your own user's rights, the way a program you install from any other source
does, rather than inside an app container. That is worth stating plainly,
because it is not a sandbox. md reads and writes only the files you open,
save, drop on it or pick as a book — by how it is written, which anyone can
read in its public source, not by a fence the operating system draws around
it.

The manifest declares no network capability, because a full-trust app does
not need one to reach the network; please do not read that as "no
network". md does use the network for one thing. If a document you open
references an image by remote URL (`![alt](https://…)`), the WebView2
renderer fetches that image so it can be shown and printed — that request
goes straight to the host **your own document names**, which sees your IP
address exactly as it would if you opened the link in a browser. It
happens only for documents that contain such a link. A local image beside
the document is deliberately *not* shown in the preview (the family
decision: md's document is its text); the one time md reads a file beside
your document is **Export ▸ TextBundle…**, which copies the local images
the document links into the new bundle's `assets/` — from disk, at your
request.

Everything else is built on your PC: the Markdown renderer, and the math
and diagram engines (KaTeX with mhchem, Mermaid, Graphviz, PlantUML,
highlight.js) are bundled inside the app and run offline. No code is
downloaded or updated at run time. md sends no data anywhere, and contacts
no servers of ours, because there are none.

## Children's privacy

Because md collects no data at all, it collects no data from children.

## Changes to this policy

Any change is committed to this file in the app's public source
repository, so the history is auditable.

## Contact

Questions: <nettrash@nettrash.me>.
