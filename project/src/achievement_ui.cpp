// achievement_ui.cpp - see achievement_ui.h.

#include "generated/default/tabletennis_init.h"
#include "achievement_ui.h"
#include "button_prompts.h"
#include "pc_features.h"
#include "intro_skip.h"
#include "frame_pacer.h"

#include <SDL3/SDL.h>
#include <imgui.h>
#include <rex/cvar.h>
#include <rex/logging.h>
#include <rex/runtime.h>
#include <rex/ui/immediate_drawer.h>
#include <rex/ui/keybinds.h>
#include <rex/ui/ui_event.h>
#include <rex/ui/virtual_key.h>
#include <rex/ui/window.h>

#include <algorithm>
#include <array>
#include <cfloat>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <random>
#include <ctime>
#include <string_view>

REXCVAR_DEFINE_BOOL(achievement_sound, true, "UI", "Play a chime when an achievement unlocks");

namespace tabletennis::achievement_ui {
namespace {

// ---------------------------------------------------------------------------------------------
// Unlock sound, synthesized here (nothing comes from the game): a soft whoosh while the pill
// slides in, two ping-pong ball bounces as it lands, then a quick marimba-like roll up a G major
// chord over a low thump. Timed to the default animation and panned towards the popup's side.
// ---------------------------------------------------------------------------------------------
constexpr int kChimeRate = 48000;
constexpr float kChimePi = 3.14159265f;

std::vector<float> SynthesizeChime(float pan) {
  constexpr float kLength = 1.6f;
  const int frames = int(kLength * kChimeRate);
  std::vector<float> mono(frames, 0.0f);
  std::minstd_rand rng(0x7AB1E);
  std::uniform_real_distribution<float> noise(-1.0f, 1.0f);
  auto mix = [&](float start, int count, auto&& sample) {
    const int first = int(start * kChimeRate);
    for (int i = 0; i < count && first + i < frames; ++i) {
      mono[first + i] += sample(float(i) / kChimeRate);
    }
  };
  auto seconds = [](float s) { return int(s * kChimeRate); };

  // Whoosh: noise through a low-pass whose cutoff rises from 400 Hz to 4 kHz.
  {
    const int count = seconds(0.28f);
    float low = 0.0f;
    for (int i = 0; i < count; ++i) {
      const float u = float(i) / float(count);
      const float k = 1.0f - std::exp(-2.0f * kChimePi * (400.0f + 3600.0f * u) / kChimeRate);
      low += k * (noise(rng) - low);
      const float bell = std::sin(kChimePi * u);
      mono[i] += 0.25f * 3.0f * bell * bell * low;
    }
  }
  // Ball bounce: a hollow 1.65/2.9 kHz body with a short noisy click.
  auto pock = [&](float start, float gain) {
    mix(start, seconds(0.05f), [&](float t) {
      const float body = std::exp(-t / 0.008f) * (std::sin(2.0f * kChimePi * 1650.0f * t) +
                                                   0.6f * std::sin(2.0f * kChimePi * 2900.0f * t));
      const float click = std::exp(-t / 0.0015f) * noise(rng);
      return gain * (0.7f * body + 0.6f * click);
    });
  };
  pock(0.18f, 0.9f);
  pock(0.30f, 0.7f);
  // Mallet: fundamental plus a fast-fading 3.9x partial and a tiny strike click.
  auto mallet = [&](float start, float freq, float gain, float decay) {
    mix(start, seconds(0.9f), [=](float t) {
      const float env = std::min(1.0f, t / 0.003f) * std::exp(-t / decay);
      return gain * env *
             (std::sin(2.0f * kChimePi * freq * t) +
              0.25f * std::exp(-t / 0.05f) * std::sin(2.0f * kChimePi * freq * 3.9f * t) +
              0.08f * std::exp(-t / 0.01f) * std::sin(2.0f * kChimePi * freq * 9.2f * t));
    });
  };
  const float roll[4] = {392.00f, 493.88f, 587.33f, 783.99f};  // G4 B4 D5 G5
  for (int i = 0; i < 4; ++i) {
    mallet(0.40f + i * 0.045f, roll[i], i < 3 ? 0.55f : 0.75f, i < 3 ? 0.30f : 0.45f);
  }
  // Low thump under the chord: a 98 Hz tone with a quick downward pitch drop.
  {
    float phase = 0.0f;
    mix(0.40f, seconds(0.25f), [&](float t) {
      phase += 2.0f * kChimePi * 98.0f * (1.0f + 1.5f * std::exp(-t / 0.02f)) / kChimeRate;
      return 0.5f * std::exp(-t / 0.07f) * std::sin(phase);
    });
  }

  float peak = 0.0f;
  for (float v : mono) peak = std::max(peak, std::fabs(v));
  const float gain = peak > 0.0f ? kToast.sound_volume / peak : 0.0f;
  const float left = std::sqrt(0.5f * (1.0f - pan)), right = std::sqrt(0.5f * (1.0f + pan));
  std::vector<float> stereo(frames * 2);
  for (int i = 0; i < frames; ++i) {
    stereo[2 * i] = mono[i] * gain * left;
    stereo[2 * i + 1] = mono[i] * gain * right;
  }
  return stereo;
}

void PlayUnlockChime() {
  if (!REXCVAR_GET(achievement_sound) || kToast.sound_volume <= 0.0f) return;
  static SDL_AudioStream* stream = nullptr;
  static bool failed = false;
  if (failed) return;
  if (!stream) {
    if (!SDL_InitSubSystem(SDL_INIT_AUDIO)) {
      REXLOG_WARN("Tabletennis: achievement chime disabled, no audio: {}", SDL_GetError());
      failed = true;
      return;
    }
    const SDL_AudioSpec spec{SDL_AUDIO_F32, 2, kChimeRate};
    stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, nullptr, nullptr);
    if (!stream) {
      REXLOG_WARN("Tabletennis: achievement chime disabled, cannot open audio: {}", SDL_GetError());
      failed = true;
      return;
    }
    SDL_ResumeAudioStreamDevice(stream);
  }
  const bool right_side = kToast.anchor == ToastAnchor::kTopRight ||
                          kToast.anchor == ToastAnchor::kBottomRight;
  const bool left_side = kToast.anchor == ToastAnchor::kTopLeft ||
                         kToast.anchor == ToastAnchor::kBottomLeft;
  static const std::vector<float> chime = SynthesizeChime(right_side ? 0.3f : left_side ? -0.3f : 0.0f);
  SDL_PutAudioStreamData(stream, chime.data(), int(chime.size() * sizeof(float)));
}

// The rectangle the game picture occupies in the window: the presenter letterboxes the
// game's 16:9 frame (or the whole window when FILL stretches it to a narrower screen) into the window.
struct PictureRect {
  float x, y, w, h;
};
PictureRect GamePicture(const ImGuiIO& io) {
  const float aspect = float(tabletennis::pc::OverlayAspect());
  const float w = std::min(io.DisplaySize.x, io.DisplaySize.y * aspect);
  const float h = w / aspect;
  return {(io.DisplaySize.x - w) * 0.5f, (io.DisplaySize.y - h) * 0.5f, w, h};
}

using Clock = std::chrono::steady_clock;
constexpr float kPi = 3.14159265f;

// --- Palette, picked from the game's menus and HUD --------------------------------------------
ImU32 Col(int r, int g, int b, float a = 1.0f) {
  return IM_COL32(r, g, b, int(std::clamp(a, 0.0f, 1.0f) * 255.0f + 0.5f));
}
ImU32 Navy(float a = 1.0f) { return Col(10, 24, 44, a); }
ImU32 NavyLight(float a = 1.0f) { return Col(26, 58, 100, a); }
ImU32 Blue(float a = 1.0f) { return Col(56, 138, 205, a); }
ImU32 BlueLight(float a = 1.0f) { return Col(128, 194, 238, a); }
ImU32 Yellow(float a = 1.0f) { return Col(255, 204, 0, a); }
ImU32 Outline(float a = 1.0f) { return Col(16, 18, 28, a); }
ImU32 White(float a = 1.0f) { return Col(255, 255, 255, a); }
ImU32 Grey(float a = 1.0f) { return Col(158, 164, 174, a); }
ImU32 PanelDark(float a = 1.0f) { return Col(5, 9, 16, a); }
ImU32 FooterLight(float a = 1.0f) { return Col(212, 215, 220, a); }
ImU32 FooterText(float a = 1.0f) { return Col(28, 30, 36, a); }
ImU32 GreenA(float a = 1.0f) { return Col(90, 186, 40, a); }
ImU32 RedB(float a = 1.0f) { return Col(210, 44, 44, a); }

// Horizontal squeeze that gives the heavy face the game's condensed headline look.
constexpr float kCondensed = 0.84f;

// --- Fonts ------------------------------------------------------------------------------------
constexpr float kHeavyLargePx = 56.0f;
constexpr float kHeavySmallPx = 28.0f;
constexpr float kBodyPx = 20.0f;

struct FontSet {
  ImFont* heavy_large = nullptr;
  ImFont* heavy_small = nullptr;
  ImFont* body = nullptr;
} g_fonts;

// The ImGui drawer bakes glyphs once, so each face is baked at fixed sizes and text is drawn
// from the nearest bake that is not smaller than the wanted size (downscaling stays crisp).
ImFont* Heavy(float px) {
  if (g_fonts.heavy_small && (px <= kHeavySmallPx * 1.15f || !g_fonts.heavy_large)) {
    return g_fonts.heavy_small;
  }
  if (g_fonts.heavy_large) return g_fonts.heavy_large;
  return ImGui::GetFont();
}
ImFont* Body() { return g_fonts.body ? g_fonts.body : ImGui::GetFont(); }

ImFont* LoadFirst(ImFontAtlas* atlas, const std::vector<std::filesystem::path>& candidates,
                  float px, const char* purpose) {
  ImFontConfig config;
  config.OversampleH = 2;
  config.OversampleV = 1;
  config.PixelSnapH = false;
  static const ImWchar ranges[] = {0x20, 0xFF, 0x2013, 0x2026, 0};
  for (const auto& path : candidates) {
    std::error_code ec;
    if (!std::filesystem::exists(path, ec)) continue;
    if (ImFont* font = atlas->AddFontFromFileTTF(path.string().c_str(), px, &config, ranges)) {
      REXLOG_INFO("Tabletennis: achievement UI {} font: {} ({}px)", purpose, path.string(), px);
      return font;
    }
  }
  REXLOG_WARN("Tabletennis: no {} font found for the achievement UI; using the ImGui default",
              purpose);
  return nullptr;
}

// --- Text helpers -----------------------------------------------------------------------------
// Caps sit between roughly 26% and 78% of the line height in Noto Sans; this returns the text
// origin that centers capitals on `center_y`.
float CapsTop(float center_y, float px) { return center_y - 0.525f * px; }

float TextWidth(ImFont* font, float px, const char* text, float x_scale = 1.0f) {
  return font->CalcTextSizeA(px, FLT_MAX, 0.0f, text).x * x_scale;
}

// Draws text, then squeezes the glyphs it added horizontally around the text origin.
void AddText(ImDrawList* dl, ImFont* font, float px, ImVec2 pos, ImU32 col, const char* text,
             float x_scale = 1.0f, float wrap_width = 0.0f) {
  const int first = dl->VtxBuffer.Size;
  dl->AddText(font, px, pos, col, text, nullptr, wrap_width > 0.0f ? wrap_width / x_scale : 0.0f);
  if (x_scale != 1.0f) {
    for (int i = first; i < dl->VtxBuffer.Size; ++i) {
      auto& v = dl->VtxBuffer[i];
      v.pos.x = pos.x + (v.pos.x - pos.x) * x_scale;
    }
  }
}

void AddOutlinedText(ImDrawList* dl, ImFont* font, float px, ImVec2 pos, ImU32 col,
                     ImU32 outline_col, float outline, const char* text, float x_scale = 1.0f) {
  if (outline > 0.0f) {
    static const float kDirs[8][2] = {{1, 0}, {-1, 0}, {0, 1}, {0, -1},
                                      {0.7f, 0.7f}, {-0.7f, 0.7f}, {0.7f, -0.7f}, {-0.7f, -0.7f}};
    for (const auto& d : kDirs) {
      AddText(dl, font, px, ImVec2(pos.x + d[0] * outline, pos.y + d[1] * outline), outline_col,
              text, x_scale);
    }
  }
  AddText(dl, font, px, pos, col, text, x_scale);
}

std::string Upper(std::string_view text) {
  std::string out(text);
  for (auto& c : out) {
    if (c >= 'a' && c <= 'z') c = char(c - 'a' + 'A');
  }
  return out;
}

// Shortens `text` with an ellipsis until it fits `max_width`.
std::string Fit(ImFont* font, float px, float x_scale, float max_width, std::string text) {
  if (TextWidth(font, px, text.c_str(), x_scale) <= max_width) return text;
  while (!text.empty()) {
    text.pop_back();
    while (!text.empty() && (uint8_t(text.back()) & 0xC0) == 0x80) text.pop_back();
    std::string candidate = text + "...";
    if (TextWidth(font, px, candidate.c_str(), x_scale) <= max_width) return candidate;
  }
  return text;
}

std::string FormatDate(uint64_t filetime) {
  if (filetime == 0) return {};
  constexpr uint64_t kEpochDelta = 116'444'736'000'000'000ULL;
  const std::time_t seconds = std::time_t((filetime - kEpochDelta) / 10'000'000ULL);
  std::tm tm{};
#if defined(_WIN32)
  localtime_s(&tm, &seconds);
#else
  localtime_r(&seconds, &tm);
#endif
  char buffer[32];
  std::strftime(buffer, sizeof(buffer), "%d %b %Y", &tm);
  return Upper(buffer);
}

std::vector<uint32_t> ParseIdList(const char* env_name) {
  std::vector<uint32_t> ids;
  const char* value = std::getenv(env_name);
  if (!value) return ids;
  const char* p = value;
  while (*p) {
    char* end = nullptr;
    const long id = std::strtol(p, &end, 10);
    if (end == p) {
      ++p;
      continue;
    }
    if (id > 0) ids.push_back(uint32_t(id));
    p = end;
  }
  return ids;
}

// --- Easing -----------------------------------------------------------------------------------
float Clamp01(float x) { return std::clamp(x, 0.0f, 1.0f); }
float Stage(float t, float start, float duration) { return Clamp01((t - start) / duration); }
float EaseOutCubic(float x) { return 1.0f - (1.0f - x) * (1.0f - x) * (1.0f - x); }
float EaseInCubic(float x) { return x * x * x; }
float EaseInOutSine(float x) { return -(std::cos(kPi * x) - 1.0f) * 0.5f; }
float EaseOutBack(float x) {
  const float c1 = 1.70158f;
  const float c3 = c1 + 1.0f;
  const float y = x - 1.0f;
  return 1.0f + c3 * y * y * y + c1 * y * y;
}

// --- Shapes -----------------------------------------------------------------------------------
// Halftone dots that grow from nothing at the left edge of the box to full size at the right,
// like the blue swooshes behind the game's menu headers. `round_right` keeps the dots inside a
// right end rounded with that radius; `phase` scrolls the pattern.
void Halftone(ImDrawList* dl, ImVec2 min, ImVec2 max, ImU32 col, float spacing, float phase,
              float round_right = 0.0f) {
  const float width = max.x - min.x;
  if (width <= 0.0f || spacing <= 1.0f) return;
  const float cy = (min.y + max.y) * 0.5f;
  const float offset = std::fmod(phase, spacing * 2.0f);
  int row = 0;
  for (float y = min.y + spacing * 0.5f; y < max.y; y += spacing * 0.8f, ++row) {
    const float stagger = (row & 1) ? spacing * 0.5f : 0.0f;
    for (float x = min.x - spacing * 2.0f + stagger + offset; x < max.x + spacing; x += spacing) {
      const float u = Clamp01((x - min.x) / width);
      const float grow = u * u * (3.0f - 2.0f * u);
      const float radius = spacing * 0.40f * grow;
      if (radius < 0.5f) continue;
      if (round_right > 0.0f && x > max.x - round_right) {
        const float dx = x - (max.x - round_right);
        const float dy = y - cy;
        if (dx * dx + dy * dy > (round_right - radius) * (round_right - radius)) continue;
      }
      dl->AddCircleFilled(ImVec2(x, y), radius, col, 10);
    }
  }
}

// Xbox-style face button: dark ring, coloured disc, letter.
void ButtonGlyph(ImDrawList* dl, ImVec2 center, float radius, ImU32 fill, const char* letter,
                 float alpha) {
  tabletennis::prompts::DrawButton(dl,center,radius,fill,letter,Heavy(radius*1.65f),alpha);
}

// Gamerscore as the game shows points: a heavy outlined number and a white "G" disc.
// Right-aligned at `right_x`, centred on `center_y`. Returns the total width.
float GamerscoreBadge(ImDrawList* dl, float right_x, float center_y, float px, int score,
                      float alpha, bool draw = true) {
  char number[16];
  std::snprintf(number, sizeof(number), "%d", score);
  ImFont* font = Heavy(px);
  const float disc_r = px * 0.40f;
  const float gap = px * 0.22f;
  const float number_w = TextWidth(font, px, number, kCondensed);
  const float total = number_w + gap + disc_r * 2.0f;
  if (!draw) return total;
  const float x = right_x - total;
  AddOutlinedText(dl, font, px, ImVec2(x, CapsTop(center_y, px)), Yellow(alpha), Outline(alpha),
                  px * 0.08f, number, kCondensed);
  const ImVec2 disc(right_x - disc_r, center_y);
  dl->AddCircleFilled(disc, disc_r, Outline(alpha), 24);
  dl->AddCircleFilled(disc, disc_r * 0.88f, White(alpha), 24);
  const float g_px = px * 0.66f;
  ImFont* g_font = Heavy(g_px);
  const float g_w = TextWidth(g_font, g_px, "G");
  AddText(dl, g_font, g_px, ImVec2(disc.x - g_w * 0.5f, CapsTop(disc.y, g_px)),
          Col(24, 36, 60, alpha), "G");
  return total;
}

void Padlock(ImDrawList* dl, ImVec2 center, float size, ImU32 col) {
  const float body_w = size;
  const float body_h = size * 0.78f;
  const ImVec2 body_min(center.x - body_w * 0.5f, center.y - body_h * 0.1f);
  const ImVec2 body_max(center.x + body_w * 0.5f, center.y + body_h * 0.9f);
  dl->AddRectFilled(body_min, body_max, col, size * 0.14f);
  const float r = size * 0.32f;
  const ImVec2 arc_center(center.x, body_min.y);
  dl->PathArcTo(arc_center, r, kPi, kPi * 2.0f, 16);
  dl->PathStroke(col, ImDrawFlags_None, size * 0.16f);
}

void Triangle(ImDrawList* dl, ImVec2 center, float size, bool up, ImU32 col) {
  const float h = size, w = size * 1.15f;
  if (up) {
    dl->AddTriangleFilled(ImVec2(center.x, center.y - h * 0.5f),
                          ImVec2(center.x + w * 0.5f, center.y + h * 0.5f),
                          ImVec2(center.x - w * 0.5f, center.y + h * 0.5f), col);
  } else {
    dl->AddTriangleFilled(ImVec2(center.x - w * 0.5f, center.y - h * 0.5f),
                          ImVec2(center.x + w * 0.5f, center.y - h * 0.5f),
                          ImVec2(center.x, center.y + h * 0.5f), col);
  }
}

// --- Controller tap state ---------------------------------------------------------------------
enum : uint16_t {
  kPadDpadUp = 0x0001,
  kPadDpadDown = 0x0002,
  kPadBack = 0x0020,
  kPadLeftShoulder = 0x0100,
  kPadRightShoulder = 0x0200,
  kPadB = 0x2000,
};
constexpr int16_t kStickThreshold = 18000;

struct UserTap {
  bool back_down = false;
  bool back_consumed = false;
  Clock::time_point back_since{};
  Clock::time_point replay_until{};
  bool b_down = false;
  bool left_shoulder_down = false;
  bool right_shoulder_down = false;
  bool swallow_until_release = false;
  int nav_direction = 0;
};

std::mutex g_tap_mutex;
std::array<UserTap, 4> g_taps;
std::atomic<bool> g_panel_open{false};
std::atomic<int> g_nav_direction{0};
std::atomic<int> g_page_requests{0};
std::atomic<int> g_key_steps{0}, g_key_jump{0};

bool PadIdle(const PadSample& pad) {
  return pad.buttons == 0 && pad.left_trigger < 30 && pad.right_trigger < 30 &&
         std::abs(int(pad.thumb_lx)) < 8000 && std::abs(int(pad.thumb_ly)) < 8000 &&
         std::abs(int(pad.thumb_rx)) < 8000 && std::abs(int(pad.thumb_ry)) < 8000;
}

}  // namespace

