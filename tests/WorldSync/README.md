# World synchronisation regression checks

```sh
dotnet run --project tests/WorldSync/WorldSync.csproj
```

This .NET 8 executable links the production `ShipOwnership` and
`PlayerDepartureSync` helpers. Small host doubles exercise driver priority, idle
ownership stability, passengers/nearby candidates, cargo use, disconnected and
out-of-range owners, transition-only impact protection, and server/client guards.

The portal case reproduces the inspected 1.0.15 ordering: invalidate using the old
position, commit the new position, then run the production receive helper. It
checks source removal, owner/destination exclusion, deferred delivery, return trips,
cache reset and guards for unknown peers/characters. No character is destroyed or
reassigned. These tests model the native invalidation contract; they do not run
Unity, network transport or rendered client cleanup.

`tests/GamePatchSmoke` independently installs the compiled plugin's ship ownership
and player receive hooks against the actual downloaded game methods using Harmony under Mono. Mono is used
because the game interfaces contain default method bodies that desktop .NET
Framework cannot load.
The harness substitutes animation-name hashing during Player static initialization;
the actual Unity native hashing service is unavailable outside the engine. No
gameplay methods are executed and this substitute is not part of the shipped DLL.
The complete build runs on PRs and main, including the existing FPS checks.

## Live acceptance checks

Use the same build on the dedicated server only; no client changes are required.
Confirm plugin version 1.9.2 and startup `ACTIVE` entries for
`Core.Ship_UpdateOwner_Patch` and `Core.ZDOMan_RPC_ZDOData_PlayerDeparture_Patch`.

- With PC and PS5 players near opposite portals, teleport in both directions while
  the other player watches the entrance. The old figure should disappear without
  the traveller returning; re-entry should show one current character.
- Repeat with both players travelling, with nearby and distant destinations, and
  during a busy area load. Queued removal still obeys the normal send budget.
- Have both players stand aboard a stopped boat, take and release the helm, and
  repeat with each player steering. Compare hull motion with the previous build.
- Test opening cargo, changing drivers, stepping ashore, leaving simulation range,
  and disconnecting the current owner. Check control, cargo interaction, saves and
  reconnects. Debug logs include actual ownership transitions.
- Check normal impact damage separately: the previous code could continually
  suppress water impacts on server-owned idle ships. This build only grants that
  protection on a real handoff back to the server.

The boat explanation remains a code-backed hypothesis until reproduced and tested
in Unity. Automated checks validate selection/notification logic and installation,
not live wave alignment or end-to-end portal rendering.
