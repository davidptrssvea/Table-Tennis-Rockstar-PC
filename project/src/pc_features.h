#pragma once
#include <functional>
#include <cstdint>
struct PPCContext;
namespace tabletennis::pc {
double OverlayAspect();
void ToggleAchievements();
void SetAchievementsToggle(std::function<void()> callback);
void SetWindow(void* window);
void SetSurfaceSize(uint32_t width, uint32_t height);
double CameraAspect(double original);
double VideoAspect(double original);
void FixUi(PPCContext& ctx, uint8_t* base, uint32_t matrix, double width);
void EndUi(PPCContext& ctx, uint8_t* base);
void Scissor(PPCContext& ctx);
void CheckQuit(PPCContext& ctx, uint8_t* base, uint32_t movie);
void RequestQuit();
}
