# TargetToken + FrameId as identity and coordinate space

HWND values are recycled by Windows, so they are not a stable Window identity. Pointer coordinates are meaningless unless they bind to one Capture. v1 issues a TargetToken for an observed Window and requires a FrameId on operate (including key/text/paste/wait); pointer Actions map through that Frame’s transform.

## Considered Options

- HWND as the public identity
- Implicit “last screenshot” coordinates when FrameId is omitted
