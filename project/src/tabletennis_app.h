// tabletennis_app.h - ReXGlue application configuration for Rockstar Games presents Table Tennis

#pragma once

#include <rex/cvar.h>
#include <rex/filesystem.h>
#include "offline_menu_device.h"
#include "intro_skip.h"
#include "pc_features.h"
#include "pc_settings.h"
#include "update_ui.h"
#include "achievement_ui.h"
#include "button_prompts.h"
#include "system_link.h"
#include "frame_pacer.h"
#include "stats_window.h"
#include "photo_mode.h"
#include "game_timer.h"
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <rex/logging.h>
#include <rex/memory.h>
#include <rex/rex_app.h>
#include <rex/ui/imgui_dialog.h>
#include <rex/ui/imgui_drawer.h>
#include <rex/ui/keybinds.h>
#include <rex/ui/overlay/achievement_toast.h>
#include <rex/ui/windowed_app_context.h>
#include <rex/ui/window.h>
#include <imgui.h>

#include <cctype>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <memory>
#include <string>

REXCVAR_DECLARE(bool, show_fps);
REXCVAR_DECLARE(int32_t, framerate);
REXCVAR_DECLARE(bool, allow_tearing);
REXCVAR_DECLARE(bool, frame_log);
REXCVAR_DECLARE(bool, variable_timestep);
REXCVAR_DECLARE(bool, high_fps_match_only);

// Startup-only ImGui work: gamepad polling during the intro and the loading
// transition. While any ImGui dialog is attached the presenter repaints on
// every display refresh, which puts 60 FPS frames on an uneven 2-3-2-3
// refresh pattern and competes with GPU emulation, so this detaches as soon
// as the startup sequence no longer needs it. The FPS overlay is a separate
// window (stats_window) for the same reason.
class StartupDialog : public rex::ui::ImGuiDialog {
 public:
  explicit StartupDialog(rex::ui::ImGuiDrawer* drawer, ImFont* loading_font)
      : rex::ui::ImGuiDialog(drawer), loading_font_(loading_font) {
    tabletennis::pacing::SetUiActive(true);  // The base constructor attached it.
  }

  bool attached() const { return attached_; }

 protected:
  void OnDraw(ImGuiIO& io) override {
    tabletennis::intro::PollGamepads();
    tabletennis::prompts::Update();
    if (tabletennis::intro::ShowTransition()) {
      const auto* viewport = ImGui::GetMainViewport();
      auto* draw = ImGui::GetForegroundDrawList();
      draw->AddRectFilled(viewport->Pos,
          ImVec2(viewport->Pos.x + viewport->Size.x, viewport->Pos.y + viewport->Size.y),
          IM_COL32(0, 0, 0, 255));
      const char* message = "LOADING MAIN MENU...";
      auto* font = loading_font_ ? loading_font_ : ImGui::GetFont();
      // Use the baked loading face size, independent of the smaller overlay font.
      const float font_size = loading_font_ ? loading_font_->LegacySize
                                            : ImGui::GetFontSize() * 3.0f;
      const auto size = font->CalcTextSizeA(font_size, FLT_MAX, 0.0f, message);
      draw->AddText(font, font_size,
                    ImVec2(viewport->Pos.x + (viewport->Size.x - size.x) * 0.5f,
                          viewport->Pos.y + (viewport->Size.y - size.y) * 0.5f),
                    IM_COL32(255, 255, 255, 255), message);
    }
    if (tabletennis::intro::UiWorkFinished()) {
      imgui_drawer()->RemoveDialog(this);
      attached_ = false;
      tabletennis::pacing::SetUiActive(false);
      REXLOG_INFO("TabletennisApp: startup dialog detached; presenting game frames only");
    }
  }

 private:
  ImFont* loading_font_ = nullptr;
  bool attached_ = true;  // ImGuiDialog's constructor attaches it
};

