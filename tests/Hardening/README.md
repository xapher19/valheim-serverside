# Hardening regression checks

Run with the .NET 8 SDK (or a newer SDK with the .NET 8 targeting pack):

```sh
dotnet run --project tests/Hardening/Hardening.csproj
```

This dependency-free executable compiles the actual plugin startup, patch loader,
Performance hooks/statistics and FPS verifier sources. Game/Unity/BepInEx stand-ins
and a fault-injectable Harmony registry exercise optional partial failure, missing
registrations, prefix/postfix completeness, Core-wide rollback, unavailable timing,
startup gating, FPS bounds, delayed fallback, verification, override warnings and
client/disabled guards. Failures return a nonzero exit code.

To check compilation against the repository's actual bundled Harmony API:

```sh
dotnet build tests/Hardening/Hardening.csproj -p:RealHarmony=true
```

These are control-flow checks, not runtime detour or gameplay tests. A full plugin
build still needs the Valheim server and BepInEx assemblies described in the main
README. Before deployment, run a dedicated test server and check:

1. Every Core hook logs `ACTIVE`; Performance hooks each log `ACTIVE` or `FAILED`.
2. With a configured target of 60, wait at least 15 seconds for `Server target FPS
   verified`. If the request hook is unavailable or missed startup, expect a
   mismatch warning followed by one fallback and a later verification message.
3. An override that changes the target again produces `FPS is not applied` without
   repeated writes or a warning every frame. A correct cap does not guarantee
   measured throughput; use the periodic statistics for actual FPS.
4. With stats enabled and an unavailable timing hook, reports say `unavailable`
   instead of reporting zero activity for that measurement.
5. Join with an unmodified PC/PS5 client; check movement, combat, object interaction
   and world saving. No client mod, RPC/payload change or new handshake is added.
