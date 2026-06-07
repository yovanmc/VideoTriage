# VideoTriage

Batch-compress a folder of videos to AV1 (NVEnc), keeping a new encode **only when it is
smaller AND verified playable**, then safely removing the original. A Windows desktop app
(WPF + Fluent) built on an engine calibrated on 1,700+ real files.

> 🚧 Early development. See `docs/superpowers/plans/` for the build plan.

## Status
- [x] Scaffold + Fluent shell
- [ ] Core engine (probe / classify / verify / safe-replace)
- [ ] UI wiring + live progress
- [ ] Embedded poster thumbnails