// ==============================================================================================
void ConfigureFonts(ImFontAtlas* atlas, const std::filesystem::path& font_dir) {
  const std::vector<std::filesystem::path> heavy = {
      font_dir / "NotoSans-Black.ttf",
      "/usr/share/fonts/noto/NotoSans-Black.ttf",
      "/usr/share/fonts/truetype/noto/NotoSans-Black.ttf",
      "/usr/share/fonts/noto/NotoSans-Bold.ttf",
      "/usr/share/fonts/truetype/noto/NotoSans-Bold.ttf",
      "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf",
      "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
      "/usr/share/fonts/TTF/LiberationSans-Bold.ttf",
#if defined(_WIN32)
      "C:\\Windows\\Fonts\\arialbd.ttf",
#endif
  };
  const std::vector<std::filesystem::path> body = {
      font_dir / "NotoSans-Regular.ttf",
      "/usr/share/fonts/noto/NotoSans-Regular.ttf",
      "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
      "/usr/share/fonts/TTF/DejaVuSans.ttf",
      "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
      "/usr/share/fonts/TTF/LiberationSans-Regular.ttf",
#if defined(_WIN32)
      "C:\\Windows\\Fonts\\arial.ttf",
#endif
  };
  g_fonts.heavy_large = LoadFirst(atlas, heavy, kHeavyLargePx, "headline");
  g_fonts.heavy_small = LoadFirst(atlas, heavy, kHeavySmallPx, "label");
  g_fonts.body = LoadFirst(atlas, body, kBodyPx, "body");
}

