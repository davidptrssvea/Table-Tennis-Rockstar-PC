// frame_pacer.h - Host frame pacing and frame-time measurement

#pragma once

#include <cstddef>
#include <cstdint>
#include <filesystem>

namespace tabletennis::pacing {

// Applies process-wide timing setup (1 ms system timer resolution). Frames
// are paced at 60 (the game's simulation advances 1/60 s per frame). A
// non-empty frame_log path writes every game frame and present to a CSV file.
void Initialize(int32_t target_fps, const std::filesystem::path& frame_log);

// Hooks DXGI Present to measure when frames reach the swap chain (call once
// after the presenter exists). Presents are not delayed.
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
