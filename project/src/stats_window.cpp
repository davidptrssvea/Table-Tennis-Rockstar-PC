// stats_window.cpp - Frame-time overlay drawn in its own window

#include "stats_window.h"

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include "frame_pacer.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <thread>

namespace tabletennis::stats_window {
namespace {

constexpr int kWidth = 560;
constexpr int kHeight = 214;
constexpr int kMargin = 16;
constexpr UINT_PTR kTimerId = 1;
constexpr UINT kRefreshMs = 100;
constexpr UINT kShowMessage = WM_APP + 1;
constexpr UINT kHideMessage = WM_APP + 2;

// Graph range: the 60 FPS budget +/- 8.3 ms, so 1-2 ms deviations are visible.
constexpr float kTargetMs = 1000.0f / 60.0f;
constexpr float kMinMs = kTargetMs - 8.33f;
constexpr float kMaxMs = kTargetMs + 8.33f;

std::atomic<HWND> g_window{nullptr};
std::atomic<HWND> g_game{nullptr};
std::atomic<bool> g_visible{false};

COLORREF FpsColor(float fps) {
  return fps >= 115.0f ? RGB(0, 230, 255)
         : fps >= 55.0f ? RGB(60, 230, 110)
         : fps >= 30.0f ? RGB(255, 215, 60)
                        : RGB(255, 80, 80);
}

COLORREF DeviationColor(float ms) {
  const float d = std::abs(ms - kTargetMs);
  return d <= 2.0f ? RGB(60, 230, 110) : d <= 4.0f ? RGB(255, 210, 60) : RGB(255, 70, 70);
}

void Text(HDC dc, int x, int y, COLORREF color, const char* text) {
  SetTextColor(dc, color);
  TextOutA(dc, x, y, text, int(std::strlen(text)));
}

void Line(HDC dc, int x0, int y0, int x1, int y1, COLORREF color, int width = 1) {
  HPEN pen = CreatePen(PS_SOLID, width, color);
  HGDIOBJ old = SelectObject(dc, pen);
  MoveToEx(dc, x0, y0, nullptr);
  LineTo(dc, x1, y1);
  SelectObject(dc, old);
  DeleteObject(pen);
}

void Paint(HDC dc) {
  namespace pacing = tabletennis::pacing;
  RECT all{0, 0, kWidth, kHeight};
  HBRUSH background = CreateSolidBrush(RGB(14, 18, 28));
  FillRect(dc, &all, background);
  DeleteObject(background);
  SetBkMode(dc, TRANSPARENT);
  HFONT font = CreateFontA(-14, 0, 0, 0, FW_SEMIBOLD, 0, 0, 0, DEFAULT_CHARSET, 0, 0,
                           CLEARTYPE_QUALITY, FIXED_PITCH, "Consolas");
  HGDIOBJ old_font = SelectObject(dc, font);

  const auto stats = pacing::GetStats();
  char line[160];
  std::snprintf(line, sizeof(line), "FPS %5.1f", stats.game.fps);
  Text(dc, 10, 6, FpsColor(stats.game.fps), line);
  std::snprintf(line, sizeof(line), "game   avg %5.2f  1%% %5.2f  worst %6.2f ms",
                stats.game.avg_ms, stats.game.p99_ms, stats.game.max_ms);
  Text(dc, 100, 6, RGB(200, 205, 215), line);
  std::snprintf(line, sizeof(line), "display avg %5.2f  1%% %5.2f  worst %6.2f ms",
                stats.present.avg_ms, stats.present.p99_ms, stats.present.max_ms);
  Text(dc, 92, 24, RGB(150, 155, 170), line);

  // Graph of game frame times.
  const int gx0 = 10, gy0 = 46, gx1 = kWidth - 48, gy1 = kHeight - 26;
  auto y_of = [&](float ms) {
    const float t = (std::clamp(ms, kMinMs, kMaxMs) - kMinMs) / (kMaxMs - kMinMs);
    return int(std::lround(gy1 - t * (gy1 - gy0)));
  };
  RECT plot{gx0, gy0, gx1, gy1};
  HBRUSH plot_brush = CreateSolidBrush(RGB(6, 8, 12));
  FillRect(dc, &plot, plot_brush);
  DeleteObject(plot_brush);
  RECT band{gx0, y_of(kTargetMs + 2.0f), gx1, y_of(kTargetMs - 2.0f)};
  HBRUSH band_brush = CreateSolidBrush(RGB(16, 46, 26));
  FillRect(dc, &band, band_brush);
  DeleteObject(band_brush);
  Line(dc, gx0, y_of(kTargetMs + 4.0f), gx1, y_of(kTargetMs + 4.0f), RGB(45, 50, 60));
  Line(dc, gx0, y_of(kTargetMs - 4.0f), gx1, y_of(kTargetMs - 4.0f), RGB(45, 50, 60));
  Line(dc, gx0, y_of(kTargetMs), gx1, y_of(kTargetMs), RGB(130, 135, 145));
  struct Label { float ms; const char* text; } labels[] = {
      {kMaxMs, "25.0"}, {kTargetMs + 4.0f, "20.7"}, {kTargetMs, "16.7"},
      {kTargetMs - 4.0f, "12.7"}, {kMinMs, " 8.3"}};
  for (const auto& label : labels) {
    Text(dc, gx1 + 6, y_of(label.ms) - 8, RGB(150, 155, 170), label.text);
  }

  float history[pacing::kHistoryFrames];
  const size_t count = pacing::CopyHistory(pacing::Series::kGame, history, pacing::kHistoryFrames);
  if (count >= 2) {
    const float step = float(gx1 - gx0) / float(pacing::kHistoryFrames - 1);
    const float x_start = float(gx1) - step * float(count - 1);
    float worst = 0.0f;
    for (size_t i = 0; i < count; ++i) {
      worst = std::max(worst, history[i]);
      const int x = int(x_start + step * float(i));
      if (i > 0) {
        Line(dc, int(x_start + step * float(i - 1)), y_of(history[i - 1]), x, y_of(history[i]),
             DeviationColor(history[i]), 2);
      }
      if (history[i] > kMaxMs || history[i] < kMinMs) {
        // Off-scale spike: clamped to the edge and marked.
        const int y = history[i] > kMaxMs ? gy0 + 3 : gy1 - 3;
        RECT mark{x - 3, y - 3, x + 4, y + 4};
        HBRUSH red = CreateSolidBrush(RGB(255, 70, 70));
        FillRect(dc, &mark, red);
        DeleteObject(red);
      }
    }
    if (worst > kMaxMs) {
      std::snprintf(line, sizeof(line), "spike %.1f ms", worst);
      Text(dc, gx0 + 6, gy0 + 4, RGB(255, 110, 110), line);
    }
  }
  std::snprintf(line, sizeof(line), "game frame time, last %zu frames   green = 16.7 +/- 2 ms",
                count);
  Text(dc, 10, kHeight - 21, RGB(120, 125, 140), line);

  SelectObject(dc, old_font);
  DeleteObject(font);
}

void UpdatePlacement(HWND hwnd) {
  HWND game = g_game.load();
  const bool show = g_visible.load() && game && IsWindowVisible(game) && !IsIconic(game) &&
                    GetForegroundWindow() == game;
  if (!show) {
    ShowWindow(hwnd, SW_HIDE);
    return;
  }
  POINT origin{kMargin, kMargin};
  ClientToScreen(game, &origin);
  SetWindowPos(hwnd, HWND_TOPMOST, origin.x, origin.y, kWidth, kHeight,
               SWP_NOACTIVATE | SWP_SHOWWINDOW);
}

LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
  switch (message) {
    case WM_TIMER:
    case kShowMessage:
    case kHideMessage:
      UpdatePlacement(hwnd);
      if (IsWindowVisible(hwnd)) {
        InvalidateRect(hwnd, nullptr, FALSE);
      }
      return 0;
    case WM_PAINT: {
      PAINTSTRUCT ps;
      HDC dc = BeginPaint(hwnd, &ps);
      // Double-buffered to avoid flicker.
      HDC mem = CreateCompatibleDC(dc);
      HBITMAP bitmap = CreateCompatibleBitmap(dc, kWidth, kHeight);
      HGDIOBJ old = SelectObject(mem, bitmap);
      Paint(mem);
      BitBlt(dc, 0, 0, kWidth, kHeight, mem, 0, 0, SRCCOPY);
      SelectObject(mem, old);
      DeleteObject(bitmap);
      DeleteDC(mem);
      EndPaint(hwnd, &ps);
      return 0;
    }
    case WM_NCHITTEST:
      return HTTRANSPARENT;
  }
  return DefWindowProcW(hwnd, message, wparam, lparam);
}

void ThreadMain() {
  WNDCLASSW wc{};
  wc.lpfnWndProc = WindowProc;
  wc.hInstance = GetModuleHandleW(nullptr);
  wc.lpszClassName = L"TabletennisStatsOverlay";
  wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
  RegisterClassW(&wc);
  HWND hwnd = CreateWindowExW(
      WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
      wc.lpszClassName, L"", WS_POPUP, 0, 0, kWidth, kHeight, nullptr, nullptr, wc.hInstance,
      nullptr);
  if (!hwnd) {
    return;
  }
  SetLayeredWindowAttributes(hwnd, 0, 225, LWA_ALPHA);
  g_window = hwnd;
  SetTimer(hwnd, kTimerId, kRefreshMs, nullptr);
  UpdatePlacement(hwnd);
  MSG msg;
  while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
    DispatchMessageW(&msg);
  }
}

}  // namespace

void Show(void* game_window) {
  g_game = static_cast<HWND>(game_window);
  g_visible = true;
  if (HWND hwnd = g_window.load()) {
    PostMessageW(hwnd, kShowMessage, 0, 0);
    return;
  }
  static std::atomic<bool> started{false};
  if (!started.exchange(true)) {
    std::thread(ThreadMain).detach();
  }
}

void Hide() {
  g_visible = false;
  if (HWND hwnd = g_window.load()) {
    PostMessageW(hwnd, kHideMessage, 0, 0);
  }
}

bool IsVisible() { return g_visible.load(); }

}  // namespace tabletennis::stats_window
