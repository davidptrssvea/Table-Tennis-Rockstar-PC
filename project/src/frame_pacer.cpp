// frame_pacer.cpp - Host frame pacing and frame-time measurement
//
// With guest vsync off the guest vblank interrupt runs at 1 kHz, so the game
// renders as fast as it can. BeforeSwap, on the guest render thread at the
// start of each D3D Swap (before the frame is kicked to the GPU), holds the
// game to the target rate with absolute deadlines on a high-resolution timer.
//
// A DXGI Present hook only measures: it records when frames actually reach
// the swap chain. Holding presents back was tried and made pacing worse,
// because the presenting thread also drives GPU emulation.

#include "frame_pacer.h"
#include "frame_schedule.h"

#include "game_timer.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <timeapi.h>
#include <d3d11.h>
#include <dxgi1_2.h>

#include <rex/logging.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <mutex>
#include <string>
#include <vector>
#if defined(_M_X64) || defined(__x86_64__)
#include <immintrin.h>
#endif

#ifndef CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
#define CREATE_WAITABLE_TIMER_HIGH_RESOLUTION 0x00000002
#endif

namespace tabletennis::pacing {
namespace {

using clock = std::chrono::steady_clock;

// Wake this long before the deadline and spin the remainder.
constexpr auto kSpinMargin = std::chrono::microseconds(600);

int64_t NowNs() {
  return std::chrono::duration_cast<std::chrono::nanoseconds>(clock::now().time_since_epoch())
      .count();
}

int32_t g_target_fps = 60;
std::atomic<int> g_ui_active{0};

// Above 60 FPS, set while something outside the game (a driver frame cap, or
// V-Sync forced in the driver) holds presents back. The game then stops
// pacing itself and lets that limiter set the rate, like other PC games;
// pacing to its own clock as well made the two fight (presents at 141/s with
// the game at 144/s doubled every few frames to ~14 ms).
std::atomic<bool> g_external_limit{false};

// "High frame rate in matches only": a match (from the final start
// confirmation: intro, serve, points, transitions) runs at the target;
// front-end menus, videos, loading and the pause menu run at exactly 60 with
// the game's original 1/60 s timer step, as at native 60. Those parts advance
// once per frame and ran too fast above 60. NoteMatchFrame is called once per
// frame by the game's match update (native edit in sub_82160698).
std::atomic<bool> g_match_only{false};
std::atomic<bool> g_variable_step{false};
std::atomic<int64_t> g_last_match_ns{0};
std::atomic<bool> g_high_rate{true};
// Stay at the high rate through short gaps in the match update (a slow
// frame); a pause menu switches to 60 once it is up.
constexpr int64_t kMatchHoldNs = 400'000'000;
// Set by a Start press in a high-rate match; cleared when the match update
// resumes after a gap (the pause is over).
std::atomic<bool> g_paused_by_start{false};
uint16_t g_prev_buttons[4] = {};

// True while frames run above 60 FPS (the only time an external limiter is
// tracked; at 60 the game keeps its own exact clock).
bool HighRateActive() { return g_target_fps > 60 && g_high_rate.load(); }

// ---- Measurement ----------------------------------------------------------

// Frame intervals of one series, written by a single thread.
class History {
 public:
  void Record(int64_t now_ns) {
    if (last_ns_) {
      std::lock_guard lock(mutex_);
      values_[next_ % values_.size()] = float(double(now_ns - last_ns_) / 1e6);
      ++next_;
    }
    last_ns_ = now_ns;
  }

  size_t Copy(float* out, size_t max) const {
    std::lock_guard lock(mutex_);
    size_t count = std::min({max, values_.size(), next_});
    for (size_t i = 0; i < count; ++i) {
      out[i] = values_[(next_ - count + i) % values_.size()];
    }
    return count;
  }

  SeriesStats Summarize() const {
    std::vector<float> v(kSummaryFrames);
    v.resize(Copy(v.data(), v.size()));
    SeriesStats stats;
    if (v.empty()) {
      return stats;
    }
    double sum = 0.0;
    for (float ms : v) {
      sum += ms;
      stats.max_ms = std::max(stats.max_ms, ms);
    }
    stats.avg_ms = float(sum / v.size());
    stats.fps = stats.avg_ms > 0.0f ? 1000.0f / stats.avg_ms : 0.0f;
    std::sort(v.begin(), v.end());
    stats.p99_ms = v[std::min(v.size() - 1, v.size() * 99 / 100)];
    return stats;
  }

