# Screen-scoped control memory (observe / remember / click by id)

Sending a full-window Frame to the model on every step burns vision tokens and still fails if we only store last-click coordinates: different Screens share similar buttons, and geometry/DPI drift. v2 adds an on-disk, per-AppKey catalog of Screen fingerprints and Control templates. MCP captures locally, identifies the Screen, and clicks by ControlId without returning PNG. Absolute pixels are a search prior, never the click target. v1 list/screenshot/operate stay unchanged.

The review packet (background, current v1 design, this proposal, open questions) is [`docs/control-memory.md`](../control-memory.md).

## Considered Options

- Remember absolute (x, y) per button and replay
- Overload `operate_window` results with screen type + boxes
- Import a game-script template loop (global COCO / wait_click_feature) as the main Agent path
- Require a screenshot on every click (v1 only)
