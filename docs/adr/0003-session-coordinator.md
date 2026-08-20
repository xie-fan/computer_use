# DesktopOperationCoordinator serializes Session side effects

Foreground window, pointer, keyboard, clipboard, and CurrentVirtualDesktop are Session-global; concurrent Capture or input would race. All restore-minimized, activate, Capture, input, and paste go through one in-process Coordinator with a bounded queue, and token, Frame, focus, and hit-tests are rechecked after the lock is held and before each side-effecting Action.
