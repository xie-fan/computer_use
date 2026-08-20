# Screen-scoped control memory (observe / remember / click by id)

Sending a full-window Frame to the model on every step burns vision tokens and still fails if we only store last-click coordinates: different Screens share similar buttons, and geometry/DPI drift. v2 adds an on-disk, per-AppKey catalog of Screen fingerprints and Control templates. MCP captures locally, identifies the Screen, and clicks by ControlId without returning PNG. Absolute pixels are a search prior, never the click target. v1 list/screenshot/operate stay unchanged.

Team review (Grok, Harper, Benjamin, Lucas) accepted the architecture and required hardening before implementation: composite AppKey, visualized-only FrameId for pointer operate and remember, HostWindow excluded from memory, ≥2 fingerprints plus entropy checks, managed ZNCC (no OpenCv), concrete quotas, imperative Skill so the catalog actually warms, and no aggressive mismatch tradeoff. Those decisions are in [`docs/control-memory.md`](../control-memory.md) §7 and §12.

## Considered Options

- Remember absolute (x, y) per button and replay
- Overload `operate_window` results with screen type + boxes
- Import a game-script template loop (global COCO / wait_click_feature) as the main Agent path
- Require a screenshot on every click (v1 only)
- Path + className as the only AppKey (too easy to split or silently merge)
- Letting observe FrameIds drive pointer operate
- OpenCvSharp in the first-ship self-contained exe
- Higher mismatch tolerance for “simple” apps by default
