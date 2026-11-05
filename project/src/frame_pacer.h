// frame_pacer.h - Host frame pacing and frame-time measurement

#pragma once

#include <cstddef>
#include <cstdint>
#include <filesystem>

namespace tabletennis::pacing {

// Applies process-wide timing setup (1 ms system timer resolution) and sets
// the frame rate target (0 = unlimited). A non-empty frame_log path writes
// every game frame and present to a CSV file.
void Initialize(int32_t target_fps, const std::filesystem::path& frame_log);

// Hooks DXGI Present to measure when frames reach the swap chain and how long
// Present blocks (call once after the presenter exists). Presents are not
// delayed. Above 60 FPS, if presents are being held back by a driver frame
// cap or forced V-Sync, BeforeSwap stops pacing and lets that limiter set
// the rate, as it does for other PC games.
void InstallPresentHook();

// An ImGui dialog was attached (true) or detached (false). While any is
// attached the presenter also repaints on every display refresh, so present
// measurements are marked as taken with the overlay shown.
void SetUiActive(bool active);

// Called on the guest render thread at the start of the D3D Swap function
// (native edit in sub_8235B750), before the frame is swapped and kicked to the
// GPU. Holding it here makes frames reach the GPU, and the screen, at fixed
// intervals however long the game's CPU work took; pacing after the kick
// passed the 5-8 ms variation in that work straight to the display.
// Absolute-deadline limiter on a high-resolution timer.
void BeforeSwap();

// Above 60 FPS: run matches at the target and front-end menus, videos,
// loading and the pause menu at exactly 60 with the game's original fixed
// timer step (they advance once per frame in this game). variable_step: use
// the measured frame time while running above 60. Without match_only the
// target and step apply everywhere.
void SetMatchOnly(bool match_only, bool variable_step);

// Called once per frame by the game's match update (native edit in
// sub_82160698).
void NoteMatchFrame();

// Controller buttons as the game reads them (XInput layout). A Start press
// during a high-rate match drops to 60 and restores the game's original timer
// step on that same frame, before the game builds its pause menu.
void OnControllerButtons(uint32_t user, uint16_t buttons, uint8_t* base);

// Called when the game starts opening a menu/popup movie (native edit in
// sub_823FDC00: help tips, the pause menu, end-of-match screens). During a
// high-rate match this does the same as a Start press: those menus read the
// timer as they are built and break if the step is scaled.
void OnPopupOpening(uint8_t* base);

struct SeriesStats {
  float fps = 0.0f;     // Over the last ~2 seconds
  float avg_ms = 0.0f;  // Mean frame interval
  float p99_ms = 0.0f;  // 99th percentile interval ("1% low")
  float max_ms = 0.0f;  // Worst interval
};
struct Stats {
  SeriesStats game;     // Game frames (submission to the GPU)
  SeriesStats present;  // DXGI presents
};
Stats GetStats();

constexpr size_t kHistoryFrames = 240;
enum class Series { kGame, kPresent };
// Copies the most recent frame intervals (ms), oldest first.
size_t CopyHistory(Series series, float* out, size_t max);

}  // namespace tabletennis::pacing
