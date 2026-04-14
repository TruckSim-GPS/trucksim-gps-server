# TruckSim GPS Telemetry Server

The open-source companion telemetry server for the [TruckSim GPS](https://trucksimgps.com/) mobile app. It runs in the background while you play **Euro Truck Simulator 2** or **American Truck Simulator**, reading live game data and making it available to the mobile app on your local network.

The telemetry plugin that reads game data is also open source under the MIT license: [trucksim-gps-plugin](https://github.com/TruckSim-GPS/trucksim-gps-plugin).

## System Requirements

- **Windows 10 or 11** (64-bit)

## How to Use

1. **Download** the latest `TruckSimGPS_Server_Setup_*.exe` from [Releases](https://github.com/TruckSim-GPS/trucksim-gps-server/releases)
2. **Run the installer** — it will:
   - Install the VC++ Redistributable if needed
   - Add a firewall rule to allow local network communication
   - Optionally create a desktop shortcut and enable start with Windows
3. **Launch** TruckSim GPS Telemetry Server
4. **First-time setup:** The program will auto-detect your game installations and copy the required telemetry plugins
5. **Keep the server running** while playing ETS2/ATS

The app checks for updates automatically on launch. You can also check manually via **Help > Check for Updates**.

## Troubleshooting

If you're having connection problems, see the [PC Connection Troubleshooting Guide](https://trucksimgps.com/pc-troubleshooting) on our website.

You can also test connectivity by opening `http://<server-ip>:31377/` in a browser on any device on your network. If it doesn't load, check your PC's firewall settings.

## Privacy

This application does not collect, store, or transmit any personal user data. The server communicates exclusively over the local network. The only external network request is an update check against GitHub's public API, which sends no user-identifiable information.

## Building from Source

```bash
# Restore NuGet packages
nuget restore source/Funbit.Ets.Telemetry.sln

# Build
msbuild source/Funbit.Ets.Telemetry.sln /p:Configuration=Release
```

The output will be in `source/Funbit.Ets.Telemetry.Server/bin/Release/`.

## Project Links

- **Official Website:** https://trucksimgps.com/
- **Support us on Patreon:** https://www.patreon.com/TruckSimGPS
- **Join our Discord:** https://discord.gg/RdC99Er37U (discussion, support, feature requests)

---

This project is built upon the foundational work of the [Funbit ETS2 Telemetry Server](https://github.com/Funbit/ets2-telemetry-server).
Open source software licensed under **GPL-3.0**. Anyone can read the source code and verify for themselves that it's safe.
