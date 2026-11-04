# Local build and source distribution

## Community integration - 2026-11-03 (unreleased)

Branch `feature/community-windows-port` adapts upstream MIT game-side code from
ThisIsAkill/Table-Tennis-Linux commit 7bff91c561fa0e424562c6d851b9008e9fc9511e.
SDK patches are combined against ReXGlue v0.10.0 commit
f5337cdc947ff6d4c4196737e2c807a48f2a1fc2. Linux-only platform changes are excluded.

The optional SDK was compiled and installed on Windows with Clang 23, Ninja and
MSVC 14.44. The combined patch was also checked/applied against a pristine SDK
worktree. The helper completed successfully using that existing source/build
directory; its fresh-clone/full-build path has not been exercised as one command.
It disables package-registry export so the test SDK does not change discovery
for other projects. SDK headers and binaries must
remain paired because the texture-load hook extends a virtual interface.

Manual validation used isolated local profiles and a private virtual-pad test
harness; that harness is excluded from public source and production builds:

- Two Windows instances entered the same private relay room, displayed each
  other's names, created/joined a lobby, loaded the court and reached gameplay.
  Score progression was visible. This is a same-PC functional test, not an
  Internet latency/NAT test or a complete match-completion test.
- PlayStation A/B/X/Y and L1/R1 texture substitutions were visible in the lobby;
  the initial saved-game screen retained a separate Xbox B variant. Forced PS
  mode was tested; physical DualSense automatic detection was not available.
- The styled 29-entry achievement list, keyboard End navigation, pad navigation,
  View hold/B close, and an authored display-only test popup were checked.
  Display-only toast tests do not award achievements or alter progress.
- The achievement overlay detaches after its queue drains. Idle game and present
  logs returned to 60 FPS; the native F10 statistics window remains separate.
- The corrected optional ONLINE label is visible and QUIT TO DESKTOP stays last.

Four opt-in C++/Python relay checks cover direct and forced-relay quality pings,
private-room isolation, names and rejoining after server restart. Run them with
`TABLETENNIS_TEST_COMMUNITY_SDK` pointing to the complete patched SDK. They do not
establish spectator gameplay or interrupted-match recovery. Those upstream paths
are ported but remain experimental. No public server was deployed or contacted.

The stock-SDK setup remains offline and keeps its six-entry menu. Optional SDK
setup generates the eight-entry menu locally from the user's verified ISO.
Neither path distributes original game files or generated game functions.

Final verification: all 16 automated tests passed with native pacing/layout,
ISO boot and the patched-SDK transport tests enabled. Stock-SDK compilation and
ISO boot also passed. A forced codegen repair recreated the native-edit inputs;
all 23 rules applied, and a second application changed zero files.

The launcher and pure production-input game build are staged separately under
`scratch/community-feature-test/dist`; the installed root `dist` remains v1.1.2.
After the user handled the Windows Firewall permission dialog, the remaining
interactive checks passed: EXTRAS > ACHIEVEMENTS opened the styled list, both
ONLINE and SYSTEM LINK stayed in the main menu with networking disabled, and
OPTIONS remained usable afterward. QUIT TO DESKTOP was selectable as the final
row and closed the process through the normal window-close path. Firewall
permission was handled manually by the user; the agent did not alter security
settings. No new release was published.

Launcher follow-up: compiled and visually checked the inline local-network guide,
server-start button, detected address and copy button. Clicking server start in
an isolated preview launched the bundled Python relay, enabled networking and
filled 192.168.50.245:27360. Its loopback HTTP status endpoint answered correctly.
The preview server was stopped afterward. ZIP SHA-256 remained
ddcb0b07432b93ab13c1354cf23795667e8fa9a24b1d12c18c85dd91cd621f56.
Official release publication remains pending multiplayer validation as requested.