// Flags owned by the GPU plugin (e.g. "vsync") are registered only when the
// plugin DLL loads, after OnPreSetup, and SetFlagByName silently rejects
// unknown names. The SDK applies REX_<NAME> environment variables when a late
// flag registers, so fall back to that.
inline void SetPluginFlag(const char* name, const char* value) {
  if (rex::cvar::SetFlagByName(name, value)) {
    return;
  }
  std::string env = "REX_";
  for (const char* c = name; *c; ++c) env += char(std::toupper(static_cast<unsigned char>(*c)));
  SetEnvironmentVariableA(env.c_str(), value);
}

// The stock toast dialog stays attached to the ImGui drawer for the whole
// session, which makes the presenter repaint on every display refresh (see
// StartupDialog). Attach it only while a toast can be on screen.
class OnDemandToastDialog : public tabletennis::achievement_ui::AchievementToast {
 public:
  OnDemandToastDialog(rex::ui::WindowedAppContext& context,rex::ui::ImGuiDrawer* drawer,
      rex::ui::ImmediateDrawer* immediate,rex::Runtime* runtime,
      rex::system::AchievementManager* manager,rex::ui::Window* window)
      : AchievementToast(drawer,immediate,runtime,manager,window),context_(context) {
    if (HasPendingWork()) { attached_=true; tabletennis::pacing::SetUiActive(true); }
    else imgui_drawer()->RemoveDialog(this);
  }
  void Push(const rex::system::AchievementEvent& event) override {
    AchievementToast::Push(event);
    context_.CallInUIThreadDeferred([this] {
      if (!attached_) {
        imgui_drawer()->AddDialog(this);attached_=true;
        tabletennis::pacing::SetUiActive(true);
      }
    });
  }
 protected:
  void OnDraw(ImGuiIO& io) override {
    AchievementToast::OnDraw(io);
    if (attached_ && !HasPendingWork()) {
      imgui_drawer()->RemoveDialog(this);attached_=false;
      tabletennis::pacing::SetUiActive(false);
    }
  }
 private:
  rex::ui::WindowedAppContext& context_;
  bool attached_=false;
};

class TabletennisApp : public rex::ReXApp {
 public:
  TabletennisApp(rex::ui::WindowedAppContext& ctx, const std::string_view name,
                 const rex::PPCImageInfo& ppc_info)
      : rex::ReXApp(ctx, name, ppc_info) {
    rex::cvar::SetFlagByName("protect_zero", "false");
    rex::cvar::SetFlagByName("gpu_allow_invalid_fetch_constants", "true");
  }

  ~TabletennisApp() override {
    photo_ui_alive_->store(false);
    tabletennis::photo::Shutdown();
    rex::ui::UnregisterBind("bind_photo_mode");
    tabletennis::updates::Stop();
    tabletennis::pc_settings::SetOpenCallback({});
    tabletennis::pc::SetAchievementsToggle({});
    tabletennis::pc::SetWindow(nullptr);
    tabletennis::intro::Uninstall();
    rex::ui::UnregisterBind("bind_toggle_fps");
    rex::ui::UnregisterBind("bind_quit_game");
    tabletennis::stats_window::Hide();
    if (imgui_drawer_ && startup_dialog_ && startup_dialog_->attached()) {
      imgui_drawer_->RemoveDialog(startup_dialog_.get());
    }
    startup_dialog_.reset();
  }

  static std::unique_ptr<rex::ui::WindowedApp> Create(
      rex::ui::WindowedAppContext& ctx) {
    return std::unique_ptr<TabletennisApp>(new TabletennisApp(ctx, "tabletennis",
        PPCImageConfig));
  }

  void OnKeyDown(rex::ui::KeyEvent& event) override {
    if (!tabletennis::photo::HandleKey(event,true) && !tabletennis::pc_settings::HandleKey(event) && !tabletennis::achievement_ui::HandleKey(event)) rex::ui::ProcessKeyEvent(event);
  }
  void OnKeyUp(rex::ui::KeyEvent& event) override {tabletennis::photo::HandleKey(event,false);}
  bool OnWindowCloseRequested() override {
    photo_ui_alive_->store(false);
    tabletennis::photo::Shutdown();
    return true;
  }