ImFont* HeadlineFont(float px) { return Heavy(px); }
ImFont* BodyFont() { return Body(); }

bool IsPanelOpen() { return g_panel_open.load(std::memory_order_relaxed); }

bool HandleKey(rex::ui::KeyEvent& event) {
  if (!IsPanelOpen()) return false;
  using Key=rex::ui::VirtualKey;
  switch(event.virtual_key()) {
    case Key::kDown: case Key::kNumpad2: g_key_steps.fetch_add(1); break;
    case Key::kUp: case Key::kNumpad8: g_key_steps.fetch_sub(1); break;
    case Key::kNext: case Key::kNumpad3: g_page_requests.fetch_add(1); break;
    case Key::kPrior: case Key::kNumpad9: g_page_requests.fetch_sub(1); break;
    case Key::kHome: case Key::kNumpad7: g_key_jump.store(-1); break;
    case Key::kEnd: case Key::kNumpad1: g_key_jump.store(1); break;
    case Key::kEscape: tabletennis::pc::ToggleAchievements(); break;
    default: return false;
  }
  event.set_handled(true);
  return true;
}

bool FilterInput(uint32_t user, PadSample& pad) {
  if (user >= g_taps.size()) return false;
  std::lock_guard lock(g_tap_mutex);
  UserTap& tap = g_taps[user];
  const auto now = Clock::now();
  const bool open = g_panel_open.load(std::memory_order_relaxed);
  const auto hold = std::chrono::duration<float>(kPanelOpenHoldSeconds);

  // Back/View: a hold toggles the list; a tap reaches the game once it is known to be a tap.
  const bool back = pad.buttons & kPadBack;
  if (back && !tap.back_down) {
    tap.back_since = now;
    tap.back_consumed = false;
  }
  if (back && !tap.back_consumed && now - tap.back_since >= hold) {
    tap.back_consumed = true;
    tap.swallow_until_release = true;
    tabletennis::pc::ToggleAchievements();
  }
  if (!back && tap.back_down && !tap.back_consumed && !open) {
    const auto held = now - tap.back_since;
    tap.replay_until = now + std::clamp(held, std::chrono::steady_clock::duration(std::chrono::milliseconds(60)),
                                        std::chrono::steady_clock::duration(std::chrono::milliseconds(150)));
  }
  tap.back_down = back;

  const bool b = pad.buttons & kPadB;
  if (open && b && !tap.b_down) {
    tap.swallow_until_release = true;
    tabletennis::pc::ToggleAchievements();
  }
  tap.b_down = b;

  if (open) {
    int direction = 0;
    if ((pad.buttons & kPadDpadUp) || pad.thumb_ly > kStickThreshold) direction = -1;
    if ((pad.buttons & kPadDpadDown) || pad.thumb_ly < -kStickThreshold) direction = 1;
    tap.nav_direction = direction;
    int combined = 0;
    for (const auto& other : g_taps) {
      if (other.nav_direction != 0) {
        combined = other.nav_direction;
        break;
      }
    }
    g_nav_direction.store(combined);

    const bool left_shoulder = pad.buttons & kPadLeftShoulder;
    const bool right_shoulder = pad.buttons & kPadRightShoulder;
    if (left_shoulder && !tap.left_shoulder_down) g_page_requests.fetch_sub(1);
    if (right_shoulder && !tap.right_shoulder_down) g_page_requests.fetch_add(1);
    tap.left_shoulder_down = left_shoulder;
    tap.right_shoulder_down = right_shoulder;

    pad = PadSample{};  // the list owns the pad
    return true;
  }
  tap.nav_direction = 0;
  tap.left_shoulder_down = false;
  tap.right_shoulder_down = false;

  // After a toggle, nothing reaches the game until every control is released, so the
  // button that closed the list cannot also act in the game.
  if (tap.swallow_until_release) {
    if (PadIdle(pad)) {
      tap.swallow_until_release = false;
    } else {
      pad = PadSample{};
      return true;
    }
  }

  if (back) {
    pad.buttons &= uint16_t(~kPadBack);
    return true;
  }
  if (now < tap.replay_until) {
    pad.buttons |= kPadBack;
    return true;
  }
  return false;
}

