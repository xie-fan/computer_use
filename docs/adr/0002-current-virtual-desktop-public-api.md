# CurrentVirtualDesktop only, public IVirtualDesktopManager

Undocumented VirtualDesktop COM is not a safe fallback: a vtable mismatch can crash the process, and `catch` cannot honor a degradation promise. v1 queries membership with public `IVirtualDesktopManager` only, operates solely on the CurrentVirtualDesktop, and does not switch or enumerate other VirtualDesktops.

## Considered Options

- Undocumented COM to switch or list other VirtualDesktops
- Treating membership-query failure as “on current desktop”