 protected:
  void OnConfigurePaths(rex::PathConfig& paths) override {
    rex::cvar::SetFlagByName("protect_zero", "false");
    rex::cvar::SetFlagByName("gpu_allow_invalid_fetch_constants", "true");

    auto exe_dir = rex::filesystem::GetExecutableFolder();
    auto root_dir = exe_dir.parent_path();
    if(std::filesystem::exists(root_dir/".updates/in-progress")) {
      MessageBoxW(nullptr,L"An update is in progress or was interrupted. Open the launcher to finish or recover it.",L"Table Tennis update",MB_OK|MB_ICONINFORMATION);
      std::exit(0);
    }

    // Scan settings.ini in executable folder
    std::filesystem::path settings_path = exe_dir / "settings.ini";
    if (std::filesystem::exists(settings_path)) {
      std::ifstream ini_file(settings_path);
      std::string line;
      while (std::getline(ini_file, line)) {
        if (line.rfind("iso_path=", 0) == 0 && (paths.game_data_root.empty() || !std::filesystem::exists(paths.game_data_root))) {
          std::string val = line.substr(9);
          while (!val.empty() && (val.back() == '\r' || val.back() == '\n' || val.back() == ' ' || val.back() == '"')) val.pop_back();
          if (!val.empty() && val.front() == '"') val = val.substr(1);
          if (!val.empty() && std::filesystem::exists(val)) {
            paths.game_data_root = val;
            REXLOG_INFO("TabletennisApp: Found ISO from settings.ini: {}", paths.game_data_root.string());
          }
        } else if (line.rfind("framerate=", 0) == 0) {
          std::string val = line.substr(10);
          try {
            int target = std::stoi(val);
            if (target >= 0 && rex::cvar::GetFlagSource("framerate") < rex::cvar::Source::kCommandLine) {
              REXCVAR_SET(framerate, target);
              REXLOG_INFO("TabletennisApp: Target framerate from settings.ini: {} FPS", target);
            }
          } catch (...) {}
        } else if (line.rfind("high_fps_match_only=", 0) == 0 &&
                   rex::cvar::GetFlagSource("high_fps_match_only") < rex::cvar::Source::kCommandLine) {
          REXCVAR_SET(high_fps_match_only, line.compare(20, 4, "true") == 0);
        } else if (line.rfind("allow_tearing=", 0) == 0 &&
                   rex::cvar::GetFlagSource("allow_tearing") < rex::cvar::Source::kCommandLine) {
          REXCVAR_SET(allow_tearing, line.compare(14, 4, "true") == 0);
        } else {
          const auto separator = line.find('=');
          if (separator == std::string::npos) continue;
          const auto key = line.substr(0, separator);
          auto value = line.substr(separator + 1);
          while (!value.empty() && (value.back() == '\r' || value.back() == '\n')) value.pop_back();
          std::string flag;
          if (key == "button_prompts" || key == "achievement_sound" || key == "show_fps") flag = key;
#if defined(TT_COMMUNITY_NETWORK)
          else if (key == "system_link" && (value == "true" || value == "false")) flag = key;
          else if (key == "online_server" || key == "room_code") flag = key;
          else if (key == "player_name") flag = "user_name";
#endif
          if (!flag.empty() && rex::cvar::GetFlagSource(flag) < rex::cvar::Source::kCommandLine)
            rex::cvar::SetFlagByName(flag, value);
        }
      }
    }

    // Auto-detect ISO if still empty
    if (paths.game_data_root.empty() || !std::filesystem::exists(paths.game_data_root)) {
      for (const auto& dir : {exe_dir, root_dir}) {
        if (std::filesystem::exists(dir)) {
          for (const auto& entry : std::filesystem::directory_iterator(dir)) {
            if (entry.is_regular_file() && entry.path().extension() == ".iso") {
              paths.game_data_root = entry.path();
              REXLOG_INFO("TabletennisApp: Auto-detected ISO: {}", paths.game_data_root.string());
              break;
            }
          }
        }
        if (!paths.game_data_root.empty()) break;
      }
    }

    // Set isolated user_data and cache directories
    if (paths.user_data_root.empty() || paths.user_data_root == rex::filesystem::GetUserFolder() / GetName()) {
      auto custom_user = root_dir / "user_data";
      if (std::filesystem::exists(custom_user) || std::filesystem::exists(root_dir / "reports")) {
        paths.user_data_root = custom_user;
      }
    }

    if (paths.cache_root.empty() || paths.cache_root == paths.user_data_root / "cache") {
      auto custom_cache = root_dir / "cache";
      if (std::filesystem::exists(custom_cache) || std::filesystem::exists(root_dir / "reports")) {
        paths.cache_root = custom_cache;
      }
    }

    // Validate ISO if specified
    if (!paths.game_data_root.empty() && std::filesystem::is_regular_file(paths.game_data_root)) {
      ValidateIsoSource(paths.game_data_root);
    }
  }