void OpenList() {
  // Request a toggle only when the list is closed, so opening it from the menu never closes it.
  if (!g_panel_open.load(std::memory_order_relaxed)) tabletennis::pc::ToggleAchievements();
}

// ==============================================================================================
// Notification popup
// ==============================================================================================
AchievementToast::AchievementToast(rex::ui::ImGuiDrawer* drawer,
                                   rex::ui::ImmediateDrawer* immediate_drawer,
                                   rex::Runtime* runtime,
                                   rex::system::AchievementManager* achievements,
                                   rex::ui::Window* window)
    : rex::ui::AchievementNotificationDialog(drawer),
      icon_cache_(immediate_drawer, runtime),
      achievements_(achievements),
      window_(window) {}

AchievementToast::~AchievementToast() = default;

bool AchievementToast::HasPendingWork() {
  std::lock_guard lock(mutex_);
  return !queue_.empty() || (!test_hook_done_ && std::getenv("TT_TEST_ACHIEVEMENT_TOAST"));
}

void AchievementToast::Push(const rex::system::AchievementEvent& event) {
  std::lock_guard lock(mutex_);
  queue_.push_back({event, std::nullopt});
}

void AchievementToast::RunTestHook() {
  if (test_hook_done_) return;
  static const std::vector<uint32_t> ids = ParseIdList("TT_TEST_ACHIEVEMENT_TOAST");
  if (ids.empty()) {
    test_hook_done_ = true;
    return;
  }
  if (!tabletennis::intro::MainMenuReached()) return;
  const auto now = Clock::now();
  if (!test_hook_at_) {
    test_hook_at_ = now + std::chrono::seconds(3);
    return;
  }
  if (now < *test_hook_at_) return;
  test_hook_done_ = true;
  if (achievements_) {
    for (const auto& info : achievements_->ListAchievements()) {
      REXLOG_INFO("Tabletennis: achievement {} = \"{}\" ({}G)", info.id, info.label, info.gamerscore);
    }
  }
  for (uint32_t id : ids) {
    const bool shown = achievements_ && achievements_->ShowAchievementNotification(id);
    REXLOG_INFO("Tabletennis: test notification for achievement {}: {}", id,
                shown ? "shown" : "unknown id");
  }
}

