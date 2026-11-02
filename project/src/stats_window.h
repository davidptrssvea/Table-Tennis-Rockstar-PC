// stats_window.h - Frame-time overlay drawn in its own window
//
// Any ImGui dialog makes the SDK presenter repaint on every display refresh,
// which competes with GPU emulation and caused frame-time spikes while the
// overlay was open. This overlay is a separate click-through window drawn
// with GDI, so the game's presentation is unaffected.

#pragma once

namespace tabletennis::stats_window {

// Shows the overlay over the top-left corner of game_window (an HWND).
void Show(void* game_window);
void Hide();
bool IsVisible();

}  // namespace tabletennis::stats_window
