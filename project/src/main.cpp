// tabletennis - ReXGlue Recompiled Project

#include "generated/default/tabletennis_init.h"
#include <rex/cvar.h>
#include "frame_pacing.h"
#include <chrono>
#include <thread>
#if defined(_M_X64) || defined(__x86_64__)
#include <immintrin.h>
#endif

#include "tabletennis_app.h"

REXCVAR_DEFINE_BOOL(show_fps, false, "UI", "Show in-game FPS counter");
REXCVAR_DEFINE_INT32(framerate, 60, "Video", "Target framerate limit (60, 120, 144, 240, 0=unlimited)");

#undef __imp__VdSwap
extern "C" REX_FUNC(__imp__VdSwap);

extern "C" void Tabletennis_VdSwap_Hook(PPCContext& ctx, uint8_t* base) {
  tabletennis::frames::BeforeSubmit(REXCVAR_GET(framerate));
  // Count guest submissions, never ImGui redraws or duplicate host presents.
  __imp__VdSwap(ctx, base);
  const auto now = tabletennis::frames::Clock::now();
  tabletennis::frames::submissions.Record(now);
  static std::mutex log_mutex;
  std::unique_lock log_lock(log_mutex, std::try_to_lock);
  if (!log_lock.owns_lock()) return;
  static auto next_log = now + std::chrono::seconds(2);
  if (now >= next_log) {
    next_log = now + std::chrono::seconds(2);
    const auto stats = tabletennis::frames::submissions.Read(now);
    REXLOG_INFO("Game submissions: {:.1f} FPS, mean {:.2f} ms, p95 {:.2f} ms, max {:.2f} ms ({} intervals)",
                stats.fps, stats.mean_ms, stats.p95_ms, stats.max_ms, stats.intervals);
  }
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
