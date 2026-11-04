// Adapted from ThisIsAkill/Table-Tennis-Linux, ded80880299e26db86289326d31f1a8a2479fc82 (MIT).
#include "generated/default/tabletennis_init.h"
#include "photo_mode.h"
#include "achievement_ui.h"
#include "frame_pacer.h"
#include "pc_features.h"
#include "pc_settings.h"
#include "button_prompts.h"
#include "photo_image.h"
#include "photo_camera.h"
#include <rex/ui/ui_event.h>

#include <imgui.h>
#include <rex/filesystem.h>
#include <rex/logging.h>
#include <rex/system/kernel_state.h>
#include <rex/system/xsocket.h>
#include <rex/ui/presenter.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <ctime>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include <optional>
#include <cstdio>

// The game's camera hook: every frame the current camera matrix (r4: right, up, back and
// position rows of four floats; y is up, the camera looks along -back) passes through here on its
// way to the renderer. Photo mode hands it a matrix of its own instead.
extern "C" void __imp__sub_821B5A68(PPCContext& ctx, uint8_t* base);
// The camera's per-frame update, which writes that matrix.
extern "C" void __imp__sub_821709F8(PPCContext& ctx, uint8_t* base);

namespace tabletennis::photo {
namespace {

// The game's timer: +80 is the time warp the focus shots' slow motion uses (1 = normal speed).
constexpr uint32_t kTimeWarp = 0x825CAEB8 + 80;
constexpr uint32_t kMatchTimer = 0x825CAEB8;
constexpr float kPhotoTimeScale = 1.0e-8f;
// rage::sysParam "nohuddraw" (name at 0x825D0704, value pointer at +4): any value hides the HUD.
constexpr uint32_t kNoHudValue = 0x825D0708;
constexpr uint32_t kNoHudName = 0x8205B0F8;

constexpr uint16_t kL3 = 0x0040, kR3 = 0x0080, kLB = 0x0100, kRB = 0x0200;
constexpr uint16_t kA = 0x1000, kB = 0x2000, kX = 0x4000, kY = 0x8000;
constexpr float kMoveSpeed = 2.5f;     // m/s
constexpr float kLookSpeed = 1.8f;     // rad/s
// Bounds that keep the free camera inside the hall (some venues have more courts around).
constexpr float kMaxDistance = 15.0f;  // horizontally from the table
constexpr float kMaxHeight = 8.0f;

using Clock = std::chrono::steady_clock;

struct Vec3 {
  float x, y, z;
};

std::mutex g_mutex;
std::function<rex::ui::Presenter*()> g_presenter;
std::function<void()> g_show_overlay;
std::atomic<bool> g_running{false}, g_overlay_open{false}, g_capture_busy{false};
std::thread g_capture;
std::atomic<int64_t> g_last_hud{0};
std::recursive_mutex g_camera_mutex;
uint8_t* g_guest_base=nullptr;
std::mutex g_input_mutex;
std::array<uint16_t,4> g_last_buttons{};
std::array<bool,4> g_drain{};
std::atomic<uint32_t> g_keys{0};
std::atomic<bool> g_escape_drain{false};
std::atomic<int64_t> g_last_pad{0};
std::atomic<uint64_t> g_photo_sequence{0};
int64_t NowNs() {return std::chrono::duration_cast<std::chrono::nanoseconds>(Clock::now().time_since_epoch()).count();}
bool MatchInPlay() {const auto stamp=g_last_hud.load();return stamp && NowNs()-stamp<500000000;}
std::atomic<bool> g_active{false};
std::atomic<bool> g_toggle_requested{false};
std::atomic<bool> g_help{true};
std::atomic<bool> g_reset_requested{false};
std::atomic<bool> g_save_requested{false};
// Pad input for the camera (normalised), written by FilterInput, read by the camera hook.
std::atomic<float> g_move_x{0}, g_move_z{0}, g_look_x{0}, g_look_y{0}, g_rise{0};
std::atomic<float> g_boost{1};
std::atomic<float> g_mouse_dx{0}, g_mouse_dy{0};  // from the overlay (UI thread)
std::atomic<float> g_zoom{1}, g_zoom_steps{0}, g_pad_zoom{0};
// Messages for the overlay.
std::string g_notice;
Clock::time_point g_notice_until{};

// Camera state (game thread).
Vec3 g_pos{}, g_start_pos{};
float g_yaw = 0, g_pitch = 0, g_start_yaw = 0, g_start_pitch = 0;
bool g_camera_ready = false;
Clock::time_point g_last_frame{};
uint32_t g_scratch = 0;
uint32_t g_matrix = 0;  // the camera data matrix (guest address)
uint32_t g_saved_warp = 0, g_saved_nohud = 0;
bool g_applied = false;  // freeze + HUD applied in guest memory

void Notice(const std::string& text) {
  std::function<void()> callback;
  {std::lock_guard lock(g_mutex);
    if(!g_running.load()) return;
    g_notice = text;
    g_notice_until = Clock::now() + std::chrono::seconds(3);
    if(!g_overlay_open.exchange(true)) callback=g_show_overlay;
  }
  if(callback) callback();
}

float LoadF(uint8_t* base, uint32_t address) {
  const uint32_t bits = REX_LOAD_U32(address);
  float f;
  std::memcpy(&f, &bits, 4);
  return f;
}
void StoreF(uint8_t* base, uint32_t address, float f) {
  uint32_t bits;
  std::memcpy(&bits, &f, 4);
  REX_STORE_U32(address, bits);
}

bool InNetworkSession() {
#if defined(TT_COMMUNITY_NETWORK)
  uint8_t kid[8];
  return rex::system::net::CurrentSessionKey(kid);
#else
  return false;
#endif
}

float Stick(int16_t v) {
  const float f = std::clamp(float(v) / 32767.0f, -1.0f, 1.0f);
  const float dead = 0.18f;
  if (std::fabs(f) < dead) return 0.0f;
  return (f - std::copysign(dead, f)) / (1.0f - dead);
}

void SavePicture() {
  std::function<rex::ui::Presenter*()> getter;
  {std::lock_guard lock(g_mutex);getter=g_presenter;}
  rex::ui::Presenter* presenter = getter ? getter() : nullptr;
  if (!presenter) {
    Notice("CANNOT SAVE PICTURES HERE");
    return;
  }
  if(g_capture_busy.exchange(true)) {Notice("PICTURE IS STILL SAVING");return;}
  if(g_capture.joinable()) g_capture.join();
  const double aspect=tabletennis::pc::CameraAspect(16.0/9.0);
  g_capture=std::thread([presenter,aspect] {
   try {
    rex::ui::RawImage image;
    if (!presenter->CaptureGuestOutput(image) || !tabletennis::photo::image::Valid({image.width,image.height,image.stride,image.data})) {
      Notice("COULD NOT CAPTURE THE PICTURE");
      g_capture_busy.store(false);
      return;
    }
    char stamp[64],name[112];
    const std::time_t now = std::time(nullptr);
    std::tm local{};localtime_s(&local,&now);
    std::strftime(stamp,sizeof(stamp),"tabletennis-%Y%m%d-%H%M%S",&local);
    std::snprintf(name,sizeof(name),"%s-%llu-%llu.png",stamp,
      static_cast<unsigned long long>(NowNs()),static_cast<unsigned long long>(g_photo_sequence.fetch_add(1)));
    const auto path = rex::filesystem::GetExecutableFolder() / "photos" / name;
    const tabletennis::photo::image::Image view{image.width,image.height,image.stride,image.data};
    if (tabletennis::photo::image::WritePng(path,view,aspect)) {
      REXLOG_INFO("Tabletennis: photo saved to {} ({}x{})", path.string(), image.width,
                  tabletennis::photo::image::OutputHeight(view,aspect));
      Notice("SAVED photos/" + std::string(name) + " (" + std::to_string(image.width) + "x" +
             std::to_string(tabletennis::photo::image::OutputHeight(view,aspect)) + ")");
    } else {
      Notice("COULD NOT SAVE THE PICTURE");
    }
   } catch(const std::exception& error) {REXLOG_ERROR("Photo save failed: {}",error.what());Notice("COULD NOT SAVE THE PICTURE");}
   g_capture_busy.store(false);
  });
}

// Enter/leave (game thread, from the camera hook): freeze time, hide the HUD, and back.
void Apply(uint8_t* base, bool on) {
  if (on == g_applied) return;
  if (on) {
    g_saved_warp = REX_LOAD_U32(kTimeWarp);
    g_saved_nohud = REX_LOAD_U32(kNoHudValue);
  } else {
    REX_STORE_U32(kTimeWarp, g_saved_warp);
    REX_STORE_U32(kNoHudValue, g_saved_nohud);
    REXLOG_INFO("Photo mode: restored original timer and HUD values");
    g_camera_ready = false;
  }
  g_applied = on;
}

void BuildMatrix(uint8_t* base, uint32_t out, uint32_t original) {
  std::memcpy(base + out, base + original, 64);  // keeps the w components
  const float cp = std::cos(g_pitch), sp = std::sin(g_pitch);
  const Vec3 look{cp * std::sin(g_yaw), sp, cp * std::cos(g_yaw)};
  const Vec3 back{-look.x, -look.y, -look.z};
  // right = world up x back, up = back x right (the game's right-handed rows).
  const float rl = std::sqrt(back.z * back.z + back.x * back.x);
  const Vec3 right{back.z / rl, 0.0f, -back.x / rl};
  const Vec3 up{back.y * right.z - back.z * right.y, back.z * right.x - back.x * right.z,
                back.x * right.y - back.y * right.x};
  const Vec3 rows[4] = {right, up, back, g_pos};
  for (int r = 0; r < 4; ++r) {
    StoreF(base, out + r * 16 + 0, rows[r].x);
    StoreF(base, out + r * 16 + 4, rows[r].y);
    StoreF(base, out + r * 16 + 8, rows[r].z);
  }
}


}  // namespace

void Init(std::function<rex::ui::Presenter*()> presenter,std::function<void()> show_overlay) {
  std::lock_guard lock(g_mutex);g_presenter=std::move(presenter);g_show_overlay=std::move(show_overlay);g_running.store(true);
}
void Shutdown() {
  g_running.store(false);
  {std::lock_guard lock(g_camera_mutex);g_active.store(false);if(g_guest_base) Apply(g_guest_base,false);}
  if(g_capture.joinable()) g_capture.join();
  {std::lock_guard lock(g_mutex);g_presenter={};g_show_overlay={};}
}
void ObserveHud(const char* name) {
  if(g_running.load() && std::memcmp(name,"ServeMeterX",sizeof("ServeMeterX"))==0) g_last_hud.store(NowNs());
}

void RequestToggle() {
  if(!g_running.load()) return;
  if(!g_active.load()) {
    if(InNetworkSession()) {Notice("PHOTO MODE IS NOT AVAILABLE IN NETWORK MATCHES");return;}
    if(!MatchInPlay() || pc_settings::IsOpen() || achievement_ui::IsPanelOpen()) {Notice("PHOTO MODE WORKS DURING AN OFFLINE MATCH");return;}
  }
  g_toggle_requested.store(true);
}
bool HandleKey(rex::ui::KeyEvent& event,bool down) {
  if(event.virtual_key()==rex::ui::VirtualKey::kEscape && g_escape_drain.load()) {
    if(!down) g_escape_drain.store(false);event.set_handled(true);return true;
  }
  if(event.virtual_key()==rex::ui::VirtualKey::kF9 && down && event.prev_state()) {event.set_handled(true);return true;}
  if(!g_active.load()) return false;
  if(event.is_alt_pressed() || event.is_super_pressed()) return false;
  const bool tap=down&&!event.prev_state();
  using K=rex::ui::VirtualKey;
  uint32_t bit=0;
  switch(event.virtual_key()) {
    case K::kW:case K::kUp:bit=1;break;case K::kS:case K::kDown:bit=2;break;
    case K::kA:case K::kLeft:bit=4;break;case K::kD:case K::kRight:bit=8;break;
    case K::kQ:bit=16;break;case K::kE:bit=32;break;
    case K::kShift:bit=64;break;case K::kControl:bit=128;break;
    case K::kPrior:if(down) g_zoom_steps.fetch_add(event.prev_state()?.25f:1.f);break;
    case K::kNext:if(down) g_zoom_steps.fetch_sub(event.prev_state()?.25f:1.f);break;
    case K::kEscape:if(tap) {g_escape_drain.store(true);RequestToggle();}break;
    case K::kReturn:if(tap) g_save_requested.store(true);break;
    case K::kR:if(tap) g_reset_requested.store(true);break;
    case K::kSpace:if(tap) g_help.store(!g_help.load());break;
    case K::kF9:return false;
    case K::kF10:return false;
    default:break;
  }
  if(down) g_keys.fetch_or(bit);else g_keys.fetch_and(~bit);
  event.set_handled(true);return true;
}

bool Active() { return g_active.load(); }

bool FilterInput(uint32_t user, achievement_ui::PadSample& pad) {
  if(user>=4) return false;
  std::lock_guard lock(g_input_mutex);
  const uint16_t pressed=pad.buttons&~g_last_buttons[user];g_last_buttons[user]=pad.buttons;
  const bool sticks_clicked =
      (pad.buttons & (kL3 | kR3)) == (kL3 | kR3) && (pressed & (kL3 | kR3));
  if (user==0 && sticks_clicked) RequestToggle();
  if (!g_active.load() && !g_toggle_requested.load()) {
    if(g_drain[user]) {
      if(!pad.buttons && pad.left_trigger<30 && pad.right_trigger<30 && std::abs(int(pad.thumb_lx))<7000 && std::abs(int(pad.thumb_ly))<7000 && std::abs(int(pad.thumb_rx))<7000 && std::abs(int(pad.thumb_ry))<7000) g_drain[user]=false;
      pad={};return true;
    }
    return false;
  }
  g_drain[user]=true;
  if(user!=0) {pad={};return true;}
  g_last_pad.store(NowNs());
  if (pressed & kB) RequestToggle();
  if (pressed & kA) g_help.store(!g_help.load());
  if (pressed & kY) g_reset_requested.store(true);
  if (pressed & kX) g_save_requested.store(true);
  g_move_x.store(Stick(pad.thumb_lx));
  g_move_z.store(Stick(pad.thumb_ly));
  g_look_x.store(Stick(pad.thumb_rx));
  g_look_y.store(Stick(pad.thumb_ry));
  g_rise.store((float(pad.right_trigger)-float(pad.left_trigger))/255.0f);
  g_boost.store((pad.buttons & kRB) ? 3.0f : (pad.buttons & kLB) ? 0.3f : 1.0f);
  g_pad_zoom.store(float(bool(pad.buttons & 0x0001))-float(bool(pad.buttons & 0x0002)));
  // The frozen game sees an idle pad.
  pad = achievement_ui::PadSample{};
  return true;
}

Overlay::Overlay(rex::ui::ImGuiDrawer* drawer) : rex::ui::ImGuiDialog(drawer) {pacing::SetUiActive(true);}
Overlay::~Overlay() {
  pacing::SetUiActive(false);
  std::function<void()> callback;
  {std::lock_guard lock(g_mutex);g_overlay_open.store(false);
    if(g_running.load() && (g_active.load() || Clock::now()<g_notice_until)) {g_overlay_open.store(true);callback=g_show_overlay;}
  }
  if(callback) callback();
}

void Overlay::OnDraw(ImGuiIO& io) {
  std::string notice;
  {
    std::lock_guard lock(g_mutex);
    if (Clock::now() < g_notice_until) notice = g_notice;
  }
  const bool active = g_active.load();
  if ((!active && notice.empty()) || !g_running.load()) {Close();return;}
  if(io.AppFocusLost) {g_keys.store(0);g_mouse_dx.store(0);g_mouse_dy.store(0);}
  if (active && (io.MouseDown[0] || io.MouseDown[1])) {
    g_mouse_dx.fetch_add(io.MouseDelta.x);
    g_mouse_dy.fetch_add(io.MouseDelta.y);
  }
  if(active && !io.AppFocusLost) g_zoom_steps.fetch_add(io.MouseWheel);
  ImDrawList* dl = ImGui::GetForegroundDrawList();
  const float s=std::min(io.DisplaySize.y/720.f,io.DisplaySize.x/820.f);
  const float width=650*s, height=264*s, margin=18*s;
  const bool help=active && g_help.load();
  const ImVec2 origin{margin,io.DisplaySize.y-margin-(help?height:0)};
  ImFont* body=achievement_ui::BodyFont();
  const ImU32 text=IM_COL32(236,240,246,255),muted=IM_COL32(156,167,185,255);
  const ImU32 gold=IM_COL32(255,210,36,255),rule=IM_COL32(255,255,255,24);
  auto at=[&](float x,float y) {return ImVec2(origin.x+x*s,origin.y+y*s);};
  auto label=[&](float x,float y,const char* value,float size,ImU32 color) {
    dl->AddText(body,size*s,at(x,y),color,value);
  };
  if(help) {
    dl->AddRectFilled(at(0,5),at(650,269),IM_COL32(0,0,0,70));
    dl->AddRectFilled(at(0,0),at(650,264),IM_COL32(13,18,27,242));
    dl->AddRect(at(0,0),at(650,264),IM_COL32(135,155,182,65),0,0,1*s);
    dl->AddRectFilled(at(16,14),at(20,40),gold);
    dl->AddText(achievement_ui::HeadlineFont(25*s),25*s,at(30,12),gold,"PHOTO MODE");
    label(225,20,"FREE CAMERA",11,muted);
    dl->AddRectFilled(at(520,12),at(634,42),IM_COL32(255,210,36,19));
    char zoom[32];std::snprintf(zoom,sizeof(zoom),"ZOOM  %.2fx",g_zoom.load());
    label(534,20,zoom,14,gold);
    dl->AddLine(at(16,52),at(634,52),rule,1*s);
    label(16,63,"CONTROLLER",11,gold);
    label(316,63,"KEYBOARD & MOUSE",11,gold);
    dl->AddLine(at(300,64),at(300,185),rule,1*s);
    const char* actions[]={"Move","Look","Height","Zoom","Speed"};
    const char* pad[]={"Left stick","Right stick",prompts::PlayStation()?"L2 / R2":"LT / RT","D-pad up / down",prompts::PlayStation()?"L1 slow / R1 fast":"LB slow / RB fast"};
    const char* keyboard[]={"W A S D / arrows","Mouse drag","Q / E","Wheel / Page Up / Down","Shift fast / Ctrl slow"};
    for(int row=0;row<5;row++) {
      const float y=85+21*row;
      label(16,y,actions[row],14,muted);label(84,y,pad[row],15,text);
      label(316,y,actions[row],14,muted);label(384,y,keyboard[row],15,text);
    }
    dl->AddLine(at(16,194),at(634,194),rule,1*s);
    const char* letters[]={"A","B","X","Y"};
    const char* labels[]={"HIDE HELP","LEAVE","SAVE PICTURE","RESET VIEW"};
    const char* keys[]={"SPACE","F9 / ESC","ENTER","R"};
    const unsigned colors[]={IM_COL32(79,185,24,255),IM_COL32(215,42,42,255),IM_COL32(42,100,225,255),IM_COL32(235,190,24,255)};
    for(int i=0;i<4;i++) {
      const float x=16+156*i;
      prompts::DrawButton(dl,at(x+12.8f,219),12.8f*s,colors[i],letters[i],achievement_ui::HeadlineFont(25*s),1);
      prompts::DrawButtonLabel(dl,at(x+34,219),13*s,text,labels[i],body);
      label(x+34,231,keys[i],11,muted);
    }
    label(16,248,"F9 / BOTH STICK CLICKS: LEAVE PHOTO MODE",9,muted);
  }
  if(!notice.empty()) {
    const float wrap=width-32*s;
    const auto size=body->CalcTextSizeA(14*s,1e6f,wrap,notice.c_str());
    const float toast_height=size.y+24*s;
    const ImVec2 p{origin.x,origin.y-toast_height-(help?10*s:0)};
    dl->AddRectFilled(p,{p.x+width,p.y+toast_height},IM_COL32(13,18,27,242));
    dl->AddRect(p,{p.x+width,p.y+toast_height},IM_COL32(255,210,36,70),0,0,1*s);
    dl->AddText(body,14*s,{p.x+16*s,p.y+12*s},gold,notice.c_str(),nullptr,wrap);
  }
}

}  // namespace tabletennis::photo