  void OnPreSetup(rex::RuntimeConfig& config) override {
    rex::cvar::SetFlagByName("protect_zero", "false");
    rex::cvar::SetFlagByName("gpu_allow_invalid_fetch_constants", "true");
    if (config.gpu_plugin.empty()) {
      config.gpu_plugin = "xenos";
    }

    // Preserve the reported retail mode. With guest vsync off the SDK uses
    // a 1 kHz VBlank worker; the host pacer independently limits submissions.
    rex::cvar::SetFlagByName("video_mode_refresh_rate", "60.0");

    int32_t target_hz = REXCVAR_GET(framerate);
    std::filesystem::path frame_log;
    if (REXCVAR_GET(frame_log)) {
      frame_log = rex::filesystem::GetExecutableFolder() / "frametimes.csv";
    }
    tabletennis::pacing::Initialize(target_hz, frame_log);
    // The SDK presenter always presents immediately; this only decides whether
    // a present may tear instead of waiting for the next display refresh.
    // Without tearing, Present can block on the display while holding the
    // D3D12 queue that GPU emulation also submits to, which stalls the game
    // (measured: 47 FPS in a rally instead of 60).
    const bool tearing = REXCVAR_GET(allow_tearing);
    SetPluginFlag("d3d12_allow_variable_refresh_rate_and_tearing", tearing ? "true" : "false");
    // Guest "vsync" off in every mode: with it on, the SDK command processor
    // sleeps >= 1 ms on every GPU wait and the game falls to 30-48 FPS. The
    // guest vblank then runs at 1 kHz and frame_pacer holds the target rate.
    SetPluginFlag("vsync", "false");
    if (target_hz != 60) {
      // At 60 the game keeps its original fixed 1/60 s timer step.
      const bool variable_step = REXCVAR_GET(variable_timestep);
      const bool match_only = REXCVAR_GET(high_fps_match_only);
      tabletennis::pacing::SetMatchOnly(match_only, variable_step);
      REXLOG_WARN("TabletennisApp::OnPreSetup - Experimental {} FPS mode{}: {} timer step; tearing {}",
                  target_hz, match_only ? " in matches only (60 elsewhere)" : "",
                  variable_step ? "variable" : "fixed (game runs faster)",
                  tearing ? "allowed" : "off");
    } else {
      REXLOG_INFO("TabletennisApp::OnPreSetup - 60 FPS, host-paced, tearing {}",
                  tearing ? "allowed" : "off");
    }

    REXLOG_INFO("TabletennisApp::OnPreSetup - GPU plugin: {}, protect_zero: false", config.gpu_plugin);
  }

  std::unique_ptr<rex::ui::AchievementNotificationDialog> CreateAchievementNotificationDialog()
      override {
    if (!imgui_drawer() || !immediate_drawer() || !runtime()) {
      return nullptr;
    }
    return std::make_unique<OnDemandToastDialog>(app_context(), imgui_drawer(), immediate_drawer(),
                                                 runtime(), &achievements(), window());
  }

