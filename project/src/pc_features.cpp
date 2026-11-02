#include "generated/default/tabletennis_init.h"
#include "pc_features.h"
#include "screen_layout.h"
#include <atomic>
#include <array>
#include <vector>
#include <cstring>
#include <algorithm>
#include <windows.h>
#include <rex/logging.h>
namespace tabletennis::pc {
namespace {
std::atomic<HWND> game_window{nullptr};
std::atomic<bool> quit_posted{false};
std::atomic<double> aspect{16.0 / 9.0};
std::atomic<uint32_t> device{0};
struct Clip { uint32_t device; int left,top,right,bottom; std::array<int,4> old; };
thread_local std::vector<Clip> clips;
void EmitScissor(const PPCContext& ctx, uint8_t* base, uint32_t dev, const std::array<int,4>& rect) {
  PPCContext call=ctx;call.r3.u64=dev;call.r4.s64=rect[0];call.r5.s64=rect[1];call.r6.s64=rect[2];call.r7.s64=rect[3];
  sub_823537B0(call,base);
}
double Aspect() { return aspect.load(); }
void StoreFloat(uint8_t* base, uint32_t address, float value) {
  PPCRegister data{}; data.f32 = value; REX_STORE_U32(address, data.u32);
}
}
void SetWindow(void* window) { game_window.store(static_cast<HWND>(window)); }
void SetSurfaceSize(uint32_t width, uint32_t height) {
  if(!width || !height) return; // keep the last valid ratio while minimized
  aspect.store(layout::Aspect(width,height));
  static uint64_t last=0;
  const uint64_t size=(uint64_t(width)<<32)|height;
  if(size!=last) {last=size;REXLOG_INFO("Display surface: {}x{}; camera aspect {:.6f}",width,height,aspect.load());}
}
double CameraAspect(double original) {
  return layout::Camera(original,Aspect());
}
double VideoAspect(double original) { return original / layout::UiScale(Aspect()); }
void FixUi(PPCContext& ctx, uint8_t* base, uint32_t matrix, double width) {
  const float scale = float(layout::UiScale(Aspect()));
  const uint32_t dev=device.load();
  if (scale > 0.9999f || width <= 0 || !dev) { clips.push_back({}); return; }
  const auto tl=REX_LOAD_U32(dev+11524),br=REX_LOAD_U32(dev+11528);
  const int x=REX_LOAD_U32(dev+12808), y=REX_LOAD_U32(dev+12812);
  const int w=REX_LOAD_U32(dev+12816), h=REX_LOAD_U32(dev+12820);
  const int inset=int(w*(1.0f-scale)*0.5f+0.5f);
  clips.push_back({dev,x+inset,y,x+w-inset,y+h,{int(tl&0x7fff),int((tl>>16)&0x7fff),int(br&0x7fff),int((br>>16)&0x7fff)}});
  EmitScissor(ctx,base,dev,clips.back().old);
  StoreFloat(base, matrix + 24, scale);
  StoreFloat(base, matrix + 40, float(width * (1.0 - scale) * 0.5));

}
void EndUi(PPCContext& ctx, uint8_t* base) {
  if(clips.empty()) return;
  const auto clip=clips.back();clips.pop_back();
  if(clip.device) EmitScissor(ctx,base,clip.device,clip.old);
}
void Scissor(PPCContext& ctx) {
  device.store(ctx.r3.u32);
  if(clips.empty() || !clips.back().device) return;
  const auto& c=clips.back();
  const auto rect=layout::Intersect({ctx.r4.s32,ctx.r5.s32,ctx.r6.s32,ctx.r7.s32},{c.left,c.top,c.right,c.bottom});
  ctx.r4.s64=rect.left;ctx.r5.s64=rect.top;ctx.r6.s64=rect.right;ctx.r7.s64=rect.bottom;
}
void CheckQuit(PPCContext& ctx, uint8_t* base, uint32_t movie) {
  if (!movie || quit_posted.load()) return;
  auto read = [&](uint32_t name) -> uint32_t {
    PPCContext query=ctx;query.r3.u64=movie;query.r4.u64=name;
    sub_823F9280(query,base);
    return query.r3.u32 ? REX_LOAD_U32(query.r3.u32) : ~0u;
  };
  const auto current=read(0x82060730);
  if(current==91) {RequestQuit();return;}
  if(current!=3 || read(0x8205DC90)==4) return;
  // Private guest stack space for names absent from the retail XEX strings.
  PPCContext query=ctx;query.r1.u32-=512;
  REX_STORE_U32(query.r1.u32,ctx.r1.u32);
  constexpr char name[]="target_STATE_NEW";
  std::memcpy(base+query.r1.u32+128,name,sizeof(name));
  query.r3.u64=movie;query.r4.u64=query.r1.u32+128;
  sub_823F9280(query,base);
  if(query.r3.u32 && REX_LOAD_U32(query.r3.u32)==91) RequestQuit();
}
void RequestQuit() {
  if(quit_posted.exchange(true)) return;
  REXLOG_INFO("Quit to desktop selected in the game menu");
  if (HWND window = game_window.load()) PostMessageW(window, WM_CLOSE, 0, 0);
}
}
