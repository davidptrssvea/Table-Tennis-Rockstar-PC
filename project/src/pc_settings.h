// Windows OPTIONS page; Flash entry/polling adapted from ThisIsAkill (MIT).
#pragma once
#include "achievement_ui.h"
#include <functional>
#include <rex/ui/imgui_dialog.h>
struct PPCContext;
namespace rex::ui { class Window; class KeyEvent; }
namespace tabletennis::pc_settings {
void SetOpenCallback(std::function<void()> callback);
void PollFrontend(PPCContext& ctx, uint8_t* base, uint32_t movie);
void PollPause(PPCContext& ctx, uint8_t* base, uint32_t pause);
bool FilterInput(uint32_t user, achievement_ui::PadSample& pad);
bool HandleKey(rex::ui::KeyEvent& event);
bool IsOpen();
class SettingsPage final : public rex::ui::ImGuiDialog {
 public:
  SettingsPage(rex::ui::ImGuiDrawer* drawer, rex::ui::Window* window);
  ~SettingsPage() override;
 private:
  void OnDraw(ImGuiIO& io) override;
  void Change(int delta);
  bool Save();
  struct State;
  std::unique_ptr<State> state_;
  rex::ui::Window* window_;
};
}