Saved-network follow-up: the user's game log reported networking disabled even
though settings.ini contained system_link=true. The direct EXE path previously
loaded ISO/pacing settings but omitted the saved networking fields. It now reads
system_link, online_server, player_name (mapped to user_name), room_code and
button_prompts, without overriding command-line settings. Effective network
configuration is logged at startup; room codes are not written to that log.

The isolated ISO boot regression now checks saved networking enabled, an explicit
false override of saved true, and an explicit true override of saved false,
including server/name precedence. All three boot cases passed; the base tests
also passed. The stock-SDK application source compiled successfully. The local
community test EXE was updated; the private ZIP and root installed game were not.

CREATE MATCH crash follow-up: the real two-PC test connected Pappa and Sebbe, but
the host aborted at unregistered guest address 0x822F99C0 immediately after
XGISessionCreate/XNetQosListen. The cached validation project's overrides.toml had
not been synchronized before a forced regeneration; all 22 added network thunks
were omitted from that local EXE. The public source overrides already had them.

Synchronized the full reviewed source set, regenerated and rebuilt. patch_native
now verifies every required override's function-table registration before making
any edits. It rejects the old generated table and reports all 22 missing entries;
the corrected table passes all 57 required registrations. A regression covers the
exact missing callback. All 17 automated tests pass with runtime/native/transport
checks enabled. An isolated UI replay selected ONLINE > PLAYER MATCH > CREATE
MATCH, confirmed the settings and reached GAME LOBBY / Waiting for Player. The
log confirms session creation succeeded and no unregistered-function failure.

The test replay uses a private virtual-pad build only. The distributed local EXE
is the separately captured pure production-input build (no controller-file hook),
SHA-256 8d3b2603bd4910a2b8e068e33b99329afb811a81cb1ba0cb9374adffc7be1f97.
Updated scratch/community-feature-test/dist/tabletennis.exe only. The existing
private ZIP remains unchanged and requires this replacement EXE on both PCs.
The user's running relay was left running; only the isolated test relay stopped.

Client-start report follow-up: host log tabletennis_007.log showed Sebbe joining
and then disappearing, without a host-side fatal error. Client crash log/build
confirmation is still required to diagnose that physical-PC failure.
Repeated the complete flow with two isolated instances of the corrected build:
HostLatest created the room, ClientLatest joined, host started the match, and both
entered pongGameState at 21:43:58 on 2026-11-03. The client displayed the court,
both names and score HUD; score progression was visible on the host. Neither
process crashed. This is a same-PC loopback test, not proof that the separate-PC
client failure is resolved. No new code change was made for that unconfirmed
failure. The isolated processes and test relay were stopped afterward.

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
6. Apply 18 verified native edit rules (17 functions plus the SDK-derived PCH).
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


### v1.0.7 PR #6 integration

Integrated malinkicat's 60 FPS pacing PR with current main. Command-line framerate
precedence is retained. The old unused limiter/statistics header is replaced by
the PR limiter and a pure, deterministically tested scheduling helper. The DXGI
Present1 measurement hook queries IDXGISwapChain1 before accessing its vtable.
The interface-sharing hook is best-effort telemetry, not physical scanout data.

On an i5-13400F / RTX 4070 Ti, two 45-second 1280x720 Exhibition windows gave
56.44% (v1.0.6) and 99.85% (PR) of submission intervals within +/-0.5 ms of
16.667 ms. Submission p99 improved from 18.030 to 16.844 ms. These measurements
use different submission hook positions and are not deterministic frame replays.
With F10 visible, about 28 seconds gave 54.00% and 100.00%, respectively.
The baseline sent about 300 Present calls/s including repeated images, versus
about 60 with the PR. Present calls are not unique game images. Three PR Present
intervals exceeded 25 ms in the off segment (max 29.056 ms), so stutter is not
claimed eliminated. Fullscreen/VRR/other hardware need external validation.

The integrated installer and source package are preview artifacts. All requested
frame-rate choices currently remain paced at 60; no high-FPS or ultrawide claim
is made. Original-derived files stay local and outside the source allowlist.