using namespace tabletennis::photo;

extern "C" void sub_821B5A68(PPCContext& ctx, uint8_t* base) {
  std::lock_guard lock(g_camera_mutex);
  if(!g_running.load()) {__imp__sub_821B5A68(ctx,base);return;}
  g_guest_base=base;
  const uint32_t matrix = ctx.r4.u32;
  if(g_active.load() && InNetworkSession()) {g_active.store(false);Notice("PHOTO MODE CLOSED FOR NETWORK PLAY");}
  if (g_toggle_requested.exchange(false)) {
    if (g_active.load()) {
      g_active.store(false);
      Notice("PHOTO MODE OFF");
      g_keys.store(0);g_mouse_dx.store(0);g_mouse_dy.store(0);g_save_requested.store(false);
      g_move_x.store(0);g_move_z.store(0);g_look_x.store(0);g_look_y.store(0);g_rise.store(0);
      g_pad_zoom.store(0);g_zoom_steps.store(0);
      REXLOG_INFO("Tabletennis: photo mode off");
    } else if (InNetworkSession()) {
      Notice("PHOTO MODE IS NOT AVAILABLE IN NETWORK MATCHES");
    } else if (!MatchInPlay()) {
      Notice("PHOTO MODE WORKS DURING A MATCH");
    } else {
      g_active.store(true);
      g_zoom.store(1);g_zoom_steps.store(0);g_pad_zoom.store(0);
      g_help.store(true);
      Notice("PHOTO MODE ON");
      REXLOG_INFO("Tabletennis: photo mode on");
    }
  }
  const bool active = g_active.load();
  Apply(base, active);
  if (!active || !matrix) {
    __imp__sub_821B5A68(ctx, base);
    return;
  }
  // Keep the action frozen: the game writes the time warp for slow motion, so every frame.
  StoreF(base, kTimeWarp, kPhotoTimeScale);
  REX_STORE_U32(kNoHudValue, kNoHudName);
  if (!g_scratch) g_scratch = rex::system::kernel_memory()->SystemHeapAlloc(64, 16);
  if (!g_scratch) {
    g_active.store(false);Apply(base,false);Notice("PHOTO MODE COULD NOT INITIALIZE");
    __imp__sub_821B5A68(ctx, base);
    return;
  }
  const auto now = Clock::now();
  for(uint32_t i=0;i<16;i++) if(!std::isfinite(LoadF(base,matrix+i*4))) {
    g_active.store(false);Apply(base,false);Notice("PHOTO MODE CAMERA IS UNAVAILABLE");__imp__sub_821B5A68(ctx,base);return;
  }
  if (!g_camera_ready) {
    // Start from the game's camera (the producer hook only takes over once this is set).
    g_start_pos = {LoadF(base, matrix + 48), LoadF(base, matrix + 52), LoadF(base, matrix + 56)};
    const Vec3 back{LoadF(base, matrix + 32), LoadF(base, matrix + 36), LoadF(base, matrix + 40)};
    g_start_pitch = std::asin(std::clamp(-back.y, -1.0f, 1.0f));
    g_start_yaw = std::atan2(-back.x, -back.z);
    g_pos = g_start_pos;
    g_yaw = g_start_yaw;
    g_pitch = g_start_pitch;
    g_zoom.store(1);g_zoom_steps.store(0);
    g_last_frame = now;
    g_camera_ready = true;
  }
  g_matrix=matrix;
  if (g_save_requested.exchange(false)) SavePicture();
  __imp__sub_821B5A68(ctx, base);
}

