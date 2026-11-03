#include <WinSock2.h>
#include <rex/cvar.h>
#include <rex/system/xsocket.h>
#include <chrono>
#include <cstdlib>
#include <iostream>
#include <thread>

// Runs only against the test's loopback relay. No game data or real profile is used.
int main(int argc, char** argv) {
  if (argc != 5) return 2;
  WSADATA wsa{};
  if (WSAStartup(MAKEWORD(2, 2), &wsa)) return 2;
  const std::string mode = argv[4];
  rex::cvar::SetFlagByName("online_server", argv[1]);
  rex::cvar::SetFlagByName("user_name", argv[2]);
  rex::cvar::SetFlagByName("online_relay_only", mode == "relay" ? "true" : "false");
  rex::system::net::SetOnline("room", argv[3]);
  bool paired = false, success = false;
  for (int i = 0; i < 300; ++i) {
    const auto s = rex::system::net::GetOnlineStatus();
    if (i % 10 == 0) std::cout << s.state << " room=" << s.room_players << std::endl;
    if (mode == "isolate") {
      if (s.room_players > 1 || !s.peers.empty()) break;
      if (i >= 40 && s.connected && s.room_players == 1) { success = true; break; }
    } else {
      if (s.connected && s.room_players == 2 && s.peers.size() == 1 &&
          s.peers[0].name != argv[2] && s.peers[0].rtt_ms >= 0 &&
          (mode != "relay" || !s.peers[0].direct)) {
        if (!paired) { paired = true; std::cout << "PAIRED " << s.peers[0].name << std::endl; }
        // Rejoin may complete between status samples. The harness also verifies
        // that the restarted server accepted a second HELLO from both names.
        if (mode != "reconnect" || i >= 100) { success = true; break; }
      }
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
  }
  if (success) std::this_thread::sleep_for(std::chrono::seconds(2));
  rex::system::net::SetOnline("off", "");
  std::this_thread::sleep_for(std::chrono::milliseconds(100));
  std::cout << (success ? "PASS" : "FAIL") << std::endl;
  // The engine's process-lifetime networking worker intentionally survives main.
  std::_Exit(success ? 0 : 1);
}