  void OnPostLoadXexImage() override {
    auto vfs = runtime()->kernel_state()->file_system();
    if (vfs) {
      vfs->RegisterSymbolicLink("cache:", "\\Device\\Harddisk0\\Cache0");

      wchar_t exe_path_buf[MAX_PATH];
      GetModuleFileNameW(NULL, exe_path_buf, MAX_PATH);
      std::filesystem::path exe_dir = std::filesystem::path(exe_path_buf).parent_path();
      std::filesystem::path flash_override = exe_dir / "assets" / "resource" / "flash";
      if (std::filesystem::exists(flash_override / "flash.rpf")) {
        if (auto* root = vfs->ResolvePath("game:")) {
          auto dev = std::make_unique<OfflineMenuDevice>(flash_override, root->device());
          if (dev->Initialize() && vfs->RegisterDevice(std::move(dev))) {
            vfs->UnregisterSymbolicLink("game:");
            vfs->UnregisterSymbolicLink("d:");
            vfs->RegisterSymbolicLink("game:", "\\Device\\TabletennisOfflineMenu");
            vfs->RegisterSymbolicLink("d:", "\\Device\\TabletennisOfflineMenu");
            REXLOG_INFO("Tabletennis: Offline main-menu override mounted from {}", flash_override.string());
          }
        }
      }
      if (auto entry = vfs->ResolvePath("game:\\assets\\resource\\flash\\flash.rpf")) {
        REXLOG_INFO("Tabletennis: flash.rpf resolves to device {} ({} bytes)",
                    entry->device()->mount_path(), entry->size());
      }
    }
  }

  std::unique_ptr<rex::ui::ImGuiDialog> CreateAchievementsOverlay() override {
    if (!imgui_drawer() || !runtime()) return nullptr;
    return std::make_unique<tabletennis::achievement_ui::AchievementsPanel>(
        imgui_drawer(),immediate_drawer(),runtime(),&achievements());
  }

  void OnConfigureFonts(ImFontAtlas* atlas) override {
    // Keep the regular overlay font first. The loading message gets its own
    // TrueType face baked at 3x size, with extra raster resolution for DPI.
    if (atlas->Fonts.empty()) atlas->AddFontDefault();
    wchar_t windows_dir[MAX_PATH]{};
    if (!GetWindowsDirectoryW(windows_dir, MAX_PATH)) return;
    ImFontConfig config;
    config.OversampleH = 2;
    config.OversampleV = 2;
    config.PixelSnapH = false;
    config.RasterizerDensity = 2.0f;
    static const ImWchar ranges[] = {0x20, 0x7E, 0};
    for (const auto* filename : {L"seguisb.ttf", L"segoeui.ttf", L"arial.ttf"}) {
      const auto path = std::filesystem::path(windows_dir) / L"Fonts" / filename;
      if (!std::filesystem::exists(path)) continue;
      loading_font_ = atlas->AddFontFromFileTTF(path.string().c_str(), 39.0f, &config, ranges);
      if (loading_font_) {
        REXLOG_INFO("Tabletennis: Loading text TrueType font: {} (39px, 2x raster density)", path.string());
        break;
      }
    }
    tabletennis::achievement_ui::ConfigureFonts(atlas, rex::filesystem::GetExecutableFolder()/"fonts");
  }

  void OnCreateDialogs(rex::ui::ImGuiDrawer* drawer) override {
    imgui_drawer_ = drawer;
    if (drawer) {
      startup_dialog_ = std::make_unique<StartupDialog>(drawer, loading_font_);
    }
    if (REXCVAR_GET(show_fps) && window()) {
      tabletennis::stats_window::Show(window()->GetNativeWindowHandle());
    }

    // Hotkey F10 toggles FPS overlay during gameplay
    rex::ui::RegisterBind("bind_photo_mode","F9","Photo Mode",[]{tabletennis::photo::RequestToggle();});
    rex::ui::RegisterBind("bind_toggle_fps", "F10", "Toggle In-Game FPS Counter", [this] {
      if (tabletennis::stats_window::IsVisible()) {
        tabletennis::stats_window::Hide();
      } else if (window()) {
        tabletennis::stats_window::Show(window()->GetNativeWindowHandle());
      }
      REXLOG_INFO("FPS overlay toggled via F10: {}",
                  tabletennis::stats_window::IsVisible() ? "ON" : "OFF");
    });

    // Hotkey ESC quits game cleanly to Windows
    rex::ui::RegisterBind("bind_quit_game", "Escape", "Quit Game to Windows", [this] {
      REXLOG_INFO("ESC pressed. Exiting cleanly to Windows...");
      if (window()) {
        window()->RequestClose();
      } else {
        std::_Exit(0);
      }
    });

    REXLOG_INFO("TabletennisApp: Binds for ESC (Quit) and F10 (FPS Toggle) are active");
  }

