# Windows Graphics Capture as the primary Capture path

PrintWindow can hang the caller on `WM_PRINT` or return a black frame. v1 captures with WGC `CreateForWindow` first; PrintWindow is a timed, isolated fallback only. Desktop-rectangle compositing must not be returned as a window Capture.

## Considered Options

- PrintWindow as the primary backend
- Synthesizing a window Capture from a desktop region
