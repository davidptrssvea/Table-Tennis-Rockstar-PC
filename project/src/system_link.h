#pragma once

#include <cstdint>

struct PPCContext;

// System link: the game's Xbox LIVE front end played over the local network.
//
// The retail game ships Rockstar's network layer with system link support behind a debug
// parameter ("-forcesyslink"): with it set, creating and finding sessions use LAN broadcast
// instead of Xbox LIVE matchmaking, and the rest of the online flow (lobby, match, results) is
// shared. The main menu's SYSTEM LINK entry opens that flow. The networking itself (addresses,
// sessions, sockets, broadcast discovery) is provided by the SDK's XNet layer.
namespace tabletennis::lan {

// True when system link is enabled (the system_link setting). When false the game stays the
// offline port: the online front end stays blocked, as in earlier versions.
#if defined(TT_COMMUNITY_NETWORK)
bool Enabled();
#else
inline bool Enabled() { return false; }
#endif

// Front-end frame (game thread): notices whether ONLINE or SYSTEM LINK was chosen.
void PollFrontend(PPCContext& ctx, uint8_t* base, uint32_t movie);

}  // namespace tabletennis::lan
