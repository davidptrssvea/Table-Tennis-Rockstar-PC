#undef NDEBUG
#include "../project/src/button_prompts.cpp"
#include <fstream>
#include <iostream>
#include <cassert>
#include <thread>
int main(int argc,char** argv) {
 using namespace tabletennis::prompts;
 rex::LogConfig log;log.log_to_console=true;rex::InitLogging(log);
 // Process-local SDL hints keep physical controllers out of this fixture.
 SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI,"0");
 SDL_SetHint(SDL_HINT_XINPUT_ENABLED,"0");
 SDL_SetHint(SDL_HINT_JOYSTICK_DIRECTINPUT,"0");
 SDL_SetHint(SDL_HINT_JOYSTICK_RAWINPUT,"0");
 SDL_SetHint(SDL_HINT_JOYSTICK_WGI,"0");
 SDL_SetHint(SDL_HINT_JOYSTICK_GAMEINPUT,"0");
 assert(SDL_Init(SDL_INIT_GAMEPAD));
 auto attach=[](bool ps) {
  SDL_VirtualJoystickDesc desc{};SDL_INIT_INTERFACE(&desc);
  desc.type=SDL_JOYSTICK_TYPE_GAMEPAD;desc.vendor_id=ps?0x054c:0x045e;
  desc.product_id=ps?0x0ce6:0x02ff;desc.name=ps?"Prompt test DualSense":"Prompt test Xbox One";
  desc.naxes=SDL_GAMEPAD_AXIS_COUNT;desc.nbuttons=SDL_GAMEPAD_BUTTON_COUNT;
  desc.axis_mask=(1u<<SDL_GAMEPAD_AXIS_COUNT)-1;desc.button_mask=(1u<<SDL_GAMEPAD_BUTTON_COUNT)-1;
  auto id=SDL_AttachVirtualJoystick(&desc);assert(id!=0);
  auto* pad=SDL_OpenGamepad(id);assert(pad);
  return pad;
 };
 auto* ps=attach(true);auto* xbox=attach(false);
 std::cout<<"Virtual controller types: "<<int(SDL_GetGamepadType(ps))<<", "<<int(SDL_GetGamepadType(xbox))<<"\n";
 assert(IsPlayStationPad(ps));assert(!IsPlayStationPad(xbox));
 auto* psJoystick=SDL_GetGamepadJoystick(ps);auto* xboxJoystick=SDL_GetGamepadJoystick(xbox);
 SDL_SetJoystickVirtualButton(psJoystick,SDL_GAMEPAD_BUTTON_SOUTH,true);SDL_UpdateGamepads();
 SetSetting("auto");assert(PlayStation());
 SDL_SetJoystickVirtualButton(psJoystick,SDL_GAMEPAD_BUTTON_SOUTH,false);
 SDL_SetJoystickVirtualButton(xboxJoystick,SDL_GAMEPAD_BUTTON_SOUTH,true);SDL_UpdateGamepads();
 SDL_Delay(300);Update();assert(!PlayStation());
 SDL_SetJoystickVirtualButton(xboxJoystick,SDL_GAMEPAD_BUTTON_SOUTH,false);
 SDL_SetJoystickVirtualAxis(psJoystick,SDL_GAMEPAD_AXIS_LEFT_TRIGGER,26000);SDL_UpdateGamepads();
 SDL_Delay(300);Update();assert(PlayStation());
 SetSetting("xbox");assert(!PlayStation());
 SetSetting("playstation");assert(PlayStation());
 SetSetting("xbox");
 auto psId=SDL_GetJoystickID(psJoystick),xboxId=SDL_GetJoystickID(xboxJoystick);
 SDL_CloseGamepad(ps);SDL_CloseGamepad(xbox);
 SDL_DetachVirtualJoystick(psId);SDL_DetachVirtualJoystick(xboxId);SDL_Quit();
 std::cout<<"AUTO button/trigger switching and forced Xbox/PlayStation passed\n";
 std::thread guest([] {for(int i=0;i<40;++i) SetSetting(i%2?"xbox":"playstation");});
 std::thread ui([] {for(int i=0;i<40;++i) SetSetting(i%2?"playstation":"xbox");});
 guest.join();ui.join();
 SetSetting("playstation");assert(PlayStation() && g_applied);
 SetSetting("xbox");assert(!PlayStation() && !g_applied);
 std::cout<<"Concurrent guest/UI setting changes converged correctly\n";

 if(argc<2) return 0;
 std::ifstream file(argv[1],std::ios::binary);
 auto word=[&] {uint32_t v=0;file.read(reinterpret_cast<char*>(&v),4);assert(file.good());return v;};
 auto count=word();assert(count==g_icons.size());
 for(uint32_t i=0;i<count;++i) {
  uint32_t w=word(),h=word(),pitch=word(),size=word();
  std::vector<uint8_t> bytes(size);file.read(reinterpret_cast<char*>(bytes.data()),size);assert(file.good());
  auto original=bytes;
  auto& icon=g_icons[i];assert(icon.width==w && icon.height==h);
  icon.playstation_hash=LinearFingerprint(icon.playstation,size_t(w/4)*(h/4)*16);
  assert(texture::Fits(pitch,w,h,size));
  assert(Fingerprint(bytes.data(),pitch,w,h)==icon.xbox_hash);
  Placement p{bytes.data(),size,pitch,int(i)};
  assert(Bring(p,true)==Brought::kWritten);
  assert(Fingerprint(bytes.data(),pitch,w,h)==icon.playstation_hash);
  assert(Bring(p,true)==Brought::kUnchanged);
  assert(Bring(p,false)==Brought::kWritten);
  assert(bytes==original);
  assert(Bring(p,false)==Brought::kUnchanged);
  bytes[0]^=1;auto unknown=bytes;
  assert(Bring(p,true)==Brought::kGone);assert(bytes==unknown);
  std::cout<<icon.name<<": PS replacement, exact Xbox restoration, reused memory guard passed\n";
 }
}
