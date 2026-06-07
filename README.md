# VideoTriage

Batch-compress a folder of videos to AV1 (NVEnc), keeping a new encode **only when it is
smaller AND verified playable**, then safely removing the original. A Windows desktop app
(WPF + Fluent) built on an engine calibrated on 1,700+ real files.

> **[WIP]** Early development. See `docs/superpowers/plans/` for the build plan.

## Status
- [x] Scaffold + Fluent shell
- [x] Core probe/classify scan API
- [ ] Core engine (verify / safe-replace)
- [ ] UI wiring + live progress
- [ ] Embedded poster thumbnails

## Non-Destructive Probe Scan

M2 includes a console harness that reads a folder, probes videos with `ffprobe`, and prints
candidate classifications. It does not encode, replace, or delete files.

```bash
dotnet run --project src/VideoTriage.Cli -- "D:\Videos\Captures"
dotnet run --project src/VideoTriage.Cli -- "D:\Videos\Captures" --recursive
```
