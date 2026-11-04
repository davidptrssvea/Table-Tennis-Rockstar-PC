// Adapted from ThisIsAkill/Table-Tennis-Linux, ded80880299e26db86289326d31f1a8a2479fc82 (MIT).
#pragma once

#include <cstdint>
#include <functional>

#include <rex/ui/imgui_dialog.h>

namespace rex::ui {
class Presenter;
class KeyEvent;
}

namespace tabletennis::achievement_ui {
struct PadSample;
}

// PHOTO MODE: during a match (not a network one), click both sticks (or press F9) to freeze the
// action, hide the HUD and fly a free camera around the table; X saves the picture to photos/
// next to the game (at the render resolution, so RENDER SCALE 2X/3X makes bigger photos).
namespace tabletennis::photo {

// Where pictures are captured from (the presenter's guest output, without overlays).
void Init(std::function<rex::ui::Presenter*()> presenter, std::function<void()> show_overlay);
void Shutdown();
bool HandleKey(rex::ui::KeyEvent& event, bool down);

// Pad poll hook (player one): toggles photo mode and, while it is on, takes the pad away from the
// game. Returns true when the pad was changed.
bool FilterInput(uint32_t user, achievement_ui::PadSample& pad);

// F9: toggle from the keyboard.
void RequestToggle();
void ObserveHud(const char* name);

bool Active();

// The controls and status in the corner while photo mode is on; mouse drag looks around.
class Overlay : public rex::ui::ImGuiDialog {
 public:
  explicit Overlay(rex::ui::ImGuiDrawer* drawer);
  ~Overlay() override;

 protected:
  void OnDraw(ImGuiIO& io) override;
};

}  // namespace tabletennis::photo
