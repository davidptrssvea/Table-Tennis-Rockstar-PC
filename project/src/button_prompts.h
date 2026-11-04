// button_prompts.h - Xbox or PlayStation button icons (BUTTON PROMPTS in PC SETTINGS).
//
// The game's menus show the Xbox 360 A, B, X, Y, LB and RB icons as textures inside each menu
// movie. SDK patch 0017 lets the game look at a texture just before it is uploaded to the GPU:
// an icon is recognised by a fingerprint of its data and, for the PlayStation style, replaced in
// guest memory by this project's own artwork (project/assets/button_prompts, generated into
// button_prompt_art.inc). The Xbox data seen there is kept, so switching back restores it. A
// switch rewrites every icon seen so far; the GPU memory watch then loads them again, so it
// applies at once in every menu.
//
// Setting: button_prompts = auto (the type of the controller used last), xbox, playstation.

#pragma once

#include <string>

struct ImDrawList;
struct ImVec2;
struct ImFont;
namespace rex::memory {
class Memory;
}
namespace rex::system {
class IGraphicsSystem;
}

namespace tabletennis::prompts {

// Registers the texture hook (SDK patch 0017). Call once at startup.
void Init(rex::memory::Memory* memory, rex::system::IGraphicsSystem* graphics);

// "auto", "xbox" or "playstation" (anything else is auto). Applies at once.
void SetSetting(const std::string& value);

// UI thread, once per frame: follows the controller in use (auto) and applies a change.
void Update();

// Whether PlayStation icons are shown now.
bool PlayStation();

// Our own overlays: draws a PlayStation face button for the Xbox letter ('A', 'B', 'X' or 'Y')
// and returns true, or returns false when Xbox icons are shown (the caller draws its own).
bool DrawFace(ImDrawList* dl, const ImVec2& center, float radius, const char* xbox_letter, float alpha);

// Shared overlay button: shaded Xbox cap or the selected PlayStation symbol.
void DrawButton(ImDrawList* dl, const ImVec2& center, float radius, unsigned int fill,
                const char* letter, ImFont* font, float alpha);

// Left-aligned text whose visible glyphs share the button's vertical center.
void DrawButtonLabel(ImDrawList* dl, const ImVec2& left_center, float px,
                     unsigned int color, const char* text, ImFont* font);

// "LB"/"RB" or "L1"/"R1".
const char* Shoulder(bool right);

}  // namespace tabletennis::prompts