  void OnWindowPixelSizeChanged(uint32_t width, uint32_t height) override {
    tabletennis::pc::SetSurfaceSize(width,height);
  }

  void OnPostSetup() override {
    tabletennis::photo::Init([this]() -> rex::ui::Presenter* {
      auto* graphics=runtime()?runtime()->graphics_system():nullptr;
      return graphics?graphics->presenter():nullptr;
    },[this,alive=photo_ui_alive_] {
      app_context().CallInUIThreadDeferred([this,alive] {
        if(alive->load() && imgui_drawer()) new tabletennis::photo::Overlay(imgui_drawer());
      });
    });
#if defined(TT_COMMUNITY_NETWORK)
    REXLOG_INFO("Tabletennis: Network setup: enabled={}, system_link={}, server={}, player={}",
                tabletennis::lan::Enabled(), rex::cvar::GetFlagByName("system_link"),
                rex::cvar::GetFlagByName("online_server"), rex::cvar::GetFlagByName("user_name"));
#endif
    tabletennis::pc::SetAchievementsToggle([this] {
      app_context().CallInUIThreadDeferred([this] {
        if(!window()) return;
        rex::ui::KeyEvent event(window(),rex::ui::VirtualKey::kF7,1,false,false,false,false,false);
        rex::ui::ProcessKeyEvent(event);
      });
    });
    tabletennis::pc_settings::SetOpenCallback([this] {
      app_context().CallInUIThreadDeferred([this] {
        if(window()) new tabletennis::pc_settings::SettingsPage(imgui_drawer(),window());
      });
    });
    tabletennis::updates::Start([this](std::string version) {
      app_context().CallInUIThreadDeferred([this,version] {new tabletennis::updates::UpdateToast(imgui_drawer(),version);});
    });
    tabletennis::prompts::Init(runtime()->memory(),runtime()->graphics_system());
    if (window()) {
      window()->SetCursorAutoHideDelayMs(2000);
      window()->SetCursorVisibility(rex::ui::Window::CursorVisibility::kAutoHidden);
      tabletennis::pc::SetWindow(window()->GetNativeWindowHandle());
      tabletennis::pc::SetSurfaceSize(window()->GetActualPhysicalWidth(),window()->GetActualPhysicalHeight());
    }
    rex::cvar::SetFlagByName("present_letterbox", "false");
    REXLOG_INFO("Tabletennis: loaded GPU timing flags: vsync={}, tearing={}",
                rex::cvar::GetFlagByName("vsync"),
                rex::cvar::GetFlagByName("d3d12_allow_variable_refresh_rate_and_tearing"));
    tabletennis::pacing::InstallPresentHook();
    tabletennis::intro::Install();
    REXLOG_INFO("TabletennisApp::OnPostSetup - Runtime and VFS fully initialized");
  }

 private:
  std::shared_ptr<std::atomic<bool>> photo_ui_alive_=std::make_shared<std::atomic<bool>>(true);
  void ValidateIsoSource(const std::filesystem::path& iso_path) {
    std::ifstream f(iso_path, std::ios::binary);
    if (!f.is_open()) {
      REXLOG_ERROR("Could not open ISO file: {}", iso_path.string());
      return;
    }

    // Check GDFX magic at sector 32 of game partition (offset 0xFDA0000 in XGD2)
    f.seekg(0xFDA0000);
    char magic[20] = {0};
    f.read(magic, 20);
    if (std::memcmp(magic, "MICROSOFT*XBOX*MEDIA", 20) == 0) {
      REXLOG_INFO("ISO validation PASSED: GDFX filesystem identified at sector 32 (offset 0xFDA0000)");
    } else {
      REXLOG_WARN("ISO validation WARNING: GDFX magic not found at 0xFDA0000. Image may be corrupt or non-standard.");
    }
  }

  ImFont* loading_font_ = nullptr;
  std::unique_ptr<StartupDialog> startup_dialog_;
  rex::ui::ImGuiDrawer* imgui_drawer_ = nullptr;
};
