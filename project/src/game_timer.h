// game_timer.h - Game frame timer step length
//
// The game's frame timer (updated by sub_82187BE8) runs in fixed-step mode:
// every frame advances game time by the step in field +28 (1/60 s), so above
// 60 FPS the whole game speeds up. While running above 60 FPS a native edit
// at the start of the update sets that step to the length of the previous
// frame, keeping the game's own fixed-step code path (its variable-step mode
// also changes other timer state, and left the pause menu unresponsive).
// At 60 the original step is put back.

#pragma once

#include <cstdint>

namespace tabletennis::timing {

// Scale the step to the real frame time (true) or keep the game's original
// step (false). Switched by the frame pacer: true while running above 60 FPS.
void SetScaledStep(bool scaled);

// Length of the frame just submitted, from the frame pacer.
void SetFrameSeconds(double seconds);

// Called by the timer update with the timer's address, its +52 (fixed-step
// mode) value and its current +28 step bits. Returns true and sets *step_bits
// when +28 should be replaced (by the frame time, or by the original step
// when switching back).
bool StepOverride(uint32_t timer, uint32_t fixed_mode, uint32_t current_bits, uint32_t* step_bits);

// Puts the original step back right now (on the current frame), including
// the step-derived dt (+8) and measured step (+84), before the game reacts to
// e.g. a pause request. base: guest memory base.
void RestoreNow(uint8_t* base);

}  // namespace tabletennis::timing