 private:
  // About two seconds at 60 FPS.
  static constexpr size_t kSummaryFrames = 120;
  mutable std::mutex mutex_;
  std::array<float, kHistoryFrames> values_{};
  size_t next_ = 0;
  int64_t last_ns_ = 0;
};

History g_game_history;
History g_present_history;

// Optional per-frame CSV: event,time_ms,interval_ms,overlay
std::mutex g_log_mutex;
FILE* g_log = nullptr;
int64_t g_log_start_ns = 0;
int64_t g_log_last_ns[2] = {0, 0};

void LogEvent(int kind, int64_t now_ns) {
  if (!g_log) {
    return;
  }
  std::lock_guard lock(g_log_mutex);
  int64_t& last = g_log_last_ns[kind];
  std::fprintf(g_log, "%s,%.3f,%.3f,%d\n", kind ? "present" : "game",
               double(now_ns - g_log_start_ns) / 1e6,
               last ? double(now_ns - last) / 1e6 : 0.0, g_ui_active.load() ? 1 : 0);
  last = now_ns;
}

void LogSummary(const char* name, const History& history, int64_t now_ns, int64_t& last_log_ns) {
  if (now_ns - last_log_ns < 5'000'000'000LL) {
    return;
  }
  last_log_ns = now_ns;
  auto s = history.Summarize();
  REXLOG_INFO("PERF TICK: {} {:.1f} FPS, avg {:.2f} ms, 1% {:.2f} ms, worst {:.2f} ms{}", name,
              s.fps, s.avg_ms, s.p99_ms, s.max_ms,
              g_ui_active.load() ? " (overlay shown)" : "");
}

// ---- Timing ---------------------------------------------------------------

struct Timer {
  HANDLE handle = nullptr;
  bool high_resolution = false;
};

Timer CreateTimer() {
  Timer t;
  t.handle = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
                                    TIMER_ALL_ACCESS);
  t.high_resolution = t.handle != nullptr;
  if (!t.handle) {
    t.handle = CreateWaitableTimerExW(nullptr, nullptr, 0, TIMER_ALL_ACCESS);
  }
  return t;
}

void SpinUntil(int64_t deadline_ns) {
  while (NowNs() < deadline_ns) {
#if defined(_M_X64) || defined(__x86_64__)
    _mm_pause();
#endif
  }
}

void SleepUntil(const Timer& timer, int64_t deadline_ns) {
  const int64_t margin_ns = timer.high_resolution
                                ? std::chrono::nanoseconds(kSpinMargin).count()
                                : std::chrono::nanoseconds(std::chrono::milliseconds(2)).count();
  int64_t coarse_ns = deadline_ns - NowNs() - margin_ns;
  if (coarse_ns > 0) {
    if (timer.handle) {
      LARGE_INTEGER due;
      // Negative = relative, in 100 ns units.
      due.QuadPart = -coarse_ns / 100;
      if (SetWaitableTimer(timer.handle, &due, 0, nullptr, nullptr, FALSE)) {
        WaitForSingleObject(timer.handle, INFINITE);
      }
    } else {
      ::Sleep(DWORD(coarse_ns / 1'000'000));
    }
  }
  SpinUntil(deadline_ns);
}

struct PacerState {
  Timer timer;
  Schedule schedule;
  int64_t last_submit_ns = 0;
  int64_t last_log_ns = 0;
};

PacerState& State() {
  static PacerState state;
  return state;
}

// ---- Present hook (measurement only) ---------------------------------------

using PresentFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
using Present1Fn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain1*, UINT, UINT,
                                               const DXGI_PRESENT_PARAMETERS*);
PresentFn g_original_present = nullptr;
Present1Fn g_original_present1 = nullptr;
int64_t g_present_last_log_ns = 0;

// Average time Present spends blocked, per one-second window, decides whether
// an external limiter is in charge. With tearing allowed and nothing else
// limiting, Present returns in well under a millisecond.
constexpr double kEnterFollowMs = 1.0;
constexpr double kLeaveFollowMs = 0.3;
// Leave follow mode only after this many quiet windows in a row: when the
// game itself briefly runs no faster than the cap, presents stop blocking for
// a moment, and switching back to its own clock then caused a hitch.
constexpr int kLeaveFollowWindows = 5;
int g_quiet_windows = 0;
int64_t g_block_window_start_ns = 0;
double g_block_sum_ms = 0.0;
uint32_t g_block_count = 0;

