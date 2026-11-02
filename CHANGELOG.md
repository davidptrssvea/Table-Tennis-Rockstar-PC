# Changelog

## Unreleased - late-frame recovery

- Submit overdue frames immediately and rebase the next deadline. This avoids adding a full frame of waiting after long stalls or producing short catch-up intervals afterward.
- Add deterministic regressions for overdue frames and recovery. This improves stall recovery; it does not eliminate the original game/driver/OS stalls or unlock higher frame rates.

## v1.1.1 - 2026-11-02

- Capitalized the final main-menu item as QUIT TO DESKTOP; it remains below EXTRAS.

## v1.1.0 � 2026-11-02

- Added 2560x1080, 3440x1440 and 3840x1600 ultrawide presets to the launcher.
- Adapt the 3D camera to the actual window aspect ratio. Menus and score HUD retain normal proportions in a centered 16:9 safe area, with GPU scissor clipping to hide off-stage artwork.
- Preserve the original video aspect when placing movie quads on a wide display.
- Added Quit to desktop as the final main-menu item. It sends the normal window-close request without entering the unavailable Xbox Live connection flow.
- Keep 60 FPS pacing, intro skip, offline profile/menu fixes, ISO support and the credited desktop shortcut from earlier releases.
- Added compiled checks for square UI proportions, centered safe areas and scissor bounds. The launcher scrolls when needed on smaller desktops.

## v1.0.7 � 2026-11-02

- Integrated malinkicat's PR #6 frame-pacing work, preserving the later command-line framerate precedence fix.
- GPU-plugin VSync now receives its setting after late registration via REX_VSYNC; all requested frame-rate options are currently paced at 60 to preserve game speed.
- Moved the single limiter to the beginning of the guest D3D Swap, using a high-resolution waitable timer and absolute deadlines.
- Startup and achievement dialogs attach only while needed. F10 uses a separate native statistics window with a graph instead of keeping ImGui presentation active.
- Added optional --frame_log=true CSV logging of submission and DXGI Present-call intervals. Present calls are not physical scanout or a count of unique rendered images.
- In a local 1280x720 comparison, 99.9% of submission intervals were within 0.5 ms of the 16.67 ms target (56.4% with v1.0.6). Occasional longer presentation intervals remain; results on other hardware/VRR modes need confirmation.
- Hardened the measurement hook's interface query, added deterministic scheduling checks and corrected the installer log's version banner.

This preview improves measured 60 FPS pacing. It does not unlock 120/144/240 FPS motion or add ultrawide support.

## v1.0.6 — 2026-11-02

- Removed the custom sleep/spin limiter from the UI draw callback. It could delay new game output independently of the guest frame producer.
- Hidden FPS/startup overlay now detaches after startup, restoring the SDK's immediate guest-output presentation path. F10 reattaches it when requested.
- Replaced the after-swap relative sleep with one producer-side deadline limiter; small sleep overshoot no longer shifts every later deadline, and long stalls do not trigger catch-up bursts.
- F10 and logs now measure guest VdSwap submissions and mean/p95/max intervals. UI redraw FPS is labeled separately. These are submission timings, not GPU render timings or a guaranteed unique-frame count.
- Default to 60 FPS/VSync in the launcher. Existing higher caps are retained as explicitly experimental; they do not unlock the game's roughly 60 FPS motion. Removed unsupported performance recommendations and high-FPS claims.
- Explicit command-line framerate now takes precedence over saved settings.
- Added deterministic native checks for pacing deadlines, stalls, cap changes, stale statistics and interval measurements.

This addresses misleading metrics and avoidable presentation delays. It is **not** a verified fix for genuine 120/144 FPS gameplay or every user's stutter report; issue #8 remains open for validation.

## v1.0.5 — 2026-10-19

- Added a transparent Windows desktop icon at 16, 24, 32, 48, 64, 128 and 256 pixels.
- The installer-created desktop shortcut now uses the icon and opens the settings launcher. The launcher executable also embeds the icon.
- Credited icon artist Gary aka POOTERMAN :) with a link to his DeviantArt profile in README and the installed credits.
- Existing installations can run this installer in the same folder to refresh the launcher and desktop shortcut.

