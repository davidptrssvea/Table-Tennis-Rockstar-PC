// NATIVE RESOLUTION: the game sizes its frame from the rage::sysParams "width" (0x825CB6F8) and
// "height" (0x825CB70C), read at startup by sub_82228F30 with defaults of 1280x720. Setting them
// makes the game itself render at that size, menus and HUD included, instead of the engine
// scaling a 720p frame. Three limits of the original sized for 720p are lifted for this:
//  - the full-frame colour surface (see sub_8214F248 below);
//  - grcDevice::BeginTiledRendering's strip rectangles and strip order hold five strips
//    (StripRects / StripOrder, wired in through native_edits.json);
//  - EDRAM: a frame-sized colour and depth surface need more than the Xbox 360's 10 MiB above
//    about 1.3 million pixels. A GPU backend that emulates more (SDK patch 0025: Vulkan, 20 MiB)
//    says so through the edram_tile_limit setting; then larger frames are allowed and the tiled
//    frame uses the room for taller strips too (StripBudget): 4 strips at 1080p instead of 7.
//    With the original 10 MiB (another backend, or an SDK without the patch) frames stay at
//    sizes where both surfaces fit side by side, up to 1440x810.
#include "native_res.h"

#include "generated/default/tabletennis_init.h"

#include <rex/cvar.h>
#include <rex/logging.h>
#include <rex/system/kernel_state.h>

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <string>

REXCVAR_DEFINE_STRING(native_resolution, "", "Tabletennis",
                      "The size the game itself renders at, e.g. 1920x1080 (empty: the original "
                      "1280x720)");

extern "C" void __imp__sub_82228F30(PPCContext& ctx, uint8_t* base);
extern "C" void __imp__sub_8214F248(PPCContext& ctx, uint8_t* base);
extern "C" void sub_823594E0(PPCContext& ctx, uint8_t* base);

namespace {

constexpr uint32_t kWidthParam = 0x825CB6F8;
constexpr uint32_t kHeightParam = 0x825CB70C;
// D3D present parameters: back buffer width (+0), height (+4), format (+8), multisample (+16).
constexpr uint32_t kPresentParameters = 0x825EB900;
// The game's full-frame colour surface, used by its front-buffer render targets.
constexpr uint32_t kFrameColorSurface = 0x825EB984;

constexpr uint32_t kMaxStrips = 64;

// EDRAM tiles (80x16 pixels at 32 bits per pixel) a full-frame 32-bit surface takes.
uint32_t FrameTiles(uint32_t width, uint32_t height) {
  return (width + 79) / 80 * ((height + 15) / 16);
}

// The largest frame the game's memory holds. The game keeps 12 frame-sized 32-bit buffers (post
// processing copies, depth copy, crossfade screenshot, frontend target and others) in the Xbox
// 360's 512 MiB, 48 MiB per million pixels: at 1080p the lowest free memory in a match is 27 MiB,
// at 1440p allocations fail, and 4K (380 MiB of buffers) cannot fit on any backend. 2.4 million
// pixels (e.g. 2048x1152) keeps about 10 MiB spare. The EDRAM bases (11 bits: up to 2047 tiles,
// about 2.6 million pixels) are the second limit.
constexpr uint32_t kMaxPixels = 2400000;

// EDRAM tiles the GPU backend lets the game use (2048 when the SDK does not report it).
uint32_t EdramTiles() {
  const std::string text = rex::cvar::GetFlagByName("edram_tile_limit");
  const unsigned long tiles = text.empty() ? 2048 : std::strtoul(text.c_str(), nullptr, 10);
  return tiles >= 2048 ? uint32_t(tiles) : 2048;
}

// Parses native_resolution. Sizes the game cannot handle fall back to the original 720p.
bool RequestedSize(uint32_t& width, uint32_t& height) {
  const std::string text = REXCVAR_GET(native_resolution);
  const size_t x = text.find('x');
  if (text.empty() || x == std::string::npos) return false;
  width = uint32_t(std::strtoul(text.c_str(), nullptr, 10));
  height = uint32_t(std::strtoul(text.c_str() + x + 1, nullptr, 10));
  if (width < 640 || height < 360 || width > 4096 || height > 4096 ||
      width * height > kMaxPixels || FrameTiles(width, height) > 2047 ||
      FrameTiles(width, height) * 2 > EdramTiles()) {
    if (EdramTiles() > 2048) {
      REXLOG_WARN("Tabletennis: native resolution {} not supported (up to 2.4 million pixels, "
                  "e.g. 1920x1080 or 2048x1152); using 1280x720",
                  text);
    } else {
      REXLOG_WARN("Tabletennis: native resolution {} needs more EDRAM than this GPU backend "
                  "provides (up to 1440x810 here); using 1280x720",
                  text);
    }
    return false;
  }
  return !(width == 1280 && height == 720);
}

}  // namespace

