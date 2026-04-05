# Changelog

## [0.1.0-beta] - 2026-03-22

### Added
- Native Windows launcher for Sigil OS VM workspaces
- Hyper-V Gen2 VM management via PowerShell
- First-run setup wizard (6 steps)
- Hardware detection via WMI (RAM, CPU, architecture, disk, GPU)
- Model catalog with hardware-filtered selection
- Model download manager with progress tracking
- Automated VM image building via Nix (VHDX output)
- A-la-carte tool selection
- VM lifecycle management with graceful shutdown
- SSH and daemon health monitoring with crash detection
- TLS credential bootstrapping
- SMB/CIFS shared directories (workspace, profile, models)
- Build progress with real-time log streaming
- 27 unit tests (xUnit)
