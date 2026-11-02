#pragma once
#include <cstdint>

namespace tabletennis::pacing {
// Absolute deadlines from the PR limiter, separated from OS waits for testing.
class Schedule {
 public:
  int64_t Next(int64_t now, int32_t fps) {
    if (fps <= 0) { initialized_ = false; return now; }
    const int64_t period = 1'000'000'000LL / fps;
    if (!initialized_ || period != period_ || now - deadline_ > period * 2) {
      period_ = period; deadline_ = now; initialized_ = true;
    }
    deadline_ += period;
    return deadline_;
  }
 private:
  int64_t period_ = 0, deadline_ = 0;
  bool initialized_ = false;
};
}  // namespace tabletennis::pacing