## v1.0.4 — 2026-10-18

- Fixed apparent endless `LOADING MAIN MENU...` on first launch: the loading overlay covered the game's missing-save question, while automatic Start input prevented answering it.
- Pause the overlay and automatic input when the retail startup message-dialog movie is active. Real controller input becomes available after releasing the skip button.
- Keep the original choice to create a saved game or continue without saving; do not silently choose for the player or replace existing saves.
- Reapply release-before-input handling when leaving a startup dialog for the main menu, so a held confirmation cannot select Tournament.
- Tested the first-run missing-save screen, new save creation and confirmation, continuing without saving, and automatic startup with the newly created save in isolated local profiles.

Repair: run the new installer with your ISO and the same installation folder. At the first-run **Saved Game** question, choose **A** to create a save or **B** to continue without saving; follow any further confirmation on screen.

## v1.0.3 — 2026-10-18

- Fixed startup incorrectly rejecting an existing ISO with `--game_data_root does not exist`. The stock SDK v0.10.0 accepts only a directory; previous tests used an installed SDK whose application source had already been modified for ISO mounting.
- Include the BSD-licensed ISO-capable application adapter in the reviewed source payload and compile it instead of the SDK's directory-only application source. The installed SDK stays unchanged.
- Mount the user's ISO read-only as `game:` and `d:` before loading the XEX, including disc images named with uppercase `.ISO`.
- Use an explicit `--game_data_root="..."` launch argument and preserve saved ISO paths containing an equals sign.
- Missing-disc errors now explain how to select the ISO again in the launcher.
- Added an opt-in runtime regression test that boots a private build with an ISO outside the install folder, using both command-line selection and saved settings from a different working directory.

Repair: download the v1.0.3 installer, select your ISO and the **same installation folder**, then click Install / Recheck. No deletion or fresh Windows component installation is required when the components are already present.

## v1.0.2 — 2026-10-18

- Added a standalone graphical installer: choose ISO, allow components, Install, then Play. The public installer contains only project-authored installer code and the reviewed source payload.
- Added automatic, checksum-verified portable Python setup. Players do not need to install Python themselves.
- Added automatic setup of missing Microsoft C++/Windows SDK/CMake/Ninja/Clang components through Microsoft's signed installer, after the user's consent and any Windows permission prompt.
- Validate the retail ISO before attempting large compiler installations.
- Added installation status, expandable log, restart guidance, repair/retry and a desktop shortcut.
- Added Visual Studio's bundled Clang discovery and ISO path updates that retain existing graphics settings.
- Added embedded-payload validation so game files or altered source cannot accidentally be bundled into the installer.
- Replaced the player-facing manual build instructions with the simple installer download. Manual setup remains available for developers.

Validation: portable Python downloaded and checksum-verified; full local backend build with existing compilers and supplied SDK passed. GUI rendered and missing-ISO validation checked. First-time Microsoft component installation and the previously blocked ReXGlue download path are not verified on a clean Windows PC.

## v1.0.1 — 2026-10-18

- Changed distribution to local build tools: no retail menu archive, game executable, XEX or generated game functions in the public source package.
- Added an ISO picker and automated extraction, version validation, code generation, patching, CMake build, launcher compilation and runtime staging.
- Added pinned SDK download verification and an offline installed-SDK option. Prerequisite compilers and Python must be installed separately.
- Recreated the verified menu and intro fixes as locally applied patch instructions with input/output hash checks.
- Removed Xbox Live from the local game's main menu and corrected Options/Extras indexes.
- Added immediate intro interruption by gamepad, a loading transition and a large antialiased loading message.
- Removed machine-specific fallback ISO paths from the published integration and launcher source.
- Added malformed-image checks, source-package auditing and tests that use synthetic data rather than game files.

Local-build output remains private and excluded from Git. This change does not itself remove artifacts from old Git history or third-party copies. Local generation is a distribution change, not a legal guarantee.

## Earlier distribution

The earlier v1.0.0 distribution included generated game code/binaries and a modified retail menu archive. Descriptions claiming it contained no game resources were inaccurate. The new workflow does not require downloading that package.
