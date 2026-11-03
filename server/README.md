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
