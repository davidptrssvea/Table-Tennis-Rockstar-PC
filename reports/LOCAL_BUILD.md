# Local build and source distribution

## v1.0.2 graphical installer

The public `Install-Table-Tennis.exe` is compiled from `project/installer/Installer.cs`
and embeds the 31-file reviewed source ZIP. It contains no compiled game or retail
archive. The installer builder verifies all ZIP member names, uniqueness and bytes
against the public source allowlist before embedding it.

The GUI chooses ISO/destination, obtains download/install consent, extracts only
the source payload and runs `bootstrap.ps1`. The bootstrap downloads the official
Python 3.14.8 x64 embedded runtime and verifies its published SHA-256. Python stays
isolated under `.tools`; no system Python installation or pip packages are needed.

The selected retail ISO is validated before compiler installation. If the compiler
check fails, the bootstrap verifies Microsoft's installer Authenticode signature
and requests the VC tools workload, recommended Windows SDK components, CMake and
Clang. Only that Microsoft installation requests elevation. Restart codes are
reported without restarting automatically. The project does not disable security
controls or bypass blocked downloads.

Sources for component IDs and the pinned runtime:
- https://learn.microsoft.com/en-us/visualstudio/install/workload-component-id-vs-build-tools?view=vs-2022
- https://learn.microsoft.com/en-us/visualstudio/install/use-command-line-parameters-to-install-visual-studio
- https://www.python.org/downloads/release/python-3148/

The bootstrap was run from an unpacked source package, downloaded/verified the
portable Python runtime, validated the ISO, and completed all 95 C++ build steps,
launcher compilation and runtime staging using existing compilers and a supplied
SDK. The GUI rendered correctly and refused to begin without an ISO. Automatic
installation of missing Microsoft components was not exercised on this PC.
The first-time ReXGlue download remains unverified due to the earlier security block.

All nine automated tests passed. These include rejection of extra game files in
the embedded payload and verification that ISO-only validation does not inspect
the compiler environment or obtain an SDK. Re-running the final bootstrap with
downloads disabled reused the isolated runtime, applied zero native edits and
completed successfully without changing system components.

The existing working game installation is separate from installer validation.

The public files are integration code, locally applied changes, build scripts,
metadata and third-party notices. Generated game functions and original/modified
retail archives are not part of the source allowlist.

## Pipeline

1. Check Windows x64, Python and the compiler tools before producing game output.
2. Use a supplied ReXGlue SDK v0.10.0, or download the pinned official Windows
   archive and verify SHA-256 before extraction. Never disable security controls.
3. Open the user's ISO read-only. Parse the bounded GDFX directory tree, reject
   cycles/out-of-image entries, and verify the supported executable's SHA-256.
4. Extract only default.xex and assets/resource/flash/flash.rpf into ignored local
   folders. Check the original archive/movie hashes before applying menu changes.
5. Generate C++ from the user's XEX with the supported SDK and function metadata.
6. Apply 17 verified native edit rules (16 functions plus the SDK-derived PCH).
   Rules contain hashes, positions and replacement text, not original bodies.
   Each rule recognizes both supported clean and patched output.
7. Configure and compile the local game. Compile the project-authored launcher,
   copy SDK runtime dependencies and license notices, and create local settings.

The patch rules reproduce the preceding working integration, including offline
profile hooks, high-refresh pacing, intro stepping and input filtering. Segoe UI
Semibold is loaded from Windows for the 39px loading text, with fallback fonts.
The two adapted SDK headers preserve the x64 FP exception-mask correction without
editing the user's installed SDK.

## Verification

### v1.0.6 submission timing and presentation

Issue #8 reports rough motion despite an approximately 141 FPS F10 reading. The
old reading was ImGui's redraw rate, and the old UI callback slept/spun for a
second frame limit independently of the producer-side VdSwap limit. A permanently
attached but hidden startup/FPS dialog also retained ownership of presentation
on the UI thread. SDK v0.10.0 can present directly from guest output when no UI
drawers are attached; the project now releases that path after startup when F10
is hidden. The new limiter waits before VdSwap on fixed deadlines, never inside
the UI draw callback. It skips missed deadlines instead of bursting after stalls.

The prior explanation that VBlank always stayed at 60 Hz was incorrect: although
the reported video mode is 60 Hz, the SDK graphics worker uses 1000 Hz when
`vsync=false`. This was checked in the tagged upstream SDK source. Raising this
rate did not establish high-FPS motion in this game.

An instrumented v1.0.5 test at a 144 cap measured approximately 60 guest swaps/s
in a visually confirmed Exhibition match while the old UI read approximately
140 FPS. Intro/menu similarly produced approximately 60 swaps/s. The correction
reports guest submission rate and mean/p95/max intervals, separately from UI FPS.
It is not a measurement of unique completed GPU images, GPU render time or
simulation ticks. Smooth true 120/144 FPS gameplay remains unresolved; higher caps
are retained as experimental and the launcher defaults to 60 FPS/VSync.

In the new 60 mode, sampled settled two-second windows measured about 60.0
submissions/s, mean 16.66–16.67 ms, p95 around 17.4–17.8 ms, with occasional
outliers. These are local submission timing observations, not a display-capture
benchmark or proof of a fix on the reporter's RTX 5070 system. Initial loading
and shader compilation stalls must not be mistaken for settled gameplay pacing.

Deterministic native tests exercise deadline overshoot, missed deadlines, cap
changes, uncapped mode, sample-window rollover, percentile calculation and stale
samples. They can be run with `TABLETENNIS_TEST_NATIVE=1` via unittest on Windows.
The ISO runtime test also checks that an explicit 144 cap overrides saved 60.

