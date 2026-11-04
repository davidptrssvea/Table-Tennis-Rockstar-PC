# Experimental Table Tennis relay

Adapted from ThisIsAkill/Table-Tennis-Linux (MIT); see licenses/Table-Tennis-Linux.

Local test only: `python server/ttlink_server.py --bind 127.0.0.1 --port 27360`.
The server implements the upstream TTV1 protocol, public/private rooms, peer
introduction and relay. It does not need a game ISO or any game assets.

Hosting for other players is a separate deployment step. No public service is
started by setup or by the game. Configure a server explicitly in the launcher;
networking remains off until enabled and ONLINE is selected. The server sees
client IP addresses and the configured player names/time-zone offsets. The
upstream project documents its service separately; Windows Internet play,
spectating and short-drop match recovery still require end-to-end validation.

On Windows the relay selects SelectorEventLoop, so an ICMP error caused by a
departing UDP peer does not leave the affected Proactor reader permanently idle.
UDP and TCP use the configured port (27360 by default). TCP /status alone does
not prove that UDP game traffic is arriving. Keep the server window open while
hosting; any Windows network permission prompt is handled by the user.

See [the Windows online review](../reports/ONLINE_REVIEW.md) for test scope,
remaining multiplayer limitations and SmartScreen/signing status.

## Friend invitations in the Windows launcher

1. Both players use the same game/engine build (shown by the launcher).
2. Enter one running server address. Internet play needs a server reachable from
   both PCs: public hosting, correctly configured home hosting, or a shared VPN.
   A `192.168.x.x` LAN address alone cannot connect different home networks.
   Home hosting can need router setup and may be unavailable behind carrier-grade
   NAT. The launcher does not configure the router or firewall.
3. Choose **Copy invitation**. An empty room becomes a new private room. Share the
   complete `TT1.` code with the friend.
4. The friend pastes it into **Invitation** and chooses **Use invitation**.
   Server and room are filled in; their own name stays. A different build is
   rejected without changing settings. The code contains connection details,
   not encrypted credentials or an already created game match.
5. Both select **Test connection**, then **Launch Game**. In the game choose
   **ONLINE > PLAYER MATCH**. One creates the match; the other uses QUICK MATCH.

The UDP readiness check does not join a room or create a player. A response proves
that PC can reach the chosen relay, not that direct peers, complete matches,
reconnect or spectating work across every NAT. Offline play skips the check.

The same Python relay runs on Windows or on a server. Its default game port is
UDP **27360**; TCP on that port serves status. Keep it running while playing.
No public service is supplied yet. A future shared server would let the release
provide its hostname by default, removing manual server setup for players.
Public hosting/deployment has not been performed by these launcher changes.
