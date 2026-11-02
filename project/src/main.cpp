// tabletennis - ReXGlue Recompiled Project

#include "generated/default/tabletennis_init.h"
#include <rex/cvar.h>
#include "tabletennis_app.h"

REXCVAR_DEFINE_BOOL(show_fps, false, "UI", "Show in-game FPS counter");
REXCVAR_DEFINE_INT32(framerate, 60, "Video", "Target framerate limit (60, 120, 144, 240, 0=unlimited)");
REXCVAR_DEFINE_BOOL(frame_log, false, "Video", "Write every game frame and present time to frametimes.csv next to the executable");
REXCVAR_DEFINE_BOOL(allow_tearing, true, "Video", "Present without waiting for display refresh. Off can drop the game below 60 FPS: a blocked present stalls GPU emulation");

#undef __imp__VdSwap
extern "C" REX_FUNC(__imp__VdSwap);

extern "C" void Tabletennis_VdSwap_Hook(PPCContext& ctx, uint8_t* base) {
  // Frame pacing happens at the start of the D3D Swap that calls this
  // (tabletennis::pacing::BeforeSwap).
  __imp__VdSwap(ctx, base);
}

extern "C" void __imp__Tabletennis_VdSwap_Hook(PPCContext& ctx, uint8_t* base) {
  Tabletennis_VdSwap_Hook(ctx, base);
}

// Returns SignedInLocally for user 0, allowing offline profiles/tournaments, but NOT Xbox Live.
extern "C" void Tabletennis_XamUserGetSigninState_Hook(PPCContext& ctx, uint8_t* base) {
  uint32_t user_index = ctx.r3.u32;
  if (user_index == 0) {
    ctx.r3.u64 = 1; // eXUserSigninState_SignedInLocally
  } else {
    ctx.r3.u64 = 0; // eXUserSigninState_NotSignedIn
  }
}

extern "C" void __imp__Tabletennis_XamUserGetSigninState_Hook(PPCContext& ctx, uint8_t* base) {
  Tabletennis_XamUserGetSigninState_Hook(ctx, base);
}

// Denies Xbox Live multiplayer privileges (XPRIVILEGE_MULTIPLAYER_SESSIONS),
// cleanly graying out and disabling the "Xbox Live" option in the main menu.
extern "C" void Tabletennis_XamUserCheckPrivilege_Hook(PPCContext& ctx, uint8_t* base) {
  uint32_t result_ptr = ctx.r5.u32;
  if (result_ptr != 0) {
    REX_STORE_U32(result_ptr, 0); // 0 = Denied / Disabled
  }
  ctx.r3.u64 = 0;
}

extern "C" void __imp__Tabletennis_XamUserCheckPrivilege_Hook(PPCContext& ctx, uint8_t* base) {
  Tabletennis_XamUserCheckPrivilege_Hook(ctx, base);
}

// Blocks any attempts to open the sign-in / Xbox Live UI, preventing infinite hangs.
extern "C" void Tabletennis_XamShowSigninUI_Hook(PPCContext& ctx, uint8_t* base) {
  REXLOG_INFO("Tabletennis: Blocked XamShowSigninUI (Xbox Live is disabled in offline PC port).");
  ctx.r3.u64 = 1223; // ERROR_CANCELLED
}

extern "C" void __imp__Tabletennis_XamShowSigninUI_Hook(PPCContext& ctx, uint8_t* base) {
  Tabletennis_XamShowSigninUI_Hook(ctx, base);
}

namespace {
struct EarlyInit {
  EarlyInit() {
    rex::cvar::SetFlagByName("protect_zero", "false");
    rex::cvar::SetFlagByName("gpu_allow_invalid_fetch_constants", "true");
  }
} g_early_init;
}  // namespace

REX_DEFINE_APP(tabletennis, TabletennisApp::Create)
