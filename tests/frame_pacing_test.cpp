#include "frame_pacing.h"
#include <cassert>
#include <cmath>
using namespace tabletennis::frames;
int main() {
  Schedule schedule;
  auto start = Clock::time_point{} + std::chrono::seconds(1);
  auto period = std::chrono::nanoseconds(1'000'000'000LL / 120);
  assert(schedule.Next(start, 120) == start);
  // Small scheduler overshoot must not shift all following frame deadlines.
  assert(schedule.Next(start + period + std::chrono::microseconds(50), 120) ==
         start + period + std::chrono::microseconds(50));
  assert(schedule.Next(start + period + std::chrono::milliseconds(1), 120) == start + 2 * period);
  auto stall = start + std::chrono::seconds(2);
  assert(schedule.Next(stall, 120) == stall);
  assert(schedule.Next(stall, 120) == stall + period);
  assert(schedule.Next(stall, 0) == stall);
  assert(schedule.Next(stall, 60) == stall);
  Statistics stats;
  assert(stats.Read(start).fps == 0);
  for (int i = 0; i <= 120; ++i) stats.Record(start + i * period);
  auto sample = stats.Read(start + 120 * period);
  assert(sample.intervals == 120);
  assert(std::abs(sample.fps - 120) < .001);
  assert(std::abs(sample.p95_ms - 1000.0 / 120) < .001);
  assert(stats.Read(start + std::chrono::seconds(4)).fps == 0);
  stats.Record(start + 120 * period + std::chrono::milliseconds(50));
  sample = stats.Read(start + 120 * period + std::chrono::milliseconds(50));
  assert(std::abs(sample.max_ms - 50) < .001);
  assert(sample.fps < 120);
}
