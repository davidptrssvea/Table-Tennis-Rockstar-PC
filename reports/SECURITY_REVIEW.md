# Player-hosted relay and experimental UPnP review

Reviewed locally on 2026-11-04. Scope: `server/ttlink_server.py`, the Windows
Host/Join supervisor, invitation handling, router discovery/requests and cleanup.
This is a code review and regression exercise, not an independent penetration
test, certification, or an audit of the recompiled game's packet parsers, Windows,
Python, router firmware or the whole ReXGlue SDK.

## Threat model and intended use

One player hosts for trusted friends. Attackers may send arbitrary UDP traffic,
spoof source addresses, guess room codes, or serve hostile router XML. No shared
public service is required. Internet play remains experimental. An invitation is
connection information, not encrypted authentication credentials. The six-character
room is about 30 bits of random selection, not a password suitable for protecting
sensitive data. Do not publish it as an open public lobby.

## Findings and changes

| Finding | Prior consequence | Change |
|---|---|---|
| High: unverified HELLO fan-out | A spoofed source could trigger room-wide membership work and be advertised to peers before receiving a reply. | WELCOME only until a valid token returns from the same endpoint. Only confirmed clients enter member lists and receive relayed traffic. Unconfirmed entries expire after six seconds; repeated HELLO does not extend that timeout. |
| High: player host accepted every room | An exposed host could serve unrelated public/private lobbies. | Host supervisor starts the relay with exactly its invitation room and a four-client ceiling. Wrong rooms are silently ignored. Changing the room requires stopping/restarting the host. |
| Medium: unauthenticated reconnect replacement | A different-nonce HELLO from a spoofed active address could evict its session. | Active sessions are not replaced by a new nonce; endpoint/token authentication remains required for normal traffic. |
| Medium: advertised LAN targets | A peer-controlled local address could make other clients send punches to unrelated LAN/loopback destinations. | Member records suppress claimed local endpoints. Direct traffic uses observed, return-path-confirmed endpoints; relay remains available. |
| Medium: incomplete resource bounds | Per-client limits did not bound all unknown-client errors, total output, fan-out or membership churn. | Global ingress 5,000 packets/s; HELLO admission 20/s; unknown/error replies 50/s; aggregate output 5,000 packets/s and 4 MiB/s. Membership updates coalesce at two/s/room. All token buckets have bounded bursts. Generic server ceiling is 128, public rooms 32; launcher hosts allow four clients in one room. |
| Medium: TCP status exposure | HTTP listened on the game interface, allowed cross-origin reads and lacked connection bounds. | Player-host mode has no TCP status listener. Manual diagnostics bind to loopback only, cap eight in-flight requests/10 requests per second, enforce size/time/method/path/Host checks and reject Origin. CORS wildcard removed. |
| Medium: static UPnP rules | A crash could leave a permanent mapping. | New mappings use IGD SOAP with an explicit 300-second lease, verified through GetSpecificPortMappingEntry. Renew every 90 seconds. Refuse zero/infinite/overlong/unconfirmed leases and routers requiring permanent mappings. The former Windows static-mapping adapter cannot create mappings; it is retained only for legacy cleanup. |
| Medium: router discovery/response trust | General discovery can expose a client to untrusted URLs/XML or indefinite responses. | SSDP is bound to the selected interface and only accepts its configured private IPv4 gateway. Description/control URLs must be literal HTTP URLs on that gateway. No proxy, redirects, DNS control URLs, external XML entities or DTDs. Responses cap at 256 KiB, depth 32, with per-operation and streaming time limits. Service types are allowlisted. |
| Low: malformed protocol fields | Some trailing data/invalid UTF-8/length combinations were accepted. | Validate HELLO lengths, optional fields and UTF-8; exact authenticated message lengths; reject invalid game ports. Preserve the SDK's 13/15-byte ping/pong control format on virtual port zero. |
| Low: forgotten sessions/log growth | Hosting could remain active or grow logs unexpectedly. | Owned PID/start-time/path monitoring, stop marker, 30-minute pre-game timeout and four-hour session cap. Logs capped at approximately 1 MiB; hosting state reads capped at 16 KiB and reject duplicate fields. |
| Medium: orphan relay after supervisor crash | Killing the helper could leave its Python child listening. | The relay holds a Windows handle to the exact supervisor creation identity and exits when it is signaled. A forced helper termination is tested with the real child process; ordinary launcher-window closure intentionally leaves the separate supervisor running. |

UPnP is explicitly **EXPERIMENTAL**, **off by default**, and never enabled from
saved preferences. Mode changes and completed/failed host setup clear permission.
An already running host is reported instead of silently treating it as a new LAN
session. No Windows firewall or security settings are modified. No global router
UPnP feature is enabled; discovery failure returns a VPN/manual-hosting suggestion.

## Ownership, cleanup and crash behavior

Before a lease request, record the machine/installation/session-specific mapping
description and client in a flushed journal. Check the current external port,
internal port, client and description before reusing, renewing or deleting a rule.
Refuse observed foreign conflicts. Verify deletion; retain the journal and report
failure if the rule remains. Stop the owned relay before cleanup. Renewal failure,
ownership changes or a changed public IP stop hosting instead of silently leaving
a misleading invitation active.

