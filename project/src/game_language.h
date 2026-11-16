#pragma once
#include <cstdint>
#include <string_view>

namespace tabletennis::language {
// Xbox system language IDs, not the game's internal localization indices.
constexpr uint32_t XboxId(std::string_view code) {
  if (code == "de") return 3;
  if (code == "fr") return 4;
  if (code == "es") return 5;
  if (code == "it") return 6;
  return 1; // English, including invalid or unavailable languages.
}
}
