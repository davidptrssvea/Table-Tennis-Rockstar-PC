#pragma once
#include <algorithm>
#include <cmath>

namespace tabletennis::photo::camera {
// Optical zoom changes the field of view while keeping camera position fixed.
inline float ZoomFov(float degrees, float magnification) {
  if (!std::isfinite(degrees) || degrees <= 0 || degrees >= 180) return degrees;
  if (!std::isfinite(magnification)) magnification = 1;
  magnification = std::clamp(magnification, 0.5f, 5.0f);
  if (magnification == 1) return degrees;
  constexpr double radians = 3.14159265358979323846 / 180;
  return float(std::clamp(2 * std::atan(std::tan(degrees * radians / 2) /
                                      magnification) / radians, 5.0, 150.0));
}
inline float ZoomStep(float current, float steps) {
  return std::clamp(current * std::exp(steps * 0.15f), 0.5f, 5.0f);
}
}