Integration validation: the reviewed 40-file source ZIP was extracted into the
isolated installation and its setup backend rebuilt the game with the verified
retail ISO and existing SDK/compiler. All 18 native edits verified. All 11 checks
passed, including compiled schedule checks, direct/saved-ISO startup, command-line
framerate precedence and effective late-loaded VSync/tearing values. The installer
was compiled at version 1.0.7.0; its embedded ZIP hash matched the reviewed source.
This does not validate automatic prerequisite installation on a clean machine.


### v1.1.0 ultrawide and Quit to desktop

Five additional hash-verified local edits extend the existing 18: the camera
aspect accessor, root Flash rendering and its caller, D3D scissor emission and
video-quad aspect fitting. The authored host code corrects the camera projection,
shrinks/centers only Flash UI horizontally and clips off-stage UI art. It restores
the previous scissor state after each root draw. Square/shadow cameras retain
their original aspect. Original-derived files remain local and excluded.

The frontend array is rebuilt in place from verified retail bytes: Tournament,
Exhibition, Training, Options, Extras, Quit to desktop. Its localization refresh
retains the offline Options/Extras indices. Quit targets the former unavailable
online transition; the host observes the pending destination on the rendered
frontend and posts the normal WM_CLOSE rather than waiting for a connection.
A scripted A press selected Quit and the runtime reported execution complete
before exiting. The desktop launcher and Escape remain available.

The original 1280x720 render target/scaled output is retained, not a claim of a
new native ultrawide framebuffer. 3D projection and 2D physical proportions are
corrected separately. Local 1280x540 tests (same aspect as 2560x1080) verified
main menus, player selection, help dialogs and the score HUD during Exhibition.
Automated geometry checks cover the three advertised ultrawide ratios and 16:9.

Final validation also exercised a 1720x720 window (same ratio as 3440x1440).
At Windows 150% scaling the SDK reported a 2580x1080 physical render surface;
the camera ratio was 2.388889 and the captured menu had normal proportions.
Aspect updates use the SDK physical resize callback, including the initial
surface size, rather than polling Win32 client coordinates. Native 3440/3840
monitor output was not tested on this 2560x1440 display.

The release candidate was rebuilt without the private controller replay harness.
All 12 checks passed with native compilation and retail-ISO runtime tests enabled.
The public package contains exactly 44 allowlisted project-authored source and
license files; the installer embeds that verified source ZIP. A clean-machine
prerequisite installation remains untested. Gameplay remains paced at 60 FPS.

### v1.1.1 menu label

The final main-menu label is QUIT TO DESKTOP, below EXTRAS. The new
verified movie hash was derived from the previously verified v1.1.0 movie by
changing only the authored label bytes, then reproduced from the original retail
archive through the public patcher. Reapplying the patch is idempotent. The
uppercase label and last position were checked in the running game. Nine routine
checks passed; the three unchanged native/runtime checks were not repeated.

### Unreleased: late-frame recovery (2026-11-02)

Schedule::Next now returns the current time when an existing deadline has been
missed, and uses that time as the next deadline's origin. Small wake overshoots
retain the absolute cadence; work that exhausts the frame budget does not cause
short catch-up frames. Long stalls no longer add a full extra period of waiting.
Native regression tests cover both cases. All 12 checks passed with native and
retail-ISO startup tests enabled; all 23 generated edits verified unchanged.
The rebuilt local game contains the original production intro/input code.

An asynchronous telemetry experiment was tested and then excluded from the
production change because the measurements did not demonstrate a consistent
pacing benefit. That experiment and raw captures remain private. Older scripted
measurements opened a replay-control file on the guest thread, which can itself
stall; a repeat comparison read that file on a separate private control thread
and only loaded an atomic button mask on the guest thread. Neither replay path
is part of the distributed source or production executable.