namespace tabletennis::native {

uint32_t StripRects(uint8_t*) {
  // x1, y1, x2, y2 per strip.
  static const uint32_t rects = rex::system::kernel_memory()->SystemHeapAlloc(kMaxStrips * 16);
  return rects;
}

uint32_t StripOrder(uint8_t* base) {
  static const uint32_t order = [base] {
    const uint32_t mem = rex::system::kernel_memory()->SystemHeapAlloc(kMaxStrips * 4);
    for (uint32_t i = 0; i < kMaxStrips; ++i) REX_STORE_U32(mem + 4 * i, i);
    return mem;
  }();
  return order;
}

uint32_t StripBudget(uint32_t original) {
  // 15/8 keeps each strip surface below 2048 EDRAM tiles: the colour surface's base tile is the
  // depth surface's size and must fit the 11-bit base fields. Only with the larger EDRAM.
  static const bool larger = EdramTiles() >= 4096;
  return larger ? original / 8 * 15 : original;
}

}  // namespace tabletennis::native

// Startup parameter read: set "width" and "height" first.
extern "C" void sub_82228F30(PPCContext& ctx, uint8_t* base) {
  uint32_t width = 0, height = 0;
  if (RequestedSize(width, height)) {
    if (const uint32_t mem = rex::system::kernel_memory()->SystemHeapAlloc(32)) {
      const std::string w = std::to_string(width), h = std::to_string(height);
      std::memcpy(base + mem, w.c_str(), w.size() + 1);
      std::memcpy(base + mem + 16, h.c_str(), h.size() + 1);
      REX_STORE_U32(kWidthParam + 4, mem);
      REX_STORE_U32(kHeightParam + 4, mem + 16);
      REXLOG_INFO("Tabletennis: native resolution {}x{} (EDRAM {} tiles)", width, height, EdramTiles());
    }
  }
  __imp__sub_82228F30(ctx, base);
}

// sub_8214F248 creates a frame-sized depth surface (at EDRAM tile 0) and colour surface with the
// D3D EDRAM allocator, which refuses anything past the Xbox 360's 2048 tiles: above about 1.2
// million pixels the colour surface is NULL and every front-buffer render target breaks. Create
// it right after the depth surface instead, where the game puts its other full-frame colour
// surfaces too (the emulated EDRAM is large enough for both).
extern "C" void sub_8214F248(PPCContext& ctx, uint8_t* base) {
  __imp__sub_8214F248(ctx, base);
  if (REX_LOAD_U32(kFrameColorSurface)) return;
  const uint32_t width = REX_LOAD_U32(kPresentParameters);
  const uint32_t height = REX_LOAD_U32(kPresentParameters + 4);
  // D3DSURFACE_PARAMETERS: EDRAM base in tiles, hierarchical Z base (none), colour exponent bias.
  static const uint32_t params = rex::system::kernel_memory()->SystemHeapAlloc(16);
  const uint32_t edram_base = std::min<uint32_t>(FrameTiles(width, height), 2047);
  REX_STORE_U32(params, edram_base);
  REX_STORE_U32(params + 4, 0xFFFFFFFFu);
  REX_STORE_U32(params + 8, 0);
  PPCContext call = ctx;
  call.r3.u64 = width;
  call.r4.u64 = height;
  call.r5.u64 = REX_LOAD_U32(kPresentParameters + 8);
  call.r6.u64 = REX_LOAD_U32(kPresentParameters + 16);
  call.r7.u64 = params;
  sub_823594E0(call, base);
  REX_STORE_U32(kFrameColorSurface, call.r3.u32);
  if (!call.r3.u32) REXLOG_ERROR("Tabletennis: no frame colour surface for {}x{}", width, height);
}
