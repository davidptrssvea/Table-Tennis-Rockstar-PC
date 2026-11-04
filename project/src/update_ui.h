#pragma once
#include <functional>
#include <string>
#include <rex/ui/imgui_dialog.h>
struct PPCContext;
namespace tabletennis::updates {
struct Status { std::string state,version,message; };
void Start(std::function<void(std::string)> notify);
void Stop();
Status GetStatus();
void Open();
void NotifyInFrontend(PPCContext& ctx,uint8_t* base,uint32_t movie);
class UpdateToast final : public rex::ui::ImGuiDialog {
 public:
  UpdateToast(rex::ui::ImGuiDrawer* drawer,std::string version);
  ~UpdateToast() override;
 private:
  void OnDraw(ImGuiIO& io) override;
  std::string version_;
  double started_=-1;
};
}