// The camera's update (camera object in r3), which writes the matrix the frame renders with.
// While photo mode is on, the free camera's matrix replaces it right after.
extern "C" void sub_821709F8(PPCContext& ctx, uint8_t* base) {
  __imp__sub_821709F8(ctx, base);
  std::lock_guard lock(g_camera_mutex);
  if (!g_active.load() || !g_camera_ready || !g_matrix || !g_scratch) return;
  const auto now = Clock::now();
  if (g_reset_requested.exchange(false)) {
    g_pos = g_start_pos;
    g_yaw = g_start_yaw;
    g_pitch = g_start_pitch;
    g_zoom.store(1);g_zoom_steps.store(0);
  }
  const float dt = std::min(0.1f, std::chrono::duration<float>(now - g_last_frame).count());
  g_last_frame = now;
  // Look: right stick and mouse drag.
  const auto keys=g_keys.load();
  if(NowNs()-g_last_pad.load()>250000000) {g_move_x.store(0);g_move_z.store(0);g_look_x.store(0);g_look_y.store(0);g_rise.store(0);g_boost.store(1);g_pad_zoom.store(0);}
  const float zoom_steps=g_zoom_steps.exchange(0)+g_pad_zoom.load()*dt*8;
  g_zoom.store(camera::ZoomStep(g_zoom.load(),zoom_steps));
  g_yaw -= g_look_x.load() * kLookSpeed * dt + g_mouse_dx.exchange(0.0f) * 0.004f;
  g_pitch += g_look_y.load() * kLookSpeed * dt - g_mouse_dy.exchange(0.0f) * 0.004f;
  g_pitch = std::clamp(g_pitch, -1.45f, 1.45f);
  // Move: left stick on the ground plane along the view, triggers up and down.
  const float speed = kMoveSpeed * ((keys&64)?3.f:(keys&128)?.3f:g_boost.load()) * dt;
  const float fx = std::sin(g_yaw), fz = std::cos(g_yaw);
  const float mx=std::clamp(g_move_x.load()+float(bool(keys&8))-float(bool(keys&4)),-1.f,1.f);
  const float mz=std::clamp(g_move_z.load()+float(bool(keys&1))-float(bool(keys&2)),-1.f,1.f);
  g_pos.x += (fx * mz - fz * mx) * speed;
  g_pos.z += (fz * mz + fx * mx) * speed;
  g_pos.y = std::clamp(g_pos.y + std::clamp(g_rise.load()+float(bool(keys&32))-float(bool(keys&16)),-1.f,1.f) * speed, 0.1f, kMaxHeight);
  const float distance = std::sqrt(g_pos.x * g_pos.x + g_pos.z * g_pos.z);
  if (distance > kMaxDistance) {
    g_pos.x *= kMaxDistance / distance;
    g_pos.z *= kMaxDistance / distance;
  }
  BuildMatrix(base, g_scratch, g_matrix);
  std::memcpy(base + g_matrix, base + g_scratch, 64);
}