void AchievementToast::OnDraw(ImGuiIO& io) {
  RunTestHook();

  const float total = kToast.enter_seconds + kToast.hold_seconds + kToast.exit_seconds;
  const auto now = Clock::now();
  std::optional<rex::system::AchievementInfo> current;
  float t = 0.0f;
  {
    std::lock_guard lock(mutex_);
    while (!queue_.empty()) {
      Pending& front = queue_.front();
      if (!front.shown_at) {
        // Wait out the gap after the previous one, and do not start under the open list.
        const bool gap_over =
            std::chrono::duration<float>(now - last_finished_).count() >= kToast.gap_seconds;
        if (!gap_over || IsPanelOpen()) break;
        front.shown_at = now;
        PlayUnlockChime();
      }
      t = std::chrono::duration<float>(now - *front.shown_at).count();
      if (t >= total) {
        last_finished_ = now;
        queue_.pop_front();
        continue;
      }
      current = front.event.achievement;
      break;
    }
  }
  if (current) DrawToast(io, *current, t, total);
}

void AchievementToast::DrawToast(ImGuiIO& io, const rex::system::AchievementInfo& info,
                                 float t, float total) {
  const PictureRect pic = GamePicture(io);
  const float s = pic.h / 720.0f * kToast.scale;
  const float enter = kToast.enter_seconds;
  const float exit_start = total - kToast.exit_seconds;
  const float in = Stage(t, 0.0f, enter);
  const float out = Stage(t, exit_start, kToast.exit_seconds);

  // --- Geometry ---
  const float h = 86.0f * s;
  const float radius = h * 0.5f;
  const float pad = 13.0f * s;
  const float icon = 60.0f * s;
  const float name_px = 27.0f * s;
  const float caption_px = 15.0f * s;
  const float score_px = 27.0f * s;
  const float margin = kToast.margin * s;

  const std::string name = Upper(info.label);
  const char* caption = "ACHIEVEMENT UNLOCKED";
  ImFont* name_font = Heavy(name_px);
  ImFont* caption_font = Heavy(caption_px);
  const float badge_w = GamerscoreBadge(nullptr, 0, 0, score_px, int(info.gamerscore), 1.0f, false);
  // Contents (icon, text, gamerscore) form one block centered in the pill; the halftone
  // dots are background decoration and take no room.
  const float side = pad * 1.6f;              // space between the pill ends and the contents
  const float icon_gap = pad * 1.15f;         // icon to text
  const float badge_gap = pad * 1.6f;         // text to gamerscore
  const float dots_w = h * 0.95f;
  const float max_w = std::min(700.0f * s, pic.w - 2.0f * margin);
  const float text_room = max_w - 2.0f * side - icon - icon_gap - badge_gap - badge_w;
  const std::string shown_name = Fit(name_font, name_px, kCondensed, text_room, name);
  const float name_w = TextWidth(name_font, name_px, shown_name.c_str(), kCondensed);
  const float caption_w = TextWidth(caption_font, caption_px, caption);
  const float text_w = std::max(name_w, caption_w);
  const float content_w = icon + icon_gap + text_w + badge_gap + badge_w;
  float w = content_w + 2.0f * side;
  w = std::clamp(w, 400.0f * s, max_w);

  const bool top = kToast.anchor == ToastAnchor::kTopCenter ||
                   kToast.anchor == ToastAnchor::kTopLeft ||
                   kToast.anchor == ToastAnchor::kTopRight;
  float x0;
  switch (kToast.anchor) {
    case ToastAnchor::kTopLeft:
    case ToastAnchor::kBottomLeft:
      x0 = pic.x + margin;
      break;
    case ToastAnchor::kTopRight:
    case ToastAnchor::kBottomRight:
      x0 = pic.x + pic.w - margin - w;
      break;
    default:
      x0 = pic.x + (pic.w - w) * 0.5f;
      break;
  }
  const float rest_y = top ? pic.y + margin : pic.y + pic.h - margin - h;
  const float travel = (h + margin) * (top ? -1.0f : 1.0f);

  // --- Motion: eased slide in with a small overshoot, eased slide out ---
  const float slide_in = EaseOutBack(in);
  const float slide_out = EaseInCubic(out);
  const float y0 = rest_y + travel * (1.0f - slide_in) + travel * slide_out;
  float alpha = EaseOutCubic(Stage(t, 0.0f, enter * 0.5f)) * (1.0f - slide_out * slide_out);
  if (alpha <= 0.001f) return;
  const float grow = 0.94f + 0.06f * EaseOutCubic(in);
  const ImVec2 center(x0 + w * 0.5f, y0 + h * 0.5f);
  const ImVec2 p0(center.x - w * 0.5f * grow, center.y - h * 0.5f * grow);
  const ImVec2 p1(center.x + w * 0.5f * grow, center.y + h * 0.5f * grow);
  const float cy = center.y;
  const float r = radius * grow;

  ImDrawList* dl = ImGui::GetForegroundDrawList();

  // --- Pill body: shadow, navy fill, blue rim ---
  dl->AddRectFilled(ImVec2(p0.x, p0.y + 4.0f * s), ImVec2(p1.x, p1.y + 4.0f * s),
                    Col(0, 0, 0, 0.35f * alpha), r);
  dl->AddRectFilled(p0, p1, Navy(0.95f * alpha), r);
  dl->PushClipRect(p0, p1, true);
  // Lighter upper half, like the lit bevel on the game's panels.
  dl->AddRectFilled(p0, ImVec2(p1.x, cy), NavyLight(0.28f * alpha), r, ImDrawFlags_RoundCornersTop);
  // Halftone swoosh across the right end, drifting slowly while the toast is up.
  Halftone(dl, ImVec2(p1.x - dots_w - pad, p0.y), p1, BlueLight(0.45f * alpha), 9.0f * s,
           t * 5.0f * s, r);
  // Highlight sweep that crosses the panel as it lands.
  const float sweep = Stage(t, enter * 0.35f, 0.55f);
  if (sweep > 0.0f && sweep < 1.0f) {
    const float sx = p0.x - w * 0.15f + (w * 1.3f) * EaseInOutSine(sweep);
    const float half = 22.0f * s;
    const float skew = h * 0.45f;
    const ImVec2 quad[4] = {ImVec2(sx - half + skew, p0.y), ImVec2(sx + half + skew, p0.y),
                            ImVec2(sx + half - skew, p1.y), ImVec2(sx - half - skew, p1.y)};
    dl->AddConvexPolyFilled(quad, 4, White(0.20f * alpha * std::sin(kPi * sweep)));
  }
  dl->PopClipRect();
  dl->AddRect(p0, p1, Blue(0.65f * alpha), r, 0, 1.6f * s);
  dl->AddRect(ImVec2(p0.x + 2.5f * s, p0.y + 2.5f * s), ImVec2(p1.x - 2.5f * s, p1.y - 2.5f * s),
              BlueLight(0.18f * alpha), r - 2.5f * s, 0, 1.0f * s);

  const float content_x = center.x - content_w * 0.5f;

  // --- Icon: pops in once the panel has landed ---
  const float icon_stage = EaseOutBack(Stage(t, enter * 0.4f, 0.4f));
  if (icon_stage > 0.0f) {
    const ImVec2 icon_center(content_x + icon * 0.5f, cy);
    const float half = icon * 0.5f * icon_stage;
    const ImVec2 i0(icon_center.x - half, icon_center.y - half);
    const ImVec2 i1(icon_center.x + half, icon_center.y + half);
    dl->AddRectFilled(ImVec2(i0.x - 3.0f * s, i0.y - 3.0f * s), ImVec2(i1.x + 3.0f * s, i1.y + 3.0f * s),
                      Outline(0.9f * alpha), 7.0f * s);
    if (rex::ui::ImmediateTexture* texture = icon_cache_.GetIcon(info)) {
      dl->AddImageRounded(reinterpret_cast<ImTextureID>(texture), i0, i1, ImVec2(0, 0),
                          ImVec2(1, 1), White(alpha), 5.0f * s);
    } else {
      dl->AddRectFilled(i0, i1, NavyLight(alpha), 5.0f * s);
    }
    dl->AddRect(i0, i1, White(0.85f * alpha), 5.0f * s, 0, 1.5f * s);
  }

  // --- Text: caption, then the name slides in, then its underline draws ---
  const float text_x = content_x + icon + icon_gap;
  const float caption_stage = EaseOutCubic(Stage(t, enter * 0.55f, 0.3f));
  if (caption_stage > 0.0f) {
    AddText(dl, caption_font, caption_px,
            ImVec2(text_x, CapsTop(cy - 19.0f * s + (1.0f - caption_stage) * 6.0f * s, caption_px)),
            BlueLight(alpha * caption_stage), caption);
  }
  const float name_stage = EaseOutCubic(Stage(t, enter * 0.7f, 0.35f));
  if (name_stage > 0.0f) {
    AddOutlinedText(dl, name_font, name_px,
                    ImVec2(text_x - (1.0f - name_stage) * 18.0f * s, CapsTop(cy + 7.0f * s, name_px)),
                    Yellow(alpha * name_stage), Outline(alpha * name_stage), 2.0f * s,
                    shown_name.c_str(), kCondensed);
  }
  const float line_stage = EaseOutCubic(Stage(t, enter * 1.0f, 0.4f));
  if (line_stage > 0.0f) {
    const float ly = cy + 21.0f * s;
    dl->AddRectFilled(ImVec2(text_x, ly), ImVec2(text_x + name_w * line_stage, ly + 2.0f * s),
                      Yellow(0.9f * alpha), 1.0f * s);
  }

  // --- Gamerscore: badge pops in while the number counts up ---
  const float badge_stage = Stage(t, enter * 1.05f, 0.45f);
  if (badge_stage > 0.0f) {
    const float pop = EaseOutBack(badge_stage);
    const int shown = int(std::lround(info.gamerscore * EaseOutCubic(Stage(t, enter * 1.05f, 0.6f))));
    GamerscoreBadge(dl, content_x + content_w, cy, score_px * pop, shown,
                    alpha * Clamp01(badge_stage * 3.0f));
  }
}

