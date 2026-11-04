# Experimental Table Tennis relay

Adapted from ThisIsAkill/Table-Tennis-Linux (MIT); see licenses/Table-Tennis-Linux.

Local test only: `python server/ttlink_server.py --bind 127.0.0.1 --port 27360`.
The server implements the upstream TTV1 protocol, public/private rooms, peer
introduction and relay. It does not need a game ISO or any game assets.

**Host & copy invitation** starts a player-owned relay only when selected. No
permanent public service is started by setup or ordinary offline play. Manual
server configuration remains under Advanced; networking is enabled by Host/Join
or the online checkbox. The server sees
client IP addresses and the configured player names/time-zone offsets. The
upstream project documents its service separately; Windows Internet play,
spectating and short-drop match recovery still require end-to-end validation.

On Windows the relay selects SelectorEventLoop, so an ICMP error caused by a
departing UDP peer does not leave the affected Proactor reader permanently idle.
UDP and TCP use the configured port (27360 by default). TCP /status alone does
not prove that UDP game traffic is arriving. Keep the server window open while
hosting manually; the Host button uses a background supervisor instead. Any
Windows network permission prompt is handled by the user.

See [the Windows online review](../reports/ONLINE_REVIEW.md) for test scope,
remaining multiplayer limitations and SmartScreen/signing status.

## Host and join in the Windows launcher

1. Both players install the complete matching test build. The host chooses a
   connection mode and clicks **Host & copy invitation**. A private server starts
   in the background; a private room and connection invitation are prepared.
2. Send the invitation to your friend. They paste it and click **Join invitation**.
   Their server/room are filled in and the host connection is tested. Their own
   player name stays. A different game/engine build is rejected before changes.
3. Both click **Launch Game**. In **ONLINE > PLAYER MATCH**, the host creates a
   match and the friend uses QUICK MATCH. Hosting the relay does not itself create
   this in-game match.

Connection choices:

- **Same network / shared VPN:** automatically uses the selected local interface.
  For a VPN, select its adapter from the host connection list. A LAN invitation
  alone cannot connect two separate home networks.
- **Internet - automatic router setup (UPnP):** tick the permission checkbox. The
  Windows router API requests UDP 27360 and gets the router's external IPv4, with
  no third-party IP lookup. Other applications' port rules are never overwritten.
  The friend's Join action tests outside reachability; a router response alone
  does not prove the port is reachable from the internet.
  The implementation uses Microsoft's [Windows NAT UPnP API](https://learn.microsoft.com/en-us/windows/win32/api/natupnp/nn-natupnp-istaticportmappingcollection).
- **Internet - I forwarded the port manually:** forward your chosen external UDP
  port to UDP 27360 on the selected host IP, then enter your public IPv4:port.
  No router or Windows firewall settings are changed by this mode.

If UPnP is unavailable, the router reports a private address, or the operator uses
CGNAT/double NAT, use a shared VPN or arrange manual forwarding if possible. No
central public game server is needed or provisioned. Windows network permission
prompts still need the player to respond; the launcher does not bypass them.

**Stop hosting** stops this installation's own relay. Hosting also ends when the
launched host game exits, or after 30 minutes without a game launch. Closing the
launcher alone leaves hosting available. The supervisor tries to remove only its
own UPnP rule when it stops. After a crash/forced shutdown, the journal is retried
on the next host start; if the router is unavailable, manual cleanup may be needed.
An invitation encodes the address, private room and build ID; it is not encrypted
credentials. More connection tools and manual server fields remain available.

The UDP readiness check does not join a room or create a player. A response proves
that PC can reach the chosen relay, not that direct peers, complete matches,
reconnect or spectating work across every NAT. Offline play skips the check.

The same Python relay runs on Windows or on a server. Its default game port is
UDP **27360**; TCP on that port serves status. Keep it running while playing.
The player who hosts supplies the server while playing. No permanent public
service or hosting subscription is part of this launcher flow.