The host game uses its local address; the invitation uses the confirmed external
address. The friend's Join action tests actual UDP reachability. A router response
alone is never described as proof of internet reachability.

Normal shutdown attempts immediate deletion. With a conforming router, a lease
expires no later than five minutes after its last renewal if the supervisor/PC
crashes. That is conditional on the router honoring its reported lease: software
cannot guarantee behavior of faulty or hostile firmware. Legacy permanent rules
still need ownership-checked cleanup or manual router removal. Port queries and
changes are separate IGD operations, not an atomic compare-and-delete transaction;
concurrent third-party port managers remain a residual race. Do not claim that
these ownership checks make the router protocol transactional.

## Validation and remaining limits

Tests use only synthetic traffic, loopback relays/HTTP servers and fake routers;
no real router setting is changed by the test suite. Regression cases cover:

- Room/client limits, no member/relay traffic to unverified addresses, nonce
  spoof attempts, bounded unknown replies/admissions/output and forbidden LAN hints.
- 5,000 deterministic malformed HELLO mutations plus length/UTF-8/port cases.
- HTTP loopback/Host/Origin/method/path/concurrency policy and removal of CORS.
- Real C# gateway client against fake SOAP servers: finite lease/add/renew/delete,
  foreign conflict, permanent lease refusal and cleanup; redirect, XML entity,
  oversized XML, deep XML and endpoint pinning rejection.
- Actual embedded-Python supervisor start, duplicate host rejection, stop and
  game-exit cleanup and forced-supervisor-crash child exit; UI construction verifies experimental text, default-off UPnP
  and permission reset. Existing direct/relayed network, room-isolation and reconnect
  checks exercise the matched native Windows SDK.

The first full run caught over-strict rejection of the SDK's zero-port ping/pong;
that control format is now explicitly allowed and regression-tested.

All 68 full-suite cases passed after the protocol correction. Final targeted
tests also cover absolute pending-client timeout, the stricter Advanced host
arguments, supervisor-crash exit and default-off/reset UI state. A flaky old
cross-room test fixture was corrected to use distinct room-local virtual IPs;
the protocol intentionally allows separate rooms to reuse virtual addresses.

The regular Host button and the legacy Advanced launcher host path both select
one private room, four clients and no status listener. Direct command-line use
without `--room` remains a generic relay with the documented global limits;
it is not the player-host configuration. Internet hosting via the original game
binary and physical routers is not validated by synthetic server tests.

Remaining limitations: no transport encryption or modern authenticated key exchange.
Protocol v1 shares peer tokens for direct connectivity and assumes trusted room
members; an invited/on-path attacker or a host can observe traffic. This review
does not prove the original game's packet handling safe against a malicious peer.
Rate limits bound application work/output, not upstream volumetric denial of
service. A leaked invitation can enable disruption despite throttling. Beyond the
single gateway test below, other IGD devices, network transitions, CGNAT/double NAT
and full matches across separate internet connections still require physical testing. Prefer LAN/shared VPN while
automatic internet hosting is experimental.

## Authorized physical-router check (2026-11-04)

The user explicitly authorized testing their router. The production gateway client
first confirmed UDP 27360 had no mapping. The initial 65-character rule description
was returned with a matching 64-character prefix followed by corrupt XML data.
Strict parsing correctly stopped setup and refused an ownership-ambiguous deletion.
The requested finite lease counted down and its expiry was independently verified;
no permanent-rule fallback or parser relaxation was used.

Mapping descriptions are now 29 ASCII characters, retaining an installation
identity prefix and random session suffix. A regression asserts the sub-32-byte
format. With that change, the real production host/supervisor path passed:

- Port initially unmapped; own mapping created and independently read back.
- Enabled mapping confirmed a 300-second lease; renewal and read-back succeeded.
- Stop hosting completed; an independent query found no mapping afterwards.
- No live supervisor remained and the cleanup journal was absent.
- A fresh process/discovery confirmed the port was still unmapped.

Focused lifecycle and gateway security tests passed after the compatibility fix.
This verifies creation, renewal, normal deletion and expiry on this router only;
it does not verify an inbound connection/match from a separate internet network.
UPnP remains experimental and off by default. No Windows firewall/global router
configuration was changed. Raw responses and local diagnostics stay private.

## Protocol references

- [UPnP IGD v2 WANIPConnection specification](https://upnp.org/specs/gw/UPnP-gw-WANIPConnection-v2-Service.pdf): mapping leases and action semantics.
- [UPnP IGD v1 WANIPConnection specification](https://upnp.org/specs/gw/UPnP-gw-WANIPConnection-v1-Service.pdf): legacy finite-lease support limitations.
- [Microsoft static port mapping API](https://learn.microsoft.com/en-us/windows/win32/api/natupnp/nn-natupnp-istaticportmappingcollection): retained only for legacy cleanup, not creation.
