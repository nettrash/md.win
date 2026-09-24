<#
.SYNOPSIS
    Proves md for Windows on a real Windows 11 machine, in one command, before a release.

.DESCRIPTION
    Run from the repo root (the working tree you want to release - copy it, do not clone it, if
    the release is not committed yet) on Windows 11 with the .NET 10 SDK and the Visual Studio
    Build Tools installed. It stops at the first failure and says which step failed.

      1. Both test suites: Md.Core.Tests and Md.App.Logic.Tests.
      2. The app, built for this machine's own architecture (ARM64 or x64).
      3. The unpackaged self-test binary, built exactly as .github/workflows/windows.yml builds it
         (for this machine's architecture rather than x64 only).
      4. md.exe --selftest: the WebView2 engines and exports, then the shell - real document
         windows, menus, find bar, Replace, the Edit > Typing switches, file associations, and
         real keystrokes (Enter, letters, Backspace, Ctrl+Z, Ctrl+H, F3). The report is printed.
      5. The MSIX, built and payload-checked as windows.yml does, plus two checks CI does not
         make: the packaged manifest still declares every extension the source manifest does,
         and no self-test code reached the package.
      6. The manual checklist: what no automation here can prove.

    It never signs, installs, publishes or submits anything, and it never bumps the package
    version: every build passes -p:BumpPackageVersion=false, so Package.appxmanifest is left
    exactly as it is (a normal build rewrites its Version on every run).

    Keep the desktop unlocked and your hands off the keyboard and mouse during step 4 - the
    keystroke checks type into md's window. Pass -NoInput to skip them (the report says so).

.PARAMETER OutDir
    Where the self-test report and the MSIX go. Default: %TEMP%\md-verify. Emptied first.

.PARAMETER NoInput
    Skip the self-test's keystroke tier (for a session without an interactive desktop).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\verify-on-windows.ps1
#>
[CmdletBinding()]
param(
    [string] $OutDir = (Join-Path $env:TEMP 'md-verify'),
    [switch] $NoInput
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Root
$App = Join-Path $Root 'src\Md.App\Md.App.csproj'
$Started = Get-Date

function Write-Step([string] $Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Stop-Verify([string] $Step, [string] $Why) {
    Write-Host ''
    Write-Host "VERIFY FAILED at step: $Step" -ForegroundColor Red
    Write-Host "  $Why" -ForegroundColor Red
    Write-Host "  Nothing after this step ran. Fix it and run the script again." -ForegroundColor Red
    exit 1
}

# Runs a native command and stops the whole verify on a non-zero exit code.
function Invoke-Native([string] $Step, [string] $File, [string[]] $Arguments) {
    Write-Host "   $File $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { Stop-Verify $Step "$([IO.Path]::GetFileName($File)) exited with $LASTEXITCODE" }
}

# -- 0. The machine ------------------------------------------------------------------------------

Write-Step '0. Checking the machine'
if ($env:OS -ne 'Windows_NT') { Stop-Verify 'machine' 'this script runs on Windows 11 only.' }
# From the registry, not Environment.OSVersion, which an unmanifested host may be lied to about.
$build = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
if ($build -lt 22000) { Stop-Verify 'machine' "Windows build $build is older than Windows 11 (22000); md targets Windows 11 only." }

# The MACHINE's architecture: this shell may itself be an x64 process emulated on ARM64, and then
# $env:PROCESSOR_ARCHITECTURE says AMD64. The system environment block is never emulated.
$osArch = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE
switch ($osArch) {
    'ARM64' { $Platform = 'ARM64'; $Rid = 'win-arm64'; $VsArch = 'arm64' }
    'AMD64' { $Platform = 'x64';   $Rid = 'win-x64';   $VsArch = 'amd64' }
    default { Stop-Verify 'machine' "unsupported architecture '$osArch' (md ships x64 and ARM64)." }
}
Write-Host "   Windows build $build, $osArch -> Platform=$Platform, RuntimeIdentifier=$Rid"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Stop-Verify 'machine' '.NET SDK not found: install the .NET 10 SDK for this architecture (https://dotnet.microsoft.com/download/dotnet/10.0).' }
$sdk = (& dotnet --version) 2>$null
if ($LASTEXITCODE -ne 0 -or -not ($sdk -match '^10\.')) { Stop-Verify 'machine' "dotnet --version answered '$sdk' here; global.json needs a 10.0.1xx SDK or later." }
Write-Host "   .NET SDK $sdk"

# The Visual Studio MSBuild windows.yml uses (microsoft/setup-msbuild), found the way that action finds it.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { Stop-Verify 'machine' 'vswhere.exe not found: install the Visual Studio Build Tools (MSBuild + .NET desktop build tools).' }
$candidates = @(& $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\**\MSBuild.exe')
$MSBuild = $candidates | Where-Object { $_ -match "\\Bin\\$VsArch\\MSBuild\.exe$" } | Select-Object -First 1
if (-not $MSBuild) { $MSBuild = $candidates | Where-Object { $_ -match '\\Bin\\MSBuild\.exe$' } | Select-Object -First 1 }
if (-not $MSBuild) { Stop-Verify 'machine' 'no MSBuild.exe in any Visual Studio installation: install the Build Tools with the "MSBuild Tools" and ".NET desktop build tools" workloads.' }
Write-Host "   MSBuild $MSBuild"

$webView = Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}' -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($webView -and $webView.PSObject.Properties['pv']) { Write-Host "   WebView2 Runtime $($webView.pv)" } else { Write-Host '   WebView2 Runtime: not found in the registry (Windows 11 ships it; step 4 will say if it is really missing)' -ForegroundColor Yellow }

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir | Out-Null
$SelfTestBuild = Join-Path $OutDir 'SelfTestBuild\'
$Report = Join-Path $OutDir 'SelfTestReport'
$Packages = Join-Path $OutDir 'AppPackages\'

# Every msbuild call shares these: never rewrite Package.appxmanifest's Version, and never
# register or launch through the WinApp tooling.
$Common = @('-p:BumpPackageVersion=false', '-p:EnableWinAppRunSupport=false', '-v:minimal', '-nologo')

# -- 1. The test suites --------------------------------------------------------------------------

Write-Step '1. Md.Core.Tests and Md.App.Logic.Tests'
Invoke-Native 'test: Md.Core' 'dotnet' @('test', 'tests\Md.Core.Tests\Md.Core.Tests.csproj', '-c', 'Release', '--nologo')
Invoke-Native 'test: Md.App.Logic' 'dotnet' @('test', 'tests\Md.App.Logic.Tests\Md.App.Logic.Tests.csproj', '-c', 'Release', '--nologo')

# -- 2. The app, for this machine ----------------------------------------------------------------

Write-Step "2. Building the app ($Platform, Release)"
Invoke-Native 'restore' $MSBuild (@($App, '-t:Restore', '-p:Configuration=Release', "-p:Platform=$Platform", "-p:RuntimeIdentifier=$Rid") + $Common)
Invoke-Native 'build: app' $MSBuild (@($App, '-p:Configuration=Release', "-p:Platform=$Platform", "-p:RuntimeIdentifier=$Rid") + $Common)

# -- 3. The unpackaged self-test binary, as windows.yml builds it --------------------------------

Write-Step "3. Building the unpackaged self-test binary ($Platform)"
Invoke-Native 'build: self-test' $MSBuild (@(
    $App, '-restore', '-p:Configuration=Release', "-p:Platform=$Platform", "-p:RuntimeIdentifier=$Rid",
    '-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=true', '-p:SelfTest=true',
    "-p:OutDir=$SelfTestBuild") + $Common)

# windows.yml's guard for the unpackaged half: the engines and the examples sit beside md.exe.
$required = @('md.exe', 'web\rich\katex.min.js', 'web\rich\md-init.js', 'web\rich\mermaid.min.js',
    'web\rich\viz-global.js', 'web\rich\plantuml.js', 'web\rich\highlight.min.js',
    'web\rich\fonts\KaTeX_Main-Regular.woff2', 'Examples\01-Welcome.md', 'Examples\08-Plots.md', 'LICENSE')
$missing = @($required | Where-Object { -not (Test-Path (Join-Path $SelfTestBuild $_)) })
if ($missing.Count -gt 0) { Stop-Verify 'self-test output' "the unpackaged build is missing: $($missing -join ', ')" }
Write-Host '   Unpackaged output carries the engines, the examples and the licence.'

# -- 4. The self-test ----------------------------------------------------------------------------

Write-Step '4. md.exe --selftest'
$arguments = @('--selftest', "`"$Report`"")
if ($NoInput) {
    $arguments += '--no-input'
    Write-Host '   -NoInput: the keystroke checks are skipped; the report will say so.' -ForegroundColor Yellow
} else {
    Write-Host '   HANDS OFF the keyboard and mouse until this step ends (about two minutes), and do not' -ForegroundColor Yellow
    Write-Host '   lock the screen or minimise the VM window: md types into its own editor with SendInput.' -ForegroundColor Yellow
    Start-Sleep -Seconds 5
}
# md.exe is a GUI executable: PowerShell's & would not wait for it (nor set $LASTEXITCODE), which
# is how CI's first self-test step passed without running. Start-Process waits, with a ceiling.
$process = Start-Process -FilePath (Join-Path $SelfTestBuild 'md.exe') -ArgumentList $arguments -PassThru
if (-not $process.WaitForExit(15 * 60 * 1000)) {
    $process.Kill()
    Stop-Verify 'self-test' "md.exe --selftest did not exit within 15 minutes and was killed; see $Report and %LOCALAPPDATA%\md\md.log"
}
$reportFile = Join-Path $Report 'report.json'
if (-not (Test-Path $reportFile)) { Stop-Verify 'self-test' "no report.json in $Report (exit code $($process.ExitCode)); see %LOCALAPPDATA%\md\md.log" }
$json = Get-Content $reportFile -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($check in $json.checks) {
    if ($check.passed) { Write-Host ("   ok    {0}" -f $check.name) -ForegroundColor DarkGreen }
    else { Write-Host ("   FAIL  {0}`n         {1}" -f $check.name, $check.detail) -ForegroundColor Red }
}
Write-Host ("   {0} passed, {1} failed - {2}" -f $json.passed, $json.failed, $reportFile)
if ($process.ExitCode -ne 0 -or $json.failed -gt 0 -or $json.passed -eq 0) {
    Stop-Verify 'self-test' "$($json.failed) check(s) failed (exit code $($process.ExitCode)). The FAIL lines above name each one."
}

# -- 5. The MSIX, as windows.yml builds and checks it --------------------------------------------

Write-Step "5. Building the unsigned MSIX ($Platform) and checking its payload"
Invoke-Native 'build: MSIX' $MSBuild (@(
    $App, '-p:Configuration=Release', "-p:Platform=$Platform", "-p:RuntimeIdentifier=$Rid",
    '-p:GenerateAppxPackageOnBuild=true', '-p:AppxPackageSigningEnabled=false', '-p:AppxBundle=Never',
    "-p:AppxPackageDir=$Packages") + $Common)

$msix = Get-ChildItem -Path $Packages -Recurse -Filter *.msix | Select-Object -First 1
if (-not $msix) { Stop-Verify 'MSIX' "no .msix was produced under $Packages" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($msix.FullName)
try {
    $names = @($zip.Entries | ForEach-Object { [System.Uri]::UnescapeDataString($_.FullName).Replace('\', '/') })

    $required = @('web/rich/katex.min.js', 'web/rich/md-init.js', 'web/rich/mermaid.min.js',
        'web/rich/viz-global.js', 'web/rich/plantuml.js', 'web/rich/highlight.min.js',
        'web/rich/fonts/KaTeX_Main-Regular.woff2', 'Examples/01-Welcome.md', 'Examples/08-Plots.md', 'LICENSE')
    $missing = @($required | Where-Object { $names -notcontains $_ })
    if ($missing.Count -gt 0) { Stop-Verify 'MSIX payload' "the MSIX is missing: $($missing -join ', ')" }

    # The associations Explorer will offer are the PACKAGED manifest's, not the source file's.
    $read = {
        param($entry)
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    $manifestEntry = $zip.Entries | Where-Object { $_.FullName -eq 'AppxManifest.xml' } | Select-Object -First 1
    if (-not $manifestEntry) { Stop-Verify 'MSIX manifest' 'the MSIX has no AppxManifest.xml' }
    $pattern = '<uap:FileType[^>]*>\s*([^<\s]+)\s*</uap:FileType>'
    $packaged = @([regex]::Matches((& $read $manifestEntry), $pattern) | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() } | Sort-Object)
    $source = @([regex]::Matches((Get-Content (Join-Path $Root 'src\Md.App\Package.appxmanifest') -Raw), $pattern) | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() } | Sort-Object)
    if ($source.Count -eq 0 -or $packaged.Count -eq 0 -or (Compare-Object $packaged $source)) {
        Stop-Verify 'MSIX manifest' "packaged file types [$($packaged -join ' ')] differ from Package.appxmanifest's [$($source -join ' ')]"
    }
    Write-Host "   The packaged manifest declares all $($packaged.Count) file types: $($packaged -join ' ')"

    # The self-test must never ship: its flag strings would be in md.dll's string heap (UTF-16).
    $dllEntry = $zip.Entries | Where-Object { $_.FullName -eq 'md.dll' } | Select-Object -First 1
    if (-not $dllEntry) { Stop-Verify 'MSIX payload' 'the MSIX has no md.dll' }
    $stream = $dllEntry.Open()
    $memory = New-Object System.IO.MemoryStream
    try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
    # A #US entry can start at an odd offset, so the bytes are read as UTF-16 from both alignments.
    $bytes = $memory.ToArray()
    $even = [System.Text.Encoding]::Unicode.GetString($bytes)
    $odd = [System.Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
    if ($even.Contains('--shell-only') -or $odd.Contains('--shell-only') -or $even.Contains('--no-input') -or $odd.Contains('--no-input')) {
        Stop-Verify 'MSIX payload' 'the packaged md.dll contains the self-test (its flags are in the string heap): the MSIX was built with -p:SelfTest=true or from a stale obj\. Clean src\Md.App\obj and rebuild.'
    }
} finally { $zip.Dispose() }
Write-Host "   MSIX payload OK: $($names.Count) entries - $($msix.FullName)"
Write-Host '   The package is UNSIGNED and was not installed.'

# -- 6. What no automation here can prove --------------------------------------------------------

$layout = Get-ChildItem -Path (Join-Path $Root "src\Md.App\bin\$Platform\Release") -Recurse -Filter AppxManifest.xml -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'AppX' } | Select-Object -First 1
$register = if ($layout) { "Add-AppxPackage -Register `"$($layout.FullName)`"" } else { 'Add-AppxPackage -Register <src\Md.App\bin\...\AppX\AppxManifest.xml>' }

Write-Step '6. Every automated step passed. Now the manual checklist'
Write-Host @"
   Install the build you just made (Developer Mode on; the Windows App SDK runtime installed):
       $register
   Remove it afterwards with: Get-AppxPackage nttrsh.nettrash.md | Remove-AppxPackage

   [ ] Explorer > right-click > Open with: md is listed for EACH of
       .md .markdown .mdown .markdn .mdtext .mdtxt .mkd .mkdn .mdwn .mkdown
       .txt .text .puml .plantuml .iuml .pu .gv .textpack
       and double-clicking one (with md chosen) opens it in a window; a second double-click on the
       same file brings that window forward instead of opening another. .dot is NOT offered.
   [ ] With md already running, double-click a file: it opens in the running md (no second process
       in Task Manager) and its window comes to the front.
   [ ] Drag a .md file onto the EDITOR text, onto the PREVIEW, and onto the title/menu area of a
       window: each opens it (or says why not) - the TextBox and the WebView2 may swallow a drop.
   [ ] Touch keyboard (Settings > Time & language > Typing > Touch keyboard, or tablet mode): type a
       sentence start - exactly one capital, not a doubled or fought-over one; delete md's capital
       and retype the letter - it stays lowercase; Enter on "- a" continues the list.
   [ ] IME composition (add Japanese/Chinese): on a "- " list item, compose and commit with Enter -
       the commit must NOT also continue the list; a committed word is not capitalised mid-composition.
   [ ] Ctrl+H with the caret in the editor, and again with focus in the find bar's query box: the
       replace field gets the caret and NOT ONE character is deleted anywhere (RichEdit's Ctrl+H is
       Backspace). The self-test checked this from the editor only.
   [ ] RichEdit undo units, by hand: type "m" at a line start -> "M"; Ctrl+Z once -> "m" (not an empty
       line). Type "hello world", Ctrl+Z x3 then Ctrl+Y x3 -> "Hello world", nothing lost, nothing
       re-capitalised. Replace All, then Ctrl+Z once -> every hit back.
   [ ] The TextBox's right-click menu: Undo / Redo / Paste there behave like Ctrl+Z / Ctrl+Y / Ctrl+V
       (a context-menu Redo of a line-start letter is not capitalised into a new edit).
   [ ] Edit > Typing: both rows tick and untick; the tick matches in a second window and in the
       Book window at once.
   [ ] View > Preview (Ctrl+3): Edit > Find..., Find Next, Find Previous, Replace..., Use Selection
       for Find are all greyed; Ctrl+F / F3 / Ctrl+H do nothing visible. Zen (Ctrl+Shift+Enter):
       Ctrl+F / Ctrl+H do nothing, F3 still finds in the writing half.
   [ ] Edit > Cut / Copy / Delete are greyed with no selection and live with one, in a document
       window and in the Book window's article editor.
   [ ] Click View > Edit while already in Edit, and your own row in the Window menu: the tick stays.
   [ ] High-DPI / a second monitor: the find bar's selection highlight is visible while focus is in
       the replacement field.
"@
$elapsed = (Get-Date) - $Started
Write-Host ''
Write-Host ("VERIFY PASSED (automated part) in {0:mm\:ss}. Report: {1}" -f $elapsed, $reportFile) -ForegroundColor Green
exit 0
