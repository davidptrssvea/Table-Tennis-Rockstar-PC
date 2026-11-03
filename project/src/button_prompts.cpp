// button_prompts.cpp - see button_prompts.h.

#include "button_prompts.h"

#include <rex/cvar.h>
#include <rex/system/interfaces/graphics.h>
#include <rex/logging.h>
#include <rex/system/xmemory.h>
#include <rex/thread/mutex.h>

#include <SDL3/SDL.h>
#include <imgui.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <chrono>
#include <cstring>
#include <map>
#include <mutex>
#include <vector>

REXCVAR_DEFINE_STRING(button_prompts, "auto", "UI",
                      "Button icons in the menus: auto (the controller in use), xbox or playstation");

namespace tabletennis::prompts {
namespace {

#include "button_prompt_art.inc"

constexpr uint32_t kFormatDxt5 = 0x14;  // xenos::TextureFormat::k_DXT4_5
constexpr uint32_t kBlockBytes = 16;

struct Icon {
  const char* name;
  uint32_t width, height;
  uint64_t xbox_hash;
  const uint8_t* playstation;  // guest-format DXT5 blocks, row-major
  uint64_t playstation_hash = 0;
  std::vector<uint8_t> xbox;   // captured the first time the icon is seen
};

std::array<Icon, 6> g_icons = {{
    {"A", 64, 64, kXboxAHash, kPlayStationA},
    {"B", 64, 64, kXboxBHash, kPlayStationB},
    {"X", 64, 64, kXboxXHash, kPlayStationX},
    {"Y", 64, 64, kXboxYHash, kPlayStationY},
    {"LB", 64, 32, kXboxLBHash, kPlayStationLB},
    {"RB", 64, 32, kXboxRBHash, kPlayStationRB},
}};

// An icon copy in guest memory (one per menu movie that has it).
struct Placement {
  uint8_t* data;
  uint32_t size;  // the texture's guest size, for the memory watch
  uint32_t pitch_blocks;
  int icon;
};

rex::memory::Memory* g_memory = nullptr;
std::mutex g_mutex;                            // g_icons[].xbox, g_placements, g_applied
std::map<uint32_t, Placement> g_placements;    // by guest address
bool g_applied = false;                        // PlayStation icons in guest memory
std::atomic<bool> g_want_playstation{false};
int g_setting = 0;                             // UI thread: 0 auto, 1 xbox, 2 playstation
bool g_pad_playstation = false;                // UI thread: controller used last

// Byte offset of 16-byte block x,y in a tiled Xenos texture with rows of `pitch_blocks` blocks
// (XGAddress2DTiledOffset, as the SDK's texture_util::GetTiledOffset2D, which is not exported).
uint32_t BlockOffset(uint32_t x, uint32_t y, uint32_t pitch_blocks) {
  constexpr uint32_t kLog2Bytes = 4;
  const uint32_t pitch = (pitch_blocks + 31) & ~31u;
  const uint32_t macro = ((x >> 5) + (y >> 5) * (pitch >> 5)) << (kLog2Bytes + 7);
  const uint32_t micro = ((x & 7) + ((y & 0xE) << 2)) << kLog2Bytes;
  const uint32_t offset = macro + ((micro & ~0xFu) << 1) + (micro & 0xF) + ((y & 1) << 4);
  return ((offset & ~0x1FFu) << 3) + ((y & 16) << 7) + ((offset & 0x1C0) << 2) +
         (((((y & 8) >> 2) + (x >> 3)) & 3) << 6) + (offset & 0x3F);
}

// FNV-1a 64 of the icon's used blocks in row-major order (as make_button_art.py computes it).
uint64_t Fingerprint(const uint8_t* data, uint32_t pitch_blocks, uint32_t width, uint32_t height) {
  uint64_t hash = 0xCBF29CE484222325ULL;
  for (uint32_t by = 0; by < height / 4; ++by) {
    for (uint32_t bx = 0; bx < width / 4; ++bx) {
      const uint8_t* block = data + BlockOffset(bx, by, pitch_blocks);
      for (uint32_t i = 0; i < kBlockBytes; ++i) hash = (hash ^ block[i]) * 0x100000001B3ULL;
    }
  }
  return hash;
}

uint64_t LinearFingerprint(const uint8_t* blocks, size_t size) {
  uint64_t hash = 0xCBF29CE484222325ULL;
  for (size_t i = 0; i < size; ++i) hash = (hash ^ blocks[i]) * 0x100000001B3ULL;
  return hash;
}

void Gather(const Placement& p, std::vector<uint8_t>& out) {
  const Icon& icon = g_icons[p.icon];
  out.resize(size_t(icon.width / 4) * (icon.height / 4) * kBlockBytes);
  size_t i = 0;
  for (uint32_t by = 0; by < icon.height / 4; ++by) {
    for (uint32_t bx = 0; bx < icon.width / 4; ++bx, i += kBlockBytes) {
      std::memcpy(out.data() + i, p.data + BlockOffset(bx, by, p.pitch_blocks), kBlockBytes);
    }
  }
}

void Scatter(const Placement& p, const uint8_t* blocks) {
  const Icon& icon = g_icons[p.icon];
  size_t i = 0;
  for (uint32_t by = 0; by < icon.height / 4; ++by) {
    for (uint32_t bx = 0; bx < icon.width / 4; ++bx, i += kBlockBytes) {
      std::memcpy(p.data + BlockOffset(bx, by, p.pitch_blocks), blocks + i, kBlockBytes);
    }
  }
}

enum class Brought { kGone, kUnchanged, kWritten };

// Brings one copy to the wanted style if it still holds one of the two versions of its icon
// (the memory may since have been reused for something else).
Brought Bring(const Placement& p, bool playstation) {
  Icon& icon = g_icons[p.icon];
  const uint64_t now = Fingerprint(p.data, p.pitch_blocks, icon.width, icon.height);
  if (now == icon.xbox_hash) {
    if (icon.xbox.empty()) Gather(p, icon.xbox);
    if (!playstation) return Brought::kUnchanged;
    Scatter(p, icon.playstation);
    return Brought::kWritten;
  }
  if (now == icon.playstation_hash) {
    if (playstation || icon.xbox.empty()) return Brought::kUnchanged;
    Scatter(p, icon.xbox.data());
    return Brought::kWritten;
  }
  return Brought::kGone;
}

// Our writes bypass the GPU memory watch: report them like the kernel does after a file read, so
// the GPU's copy of those pages and the textures in them are loaded again. Physical memory is
// mapped at 0xA0000000, 0xC0000000 and 0xE0000000 (that one 0x1000 lower), and the watch lives in
// the view the memory was allocated through, so all three are told. Takes the global lock, so
// call it without holding g_mutex.
void Invalidate(uint32_t physical_address, uint32_t size) {
  for (const uint32_t view : {0xA0000000u | physical_address, 0xC0000000u | physical_address,
                              physical_address >= 0x1000 ? 0xE0000000u + (physical_address - 0x1000) : 0u}) {
    if (view) {
      g_memory->TriggerPhysicalMemoryCallbacks(rex::thread::global_critical_region::AcquireDirect(), view, size,
                                               true, true);
    }
  }
}

// SDK patch 0017: called on the GPU command thread before a texture's base level is uploaded.
#if defined(TT_TEXTURE_LOAD_HOOK)
void OnTextureLoad(const rex::system::TextureLoadHookInfo& info) {
  if (info.format != kFormatDxt5 || !info.tiled || info.width != 64 || (info.height != 64 && info.height != 32)) {
    return;
  }
  uint8_t* data = g_memory->TranslatePhysical<uint8_t*>(info.guest_address);
  const uint32_t pitch_blocks = info.pitch_texels / 4;
  const uint64_t hash = Fingerprint(data, pitch_blocks, info.width, info.height);
  for (int i = 0; i < int(g_icons.size()); ++i) {
    const Icon& icon = g_icons[i];
    if (icon.height != info.height || (hash != icon.xbox_hash && hash != icon.playstation_hash)) continue;
    bool written = false;
    {
      std::lock_guard lock(g_mutex);
      const Placement p{data, info.size, pitch_blocks, i};
      const bool first = g_placements.emplace(info.guest_address, p).second;
      g_placements[info.guest_address] = p;
      written = Bring(p, g_applied) == Brought::kWritten;
      if (first) {
        REXLOG_INFO("Tabletennis: button icon {} at {:08X} ({})", icon.name, info.guest_address,
                    g_applied ? "PlayStation" : "Xbox");
      }
    }
    if (written) Invalidate(info.guest_address, info.size);
    return;
  }
}

#endif

void Apply(bool playstation) {
  std::vector<std::pair<uint32_t, uint32_t>> written;
  {
    std::lock_guard lock(g_mutex);
    if (playstation == g_applied) return;
    g_applied = playstation;
    for (auto it = g_placements.begin(); it != g_placements.end();) {
      const Brought result = Bring(it->second, playstation);
      if (result == Brought::kGone) {
        it = g_placements.erase(it);
        continue;
      }
      if (result == Brought::kWritten) written.emplace_back(it->first, it->second.size);
      ++it;
    }
  }
  for (const auto& [address, size] : written) Invalidate(address, size);
  REXLOG_INFO("Tabletennis: button icons {} ({} rewritten)", playstation ? "PlayStation" : "Xbox", written.size());
}

bool IsPlayStationPad(SDL_Gamepad* pad) {
  switch (SDL_GetGamepadType(pad)) {
    case SDL_GAMEPAD_TYPE_PS3:
    case SDL_GAMEPAD_TYPE_PS4:
    case SDL_GAMEPAD_TYPE_PS5:
      return true;
    default:
      return false;
  }
}

// The controller with a button or stick in use; the first one until any is used.
void FollowController() {
  static auto next=std::chrono::steady_clock::time_point{};
  const auto now=std::chrono::steady_clock::now();
  if (now<next) return;
  next=now+std::chrono::milliseconds(250);
  int count = 0;
  SDL_JoystickID* ids = SDL_GetGamepads(&count);
  static bool any_used = false;
  for (int i = 0; ids && i < count; ++i) {
    SDL_Gamepad* pad = SDL_GetGamepadFromID(ids[i]);
    if (!pad) continue;
    bool used = false;
    for (int b = 0; b < SDL_GAMEPAD_BUTTON_COUNT && !used; ++b) {
      used = SDL_GetGamepadButton(pad, SDL_GamepadButton(b));
    }
    for (int a = SDL_GAMEPAD_AXIS_LEFTX; a <= SDL_GAMEPAD_AXIS_RIGHTY && !used; ++a) {
      used = std::abs(int(SDL_GetGamepadAxis(pad, SDL_GamepadAxis(a)))) > 16000;
    }
    if (used || (!any_used && i == 0)) {
      g_pad_playstation = IsPlayStationPad(pad);
      any_used = any_used || used;
      if (used) break;
    }
  }
  SDL_free(ids);
}

void DrawSymbol(ImDrawList* dl, const ImVec2& c, float r, char letter, float alpha) {
  const float t = std::max(1.5f, r * 0.17f);
  auto color = [alpha](int red, int green, int blue) { return IM_COL32(red, green, blue, int(255 * alpha)); };
  switch (letter) {
    case 'A': {  // cross
      const float d = r * 0.40f;
      const ImU32 col = color(127, 180, 240);
      dl->AddLine(ImVec2(c.x - d, c.y - d), ImVec2(c.x + d, c.y + d), col, t);
      dl->AddLine(ImVec2(c.x - d, c.y + d), ImVec2(c.x + d, c.y - d), col, t);
      break;
    }
    case 'B':  // circle
      dl->AddCircle(c, r * 0.42f, color(242, 112, 122), 24, t);
      break;
    case 'X': {  // square
      const float d = r * 0.38f;
      dl->AddRect(ImVec2(c.x - d, c.y - d), ImVec2(c.x + d, c.y + d), color(224, 154, 203), 0.0f, 0, t);
      break;
    }
    default: {  // triangle
      const float d = r * 0.46f;
      dl->AddTriangle(ImVec2(c.x, c.y - d), ImVec2(c.x + d * 0.94f, c.y + d * 0.62f),
                      ImVec2(c.x - d * 0.94f, c.y + d * 0.62f), color(62, 214, 166), t);
      break;
    }
  }
}

}  // namespace

void Init(rex::memory::Memory* memory, rex::system::IGraphicsSystem* graphics) {
  g_memory = memory;
  for (Icon& icon : g_icons) {
    icon.playstation_hash = LinearFingerprint(icon.playstation, size_t(icon.width / 4) * (icon.height / 4) * kBlockBytes);
  }
  SetSetting(REXCVAR_GET(button_prompts));
#if defined(TT_TEXTURE_LOAD_HOOK)
  if (g_memory && graphics) graphics->SetTextureLoadHook(OnTextureLoad);
#endif
}

void SetSetting(const std::string& value) {
  g_setting = value == "xbox" ? 1 : value == "playstation" ? 2 : 0;
  Update();
}

void Update() {
  if (g_setting == 0) FollowController();
  const bool want = g_setting == 2 || (g_setting == 0 && g_pad_playstation);
  g_want_playstation.store(want);
  Apply(want);
}

bool PlayStation() { return g_want_playstation.load(); }

bool DrawFace(ImDrawList* dl, const ImVec2& center, float radius, const char* xbox_letter, float alpha) {
  if (!PlayStation() || !xbox_letter || !xbox_letter[0]) return false;
  dl->AddCircleFilled(center, radius, IM_COL32(12, 13, 16, int(230 * alpha)), 24);
  dl->AddCircleFilled(center, radius * 0.86f, IM_COL32(44, 46, 52, int(255 * alpha)), 24);
  dl->AddCircleFilled(center, radius * 0.74f, IM_COL32(30, 31, 36, int(255 * alpha)), 24);
  DrawSymbol(dl, center, radius, xbox_letter[0], alpha);
  return true;
}

const char* Shoulder(bool right) {
  if (PlayStation()) return right ? "R1" : "L1";
  return right ? "RB" : "LB";
}

}  // namespace tabletennis::prompts
