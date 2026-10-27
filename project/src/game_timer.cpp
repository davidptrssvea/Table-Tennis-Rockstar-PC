// game_timer.cpp - Game frame timer step length

#include "game_timer.h"

#include <algorithm>
#include <atomic>
#include <cstring>
#include <mutex>
#include <unordered_map>

namespace tabletennis::timing {
namespace {
std::atomic<bool> g_scaled{false};
std::atomic<double> g_frame_seconds{1.0 / 60.0};
std::mutex g_mutex;
// Timers whose step was replaced, with their original +28 bits.
std::unordered_map<uint32_t, uint32_t> g_original;

uint32_t LoadBE(uint8_t* base, uint32_t address) {
  uint32_t v;
  std::memcpy(&v, base + address, 4);
  return __builtin_bswap32(v);
}
void StoreBE(uint8_t* base, uint32_t address, uint32_t value) {
  value = __builtin_bswap32(value);
  std::memcpy(base + address, &value, 4);
}
float AsFloat(uint32_t bits) { float f; std::memcpy(&f, &bits, 4); return f; }
uint32_t AsBits(float f) { uint32_t b; std::memcpy(&b, &f, 4); return b; }
}  // namespace

void SetScaledStep(bool scaled) { g_scaled = scaled; }

void SetFrameSeconds(double seconds) { g_frame_seconds = seconds; }

void RestoreNow(uint8_t* base) {
  g_scaled = false;
  std::lock_guard lock(g_mutex);
  for (const auto& [timer, original] : g_original) {
    const float step = AsFloat(original);
    const float scale = AsFloat(LoadBE(base, timer + 80));
    StoreBE(base, timer + 28, original);
    StoreBE(base, timer + 84, original);
    StoreBE(base, timer + 8, AsBits(step * scale));
  }
  g_original.clear();
}

bool StepOverride(uint32_t timer, uint32_t fixed_mode, uint32_t current_bits, uint32_t* step_bits) {
  std::lock_guard lock(g_mutex);
  if (g_scaled.load(std::memory_order_relaxed) && fixed_mode != 0) {
    g_original.try_emplace(timer, current_bits);
    // Same bounds the game applies in its variable-step mode (0.1-100 ms).
    const float seconds = float(std::clamp(g_frame_seconds.load(), 0.0001, 0.1));
    std::memcpy(step_bits, &seconds, sizeof(seconds));
    return true;
  }
  auto it = g_original.find(timer);
  if (it == g_original.end()) {
    return false;
  }
  *step_bits = it->second;
  g_original.erase(it);
  return true;
}

}  // namespace tabletennis::timing