extern "C" void __imp__sub_823F9318(PPCContext& ctx,uint8_t* base);
extern "C" void sub_823F9318(PPCContext& ctx,uint8_t* base) {
  if(ctx.r4.u32) tabletennis::photo::ObserveHud(reinterpret_cast<const char*>(base+ctx.r4.u32));
  __imp__sub_823F9318(ctx,base);
}

// The slow-motion controller overwrites time warp before updating this timer.
// Apply the photo scale at the actual timer update, including its minimum delta.
// Keep delta positive so the original reciprocal-delta calculation stays finite.
extern "C" void __imp__sub_82187BE8(PPCContext& ctx,uint8_t* base);
extern "C" void sub_82187BE8(PPCContext& ctx,uint8_t* base) {
  std::lock_guard lock(g_camera_mutex);
  if(!g_running.load() || !g_active.load() || ctx.r3.u32!=kMatchTimer) {
    __imp__sub_82187BE8(ctx,base);
    return;
  }
  const uint32_t minimum=REX_LOAD_U32(kMatchTimer+36);
  StoreF(base,kTimeWarp,kPhotoTimeScale);
  StoreF(base,kMatchTimer+36,kPhotoTimeScale);
  __imp__sub_82187BE8(ctx,base);
  REX_STORE_U32(kMatchTimer+36,minimum);
}

// Apply zoom where the renderer rebuilds a view's perspective projection.
// Restore the source FOV immediately so repeated frames never compound zoom.
extern "C" void __imp__sub_82153598(PPCContext& ctx,uint8_t* base);
extern "C" void sub_82153598(PPCContext& ctx,uint8_t* base) {
  std::lock_guard lock(g_camera_mutex);
  const uint32_t view=ctx.r3.u32;
  if(!g_running.load() || !g_active.load() || !view) {
    __imp__sub_82153598(ctx,base);return;
  }
  const uint32_t original=REX_LOAD_U32(view+824);
  StoreF(base,view+824,camera::ZoomFov(LoadF(base,view+824),g_zoom.load()));
  __imp__sub_82153598(ctx,base);
  REX_STORE_U32(view+824,original);
}