void RecordPresentBlock(int64_t start_ns, int64_t end_ns) {
  if (!HighRateActive() || g_ui_active.load()) {
    // At 60 the game must keep its own exact clock (its simulation advances
    // once per frame). While an ImGui dialog is attached the presenter also
    // repaints every refresh, so its presents are not a useful signal.
    g_block_window_start_ns = 0;
    return;
  }
  if (!g_block_window_start_ns) {
    g_block_window_start_ns = start_ns;
    g_block_sum_ms = 0.0;
    g_block_count = 0;
  }
  g_block_sum_ms += double(end_ns - start_ns) / 1e6;
  ++g_block_count;
  if (end_ns - g_block_window_start_ns < 1'000'000'000LL) {
    return;
  }
  const double avg_ms = g_block_sum_ms / g_block_count;
  const bool was_following = g_external_limit.load();
  bool follow = was_following;
  if (!was_following) {
    follow = avg_ms > kEnterFollowMs;
  } else {
    g_quiet_windows = avg_ms < kLeaveFollowMs ? g_quiet_windows + 1 : 0;
    follow = g_quiet_windows < kLeaveFollowWindows;
  }
  if (follow != was_following) {
    g_quiet_windows = 0;
    g_external_limit = follow;
    if (follow) {
      REXLOG_INFO("FramePacer: presents blocked {:.2f} ms on average: an external limiter "
                  "(driver frame cap or V-Sync) is pacing the game; not pacing to {} FPS",
                  avg_ms, g_target_fps);
    } else {
      REXLOG_INFO("FramePacer: presents blocked {:.2f} ms on average: no external limiter; "
                  "pacing to {} FPS again", avg_ms, g_target_fps);
    }
  }
  g_block_window_start_ns = 0;
}

void RecordPresent(UINT flags) {
  const int64_t now = NowNs();
  g_present_history.Record(now);
  LogEvent(1, now);
  LogSummary("presents", g_present_history, now, g_present_last_log_ns);
}

HRESULT STDMETHODCALLTYPE HookPresent(IDXGISwapChain* swap_chain, UINT sync, UINT flags) {
  if (flags & DXGI_PRESENT_TEST) {
    return g_original_present(swap_chain, sync, flags);
  }
  RecordPresent(flags);
  const int64_t start = NowNs();
  HRESULT result = g_original_present(swap_chain, sync, flags);
  RecordPresentBlock(start, NowNs());
  return result;
}

HRESULT STDMETHODCALLTYPE HookPresent1(IDXGISwapChain1* swap_chain, UINT sync, UINT flags,
                                       const DXGI_PRESENT_PARAMETERS* params) {
  if (flags & DXGI_PRESENT_TEST) {
    return g_original_present1(swap_chain, sync, flags, params);
  }
  RecordPresent(flags);
  const int64_t start = NowNs();
  HRESULT result = g_original_present1(swap_chain, sync, flags, params);
  RecordPresentBlock(start, NowNs());
  return result;
}

void PatchSlot(void** slot, void* hook, void** original) {
  DWORD old_protect;
  if (VirtualProtect(slot, sizeof(void*), PAGE_EXECUTE_READWRITE, &old_protect)) {
    *original = *slot;
    *slot = hook;
    VirtualProtect(slot, sizeof(void*), old_protect, &old_protect);
  }
}

}  // namespace

void Initialize(int32_t target_fps, const std::filesystem::path& frame_log) {
  g_target_fps = target_fps;
  if (target_fps == 60) {
    REXLOG_INFO("FramePacer: target 60 FPS");
  } else {
    REXLOG_INFO("FramePacer: target {} FPS (experimental above 60: timer step follows frame time)",
                target_fps > 0 ? std::to_string(target_fps) : std::string("unlimited"));
  }
  // The SDK's vblank and wait loops use ::Sleep(1); without this they run on
  // the default ~15.6 ms scheduler tick and deliver vblanks in bursts.
  timeBeginPeriod(1);
  auto& s = State();
  if (!s.timer.handle) {
    s.timer = CreateTimer();
  }
  if (!frame_log.empty() && !g_log) {
    g_log = _wfopen(frame_log.c_str(), L"w");
    if (g_log) {
      std::fputs("event,time_ms,interval_ms,overlay\n", g_log);
      g_log_start_ns = NowNs();
      REXLOG_INFO("FramePacer: logging frame times to {}", frame_log.string());
    }
  }
  REXLOG_INFO("FramePacer: 1 ms timer resolution, {} waitable timer",
              s.timer.high_resolution ? "high-resolution" : "standard");
}

