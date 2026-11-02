#include "generated/default/tabletennis_init.h"
#include "intro_skip.h"

#include <SDL3/SDL.h>
#include <array>
#include <atomic>
#include <chrono>
#include <vector>

namespace tabletennis::intro {
namespace {
enum class Phase { Watching, Skipping, Prompt, Drain, Done };
std::atomic<Phase> phase{Phase::Watching};
std::atomic<bool> frontend_started{false};
std::atomic<uint32_t> frontend_movie{0};
std::atomic<uint32_t> active_user{4};
std::array<std::atomic<bool>, 4> released{};
// These handles belong to the UI thread. The SDK only opens its own gamepad
// handles on the first guest input query, which happens AFTER the intro.
bool installed = false;
bool gamepad_subsystem = false;
std::vector<SDL_Gamepad*> startup_gamepads;
std::chrono::steady_clock::time_point next_enumeration{};

void CloseStartupGamepads() {
  for (auto* gamepad : startup_gamepads) SDL_CloseGamepad(gamepad);
  startup_gamepads.clear();
}

void Request() {
  auto expected = Phase::Watching;
  if (phase.compare_exchange_strong(expected, Phase::Skipping)) {
    REXLOG_INFO("Tabletennis: Intro skip requested by gamepad; continuing to main menu");
  }
}

bool SDLCALL WatchGamepad(void*, SDL_Event* event) {
  if (event->type == SDL_EVENT_GAMEPAD_BUTTON_DOWN ||
      event->type == SDL_EVENT_JOYSTICK_BUTTON_DOWN ||
      (event->type == SDL_EVENT_GAMEPAD_AXIS_MOTION &&
       (event->gaxis.axis == SDL_GAMEPAD_AXIS_LEFT_TRIGGER ||
        event->gaxis.axis == SDL_GAMEPAD_AXIS_RIGHT_TRIGGER) &&
       event->gaxis.value > 16384)) {
    Request();
  }
  return true;
}
}

void Install() {
  // SDK Setup initializes its event driver, but SDL_INIT_GAMEPAD is deferred
  // until the guest's first GetState. Initialize our own reference now.
  gamepad_subsystem = SDL_InitSubSystem(SDL_INIT_GAMEPAD);
  if (!gamepad_subsystem) {
    REXLOG_ERROR("Tabletennis: Early gamepad initialization failed: {}", SDL_GetError());
    return;
  }
  SDL_AddEventWatch(WatchGamepad, nullptr);
  installed = true;
  PollGamepads();
}

void Uninstall() {
  installed = false;
  SDL_RemoveEventWatch(WatchGamepad, nullptr);
  CloseStartupGamepads();
  if (gamepad_subsystem) SDL_QuitSubSystem(SDL_INIT_GAMEPAD);
  gamepad_subsystem = false;
}

void PollGamepads() {
  if (!installed) return;
  if (phase.load() == Phase::Done || phase.load() == Phase::Drain) {
    CloseStartupGamepads();
    return;
  }
  // Open actual devices before any guest polling, so SDL can generate their
  // button events during boot. Re-enumeration also covers hotplugging.
  const auto now = std::chrono::steady_clock::now();
  if (now >= next_enumeration) {
    next_enumeration = now + std::chrono::milliseconds(250);
    for (auto it = startup_gamepads.begin(); it != startup_gamepads.end();) {
      if (!SDL_GamepadConnected(*it)) {
        SDL_CloseGamepad(*it);
        it = startup_gamepads.erase(it);
      } else {
        ++it;
      }
    }
    int count = 0;
    auto* ids = SDL_GetGamepads(&count);
    for (int i = 0; i < count; ++i) {
      bool opened = false;
      for (auto* pad : startup_gamepads) {
        if (SDL_GetGamepadID(pad) == ids[i]) opened = true;
      }
      if (!opened) {
        if (auto* pad = SDL_OpenGamepad(ids[i])) {
          startup_gamepads.push_back(pad);
          REXLOG_INFO("Tabletennis: Startup gamepad opened before intro: {} (id {})",
                      SDL_GetGamepadName(pad), ids[i]);
        }
      }
    }
    SDL_free(ids);
  }
  SDL_UpdateGamepads();
  for (auto* pad : startup_gamepads) {
    for (int button = 0; button < SDL_GAMEPAD_BUTTON_COUNT; ++button) {
      if (SDL_GetGamepadButton(pad, static_cast<SDL_GamepadButton>(button))) Request();
    }
    if (SDL_GetGamepadAxis(pad, SDL_GAMEPAD_AXIS_LEFT_TRIGGER) > 3855 ||
        SDL_GetGamepadAxis(pad, SDL_GAMEPAD_AXIS_RIGHT_TRIGGER) > 3855) Request();
  }
}

bool SkipMovies() {
  return phase.load() == Phase::Skipping && !frontend_started.load();
}

bool ShowTransition() { return phase.load() == Phase::Skipping; }

bool UiWorkFinished() {
  const auto current = phase.load();
  if (current == Phase::Done || current == Phase::Drain) return startup_gamepads.empty();
  // At the startup save prompt input is already handled by FilterInput, and
  // the loading transition is no longer shown.
  return current == Phase::Prompt && frontend_started.load();
}

void AdvanceIntroMovie(PPCContext& ctx, uint8_t* base) {
  // These call sites request single-frame stepping, regardless of dt.
  // Run the normal frames to their Stop action, preserving the real completion
  // handshake instead of reporting that rendering has stopped prematurely.
  const PPCContext entry = ctx;
  const int count = SkipMovies() ? 240 : 1;
  for (int i = 0; i < count; ++i) {
    ctx = entry;
    REX_CALL_INDIRECT_FUNC(ctx.ctr.u32);
    if (count == 1) break;
    PPCContext query = entry;
    query.r3 = entry.r3;
    query.r4.u64 = 0x8205DFB8; // "done"
    sub_823F9280(query, base);
    if (query.r3.u32 && REX_LOAD_U32(query.r3.u32) == 1) break;
  }
}

bool FrontendReady(PPCContext& ctx, uint8_t* base, uint32_t movie) {
  PPCContext query = ctx;
  query.r3.u64 = movie;
  query.r4.u64 = 0x82060730; // STATE_CURRENT
  sub_823F9280(query, base);
  if (!query.r3.u32 || REX_LOAD_U32(query.r3.u32) != 3) return false;
  query = ctx;
  query.r3.u64 = movie;
  query.r4.u64 = 0x8205DC90; // STATE_ACTION
  sub_823F9280(query, base);
  return query.r3.u32 && REX_LOAD_U32(query.r3.u32) == 4; // INPUT_READY_ON
}

bool AdvanceFrontendMovie(PPCContext& ctx, uint8_t* base) {
  static thread_local bool advancing = false;
  const auto movie = ctx.r3.u32;
  // The retail message-dialog update advances its own movie from this call
  // site. First-time save creation (and other startup questions) must remain
  // visible and receive real input instead of our repeated Start presses.
  // This address belongs to the exact retail executable validated at setup.
  constexpr uint32_t kMessageDialogAdvance = 0x822EB0B4;
  if (!advancing && movie && frontend_started.load() && frontend_movie.load() &&
      movie != frontend_movie.load() && static_cast<uint32_t>(ctx.lr) == kMessageDialogAdvance) {
    auto expected = Phase::Skipping;
    if (phase.compare_exchange_strong(expected, Phase::Prompt)) {
      for (auto& value : released) value.store(false);
      REXLOG_INFO("Tabletennis: Startup dialog needs a choice; loading overlay and automatic Start paused");
    }
    return false;
  }
  if (advancing || phase.load() != Phase::Skipping || !movie ||
      movie != frontend_movie.load()) return false;
  const auto rate = REX_LOAD_U16(REX_LOAD_U32(movie + 24) + 44);
  if (!rate) return false;
  const PPCContext entry = ctx;
  advancing = true;
  // Yield between short batches so controller/profile loading can progress.
  for (int i = 0; i < 8; ++i) {
    ctx = entry;
    // The file stores the frame rate in 8.8 fixed point.
    ctx.f1.f64 = 256.0 / rate;
    ctx.r5.u64 = 1;
    sub_823F90C8(ctx, base);
    // Query outside Advance, after the VM has restored its global context.
    if (FrontendReady(ctx, base, movie)) break;
  }
  advancing = false;
  return true;
}

void FilterInput(PPCContext& ctx, uint8_t* base, uint32_t user, uint32_t state) {
  if (user >= 4 || !state || ctx.r3.u32 != 0) return;
  const uint16_t buttons = REX_LOAD_U16(state + 4);
  const bool pressed = buttons || REX_LOAD_U8(state + 6) > 30 ||
                       REX_LOAD_U8(state + 7) > 30;
  if (phase.load() == Phase::Watching && pressed) {
    active_user.store(user);
    Request();
  }
  const auto current = phase.load();
  if (current == Phase::Skipping) {
    if (pressed || active_user.load() >= 4) active_user.store(user);
    // Start opens the title screen's normal controller/profile path. Repeating
    // edges also covers a short press made before the title movie was loaded.
    const auto ticks = std::chrono::steady_clock::now().time_since_epoch();
    const bool pulse = (std::chrono::duration_cast<std::chrono::milliseconds>(ticks).count() / 150) % 2 == 0;
    REX_STORE_U32(state + 4, user == active_user.load() && pulse ? 0x00100000 : 0);
    REX_STORE_U32(state + 8, 0);
    REX_STORE_U32(state + 12, 0);

  } else if (current == Phase::Prompt || current == Phase::Drain) {
    // Resume each controller only after its startup button has been released.
    // A held A must not select Tournament when the main menu appears.
    if (!released[user].load()) {
      REX_STORE_U32(state + 4, 0);
      REX_STORE_U32(state + 8, 0);
      REX_STORE_U32(state + 12, 0);
      if (!pressed) released[user].store(true);
    }
  }
}

void ObserveFrontend(PPCContext& ctx, uint8_t* base) {
  frontend_started.store(true);
  if (phase.load() == Phase::Done || phase.load() == Phase::Drain) return;
  const auto movie = REX_LOAD_U32(ctx.r4.u32 + 92);
  if (!movie) return;
  frontend_movie.store(movie);
  PPCContext lookup = ctx;
  lookup.r3.u64 = movie;
  lookup.r4.u64 = 0x82060730;  // Retail image's existing "STATE_CURRENT" string.
  sub_823F9280(lookup, base);
  if (!lookup.r3.u32) return;
  const auto value = REX_LOAD_U32(lookup.r3.u32);
  static uint32_t last_state = ~0u;
  if (value != last_state) {
    last_state = value;
    REXLOG_INFO("Tabletennis: Startup Flash state {}", value);
  }
  if (value == 3 && FrontendReady(ctx, base, movie)) {
    for (auto& released_button : released) released_button.store(false);
    if (phase.exchange(Phase::Drain) == Phase::Watching) phase.store(Phase::Done);
    REXLOG_INFO("Tabletennis: Main menu reached; intro-skip input mapping finished");
  }
}
}
