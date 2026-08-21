# WinUI desktop retirement

- Retirement date: 2026-08-21
- Retirement base SHA: `13962a843a539f283d87d5bb81706aca8aba75fb` (branch `feature/avalonia-migration`, "Cut over Windows identity to Avalonia")
- Source recovery tag: `aeda-windows-cut-lkg-13962a8`
- The pre-retirement `PersonalAI.Desktop.WinUI` project remains fully recoverable through Git history at the tag above. A separate, immutable rollback archive of that exact state is maintained externally; verify that archive before any integration that assumes it is current.
- Avalonia (`PersonalAI.Desktop.Avalonia`) is now the sole active Windows desktop implementation. `AEDA.exe`, the `AEDA` startup Run value, and `Local\AEDA.SingleInstance` remain the production identity.
- Obsolete-Run-value cleanup logic for the legacy `PersonalAI.WinUI` startup registration (see `PersonalAI.Desktop.Presentation/Services/WindowsStartupRegistrationService.cs`) is intentionally retained; it is still needed to clean up that value on machines that registered it before the cutover.
