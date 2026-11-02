#pragma once

#include <algorithm>
#include <array>
#include <chrono>
#include <mutex>
#include <thread>

namespace tabletennis::frames {
using Clock = std::chrono::steady_clock;

// One producer-side limiter. A fixed deadline avoids adding the previous
// frame's sleep overshoot to every following interval. Never catch up a stall
// by submitting a burst of frames, and never sleep on the UI/presentation thread.
class Schedule {
 public:
  Clock::time_point Next(Clock::time_point now, int fps) {
    if (fps <= 0) { rate_ = 0; return now; }
    auto period = std::chrono::nanoseconds(1'000'000'000LL / fps);
    if (rate_ != fps || now >= deadline_ + period) {
      rate_ = fps;
      deadline_ = now;
    }
    auto result = std::max(now, deadline_);
    deadline_ += period;
    return result;
  }
 private:
  int rate_ = 0;
  Clock::time_point deadline_{};
};

struct Sample {
  double fps = 0, mean_ms = 0, p95_ms = 0, max_ms = 0;
  size_t intervals = 0;
};

class Statistics {
 public:
  void Record(Clock::time_point now) {
    std::lock_guard lock(mutex_);
    if (has_previous_) {
      intervals_[write_] = std::chrono::duration<double, std::milli>(now - previous_).count();
      write_ = (write_ + 1) % intervals_.size();
      count_ = std::min(count_ + 1, intervals_.size());
    }
    previous_ = now;
    has_previous_ = true;
  }
  Sample Read(Clock::time_point now) const {
    std::lock_guard lock(mutex_);
    Sample out;
    if (!count_ || now - previous_ > std::chrono::seconds(1)) return out;
    auto sorted = intervals_;
    std::sort(sorted.begin(), sorted.begin() + count_);
    double sum = 0;
    for (size_t i = 0; i < count_; ++i) sum += sorted[i];
    out.intervals = count_;
    out.mean_ms = sum / count_;
    out.fps = sum > 0 ? 1000.0 * count_ / sum : 0;
    out.p95_ms = sorted[(count_ * 95 + 99) / 100 - 1];
    out.max_ms = sorted[count_ - 1];
    return out;
  }
 private:
  mutable std::mutex mutex_;
  std::array<double, 120> intervals_{};
  size_t write_ = 0, count_ = 0;
  bool has_previous_ = false;
  Clock::time_point previous_{};
};

inline Statistics submissions;

inline void BeforeSubmit(int fps) {
  static thread_local Schedule schedule;
  auto deadline = schedule.Next(Clock::now(), fps);
  // A short final yield bounds sleep overshoot without a full-frame busy wait.
  auto coarse = deadline - std::chrono::microseconds(300);
  if (Clock::now() < coarse) std::this_thread::sleep_until(coarse);
  while (Clock::now() < deadline) std::this_thread::yield();
}
}  // namespace tabletennis::frames
