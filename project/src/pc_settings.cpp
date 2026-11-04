#include "pc_settings.h"
#include "settings_store.h"
#include "button_prompts.h"
#include "frame_pacer.h"
#include "stats_window.h"
#include "generated/default/tabletennis_init.h"
#include <rex/cvar.h>
#include <rex/filesystem.h>
#include <rex/logging.h>
#include <rex/system/kernel_state.h>
#include <rex/ui/window.h>
#include <rex/ui/ui_event.h>
#include <rex/ui/virtual_key.h>
#include <cfloat>
#include <imgui.h>
#include <windows.h>
#include <array>
#include <atomic>
#include <cstring>
#include <fstream>
#include <mutex>
#include <vector>

REXCVAR_DECLARE(bool, show_fps);
REXCVAR_DECLARE(bool, achievement_sound);
namespace tabletennis::pc_settings {
namespace {
std::atomic<bool> open{false};
std::atomic<uint16_t> pressed{0}, held{0};
std::mutex mutex;
std::function<void()> open_callback;
struct PadState { uint16_t last=0, ignore=0; bool drain=false; };
std::array<PadState,4> pads{};
uint16_t Buttons(const achievement_ui::PadSample& p) {
  auto b=p.buttons;
  if(p.thumb_ly>16000) b|=1;
  if(p.thumb_ly<-16000) b|=2;
  if(p.thumb_lx<-16000) b|=4;
  if(p.thumb_lx>16000) b|=8;
  return b;
}
void RequestOpen() {
  if(achievement_ui::IsPanelOpen()) return;
  std::function<void()> callback;
  {
    std::lock_guard lock(mutex);
    if(!open_callback || open.exchange(true)) return;
    callback=open_callback;
    pressed.store(0); held.store(0);
    for(auto& p:pads) { p.ignore=p.last; p.drain=true; }
  }
  REXLOG_INFO("Tabletennis: PC SETTINGS opened from OPTIONS");
  callback();
}
uint32_t FlagName(uint8_t* base) {
  // Both polling sites execute on the guest menu thread.
  static uint32_t name=0;
  if(!name) {
    constexpr char flag[]="GLOBAL_PC_SETTINGS";
    name=rex::system::kernel_memory()->SystemHeapAlloc(sizeof(flag));
    if(name) std::memcpy(base+name,flag,sizeof(flag));
  }
  return name;
}
std::string ReadIni() {
  auto path=rex::filesystem::GetExecutableFolder()/"settings.ini";
  if(!std::filesystem::exists(path)) return {};
  std::ifstream file(path,std::ios::binary);
  if(!file) throw std::runtime_error("Could not read settings.ini");
  return {std::istreambuf_iterator<char>(file),{}};
}
struct Row { const char* key; const char* label; const char* help;
  std::vector<const char*> values, labels; int selected=0; bool restart=false; };
}
bool IsOpen() { return open.load(); }
void SetOpenCallback(std::function<void()> callback) {
  std::lock_guard lock(mutex); open_callback=std::move(callback);
}
void PollFrontend(PPCContext& ctx,uint8_t* base,uint32_t movie) {
  if(!movie) return;
  auto name=FlagName(base); if(!name) return;
  auto call=ctx; call.r3.u64=movie;call.r4.u64=name;sub_823F9280(call,base);
  if(!call.r3.u32) return;
  auto slot=call.r3.u32;call=ctx;call.r3.u64=slot;sub_823FA5C8(call,base);
  if(call.r3.s32<=0) return;
  call=ctx;call.r3.u64=movie;call.r4.u64=name;sub_823F9318(call,base);
  if(call.r3.u32) { REX_STORE_U32(call.r3.u32,0); REX_STORE_U32(call.r3.u32+4,3); }
  RequestOpen();
}
void PollPause(PPCContext& ctx,uint8_t* base,uint32_t pause) {
  if(!pause) return;
  auto name=FlagName(base);if(!name) return;
  auto call=ctx;call.r3.u64=pause;call.r4.u64=name;sub_823246E8(call,base);
  if(call.r3.u32&255) RequestOpen();
}
bool FilterInput(uint32_t user,achievement_ui::PadSample& pad) {
  if(user>=pads.size()) return false;
  std::lock_guard lock(mutex);
  auto& p=pads[user]; auto b=Buttons(pad);
  if(open.load()) {
    p.ignore&=b;
    const auto usable=uint16_t(b&~p.ignore);
    pressed.fetch_or(usable&~p.last);
    p.last=usable;
    uint16_t h=0;for(const auto& other:pads) h|=other.last;
    held.store(h);
    pad={};return true;
  }
  p.last=b;
  if(p.drain) {
    if(!b && pad.left_trigger<30 && pad.right_trigger<30) p.drain=false;
    pad={};return true;
  }
  return false;
}
bool HandleKey(rex::ui::KeyEvent& event) {
  if(!IsOpen()) return false;
  using K=rex::ui::VirtualKey;
  uint16_t b=0;
  switch(event.virtual_key()) {
    case K::kUp:b=1;break; case K::kDown:b=2;break;
    case K::kLeft:b=4;break;case K::kRight:b=8;break;
    case K::kReturn:case K::kSpace:b=4096;break;
    case K::kEscape:case K::kBack:b=8192;break;
    default:break;
  }
  pressed.fetch_or(b);event.set_handled(true);return true;
}
struct SettingsPage::State {
  std::vector<Row> rows{
    {"resolution","RESOLUTION","Window and 3D rendering size. Applies after restarting the game.",
     {"720p","1080p","1440p","4k","2560x1080","3440x1440","3840x1600"},
     {"1280 X 720","1920 X 1080","2560 X 1440","3840 X 2160","2560 X 1080 (21:9)","3440 X 1440 (21:9)","3840 X 1600 (21:9)"},1,true},
    {"fullscreen","FULLSCREEN","Switch between a window and fullscreen immediately.",{"false","true"},{"OFF","ON"}},
    {"aa","ANTI-ALIASING","Smooth jagged edges. Applies after restarting the game.",{"none","fxaa","fxaa_extreme"},{"OFF","FXAA","FXAA EXTREME"},1,true},
    {"post","SHARPENING / SCALING","Choose the output filter. Applies after restarting the game.",{"bilinear","cas","fsr"},{"BILINEAR","CAS","FSR"},2,true},
    {"show_fps","FPS / FRAME TIMES","Open the separate F10 timing window. Gameplay stays paced at 60 FPS.",{"false","true"},{"OFF","ON"}},
    {"button_prompts","BUTTON ICONS","AUTO follows the controller in use. Changes apply immediately.",{"auto","xbox","playstation"},{"AUTO","XBOX","PLAYSTATION"}},
    {"achievement_sound","ACHIEVEMENT SOUND","Play a sound when an achievement unlocks.",{"false","true"},{"OFF","ON"},1}
  };
  int row=0,repeat_direction=0;
  double repeat_at=0;
  std::string error;
};
SettingsPage::SettingsPage(rex::ui::ImGuiDrawer* drawer,rex::ui::Window* window)
  :ImGuiDialog(drawer),state_(std::make_unique<State>()),window_(window) {
  pacing::SetUiActive(true);
  try {
    auto values=settings_store::Parse(ReadIni());
    for(auto& row:state_->rows) for(size_t i=0;i<row.values.size();++i)
      if(values[row.key]==row.values[i]) row.selected=int(i);
  } catch(const std::exception& e) {state_->error=e.what();}
  state_->rows[1].selected=window_->IsFullscreen()?1:0;
  state_->rows[4].selected=stats_window::IsVisible()?1:0;
  state_->rows[6].selected=REXCVAR_GET(achievement_sound)?1:0;
}
SettingsPage::~SettingsPage() {
  pacing::SetUiActive(false);open.store(false);
  REXLOG_INFO("Tabletennis: PC SETTINGS closed; game-only presentation restored");
}
void SettingsPage::Change(int delta) {
  auto& row=state_->rows[state_->row];
  row.selected=(row.selected+delta+int(row.values.size()))%int(row.values.size());
  const std::string value=row.values[row.selected];
  switch(state_->row) {
    case 1:window_->SetFullscreen(value=="true");break;
    case 4:
      REXCVAR_SET(show_fps,value=="true");
      if(value=="true") stats_window::Show(window_->GetNativeWindowHandle());else stats_window::Hide();break;
    case 5:prompts::SetSetting(value);break;
    case 6:REXCVAR_SET(achievement_sound,value=="true");break;
  }
}
bool SettingsPage::Save() {
  auto path=rex::filesystem::GetExecutableFolder()/"settings.ini";
  auto temp=path;temp+=".pc-settings.tmp";
  try {
    settings_store::Values updates;
    for(const auto& row:state_->rows) updates[row.key]=row.values[row.selected];
    auto text=settings_store::Merge(ReadIni(),updates);
    { std::ofstream file(temp,std::ios::binary|std::ios::trunc);
      file.exceptions(std::ios::failbit|std::ios::badbit);file.write(text.data(),text.size());file.close(); }
    if(!MoveFileExW(temp.c_str(),path.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH))
      throw std::runtime_error("Could not replace settings.ini (Windows error "+std::to_string(GetLastError())+")");
    REXLOG_INFO("Tabletennis: PC SETTINGS saved to settings.ini");return true;
  } catch(const std::exception& e) {state_->error=e.what();REXLOG_ERROR("PC SETTINGS save failed: {}",e.what());return false;}
}
void SettingsPage::OnDraw(ImGuiIO& io) {
  auto b=pressed.exchange(0);auto h=uint16_t(held.load()&15);
  if(!h) state_->repeat_direction=0;
  else if(h!=state_->repeat_direction) {state_->repeat_direction=h;state_->repeat_at=ImGui::GetTime()+.4;}
  else if(ImGui::GetTime()>state_->repeat_at) {b|=h;state_->repeat_at=ImGui::GetTime()+.13;}
  if(b&1) state_->row=(state_->row+int(state_->rows.size())-1)%int(state_->rows.size());
  if(b&2) state_->row=(state_->row+1)%int(state_->rows.size());
  if(b&4) Change(-1);
  if(b&(8|4096)) Change(1);
  if((b&8192) && Save()) { Close();return; }
  const float s=io.DisplaySize.y/720.f,w=io.DisplaySize.x;
  const float x=(w-920*s)/2,y=95*s,r=x+920*s;
  auto* dl=ImGui::GetForegroundDrawList();
  dl->AddRectFilled({0,0},io.DisplaySize,IM_COL32(0,0,0,185));
  dl->AddRectFilled({x,y},{r,y+65*s},IM_COL32(30,56,75,255),18*s);
  dl->AddRectFilled({x,y+70*s},{r,y+490*s},IM_COL32(12,13,16,245));
  auto text=[&](float px,float xx,float yy,ImU32 color,const char* value,bool heavy=true){
    auto* font=heavy?achievement_ui::HeadlineFont(px):achievement_ui::BodyFont();
    dl->AddText(font,px,{xx,yy},color,value);
  };
  text(38*s,x+28*s,y+12*s,IM_COL32(255,209,0,255),"PC SETTINGS");
  for(size_t i=0;i<state_->rows.size();++i) {
    const auto& row=state_->rows[i];const float yy=y+84*s+i*46*s;
    const bool selected=int(i)==state_->row;
    if(selected) dl->AddRectFilled({x+12*s,yy-3*s},{r-12*s,yy+40*s},IM_COL32(47,49,51,255));
    const auto color=selected?IM_COL32(255,209,0,255):IM_COL32(205,207,211,255);
    text(23*s,x+28*s,yy,color,row.label);
    const auto label=std::string(row.labels[row.selected])+(row.restart?" *":"");
    auto* font=achievement_ui::HeadlineFont(23*s);
    auto size=font->CalcTextSizeA(23*s,FLT_MAX,0,label.c_str());
    text(23*s,r-28*s-size.x,yy,color,label.c_str());
    if(ImGui::IsMouseClicked(0) && io.MousePos.x>x && io.MousePos.x<r && io.MousePos.y>yy && io.MousePos.y<yy+40*s) {
      state_->row=int(i);Change(1);
    }
  }
  dl->PushClipRect({x+20*s,y+414*s},{r-20*s,y+484*s},true);
  text(19*s,x+28*s,y+416*s,IM_COL32(220,220,220,255),state_->rows[state_->row].help,false);
  text(18*s,x+28*s,y+449*s,IM_COL32(190,190,190,255),"* AFTER RESTART     |     FRAME PACING: 60 FPS",false);
  dl->PopClipRect();
  dl->AddRectFilled({x,y+495*s},{r,y+543*s},IM_COL32(205,208,210,255));
  const auto ink=IM_COL32(20,22,25,255);
  text(21*s,x+24*s,y+505*s,ink,"D-PAD: SELECT / CHANGE");
  auto action=[&](float xx,const char* letter,const char* label,ImU32 color) {
    ImVec2 center{xx,y+519*s};
    prompts::DrawButton(dl,center,13*s,color,letter,
                        achievement_ui::HeadlineFont(13*s*1.65f),1.f);
    text(21*s,xx+21*s,y+505*s,ink,label);
  };
  action(x+415*s,"A","CHANGE",IM_COL32(90,186,40,255));
  action(x+640*s,"B","SAVE & BACK",IM_COL32(210,44,44,255));
  if(!state_->error.empty()) text(16*s,x+12*s,y+552*s,IM_COL32(255,130,110,255),state_->error.c_str(),false);
}
}
// Keep the original pause update intact, like the other guest callback wrappers.
extern "C" void __imp__sub_823132F0(PPCContext& ctx,uint8_t* base);
extern "C" void sub_823132F0(PPCContext& ctx,uint8_t* base) {
  tabletennis::pc_settings::PollPause(ctx,base,ctx.r3.u32);
  __imp__sub_823132F0(ctx,base);
}
