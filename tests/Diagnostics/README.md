# Diagnostics regression checks

`dotnet run --project tests/Diagnostics/Diagnostics.csproj`

Source-links the production metrics, runtime and Harmony hook bodies against small
host doubles. Covers timing/grace/cooldown/recovery, attempt classification, idle
gaps, reconnect cleanup, non-destructive status reads, independent alert/report
settings, queue-budget selection, flush exclusion, save-state wording, RPC trace
limits and missing registration reporting. No client delivery or Unity gameplay
is simulated. GamePatchSmoke separately installs the actual compiled hooks against
the downloaded game using its documented animation-service shim.

Live acceptance: use status before joining, after joining from PC/PS5, after a
save and after reconnect. Wait for the periodic summary; confirm no-data sends
are distinct from blocked sends. Temporarily enable InteractionTrace and open a
chest/pick up an item; confirm original gameplay continues and trace output stays
bounded. Check the server's own save logs to establish actual write success.
