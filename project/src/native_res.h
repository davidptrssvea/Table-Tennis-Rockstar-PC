#pragma once
#include <cstdint>

namespace tabletennis::native {

// Guest address of the strip rectangles grcDevice::BeginTiledRendering (sub_82150500) writes and
// the two EndTiledRendering variants (sub_82150E28, sub_82150A38) read. The game's own array
// (0x825EB990) holds five strips and larger native resolutions need more, so all three use this
// bigger one instead (native_edits.json).
uint32_t StripRects(uint8_t* base);
// Guest address of the strip order BeginTiledRendering reads (the game's table, 0x825C906C, lists
// strips 0-4 only): 0, 1, 2, ... 63.
uint32_t StripOrder(uint8_t* base);
// The EDRAM budget BeginTiledRendering sizes its strips from (the original: half of the Xbox 360's
// 10 MiB for each of the strip's depth and colour surfaces). Every strip replays the frame's draw
// calls, so fewer, taller strips save CPU time; the emulated EDRAM (SDK patch 0025) has room.
uint32_t StripBudget(uint32_t original);

}  // namespace tabletennis::native