// ==============================================================================================
// Achievement list
// ==============================================================================================
AchievementsPanel::AchievementsPanel(rex::ui::ImGuiDrawer* drawer,
                                     rex::ui::ImmediateDrawer* immediate_drawer,
                                     rex::Runtime* runtime,
                                     rex::system::AchievementManager* achievements)
    : rex::ui::ImGuiDialog(drawer),
      achievements_(achievements),
      icon_cache_(immediate_drawer, runtime),
      opened_at_(Clock::now()) {
  tabletennis::pacing::SetUiActive(true);
  Refresh();
  // Start on the first locked achievement so the next goal is in view.
  for (size_t i = 0; i < entries_.size(); ++i) {
    if (!entries_[i].unlocked) {
      selected_ = int(i);
      break;
    }
  }
  g_nav_direction.store(0);
  g_page_requests.store(0);
  g_key_steps.store(0);g_key_jump.store(0);
  g_panel_open.store(true);
  REXLOG_INFO("Tabletennis: achievements list opened ({} / {} unlocked)", unlocked_count_,
              entries_.size());
}

AchievementsPanel::~AchievementsPanel() {
  tabletennis::pacing::SetUiActive(false);
  g_panel_open.store(false);
  g_nav_direction.store(0);
}

void AchievementsPanel::Refresh() {
  static const std::vector<uint32_t> test_unlocked = ParseIdList("TT_TEST_ACHIEVEMENTS_UNLOCKED");
  entries_.clear();
  unlocked_count_ = 0;
  earned_gamerscore_ = 0;
  total_gamerscore_ = 0;
  if (achievements_) {
    for (auto& info : achievements_->ListAchievements()) {
      Entry entry;
      entry.unlocked = achievements_->IsUnlocked(info.id);
      uint64_t unlocked_at = entry.unlocked ? achievements_->GetUnlockTime(info.id) : 0;
      if (!entry.unlocked &&
          std::find(test_unlocked.begin(), test_unlocked.end(), info.id) != test_unlocked.end()) {
        entry.unlocked = true;
        unlocked_at = uint64_t(std::time(nullptr)) * 10'000'000ULL + 116'444'736'000'000'000ULL;
      }
      entry.unlock_date = entry.unlocked ? FormatDate(unlocked_at) : std::string();
      total_gamerscore_ += info.gamerscore;
      if (entry.unlocked) {
        ++unlocked_count_;
        earned_gamerscore_ += info.gamerscore;
      }
      entry.info = std::move(info);
      entries_.push_back(std::move(entry));
    }
  }
  next_refresh_ = Clock::now() + std::chrono::seconds(1);
}

void AchievementsPanel::Move(int delta, int row_count) {
  if (row_count <= 0) return;
  selected_ = std::clamp(selected_ + delta, 0, row_count - 1);
}

void AchievementsPanel::HandleNavigation(ImGuiIO& io, int row_count, int rows_per_page) {
  const auto now = Clock::now();
  // Controller: D-pad or stick with auto-repeat, shoulders page.
  const int direction = g_nav_direction.load();
  if (direction != held_direction_) {
    held_direction_ = direction;
    if (direction != 0) {
      Move(direction, row_count);
      next_repeat_ = now + std::chrono::milliseconds(380);
    }
  } else if (direction != 0 && now >= next_repeat_) {
    Move(direction, row_count);
    next_repeat_ = now + std::chrono::milliseconds(70);
  }
  if (const int pages = g_page_requests.exchange(0)) Move(pages * rows_per_page, row_count);

  // Keyboard and mouse wheel.
  if (const int steps=g_key_steps.exchange(0)) Move(steps,row_count);
  if (const int jump=g_key_jump.exchange(0)) Move(jump*row_count,row_count);
  if (io.MouseWheel != 0.0f) Move(io.MouseWheel > 0.0f ? -1 : 1, row_count);

  // Keep the selection on screen, then glide there.
  if (selected_ < int(scroll_target_)) scroll_target_ = float(selected_);
  if (selected_ >= int(scroll_target_) + rows_per_page) {
    scroll_target_ = float(selected_ - rows_per_page + 1);
  }
  scroll_target_ = std::clamp(scroll_target_, 0.0f, float(std::max(0, row_count - rows_per_page)));
  const float dt = std::clamp(io.DeltaTime, 0.0f, 0.1f);
  scroll_ += (scroll_target_ - scroll_) * (1.0f - std::exp(-dt * 16.0f));
  if (std::fabs(scroll_target_ - scroll_) < 0.002f) scroll_ = scroll_target_;
}

