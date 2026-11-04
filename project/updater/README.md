# Windows release updates

The launcher checks the latest stable GitHub release in the background, at most
once per six hours after a successful check. Disable automatic checks under
UPDATES. Check now and OPTIONS > PC SETTINGS > UPDATES open the same updater.
Nothing installs until the player approves it. Old installations need a one-time
installation of the first updater-enabled release.

Updates download public source and a matched game-free ReXGlue engine. They rebuild
locally from the player's existing verified retail ISO. The ISO, saves, settings,
and unrelated files stay in place. Missing Microsoft build components require a
separate opt-in and may show the normal Windows administrator prompt. No security
settings are changed. Internet access is required for release checks/downloads.

The updater verifies an RSA-3072/SHA-256 manifest with its embedded public key,
checks both archives, builds in `.updates/work`, then installs with a recovery
journal and backups. Restart the launcher after an interruption to recover. Use
Restore previous version to undo the latest update while retaining current saves
and settings. Logs are in `.updates/logs`. Keep `.updates/runner` and backups until
recovery/rollback is no longer needed.

Release signing authenticates our update metadata. It is not Windows Authenticode
code signing and does not remove SmartScreen's unknown-publisher warnings.

## Maintainer preparation (does not publish)

1. Update `project/release.json` and build the matched community SDK.
2. Run `python project/tools/prepare_update.py --output OUTPUT --sdk SDK --notes NOTES`.
3. Validate the signed candidate and test an update and rollback before publishing.
4. Publish the four generated assets together in the matching stable GitHub tag:
   the source ZIP, engine ZIP, `tabletennis-update.json`, and its `.sig` file.
   Include `SHA256SUMS.txt` for manual checks.

The packager updates `project/engine.json` before creating the source archive so
fresh installations use the same engine as updates. No game executables, generated
game code, ISO, or original assets belong in a public release.

Signing requires the existing maintainer key at
`%LOCALAPPDATA%\TableTennisReleaseSigning\release-key.dpapi`. It is encrypted with
Windows DPAPI for the signing user's profile and is never packaged. Back up the
Windows profile and protected key securely; copying the file alone to another
profile does not make it usable. Do not regenerate the key for normal releases.
Changing the trust key requires a planned migration for already installed clients.
`sign_update.ps1 -Initialize` is only for the first initialization.