In roughly 48-second, windowed 1280x720 Exhibition samples (Liu Ping vs Haley,
National TT Auditorium, 60 FPS, optional CSV enabled, F10 hidden), the clean
v1.1.1 baseline recorded game p99/max 16.667/16.739 ms and Present p99/max
16.873/18.258 ms. The deadline+telemetry experiment recorded game p99/max
16.669/22.323 ms and Present p99/max 17.168/29.464 ms. Thus these samples do NOT
establish reduced overall stutter, and not all prior spikes can be attributed
to the application. They are submission/Present timings, not physical scanout.

Recovery behavior was confirmed: an old 21.388 ms game interval was followed
by an 11.946 ms catch-up interval; the experiment's 22.323 ms interval was
followed by 16.666 ms. Deterministic tests verify that this comes from the
retained deadline correction. The original causes of late work, shader/loading,
OS scheduling and GPU-driver stalls are not claimed eliminated. No new public
release is part of this development change.

### v1.1.2 release packaging

The late-frame recovery change above is packaged as v1.1.2 at the user's request.
The production game code is unchanged from the candidate that passed all 12
checks. The telemetry experiment remains excluded. The installer version and
public download links are updated, with the same 44-file public source allowlist.


## Windows PC SETTINGS - 2026-11-04 (unreleased)

Added OPTIONS > PC SETTINGS in the front-end and in-match pause OPTIONS, using
ThisIsAkill's MIT AVM1 assembler edits and guest flag polling. The Windows page
uses the shared achievement fonts and the game's blue/yellow/dark style, with
D-pad/stick/A/B, keyboard arrows/Enter/Escape and mouse row selection.

Supported settings: resolution (including the launcher's three ultrawide sizes),
fullscreen, anti-aliasing, output filter, separate FPS/frame-time window, button
prompts and achievement sound. Resolution/AA/filter explicitly require restart
through the launcher. Fullscreen, timing window, prompts and sound apply live.
The 60 FPS scheduler is retained; VSync/brightness/haptics/updates are not exposed
as unsupported choices. The ImGui page exists only while open, and closing it
returns presentation to game-only work. Guest input is swallowed while open and
held buttons are drained until release on exit.

Saving merges known page fields into settings.ini, retaining comments, ISO paths,
network settings and unknown keys. A temporary file is checked/closed and then
replaced atomically with MoveFileExW; a failure keeps the page open with an error.
The launcher also preserves in-game/unknown keys when saving its own controls.

Validation:
- Both offline/online front-end movie hashes and the pause movie hash are checked;
  both complete archive variants were generated from verified local disc data
  and passed a second idempotent patch pass. Unsupported input fails closed.
- The 27-test suite passed 26 tests with the runtime test skipped in that run;
  the runtime test then passed separately with all three production ISO boot
  subcases enabled. The expanded six launcher/protocol tests passed after adding
  the launcher's preservation regression. Native layout checks also exercise
  settings merge/idempotence, duplicate keys and preservation of comments/paths.
- All 57 generated override registrations and all 23 native-edit hashes pass.
- A private virtual-pad game entered real front-end OPTIONS > PC SETTINGS,
  changed resolution to 1440p, enabled FPS, changed prompts to PlayStation and
  disabled achievement sound. Saved values were verified; ISO/server/player/room
  and an unknown sentinel key were retained. Logs confirm page open/save/close
  and subsequent 60 FPS game-only presentation. The private profile is isolated.
- The game subsequently loaded an Exhibition match and entered pongPauseState
  without a fatal. Opening the new pause OPTIONS row has not yet been verified.
- Visual layout review is still pending: Windows Security's firewall permission
  dialog obstructs the test window. No permission was answered automatically and
  no security settings were changed. The user was asked to dismiss it.

The production EXE is separately rebuilt with the original intro source and
checked to exclude TT_TEST_CONTROLLER_FILE. Its matched online build ID is
2B57B523CC4A. Only the experimental local installation and the uncompressed
private update directory were refreshed (EXE, launcher, frontend/pause archive,
credit notice). Existing settings were preserved and prior files backed up.
The normal desktop shortcut's stable dist, the private ZIP and public release
remain unchanged. No game/archive/generated function is added to public source.