void InstallPresentHook() {
  if (g_original_present) {
    return;
  }
  // Best-effort measurement for the common DXGI implementation shared by
  // D3D11/D3D12. Unshared implementations may not produce timing samples.
  WNDCLASSW wc{};
  wc.lpfnWndProc = DefWindowProcW;
  wc.hInstance = GetModuleHandleW(nullptr);
  wc.lpszClassName = L"TabletennisPresentHook";
  RegisterClassW(&wc);
  HWND hwnd = CreateWindowW(wc.lpszClassName, L"", WS_OVERLAPPEDWINDOW, 0, 0, 64, 64, nullptr,
                            nullptr, wc.hInstance, nullptr);
  DXGI_SWAP_CHAIN_DESC desc{};
  desc.BufferCount = 2;
  desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
  desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
  desc.OutputWindow = hwnd;
  desc.SampleDesc.Count = 1;
  desc.Windowed = TRUE;
  desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
  IDXGISwapChain* swap_chain = nullptr;
  ID3D11Device* device = nullptr;
  ID3D11DeviceContext* context = nullptr;
  if (SUCCEEDED(D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
                                              nullptr, 0, D3D11_SDK_VERSION, &desc, &swap_chain,
                                              &device, nullptr, &context))) {
    void** vtable = *reinterpret_cast<void***>(swap_chain);
    PatchSlot(&vtable[8], reinterpret_cast<void*>(&HookPresent),
              reinterpret_cast<void**>(&g_original_present));
    // Present1 belongs to IDXGISwapChain1; query it before accessing its slot.
    IDXGISwapChain1* extended = nullptr;
    if (SUCCEEDED(swap_chain->QueryInterface(IID_PPV_ARGS(&extended)))) {
      void** extended_vtable = *reinterpret_cast<void***>(extended);
      PatchSlot(&extended_vtable[22], reinterpret_cast<void*>(&HookPresent1),
                reinterpret_cast<void**>(&g_original_present1));
      extended->Release();
    }
    swap_chain->Release();
    context->Release();
    device->Release();
    REXLOG_INFO("FramePacer: present measurement installed");
  } else {
    REXLOG_WARN("FramePacer: present measurement unavailable");
  }
  DestroyWindow(hwnd);
  UnregisterClassW(wc.lpszClassName, wc.hInstance);
}

void SetMatchOnly(bool enabled, bool variable_step) {
  g_match_only = enabled;
  g_variable_step = variable_step;
  g_high_rate = !enabled;
  timing::SetScaledStep(!enabled && variable_step);
}

void NoteMatchFrame() {
  const int64_t now = NowNs();
  if (g_paused_by_start.load() && now - g_last_match_ns.load() > 100'000'000) {
    g_paused_by_start = false;  // match update resumed after the pause
  }
  g_last_match_ns = now;
}

void OnControllerButtons(uint32_t user, uint16_t buttons, uint8_t* base) {
  if (user >= 4) {
    return;
  }
  constexpr uint16_t kStart = 0x0010;
  const bool start_edge = (buttons & kStart) && !(g_prev_buttons[user] & kStart);
  g_prev_buttons[user] = buttons;
  if (!start_edge || !g_match_only.load() || g_target_fps <= 60 || !g_high_rate.load()) {
    return;
  }
  g_paused_by_start = true;
  g_high_rate = false;
  timing::RestoreNow(base);
  REXLOG_INFO("FramePacer: Start pressed in a match: 60 FPS, original timer step");
}

void OnPopupOpening(uint8_t* base) {
  if (!g_match_only.load() || g_target_fps <= 60 || !g_high_rate.load()) {
    return;
  }
  g_paused_by_start = true;
  g_high_rate = false;
  timing::RestoreNow(base);
  REXLOG_INFO("FramePacer: popup opening in a match: 60 FPS, original timer step");
}

void SetUiActive(bool active) {
  g_ui_active += active ? 1 : -1;
}

void BeforeSwap() {
  auto& s = State();
  int32_t target_fps = g_target_fps;
  if (g_match_only.load() && target_fps > 60) {
    const bool high = !g_paused_by_start.load() && NowNs() - g_last_match_ns.load() < kMatchHoldNs;
    if (high != g_high_rate.load()) {
      g_high_rate = high;
      timing::SetScaledStep(high && g_variable_step.load());
      REXLOG_INFO("FramePacer: {}", high ? "match running: high frame rate"
                                         : "not in a running match: 60 FPS, original timer step");
    }
    if (!high) {
      target_fps = 60;
    }
  }
  // Above 60 with a driver frame cap or forced V-Sync, let that limiter set
  // the rate (Next(.., 0) also resets the schedule for when pacing resumes).
  const bool follow_external = target_fps > 60 && g_external_limit.load();
  const int32_t paced_fps = follow_external ? 0 : target_fps;
  if (paced_fps > 0) {
    SleepUntil(s.timer, s.schedule.Next(NowNs(), paced_fps));
  } else {
    s.schedule.Next(NowNs(), 0);
  }
  // Recorded on release: this is when the frame is submitted.
  const int64_t submitted = NowNs();
  if (s.last_submit_ns) {
    timing::SetFrameSeconds(double(submitted - s.last_submit_ns) / 1e9);
  }
  s.last_submit_ns = submitted;
  g_game_history.Record(submitted);
  LogEvent(0, submitted);
  LogSummary("game", g_game_history, submitted, s.last_log_ns);
}

Stats GetStats() {
  Stats stats;
  stats.game = g_game_history.Summarize();
  stats.present = g_present_history.Summarize();
  return stats;
}

size_t CopyHistory(Series series, float* out, size_t max) {
  return (series == Series::kGame ? g_game_history : g_present_history).Copy(out, max);
}

}  // namespace tabletennis::pacing