void AchievementsPanel::OnDraw(ImGuiIO& io) {
  if (Clock::now() >= next_refresh_) Refresh();

  const float s = io.DisplaySize.y / 720.0f;
  const float W = io.DisplaySize.x;
  const float H = io.DisplaySize.y;
  const float t = std::chrono::duration<float>(Clock::now() - opened_at_).count();
  const float in = EaseOutCubic(Stage(t, 0.0f, 0.22f));
  const float rise = (1.0f - in) * 12.0f * s;
  ImDrawList* dl = ImGui::GetForegroundDrawList();

  dl->AddRectFilled(ImVec2(0, 0), ImVec2(W, H), Col(0, 0, 0, 0.64f * in));

  // --- Layout ---
  const float panel_w = std::min(920.0f * s, W - 36.0f * s);
  const float px0 = (W - panel_w) * 0.5f;
  const float px1 = px0 + panel_w;
  const float header_h = 58.0f * s;
  const float header_y0 = 30.0f * s + rise;
  const float header_y1 = header_y0 + header_h;
  const float header_cy = (header_y0 + header_y1) * 0.5f;
  const float footer_h = 34.0f * s;
  const float footer_y1 = H - 22.0f * s;
  const float footer_y0 = footer_y1 - footer_h;
  const float py0 = header_y1 + 12.0f * s;
  const float py1 = footer_y0 - 12.0f * s;

  // --- Header band: dark bar running off the right edge, halftone swoosh, yellow title ---
  {
    const float hx0 = px0 - 16.0f * s;
    dl->AddRectFilled(ImVec2(hx0, header_y0), ImVec2(W + header_h, header_y1), Navy(0.96f * in),
                      header_h * 0.5f, ImDrawFlags_RoundCornersLeft);
    dl->PushClipRect(ImVec2(hx0, header_y0), ImVec2(W, header_y1), true);
    dl->AddRectFilled(ImVec2(hx0, header_y0), ImVec2(W, header_cy), NavyLight(0.25f * in),
                      header_h * 0.5f, ImDrawFlags_RoundCornersTopLeft);
    // The swoosh tail sits beyond the panel's right edge, past the progress figures.
    Halftone(dl, ImVec2(px1 + 8.0f * s, header_y0), ImVec2(W, header_y1), BlueLight(0.42f * in),
             9.0f * s, t * 4.0f * s);
    dl->PopClipRect();
    dl->AddLine(ImVec2(hx0 + header_h * 0.3f, header_y1 + 1.0f * s), ImVec2(W, header_y1 + 1.0f * s),
                Blue(0.7f * in), 2.0f * s);

    const float title_px = 40.0f * s;
    AddOutlinedText(dl, Heavy(title_px), title_px, ImVec2(px0 + 8.0f * s, CapsTop(header_cy, title_px)),
                    Yellow(in), Outline(in), 2.6f * s, "ACHIEVEMENTS", kCondensed);

    // Progress block on the right of the band.
    char line[64];
    const float stat_px = 17.0f * s;
    ImFont* stat_font = Heavy(stat_px);
    std::snprintf(line, sizeof(line), "%d / %d UNLOCKED", unlocked_count_, int(entries_.size()));
    float wline = TextWidth(stat_font, stat_px, line, kCondensed);
    const float stats_right = px1 - 10.0f * s;
    AddOutlinedText(dl, stat_font, stat_px, ImVec2(stats_right - wline, CapsTop(header_cy - 11.0f * s, stat_px)),
                    White(in), Outline(in), 1.2f * s, line, kCondensed);
    std::snprintf(line, sizeof(line), "%u / %u", earned_gamerscore_, total_gamerscore_);
    const float badge_px = 19.0f * s;
    // Number pair in yellow with the G disc at the end.
    {
      ImFont* f = Heavy(badge_px);
      const float disc_r = badge_px * 0.40f;
      const float gap = badge_px * 0.22f;
      const float num_w = TextWidth(f, badge_px, line, kCondensed);
      const float x = stats_right - (num_w + gap + disc_r * 2.0f);
      const float cy = header_cy + 12.0f * s;
      AddOutlinedText(dl, f, badge_px, ImVec2(x, CapsTop(cy, badge_px)), Yellow(in), Outline(in),
                      1.3f * s, line, kCondensed);
      const ImVec2 disc(stats_right - disc_r, cy);
      dl->AddCircleFilled(disc, disc_r, Outline(in), 24);
      dl->AddCircleFilled(disc, disc_r * 0.88f, White(in), 24);
      const float g_px = badge_px * 0.66f;
      const float g_w = TextWidth(Heavy(g_px), g_px, "G");
      AddText(dl, Heavy(g_px), g_px, ImVec2(disc.x - g_w * 0.5f, CapsTop(disc.y, g_px)),
              Col(24, 36, 60, in), "G");
    }
  }

  // --- Panel body with the progress bar along its top ---
  dl->AddRectFilled(ImVec2(px0, py0), ImVec2(px1, py1), PanelDark(0.90f * in), 10.0f * s);
  dl->AddRect(ImVec2(px0, py0), ImVec2(px1, py1), NavyLight(0.8f * in), 10.0f * s, 0, 1.5f * s);
  {
    const float bx0 = px0 + 18.0f * s, bx1 = px1 - 18.0f * s;
    const float by0 = py0 + 12.0f * s, by1 = by0 + 5.0f * s;
    const float fraction = total_gamerscore_ ? float(earned_gamerscore_) / float(total_gamerscore_) : 0.0f;
    dl->AddRectFilled(ImVec2(bx0, by0), ImVec2(bx1, by1), White(0.12f * in), 2.5f * s);
    if (fraction > 0.0f) {
      dl->AddRectFilled(ImVec2(bx0, by0), ImVec2(bx0 + (bx1 - bx0) * fraction * in, by1), Yellow(in), 2.5f * s);
    }
  }

  // --- Rows ---
  const float list_y0 = py0 + 28.0f * s;
  const float list_y1 = py1 - 10.0f * s;
  const float row_h = 82.0f * s;
  const int rows_per_page = std::max(1, int((list_y1 - list_y0) / row_h));
  const int row_count = int(entries_.size());
  HandleNavigation(io, row_count, rows_per_page);

  const float rx0 = px0 + 16.0f * s;
  const float rx1 = px1 - 28.0f * s;
  const float icon = 56.0f * s;
  const float name_px = 21.0f * s;
  const float desc_px = 14.5f * s;
  const float small_px = 12.0f * s;
  const float right_col = 126.0f * s;

  dl->PushClipRect(ImVec2(px0 + 4.0f * s, list_y0), ImVec2(px1 - 4.0f * s, list_y1), true);
  for (int i = 0; i < row_count; ++i) {
    const float y = list_y0 + (float(i) - scroll_) * row_h;
    if (y + row_h < list_y0 || y > list_y1) continue;
    const Entry& e = entries_[i];
    const bool selected = i == selected_;
    const float ry0 = y + 3.0f * s;
    const float ry1 = y + row_h - 3.0f * s;
    const float rcy = (ry0 + ry1) * 0.5f;

    if (selected) {
      dl->AddRectFilled(ImVec2(rx0, ry0), ImVec2(rx1, ry1), Col(40, 96, 160, 0.40f * in), 6.0f * s);
      dl->AddRectFilled(ImVec2(rx0, ry0), ImVec2(rx0 + 4.0f * s, ry1), Yellow(in), 2.0f * s);
    } else {
      dl->AddLine(ImVec2(rx0 + 14.0f * s, ry1 + 2.0f * s), ImVec2(rx1 - 8.0f * s, ry1 + 2.0f * s),
                  White(0.07f * in), 1.0f);
    }

    // Icon, dimmed with a padlock while locked.
    const ImVec2 i0(rx0 + 14.0f * s, rcy - icon * 0.5f);
    const ImVec2 i1(i0.x + icon, i0.y + icon);
    dl->AddRectFilled(ImVec2(i0.x - 2.0f * s, i0.y - 2.0f * s), ImVec2(i1.x + 2.0f * s, i1.y + 2.0f * s),
                      Outline(0.9f * in), 6.0f * s);
    if (rex::ui::ImmediateTexture* texture = icon_cache_.GetIcon(e.info)) {
      dl->AddImageRounded(reinterpret_cast<ImTextureID>(texture), i0, i1, ImVec2(0, 0), ImVec2(1, 1),
                          e.unlocked ? White(in) : Col(150, 150, 160, 0.45f * in), 5.0f * s);
    } else {
      dl->AddRectFilled(i0, i1, NavyLight(in), 5.0f * s);
    }
    if (e.unlocked) {
      dl->AddRect(i0, i1, White(0.8f * in), 5.0f * s, 0, 1.3f * s);
    } else {
      Padlock(dl, ImVec2((i0.x + i1.x) * 0.5f, rcy - 3.0f * s), 17.0f * s, White(0.85f * in));
    }

    // Name and description.
    const float text_x = i1.x + 14.0f * s;
    const float text_w = rx1 - 12.0f * s - right_col - text_x;
    ImFont* nf = Heavy(name_px);
    const ImU32 name_col = selected ? Yellow(in) : (e.unlocked ? White(in) : Grey(in));
    const std::string name = Fit(nf, name_px, kCondensed, text_w, Upper(e.info.label));
    AddOutlinedText(dl, nf, name_px, ImVec2(text_x, ry0 + 8.0f * s), name_col, Outline(in), 1.4f * s,
                    name.c_str(), kCondensed);
    const std::string& desc = (!e.unlocked && !e.info.unachieved_description.empty())
                                  ? e.info.unachieved_description
                                  : e.info.description;
    dl->PushClipRect(ImVec2(text_x, ry0), ImVec2(text_x + text_w, ry1 - 2.0f * s), true);
    AddText(dl, Body(), desc_px, ImVec2(text_x, ry0 + 33.0f * s),
            e.unlocked ? White(0.85f * in) : Grey(0.9f * in), desc.c_str(), 1.0f, text_w);
    dl->PopClipRect();

    // Gamerscore and state on the right.
    const float right_x = rx1 - 12.0f * s;
    GamerscoreBadge(dl, right_x, ry0 + 20.0f * s, 20.0f * s, int(e.info.gamerscore), e.unlocked ? in : 0.55f * in);
    const std::string state = e.unlocked ? "UNLOCKED " + e.unlock_date : std::string("LOCKED");
    ImFont* sf = Heavy(small_px);
    const float sw = TextWidth(sf, small_px, state.c_str(), kCondensed);
    AddText(dl, sf, small_px, ImVec2(right_x - sw, CapsTop(ry0 + 44.0f * s, small_px)),
            e.unlocked ? BlueLight(in) : Grey(0.8f * in), state.c_str(), kCondensed);
  }
  dl->PopClipRect();

  // --- Scrollbar ---
  if (row_count > rows_per_page) {
    const float tx = px1 - 16.0f * s;
    const float track = list_y1 - list_y0;
    dl->AddRectFilled(ImVec2(tx, list_y0), ImVec2(tx + 4.0f * s, list_y1), White(0.10f * in), 2.0f * s);
    const float thumb_h = std::max(18.0f * s, track * float(rows_per_page) / float(row_count));
    const float thumb_y = list_y0 + (track - thumb_h) * (scroll_ / float(row_count - rows_per_page));
    dl->AddRectFilled(ImVec2(tx, thumb_y), ImVec2(tx + 4.0f * s, thumb_y + thumb_h), White(0.6f * in), 2.0f * s);
  }

  // --- Footer: light pill with the controls, like the game's ACCEPT / BACK bar ---
  {
    dl->AddRectFilled(ImVec2(px0, footer_y0), ImVec2(px1, footer_y1), FooterLight(0.96f * in), footer_h * 0.5f);
    const float fcy = (footer_y0 + footer_y1) * 0.5f;
    const float label_px = 16.0f * s;
    ImFont* lf = Heavy(label_px);
    // Left: scroll arrows.
    float x = px0 + 22.0f * s;
    Triangle(dl, ImVec2(x + 5.0f * s, fcy - 5.0f * s), 7.0f * s, true, Yellow(in));
    Triangle(dl, ImVec2(x + 5.0f * s, fcy + 5.0f * s), 7.0f * s, false, Yellow(in));
    x += 20.0f * s;
    AddText(dl, lf, label_px, ImVec2(x, CapsTop(fcy, label_px)), FooterText(in), "SCROLL", kCondensed);
    // Right: close with B.
    const char* close_label = "CLOSE";
    const float close_w = TextWidth(lf, label_px, close_label, kCondensed);
    const float glyph_r = 10.0f * s;
    float right = px1 - 20.0f * s;
    ButtonGlyph(dl, ImVec2(right - glyph_r, fcy), glyph_r, RedB(in), "B", in);
    right -= glyph_r * 2.0f + 8.0f * s;
    AddText(dl, lf, label_px, ImVec2(right - close_w, CapsTop(fcy, label_px)), FooterText(in), close_label, kCondensed);
    // Middle: how the list opens, for players who found it by accident.
    const char* hint = "HOLD VIEW OR PRESS F7 TO OPEN";
    const float hint_px = 11.0f * s;
    const float hint_w = TextWidth(lf, hint_px, hint, kCondensed);
    AddText(dl, lf, hint_px, ImVec2((px0 + px1 - hint_w) * 0.5f, CapsTop(fcy, hint_px)), FooterText(0.55f * in), hint, kCondensed);
  }
}

}  // namespace tabletennis::achievement_ui

// The game's EXTRAS > ACHIEVEMENTS entry raises GLOBAL_SHOW_ACHIEVEMENTS; sub_8230DB30 clears it
// and calls sub_8230F770 to open the retail Xbox LIVE achievements screen (empty offline). That is
// its only caller and the last thing the dispatcher does, so we replace it with our own list. F7
// and the View-button hold still work through their own path.
extern "C" void sub_8230F770(PPCContext& ctx, uint8_t* base) {
  (void)base;
  REXLOG_INFO("Tabletennis: ACHIEVEMENTS chosen in the menu");
  tabletennis::achievement_ui::OpenList();
  ctx.r3.u64 = 0;
}
