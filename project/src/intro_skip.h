#pragma once

#include <cstdint>

struct PPCContext;

namespace tabletennis::intro {
void Install();
void Uninstall();
// Called on the UI thread, including while the guest is loading its intro.
void PollGamepads();
bool ShowTransition();
// True once the startup sequence no longer needs PollGamepads/ShowTransition
// on the UI thread (UI thread only).
bool UiWorkFinished();
bool SkipMovies();
void AdvanceIntroMovie(PPCContext& ctx, uint8_t* base);
bool AdvanceFrontendMovie(PPCContext& ctx, uint8_t* base);
bool FrontendReady(PPCContext& ctx, uint8_t* base, uint32_t movie);
void FilterInput(PPCContext& ctx, uint8_t* base, uint32_t user, uint32_t state);
void ObserveFrontend(PPCContext& ctx, uint8_t* base);
}
