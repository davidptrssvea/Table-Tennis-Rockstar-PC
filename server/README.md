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
