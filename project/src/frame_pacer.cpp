// frame_pacer.cpp - Host frame pacing and frame-time measurement
//
// With guest vsync off the guest vblank interrupt runs at 1 kHz, so the game
// renders as fast as it can. BeforeSwap, on the guest render thread at the
// start of each D3D Swap (before the frame is kicked to the GPU), holds the
// game to 60 FPS with absolute deadlines on a high-resolution timer.
//
// A DXGI Present hook only measures: it records when frames actually reach
// the swap chain. Holding presents back was tried and made pacing worse,
// because the presenting thread also drives GPU emulation.

#include "frame_pacer.h"
#include "frame_schedule.h"

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
  return g_original_present(swap_chain, sync, flags);
}

HRESULT STDMETHODCALLTYPE HookPresent1(IDXGISwapChain1* swap_chain, UINT sync, UINT flags,
                                       const DXGI_PRESENT_PARAMETERS* params) {
  if (flags & DXGI_PRESENT_TEST) {
    return g_original_present1(swap_chain, sync, flags, params);
  }
  RecordPresent(flags);
  return g_original_present1(swap_chain, sync, flags, params);
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
  // The game advances its simulation a fixed 1/60 s per frame, so it is paced
  // at 60 whatever the setting; higher rates need the timer handled as well.
  g_target_fps = 60;
  if (target_fps != 60) {
    REXLOG_INFO("FramePacer: framerate {} requested; pacing at 60 (the game runs too fast above 60)",
                target_fps);
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

void SetUiActive(bool active) {
  g_ui_active += active ? 1 : -1;
}

void BeforeSwap() {
  auto& s = State();
  const int32_t target_fps = g_target_fps;
  if (target_fps > 0) {
    SleepUntil(s.timer, s.schedule.Next(NowNs(), target_fps));
  }
  // Recorded on release: this is when the frame is submitted.
  const int64_t submitted = NowNs();
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
