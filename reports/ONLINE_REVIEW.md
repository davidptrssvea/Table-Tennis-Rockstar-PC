# Windows online review - 2026-11-03

This review covers the community Windows branch, not the published v1.1.2 release.
The client crash on the second physical PC still requires that PC's crash log.
Two corrected local instances reaching gameplay does not diagnose that crash.

## Corrections

| Area | Finding and correction |
| --- | --- |
| Windows relay | A departing peer could provoke ICMP port-unreachable. The affected Python Proactor datagram reader then stopped receiving while the server process and HTTP status endpoint remained alive. Use SelectorEventLoop for the Windows relay. |
| Socket handles | Online WireSend accepted int although the stored Windows socket is pointer-sized. Keep SocketHandle throughout. |
| Delayed sends | The delayed sender read the socket under the queue lock rather than its lifetime lock. Serialize send/close and attach an epoch to queued packets; discard old-room traffic on disconnect and bound the queue. |
| Socket setup | Check socket creation, nonblocking configuration, bind and getsockname. Retry failures with an error state instead of polling an invalid socket. |
| Protocol input | Commit welcome fields only after the whole message parses, reject impossible member counts/oversized delivered payloads, and reject malformed error messages. Expire stale peers from partial large-room refreshes. |
| Relay input | Bound HELLO rate-tracker memory, use cryptographic randomness for client IDs/tokens, retain endpoint/token authentication and room isolation, and truncate names at valid UTF-8 boundaries. |
| Launcher | Use the exact server room alphabet; reject zero/overflowing ports. Show one build ID covering the game EXE and both engine DLLs. Provide Open game logs. Find server Python in the official installer's .tools/python314 as well as the private bundle's tools/python. Prevent duplicate server starts. |

The Windows relay issue is consistent with
[CPython issue 91227](https://github.com/python/cpython/issues/91227).
The local Python datagram reader was inspected and the stopped-reader behavior
was reproduced by the room-switch test. The replacement uses the documented
[Runner loop_factory](https://docs.python.org/3/library/asyncio-runner.html), with
a Windows selector-policy fallback for Python 3.8-3.10.

## Validation scope

Tests cover malformed/truncated packets, authentication and cross-room isolation,
UTF-8 name length, rate-tracker bounds, launcher room/port validation, native
Windows direct/relay pings, delayed traffic, room changes, server restart/rejoin,
and a relay receiving new clients after replies to a closed UDP port.

The loopback harness binds both clients explicitly to loopback, checks both TCP
and UDP availability, retains optional failure logs, and paces both sides of a
room change. Earlier harness failures included port-selection and timing defects;
those were distinguished from the actual Windows stopped-reader defect.

The SDK patch is replayed against pinned pristine v0.10.0 source. Game generation
must pass all 57 override registrations and all 23 native-edit hashes. Runtime
checks cover ISO paths and saved/explicit network settings. Interactive host and
client gameplay remains a local-PC test, not an Internet/NAT certification.

### Results for the reviewed build

- Full suite: **27 tests passed** in 66.466 seconds, with matched SDK, native
  checks and production runtime boot tests enabled.
- The previously unreliable delayed room-switch case passed three additional
  consecutive runs (30.541 seconds total).
- Patch application and reversal checked against pinned pristine SDK source;
  **57 override registrations and 23 native-edit hashes** passed.
- Two isolated local game processes, with private virtual-input test EXEs and
  the reviewed engine DLLs/server, joined one lobby, started gameplay, and
  progressed through scoring (2-2 observed on the client). Both logged
  `pongGameState` at 22:30:19 local time; no fatal was logged in either.
  This is a local smoke test, not a completed match on two physical PCs.
- The production update EXE was separately checked to exclude the private
  `TT_TEST_CONTROLLER_FILE` input harness. Online build ID: **572B1869C071**.
- Launcher validators compiled and passed; its accessibility tree exposed the
  enabled server button, build ID and log button. Interactive button/layout
  verification could not be completed: the capture showed a white content
  area and the input helper reported unavailable coordinate geometry. The
  unchanged stable launcher showed the same capture limitation.

The matched production EXE, launcher, engine DLLs, build marker and relay were
installed into the existing local experimental installation. Its settings were
preserved and the previous files backed up. A separate uncompressed update
folder contains the complete replacement set and SHA-256 manifest. Do not
transfer just the EXE; update both PCs and restart the relay.

Still pending: the second PC's exact crash, complete matches on two physical PCs,
interrupted-match recovery, spectator gameplay, large-room load, and Internet/NAT
combinations. The TTV1 relay has room/token checks but is not an encrypted,
independently security-audited public multiplayer service.

## SmartScreen and release readiness

The user identified the transfer/start warnings as SmartScreen. Inspection found
the custom game, launcher and SDK DLLs unsigned; the bundled Microsoft VC runtime
DLLs have valid Microsoft signatures. No matching project Defender detection was
found in this PC's inspected recent event log. This is not a malware-scan result
or a diagnosis of another PC's antivirus state.

SmartScreen evaluates file and publisher reputation. Unsigned updates start with
new file reputation. A trusted signing identity can reduce interruptions, but a
new signed binary can still warn. A self-signed certificate does not establish
publisher reputation. See [Microsoft's signing/reputation guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

No code-signing certificate was found in the current user's code-signing store.
Trusted signing and publisher identity setup remain release prerequisites to
address these warnings properly. No security settings, exclusions, trusted roots
or firewall rules were changed. The private ISO ZIP and public release are not
updated by this review.
