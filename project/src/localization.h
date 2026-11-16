#pragma once
#include <algorithm>
#include <atomic>
#include <iterator>
#include <string_view>

namespace tabletennis::l10n {
inline std::atomic<unsigned> language{0};
inline void SetLanguage(std::string_view code) {
  language.store(code=="fr"?1:code=="de"?2:code=="es"?3:code=="it"?4:0);
}
struct Entry { const char* text[5]; };
inline constexpr Entry entries[] = {
#include "localization_data.inc"
};
inline const char* T(const char* english) {
  const auto index=language.load(std::memory_order_relaxed);
  if(!english || !index) return english;
  auto at=std::lower_bound(std::begin(entries),std::end(entries),std::string_view(english),
      [](const Entry& entry,std::string_view text){return std::string_view(entry.text[0])<text;});
  return at!=std::end(entries) && std::string_view(at->text[0])==english ? at->text[index] : english;
}
}
