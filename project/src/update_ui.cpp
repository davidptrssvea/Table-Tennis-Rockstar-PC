#include "update_ui.h"
#include "pc_settings.h"
#include "achievement_ui.h"
#include "frame_pacer.h"
#include "settings_store.h"
#include "generated/default/tabletennis_init.h"
#include <rex/filesystem.h>
#include <rex/logging.h>
#include <imgui.h>
#include <windows.h>
#include <chrono>
#include <condition_variable>
#include <fstream>
#include <mutex>
#include <thread>

namespace tabletennis::updates {
namespace {
std::mutex mutex;
std::condition_variable wake;
std::thread watcher;
bool stopping=false;
Status status;
std::string notified,installed;
std::function<void(std::string)> notify;
std::string Read(const std::filesystem::path& path) {
  std::ifstream file(path,std::ios::binary);std::string result;
  for(char c;result.size()<8192 && file.get(c);) result+=c;
  return result;
}
bool Launch(const wchar_t* args) {
  auto executable=rex::filesystem::GetExecutableFolder()/"tabletennis_updater.exe";
  if(!std::filesystem::is_regular_file(executable)) return false;
  std::wstring command=L"\""+executable.wstring()+L"\" "+args;
  STARTUPINFOW startup{};startup.cb=sizeof(startup);PROCESS_INFORMATION process{};
  auto directory=executable.parent_path().wstring();
  if(!CreateProcessW(executable.c_str(),command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,directory.c_str(),&startup,&process)) return false;
  CloseHandle(process.hThread);CloseHandle(process.hProcess);return true;
}
}
void Start(std::function<void(std::string)> callback) {
  Stop();
  auto folder=rex::filesystem::GetExecutableFolder();
  auto fields=settings_store::Parse(Read(folder/"settings.ini"));
  installed=Read(folder/"install-version.txt");
  while(!installed.empty() && (installed.back()=='\r'||installed.back()=='\n')) installed.pop_back();
  {
    std::lock_guard lock(mutex);stopping=false;notify=std::move(callback);notified.clear();status={};
  }
  // Disk/network work lives outside guest/render threads. The helper caches
  // successful checks for six hours and never installs from this code path.
  if(fields["check_updates"]!="false") Launch(L"--check-only");
  watcher=std::thread([path=folder.parent_path()/".updates/status.txt"] {
    for(;;) {
      auto fields=settings_store::Parse(Read(path));
      Status next{fields["state"],fields["version"],fields["message"]};
      std::unique_lock lock(mutex);
      if(next.version==installed && next.state=="available") next.state="up_to_date";
      status=std::move(next);
      if(wake.wait_for(lock,std::chrono::seconds(3),[]{return stopping;})) break;
    }
  });
}
void Stop() {
  {std::lock_guard lock(mutex);stopping=true;notify={};}
  wake.notify_all();if(watcher.joinable()) watcher.join();
}
Status GetStatus() {std::lock_guard lock(mutex);return status;}
void Open() {
  if(!Launch(L"")) MessageBoxW(nullptr,L"Update support is missing. Install the current full release once to enable updates.",L"Table Tennis updates",MB_OK|MB_ICONINFORMATION);
}
void NotifyInFrontend(PPCContext& ctx,uint8_t* base,uint32_t movie) {
  if(!movie || pc_settings::IsOpen() || achievement_ui::IsPanelOpen()) return;
  Status next=GetStatus();
  if(next.state!="available" || next.version.empty()) return;
  {
    std::lock_guard lock(mutex);if(next.version==notified) return;
  }
  auto call=ctx;call.r3.u64=movie;call.r4.u64=0x82060730;sub_823F9280(call,base);
  if(!call.r3.u32 || REX_LOAD_U32(call.r3.u32)!=3) return;
  std::function<void(std::string)> callback;
  {std::lock_guard lock(mutex);if(next.version==notified || !notify) return;notified=next.version;callback=notify;}
  callback(next.version);
}
UpdateToast::UpdateToast(rex::ui::ImGuiDrawer* drawer,std::string version)
  :ImGuiDialog(drawer),version_(std::move(version)) {pacing::SetUiActive(true);}
UpdateToast::~UpdateToast() {pacing::SetUiActive(false);}
void UpdateToast::OnDraw(ImGuiIO& io) {
  if(started_<0) started_=ImGui::GetTime();
  if(ImGui::GetTime()-started_>7) {Close();return;}
  const float s=io.DisplaySize.y/720.f,x=io.DisplaySize.x-610*s,y=io.DisplaySize.y-132*s;
  auto* draw=ImGui::GetForegroundDrawList();
  draw->AddRectFilled({x,y},{io.DisplaySize.x-22*s,y+104*s},IM_COL32(20,36,50,245),8*s);
  std::string title="UPDATE "+version_+" AVAILABLE";
  draw->AddText(achievement_ui::HeadlineFont(26*s),26*s,{x+20*s,y+16*s},IM_COL32(255,209,0,255),title.c_str());
  draw->AddText(achievement_ui::BodyFont(),18*s,{x+20*s,y+59*s},IM_COL32(235,237,240,255),"OPTIONS > PC SETTINGS > UPDATES");
}
}
