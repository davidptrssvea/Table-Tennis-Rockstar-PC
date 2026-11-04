// button_prompts.cpp - see button_prompts.h.

#include "button_prompts.h"
#include "button_texture_layout.h"

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

std::array<Icon, 23> g_icons = {{
    {"A", 64, 64, kXboxAHash, kPlayStationA},
    {"B", 64, 64, kXboxBHash, kPlayStationB},
    {"X", 64, 64, kXboxXHash, kPlayStationX},
    {"Y", 64, 64, kXboxYHash, kPlayStationY},
    {"LB", 64, 32, kXboxLBHash, kPlayStationLB},
    {"RB", 64, 32, kXboxRBHash, kPlayStationRB},
    // Additional menu/HUD/training variants: hashes only, never original pixels.
    {"A (boot)", 64, 64, 0x408C83A34157E1D6ULL, kPlayStationA},
    {"B (dialogs)", 64, 64, 0xACBB72E1AFDE2EDCULL, kPlayStationB},
    {"LB (pause)", 64, 32, 0x87068736A7DAF86AULL, kPlayStationLB},
    {"RB (pause)", 64, 32, 0xDDECCC6B7C599500ULL, kPlayStationRB},
    {"RT (HUD)", 64, 64, 0x548845E0E78B7998ULL, kPlayStationRT},
    {"A (training glow)", 64, 64, 0x0209AA12C210BCAEULL, kPlayStationA},
    {"Y (training glow)", 64, 64, 0x631E0245FFAF4414ULL, kPlayStationY},
    {"X (training glow)", 64, 64, 0x720D13F4C8EB70FEULL, kPlayStationX},
    {"B (training glow)", 64, 64, 0xDAC1169020410FA2ULL, kPlayStationB},
    {"LB (training)", 128, 64, 0x164C3B7D492B2A4CULL, kPlayStationLBLarge},
    {"RB (training)", 128, 64, 0x57564441E1FC064FULL, kPlayStationRBLarge},
    {"LT (training)", 64, 64, 0x1AB2D230E00A8366ULL, kPlayStationLT},
    {"RT (training)", 64, 64, 0xEBF606C61B2210C0ULL, kPlayStationRT},
    {"X (training)", 64, 64, 0xB84A3D6ED8F13FBCULL, kPlayStationX},
    {"Y (training)", 64, 64, 0x999C4A9DA510B538ULL, kPlayStationY},
    {"A (training)", 64, 64, 0x9825952A3689CA4FULL, kPlayStationA},
    {"Guide", 128, 128, 0x8D53A9E805FC317CULL, kPlayStationGuide},
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
std::atomic<int> g_setting{0};                             // UI thread: 0 auto, 1 xbox, 2 playstation
std::mutex g_follow_mutex;
std::array<texture::Signature,23> g_signatures{};
bool g_pad_playstation = false;                // UI thread: controller used last

// Byte offset of 16-byte block x,y in a tiled Xenos texture with rows of `pitch_blocks` blocks
// (XGAddress2DTiledOffset, as the SDK's texture_util::GetTiledOffset2D, which is not exported).
using texture::BlockOffset;

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
  if(info.format!=kFormatDxt5 || !info.tiled ||
     (info.width!=64 && info.width!=128) || (info.height!=64 && info.height!=32 && info.height!=128)) return;
  const uint32_t pitch_blocks=info.pitch_texels/4;
  if(!texture::Fits(pitch_blocks,info.width,info.height,info.size)) return;
  uint8_t* data=g_memory->TranslatePhysical<uint8_t*>(info.guest_address);
  bool written=false;
  {
    std::lock_guard lock(g_mutex);
    const uint64_t hash=Fingerprint(data,pitch_blocks,info.width,info.height);
    auto previous=g_placements.find(info.guest_address);
    int known=previous==g_placements.end() ? -1 : previous->second.icon;
    int i=texture::Identify(g_signatures,info.width,info.height,hash,known);
    if(i<0) return;
    const Placement placement{data,info.size,pitch_blocks,i};
    const bool first=previous==g_placements.end() || previous->second.icon!=i;
    g_placements[info.guest_address]=placement;
    written=Bring(placement,g_applied)==Brought::kWritten;
    if(first) REXLOG_INFO("Tabletennis: button icon {} at {:08X} ({})",g_icons[i].name,
                         info.guest_address,g_applied ? "PlayStation" : "Xbox");
  }
  if(written) Invalidate(info.guest_address,info.size);
}

#endif

void Apply() {
  std::vector<std::pair<uint32_t, uint32_t>> written;
  bool playstation;
  {
    std::lock_guard lock(g_mutex);
    playstation=g_want_playstation.load();
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
    for (int a = SDL_GAMEPAD_AXIS_LEFTX; a < SDL_GAMEPAD_AXIS_COUNT && !used; ++a) {
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
  for(size_t i=0;i<g_icons.size();++i) {
    const auto& icon=g_icons[i];
    g_signatures[i]={icon.width,icon.height,icon.xbox_hash,icon.playstation_hash};
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
  {
    std::lock_guard follow_lock(g_follow_mutex);
    const auto setting=g_setting.load();
    if (setting == 0) FollowController();
    g_want_playstation.store(setting == 2 || (setting == 0 && g_pad_playstation));
  }
  // Apply/invalidation may acquire the engine's global memory-watch lock.
  // Never hold the controller lock across that operation on a guest/UI thread.
  Apply();
}

bool PlayStation() { return g_want_playstation.load(); }

bool DrawFace(ImDrawList* dl, const ImVec2& center, float radius, const char* xbox_letter, float alpha) {
  if (!PlayStation() || !xbox_letter || !xbox_letter[0]) return false;
  dl->AddCircleFilled({center.x,center.y+radius*.1f},radius,IM_COL32(0,0,0,int(60*alpha)),64);
  dl->AddCircleFilled(center, radius, IM_COL32(12, 13, 16, int(255 * alpha)), 64);
  dl->AddCircleFilled(center, radius * 0.86f, IM_COL32(58, 62, 70, int(255 * alpha)), 64);
  dl->AddCircleFilled(center, radius * 0.74f, IM_COL32(30, 33, 40, int(255 * alpha)), 64);
  dl->PathArcTo(center,radius*.86f,3.55f,5.88f,32);
  dl->PathStroke(IM_COL32(255,255,255,int(60*alpha)),0,std::max(1.f,radius*.04f));
  DrawSymbol(dl, center, radius, xbox_letter[0], alpha);
  return true;
}

void DrawButton(ImDrawList* dl,const ImVec2& center,float radius,unsigned int fill,
                const char* letter,ImFont* font,float alpha) {
  if(DrawFace(dl,center,radius,letter,alpha)) return;
  const auto opacity=[alpha](int a) {return int(std::clamp(alpha,0.f,1.f)*a);};
  dl->AddCircleFilled({center.x,center.y+radius*.10f},radius*1.02f,
                      IM_COL32(0,0,0,opacity(48)),64);
  dl->AddCircleFilled(center,radius,IM_COL32(32,35,37,opacity(255)),64);
  const int first=dl->VtxBuffer.Size;
  dl->AddCircleFilled(center,radius*.87f,fill,64);
  // Shade the actual circle mesh, preserving the antialiased edge alpha.
  for(int i=first;i<dl->VtxBuffer.Size;++i) {
    auto& v=dl->VtxBuffer[i];
    const float t=std::clamp((v.pos.y-center.y)/radius,-1.f,1.f);
    const float light=1.f-.27f*t;
    const auto channel=[&](int shift) {return std::clamp(int(((fill>>shift)&255)*light),0,255);};
    v.col=IM_COL32(channel(IM_COL32_R_SHIFT),channel(IM_COL32_G_SHIFT),channel(IM_COL32_B_SHIFT),(v.col>>IM_COL32_A_SHIFT)&255);
  }
  dl->PathArcTo(center,radius*.88f,3.55f,5.88f,32);
  dl->PathStroke(IM_COL32(255,255,255,opacity(145)),0,std::max(1.f,radius*.055f));
  dl->AddEllipseFilled({center.x,center.y-radius*.43f},{radius*.48f,radius*.15f},
                       IM_COL32(255,255,255,opacity(38)),0,48);
  if(!letter || !letter[0]) return;
  const float px=radius*1.55f;
  if(!font) font=ImGui::GetFont();
  auto* baked=font->GetFontBaked(px);
  const auto* glyph=baked->FindGlyph(ImWchar(letter[0]));
  // A fixed-atlas renderer returns its existing bake, not necessarily px.
  // Match RenderText's scale when converting visible glyph bounds to pixels.
  const float scale=px/baked->Size;
  const ImVec2 at{center.x-(glyph->X0+glyph->X1)*scale*.5f,
                  center.y-(glyph->Y0+glyph->Y1)*scale*.5f};
  const int first_letter_vertex=dl->VtxBuffer.Size;
  dl->AddText(font,px,at,IM_COL32(12,19,12,opacity(255)),letter);
  // RenderText truncates its origin to whole pixels, biasing the result left/up.
  // Center the emitted glyph itself so fractional DPI and icon sizes stay exact.
  if(dl->VtxBuffer.Size>first_letter_vertex) {
    ImVec2 lo=dl->VtxBuffer[first_letter_vertex].pos,hi=lo;
    for(int i=first_letter_vertex+1;i<dl->VtxBuffer.Size;i++) {
      const auto& p=dl->VtxBuffer[i].pos;
      lo.x=std::min(lo.x,p.x);lo.y=std::min(lo.y,p.y);
      hi.x=std::max(hi.x,p.x);hi.y=std::max(hi.y,p.y);
    }
    const ImVec2 offset{center.x-(lo.x+hi.x)*.5f,center.y-(lo.y+hi.y)*.5f};
    for(int i=first_letter_vertex;i<dl->VtxBuffer.Size;i++) {
      dl->VtxBuffer[i].pos.x+=offset.x;dl->VtxBuffer[i].pos.y+=offset.y;
    }
  }
}

void DrawButtonLabel(ImDrawList* dl,const ImVec2& left_center,float px,
                     unsigned int color,const char* text,ImFont* font) {
  if(!text || !text[0]) return;
  if(!font) font=ImGui::GetFont();
  const int first=dl->VtxBuffer.Size;
  dl->AddText(font,px,{left_center.x,left_center.y-px*.5f},color,text);
  if(dl->VtxBuffer.Size==first) return;
  float top=dl->VtxBuffer[first].pos.y,bottom=top;
  for(int i=first+1;i<dl->VtxBuffer.Size;i++) {
    top=std::min(top,dl->VtxBuffer[i].pos.y);
    bottom=std::max(bottom,dl->VtxBuffer[i].pos.y);
  }
  const float offset=left_center.y-(top+bottom)*.5f;
  for(int i=first;i<dl->VtxBuffer.Size;i++) dl->VtxBuffer[i].pos.y+=offset;
}

const char* Shoulder(bool right) {
  if (PlayStation()) return right ? "R1" : "L1";
  return right ? "RB" : "LB";
}

}  // namespace tabletennis::prompts
