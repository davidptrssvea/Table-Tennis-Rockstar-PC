#include "frame_schedule.h"
#include <cassert>
int main() {
  using tabletennis::pacing::Schedule;
  constexpr int64_t period = 1'000'000'000LL / 60;
  Schedule s;
  assert(s.Next(0, 60) == period);
  assert(s.Next(period + 100'000, 60) == 2 * period);
  // CPU work and small wake overshoot don't accumulate drift.
  for (int64_t i = 3; i < 1000; ++i)
    assert(s.Next((i - 1) * period + (i % 5) * 100'000, 60) == i * period);
  // Work that misses one deadline must not cause a catch-up burst.
  Schedule late;
  assert(late.Next(0, 60) == period);
  const auto missed = 2 * period + 5'000'000;
  assert(late.Next(missed, 60) == missed);
  assert(late.Next(missed + 8'000'000, 60) == missed + period);
  assert(late.Next(missed + period + 8'000'000, 60) == missed + 2 * period);
  // Long stalls submit immediately, with no additional full-period wait.
  const int64_t stall = 2000 * period;
  assert(s.Next(stall, 60) == stall);
  assert(s.Next(stall + 1'000'000, 60) == stall + period);
  assert(s.Next(stall + period, 60) == stall + 2 * period);
  assert(s.Next(stall + 2 * period, 120) == stall + 2 * period + 1'000'000'000LL / 120);
  assert(s.Next(stall + 3 * period, 0) == stall + 3 * period);
  assert(s.Next(stall + 4 * period, 60) == stall + 5 * period);
}