The public v1.0.6 installer payload was extracted into the isolated installation
and its backend completed generation, patching, C++ compilation, launcher/icon
compilation and staging with cached components and a supplied SDK.
All 11 tests (including native pacing and ISO runtime checks) then passed. No
private controller replay or sleep diagnostic remains in the production source.
No clean-Windows component download/install test or physical-controller retest
was performed for this update, and issue #8 needs validation on the reporter's PC.

### v1.0.4 first-run save-dialog regression

Reproduced the reported endless loading screen with an empty, isolated user-data
folder: the guest was still running and playing music, but was waiting at
**Saved Game / No saved game data found**. The foreground loading overlay hid
that question and the synthetic Start mapping consumed the A/B choices.

The intro helper now recognizes a separate message-dialog movie advanced by the
retail dialog update call site `0x822EB0B4`, after the frontend movie exists. It
pauses the loading overlay and synthetic Start input, preserving the normal
choice to create a save or continue without saving. The skip button must be
released before input reaches that question. Releasing a dialog confirmation is
also required before it can select a main-menu item. Existing save files are not
modified by this transition logic.

Private diagnostic input replay and visual checks covered:

- Short skip press before guest input polling, with no pre-existing save: the
  save question becomes visible instead of an endless loading screen.
- Create save with A, acknowledge the next screen with A held: a save is written
  only under the isolated test profile, and the main menu appears without
  accidentally selecting Tournament.
- Restart using that new save: the main menu is reached without a manual Start.
- Continue without saving with B: the main menu appears and no save directory
  is created for that isolated profile.
- Let the entire intro play, then press Start: the first-run question is visible;
  continuing without saving reaches the main menu, and D-pad navigation selects
  Exhibition normally.

The replay used a private virtual skip event and simulated XInput-state buttons;
it was not a new physical-controller test. No replay controls, private paths or
diagnostic instrumentation are included in the public source payload. The normal
production source was restored in the test build after these checks.

Manual regression: launch a local build with a new, empty `--user_data_root`,
skip with a short gamepad press, release it, then test A/create and B/no-save in
separate profiles. Also repeat with an existing save and with the full intro.
Use isolated folders; do not delete real saves to create this test condition.

### v1.0.3 ISO startup regression

The earlier installed-SDK validation missed a local modification to the SDK's
`share/rexglue/rex_app.cpp`. The stock v0.10.0 file rejects a regular ISO file
because it checks `is_directory`. Compiling that unmodified application source
reproduced `--game_data_root does not exist` with an existing supported ISO.

The project now compiles its own BSD-licensed `sdk-overrides/rex_app.cpp` adapter,
independent of the installed SDK application source. It validates and mounts a
disc image read-only before XEX loading. CMake removes the SDK source explicitly
and stops if the expected source is missing. No installed SDK file is edited.

The fixed build mounted the same ISO and ran the intro for 23 seconds. Runtime
tests also passed in isolated temporary folders with the ISO outside the install
folder, once via an explicit argument and once via saved settings. Both launches
used a different working directory and initialized the offline menu and runtime.
The nine synthetic tests and the opt-in runtime test passed.

The new installer's embedded payload was extracted into the existing isolated
installation, then its normal PowerShell backend completed ISO validation,
generation, patching, all 94 C++ build steps, launcher compilation and staging
using cached Python, existing compiler tools and the supplied SDK. An actual
launcher invocation then loaded a saved path containing `=`, launched the rebuilt
game and mounted an uppercase `.ISO` from a different working directory. The ISO
test name was a temporary hard link; no disc contents were changed. The GUI's
Install button itself was not exercised for this repair test.

To repeat with a private local build on Windows (never upload these game files):

```powershell
$env:TABLETENNIS_TEST_DIST = 'E:\YourInstall\dist'
$env:TABLETENNIS_TEST_ISO = 'E:\YourDiscs\TableTennis.iso'
py -3 -m unittest discover -s tests -v
```

The SDK download and first-time Microsoft component installation remain subject
to the limitations below; this startup repair does not establish that those
paths work on a clean Windows installation.

### Earlier build validation

- Extracted the supported XEX and menu archive from the user's ISO locally.
- The patched menu archive SHA-256 matches the preceding verified game build:
  BF0A4D39A784D9924FB03FE7B7B21380F5AC4B337932F6946B52F9D23CABD6F9.
- Regenerated game code from the retail XEX; all 17 native rules applied and
  a repeat application changed zero files.
- Tests cover read-only ISO access, bounds/cycle rejection, unsupported input,
  native hash validation/idempotence and public package contents.
- The automatic SDK download was blocked by Windows security on this PC. It
  remains unverified end to end; the build test uses the installed SDK option.
- A full clean local build passed: retail ISO extraction, code generation,
  all native/menu patches, 95 C++ build steps, linking and C# launcher compilation.
- All 16,971 generated function bodies match the preceding working integration
  after applying the local rules. The new build reached the normal title screen;
  its runtime log confirms early gamepad initialization, the local menu mount and
  loading-font registration. No new physical-controller test was requested.
- All seven synthetic-input tests passed. The public Git index and the 28-file
  source package passed the explicit allowlist audit.

Public source packaging and a fresh local compilation are checked separately.
The ISO, generated source and resulting executable must not be added to Git or
the release ZIP. Old Git objects/release assets require separate cleanup; an
ignore rule or a new commit does not erase them.
