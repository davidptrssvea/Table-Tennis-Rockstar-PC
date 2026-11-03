// achievement_ui.h - achievement notification and achievement list drawn in the game's own
// menu style (condensed yellow headlines, dark navy panels, halftone swooshes, button glyphs).
//
// The notification (toast) replaces the SDK's debug-styled one through
// ReXApp::CreateAchievementNotificationDialog; the list replaces the F7 overlay through
// ReXApp::CreateAchievementsOverlay. The list can also be opened from a controller: hold the
// Back/View button for kPanelOpenHoldSeconds. While it is open the game receives no pad
// input; D-pad or left stick scroll, shoulder buttons page, B (or another hold) closes.
//
// Test hooks (environment variables, display-only, never change saved unlocks):
//   TT_TEST_ACHIEVEMENT_TOAST=<id>[,<id>...]  show these notifications shortly after the
//                                             main menu appears
//   TT_TEST_ACHIEVEMENTS_UNLOCKED=<id>[,...]  draw these as unlocked in the list

#pragma once

#include <atomic>
#include <chrono>
#include <cstdint>
#include <deque>
#include <filesystem>
#include <mutex>
#include <optional>
#include <string>
#include <vector>

#include <rex/system/achievement_manager.h>
#include <rex/ui/imgui_dialog.h>
#include <rex/ui/overlay/achievement_icon_cache.h>
#include <rex/ui/overlay/achievement_notification.h>

struct ImFont;
struct ImFontAtlas;

namespace rex::ui {
class ImmediateDrawer;
class Window;
}  // namespace rex::ui

namespace tabletennis::achievement_ui {

// ---------------------------------------------------------------------------------------------
// Tunables. Sizes are in 720p units; everything scales with the window height.
// ---------------------------------------------------------------------------------------------
enum class ToastAnchor { kTopCenter, kTopLeft, kTopRight, kBottomCenter, kBottomLeft, kBottomRight };

struct ToastConfig {
  // Bottom right, sliding up from the screen edge like a desktop achievement popup. It can
  // briefly cover the in-match rally counter; top center is the spot that clears all HUD.
  ToastAnchor anchor = ToastAnchor::kBottomRight;
  float scale = 0.68f;          // overall size (1.0 = 86 px tall at 720p)
  float margin = 30.0f;         // distance from the edge of the game picture, before scale
  float enter_seconds = 0.55f;  // slide/scale in, with the highlight sweep
  float hold_seconds = 4.2f;    // fully visible, measured from the end of the entry
  float exit_seconds = 0.45f;   // slide/fade out
  float gap_seconds = 0.25f;    // pause between queued notifications
  float sound_volume = 0.7f;    // unlock sound peak level, 0 = silent (also cvar achievement_sound)
};
inline constexpr ToastConfig kToast{};

// How long Back/View must be held to open or close the list.
inline constexpr float kPanelOpenHoldSeconds = 0.8f;

// ---------------------------------------------------------------------------------------------
// Fonts: a heavy face for headlines and names, a regular face for descriptions. Loaded from
// <font_dir> (shipped Noto Sans subsets), then common system fonts; missing fonts fall back to
// the ImGui default so the UI always works.
// ---------------------------------------------------------------------------------------------
void ConfigureFonts(ImFontAtlas* atlas, const std::filesystem::path& font_dir);

// The loaded faces, shared with the PC settings page: the heavy face baked nearest to `px`,
// and the regular face.
ImFont* HeadlineFont(float px);
ImFont* BodyFont();

// ---------------------------------------------------------------------------------------------
// Controller access. The game polls its pads through one XamInputGetState call site; that
// site hands every sample through FilterInput before the game sees it.
// ---------------------------------------------------------------------------------------------
struct PadSample {
  uint16_t buttons = 0;
  uint8_t left_trigger = 0;
  uint8_t right_trigger = 0;
  int16_t thumb_lx = 0;
  int16_t thumb_ly = 0;
  int16_t thumb_rx = 0;
  int16_t thumb_ry = 0;
};

// Called on a guest thread for every pad sample. Returns true when `pad` was changed and must
// be written back to the game.
bool FilterInput(uint32_t user, PadSample& pad);

// Called every frame on the UI thread: performs list open/close requests made by the pad by
// synthesizing the F7 toggle, so keyboard and controller share one state.
void ProcessControllerRequests(rex::ui::Window* window);

// Opens the achievements list if it is not already open. Used by the game's EXTRAS > ACHIEVEMENTS
// menu entry. Safe to call from a guest thread; it is applied on the UI thread.
void OpenList();

bool IsPanelOpen();

// ---------------------------------------------------------------------------------------------
// Notification popup.
// ---------------------------------------------------------------------------------------------
class AchievementToast : public rex::ui::AchievementNotificationDialog {
 public:
  AchievementToast(rex::ui::ImGuiDrawer* drawer, rex::ui::ImmediateDrawer* immediate_drawer,
                   rex::Runtime* runtime, rex::system::AchievementManager* achievements,
                   rex::ui::Window* window);
  ~AchievementToast() override;

  // Thread-safe; the unlock callback arrives on a guest thread.
  void Push(const rex::system::AchievementEvent& event) override;

 protected:
  void OnDraw(ImGuiIO& io) override;

 private:
  using Clock = std::chrono::steady_clock;
  struct Pending {
    rex::system::AchievementEvent event;
    std::optional<Clock::time_point> shown_at;
  };

  void RunTestHook();
  void DrawToast(ImGuiIO& io, const rex::system::AchievementInfo& info, float t,
                 float total_seconds);

  std::mutex mutex_;
  std::deque<Pending> queue_;
  Clock::time_point last_finished_{};
  rex::ui::AchievementIconCache icon_cache_;
  rex::system::AchievementManager* achievements_ = nullptr;
  rex::ui::Window* window_ = nullptr;
  bool test_hook_done_ = false;
  std::optional<Clock::time_point> test_hook_at_;
};

// ---------------------------------------------------------------------------------------------
// Achievement list (F7, or hold Back/View).
// ---------------------------------------------------------------------------------------------
class AchievementsPanel : public rex::ui::ImGuiDialog {
 public:
  AchievementsPanel(rex::ui::ImGuiDrawer* drawer, rex::ui::ImmediateDrawer* immediate_drawer,
                    rex::Runtime* runtime, rex::system::AchievementManager* achievements);
  ~AchievementsPanel() override;

 protected:
  void OnDraw(ImGuiIO& io) override;

 private:
  using Clock = std::chrono::steady_clock;
  struct Entry {
    rex::system::AchievementInfo info;
    bool unlocked = false;
    std::string unlock_date;  // empty while locked
  };

  void Refresh();
  void HandleNavigation(ImGuiIO& io, int row_count, int rows_per_page);
  void Move(int delta, int row_count);

  rex::system::AchievementManager* achievements_ = nullptr;
  rex::ui::AchievementIconCache icon_cache_;
  std::vector<Entry> entries_;
  int unlocked_count_ = 0;
  uint32_t earned_gamerscore_ = 0;
  uint32_t total_gamerscore_ = 0;
  Clock::time_point opened_at_;
  Clock::time_point next_refresh_;
  int selected_ = 0;
  float scroll_ = 0.0f;         // current list offset, in rows
  float scroll_target_ = 0.0f;
  int held_direction_ = 0;      // pad direction currently auto-repeating
  Clock::time_point next_repeat_;
};

}  // namespace tabletennis::achievement_ui
