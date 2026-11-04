# Rockstar Games presents Table Tennis — Windows PC Recompilation

Bring the Xbox 360 classic to your PC with **up to 4K output, higher internal 3D resolution, and optional image filters**. The game code is recompiled to native Windows x64 with ReXGlue SDK v0.10.0 and DirectX 12 rendering. **Gameplay is currently paced at 60 FPS in every mode. Actual 120/144/240 FPS gameplay is not unlocked.**

Build and play from **your own Xbox 360 disc image**. A graphical installer prepares the required components and creates the local game build for you.

## Get started

1. Download **[Install-Table-Tennis.exe — installer](https://github.com/davidptrssvea/Table-Tennis-Rockstar-PC/releases/download/v1.1.2/Install-Table-Tennis.exe)**.
2. Open it, choose your retail **Table Tennis ISO**, allow the required components, and click **Install**.
3. When it says **Ready to play**, click **Play**. A desktop shortcut is also created when Windows permits it.

On the first launch, **Saved Game** may ask whether to create a save. Choose **A** to create one or **B** to continue without saving, then follow any further on-screen confirmation. This question is now visible and interactive during intro skipping.

**You do not need to install Python, compilers or SDKs manually. You do not need a command prompt or the source ZIP.**

First setup needs internet access, several GB of free space and time to download components and build the game. If Microsoft build components are missing, Windows may ask for administrator permission. Nothing restarts Windows automatically; if a restart is required, the installer tells you to reopen it afterward.

## What you need

- Windows 10/11 **x64**, an AVX2 CPU and a DirectX 12 GPU.
- Your own unmodified **USA/Europe retail Xbox 360 Table Tennis ISO**, Title ID `545407DF`.
- A writable installation folder. The default is `%LOCALAPPDATA%\TableTennisPC`.

The ISO is checked before installing large compiler components and is always opened read-only. Other game versions are rejected. Keep your ISO available after installation: the game still reads it during play.

## PC enhancements

### Community features (development branch)

The Windows port adapts the achievement UI, PlayStation artwork and networking
from [ThisIsAkill/Table-Tennis-Linux](https://github.com/ThisIsAkill/Table-Tennis-Linux),
commit `7bff91c561fa0e424562c6d851b9008e9fc9511e`. Thanks to **ThisIsAkill**!
The [MIT notice](licenses/Table-Tennis-Linux/NOTICE.txt) is retained; SDK changes
retain ReXGlue's BSD license. These features are not in the v1.1.2 download above.

- **PC SETTINGS:** open **OPTIONS > PC SETTINGS** from the main menu or pause
  menu. Change fullscreen, FPS/frame-time window, button icons and achievement
  sound immediately. Resolution (including ultrawide), anti-aliasing and output
  filtering are marked **AFTER RESTART**. Use D-pad/stick to navigate/change,
  A to change and B to save/back; keyboard arrows, Enter and Escape also work.
  Settings share the launcher's `settings.ini`; ISO/network/unknown keys survive.
- **Achievements:** styled unlock notifications and a list of all 29 achievements,
  opened from **EXTRAS > ACHIEVEMENTS**, **F7**, or by holding **View/Back** for
  0.8 seconds. Scroll with the D-pad/stick, mouse wheel or arrow keys; shoulders
  and Page Up/Down change pages. B or F7 closes the list. Progress remains local.
- **Button prompts:** Auto, Xbox and PlayStation choices in the launcher. Auto
  detects SDL PlayStation controllers. Replacing the game's texture icons needs
  the matched community SDK below; stock SDK builds apply this choice to the
  achievement overlay only. Some separate/vector icons retain their Xbox art.
- **Experimental ONLINE / SYSTEM LINK:** optional matched-SDK builds expose these
  menu entries and launcher fields for server address, player name and private
  room code. Networking is disabled by default; no public server is supplied or
  automatically contacted. **QUIT TO DESKTOP** remains the final menu item.

The launcher explains which player hosts the server and what both players enter.
It displays this PC's local server address and provides a copy button. Where the
portable server/Python tools are installed, **Start server on this PC** opens the
server console and fills the local server address automatically. Keep that console
open while playing. The next official community release must include these tools;
that release remains pending multiplayer testing. The existing private test ZIP
has not been rebuilt with this launcher change.

The launcher now shows an **Online build ID** derived from the game and engine
files; compare it on both PCs before reporting a multiplayer failure. **Open game
logs** opens the folder needed for the client crash report. See the
[Windows online review](reports/ONLINE_REVIEW.md) for hardening, test scope and
SmartScreen/signing status. The two-PC test is now user-confirmed working;
Internet play still needs broader testing. Launching with your own IP/localhost
checks the local relay and offers to start it if needed, using the selected port.
A public community release has not been published.

Two Windows clients have joined a private room, created/joined a lobby and started
a match on one PC. Automated checks cover direct/relayed peer traffic, room
isolation and rejoining after a server restart. Internet/NAT combinations,
spectating and recovery of a running match after a connection drop are not yet
validated. Their upstream engine paths are ported, but remain experimental.
Xbox LIVE accounts, ranked services and leaderboards are unavailable.

The existing intro skip, ultrawide proportions, Windows 60 FPS frame scheduler and
native F10 window are retained. Achievement overlays detach when idle. Linux-only
write-watch and FidelityFX build changes are excluded.

Developers with the prerequisites installed can build the separate SDK, then the
local game from an unpatched source checkout:

```powershell
py -3 project/tools/build_community_sdk.py --jobs 4
py -3 project/tools/setup_build.py --iso 'E:\Your disc.iso' --sdk '.tools/community-sdk'
```

This compiles pinned ReXGlue v0.10.0 source plus the included Windows patch into a
separate SDK folder. Keep its headers, libraries and DLLs together; the texture
hook changes an engine interface. The ordinary installer uses the stock SDK.
See [relay setup](server/README.md) and [validation details](reports/LOCAL_BUILD.md).

### Sharper 3D rendering — up to 4K

Choose your display preset directly in the game launcher:

| Preset | Output resolution | Internal 3D rendering scale |
|---|---|---|
| **720p** | 1280 × 720 | 1× |
| **1080p / Full HD** | 1920 × 1080 | 1×, presented with your chosen image filter |
| **1440p / QHD** | 2560 × 1440 | 2× on each axis |
| **4K / Ultra HD** | 3840 × 2160 | 3× on each axis |

The 1440p and 4K presets raise the internal rendering scale as well as the output size. The 1080p preset keeps 1× internal rendering and uses the selected presentation filter for output scaling.

### Ultrawide and the PC menu

The 21:9 presets widen the 3D camera view instead of stretching players. Menus,
loading screens and the score HUD retain their proportions in a centered 16:9
safe area. UI drawing is clipped to that area so hidden off-stage artwork does
not appear in the extra space. The camera adapts to the actual game window,
including fullscreen. Internal 3D rendering still uses the original render target
with the selected scale; the listed sizes are display output dimensions.

**Quit to desktop** is the last main-menu item. Select it and press A to close the
game normally. Xbox Live remains unavailable.

### High-refresh display options

Use **60 FPS** for normal play. Retained 120/144/240/no-cap selections are currently paced at 60 as well, preserving normal game speed until game-timer changes are independently verified. Guest VSync is disabled through the late-loaded GPU plugin, and the single host limiter controls submission timing. Tearing is allowed by default; this can reduce blocking in GPU emulation but visible tearing depends on the display configuration.

F10 opens a separate native statistics window with submission rate, average/p99/worst intervals and a frame-time graph. DXGI Present-call intervals are shown separately. These measure host calls, not physical scanout, GPU render duration or a guarantee of unique completed images. Earlier high-FPS claims based on ImGui redraw counts were unsupported. On a fixed-refresh display, a 60 Hz mode or integer multiple such as 120 Hz matches 60 FPS cadence more evenly than 144 Hz.

### Image filters and anti-aliasing

- **FXAA** and **FXAA Extreme** options for edge smoothing, plus an Off setting.
- **AMD FidelityFX FSR**, **Contrast Adaptive Sharpening (CAS)** or **Bilinear** presentation options.
- **Fullscreen or windowed mode**, configured before launching the game.

### A more convenient PC experience

- **Graphical settings launcher:** choose your ISO, resolution, framerate, display mode and image filters in one place; settings are saved for the next launch.
- **Separate FPS/timing window:** toggle **F10** for submission rate, average/p99/worst intervals and a frame-time graph. DXGI Present-call timings are shown separately; they do not measure physical scanout. Use **--frame_log=true** to write `dist/frametimes.csv`.
- **Quick exit:** press **Escape** to return to Windows.
- **SDL3 gamepad support**, with controller detection active before the startup intro.

### Offline menu and startup improvements

- Xbox Live removed from the main menu to avoid the unavailable online screen. Tournament, Exhibition, Training Mode, Options and Extras remain available, followed by Quit to desktop.
- A gamepad button immediately stops the startup intro and continues to the main menu after loading, without another Start press.
- A large, centered, antialiased **LOADING MAIN MENU...** transition while the menu initializes. The skip press cannot accidentally select a menu item.

## If installation stops

**Music plays but `LOADING MAIN MENU...` never disappears?** In v1.0.3 and earlier, the loading overlay could hide the first-run save question and block the controls needed to answer it. Download the **v1.1.2 installer**, choose the same ISO and installation folder, and rebuild. Your existing saves are retained. Afterward, answer the visible **Saved Game** question if it appears.

**Already installed, but starting the game says `--game_data_root does not exist` even though your ISO exists?** Versions 1.0.1/1.0.2 could build with the SDK's directory-only startup code. Download the **v1.1.2 installer** above, choose your ISO and the **same installation folder**, then run Install / Recheck to rebuild with ISO support. You do not need to delete your installation. Start through **tabletennis_launcher.exe** afterward; the ISO must remain available.

Click **Open log** in the installer. You can retry after fixing the reported issue; downloaded components are cached. If Windows security blocks a required component, installation stops and security settings remain unchanged.

The installer is not code-signed. Automatic installation of missing Microsoft components has not yet been tested on a completely clean Windows machine. The installer backend was tested with its downloaded portable Python runtime and existing compiler/SDK installations. The separate ReXGlue download was previously blocked by security software on the development PC; that first-download path remains unverified.

Existing local saves and graphics settings are retained during repair; the selected ISO path is updated. First-run save creation and startup with that save were checked in isolated profiles; gameplay progress save/load was not fully revalidated.

## How it works

The small installer contains project source and patch instructions. It downloads a checksum-verified, isolated Python runtime and checks for suitable build tools. If needed, it starts Microsoft's signed Visual Studio 2022 Build Tools installer with the required C++/Windows SDK/CMake/Ninja/Clang components selected. The screen asks permission for component downloads and installation before starting.

It then obtains ReXGlue SDK v0.10.0, checks its pinned SHA-256, reads the supported executable and menu archive from your ISO, generates and patches the game code, and builds everything **on your PC**. No retail menu archive, XEX, ISO, generated game functions or prebuilt game executable is included in the public downloads. `Install-Table-Tennis.exe` is our own installer, not the game executable.

Locally generated files stay under the installation folder. Do not upload `dist`, `private_work` or `project/generated`.

<details>
<summary>Developer / advanced installation</summary>

The **Table-Tennis-Local-Build-v1.1.2.zip** asset is the source package for developers. It is not required by players using the installer.

For manual builds, install 64-bit Python 3.10+, Visual Studio 2022 **17.14 / MSVC v143 14.44 or newer** C++ Build Tools with Windows SDK/CMake/Ninja, and Clang 19+. Clang can be standalone or included in Visual Studio. Then use:

```powershell
.\setup.ps1 -IsoPath 'E:\Games\TableTennis.iso' -SdkPath 'E:\Tools\rexglue-sdk-0.10.0' -NoDownload
```

`SdkPath` must contain `bin\rexglue.exe` and `lib\cmake\rexglue`. Add `-Jobs 2` for lower memory use or `-PrepareOnly` to stop after code generation and patches. The installer can also use a supplied SDK via its optional `--sdk PATH` argument.

Checks and source packaging:

```powershell
py -3 -m unittest discover -s tests -v
py -3 project/tools/package_source.py --audit-index
py -3 project/tools/package_source.py --output 'E:\ReleaseOutput'
powershell -File project/tools/build_installer.ps1 -SourceZip 'E:\ReleaseOutput\Table-Tennis-Local-Build-v1.1.2.zip' -OutputPath 'E:\ReleaseOutput\Install-Table-Tennis.exe'
```

The installer builder validates every embedded source file against the public allowlist before compilation. It rejects extra or altered payload files. Native patch rules store offsets, hashes and replacement text rather than removed original function bodies. See [implementation and validation](reports/LOCAL_BUILD.md) and [changelog](CHANGELOG.md).

</details>

## License and rights

### Desktop icon credit

Desktop icon artwork by **Gary aka POOTERMAN :)** — [visit POOTERMAN on DeviantArt](https://www.deviantart.com/pooterman). Thank you for the artwork! The supplied image was converted into a Windows icon in multiple sizes for the launcher and desktop shortcut. The artwork is credited separately and is not covered by this project's MIT license.

Project-authored integration, installer and tools use the [MIT license](LICENSE). Adapted ReXGlue SDK headers, the ISO application adapter and SDK-derived CMake integration retain their BSD 3-Clause notices; see [ReXGlue license](licenses/ReXGlue/LICENSE.txt). [SDL's notice](licenses/SDL3/LICENSE.txt) is retained. Downloaded components have their own licenses: [Microsoft](https://visualstudio.microsoft.com/license-terms/) and [Python](https://docs.python.org/3/license.html).

Rockstar Games and Take-Two retain rights to their original game and material. This project is independent and unaffiliated. No project license grants rights to the original game. Local generation reduces what this project distributes; it is **not a guarantee of legal compliance**. You must have the necessary rights to use and process your copy.
