# TruckSim GPS Telemetry Server v1.1.0

A telemetry server for **Euro Truck Simulator 2** and **American Truck Simulator** that enables real-time communication between your PC and the TruckSim GPS mobile app.

This server runs in the background while you play ETS2 or ATS, allowing your mobile device to receive live truck data for GPS navigation features.

## System Requirements

- **Windows 10/11** (64-bit)
- **Microsoft Visual C++ 2015-2022 Redistributable (x64)** - [Download from Microsoft](https://aka.ms/vs/17/release/vc_redist.x64.exe)

## Compatibility

**Server Version:** 1.1.0
**Compatible with:** TruckSim GPS alpha-8 mobile app (v0.8.0)

## How to Use

1. **Download** the latest `TruckSimGPS_Server_Setup_*.exe` from [Releases](https://github.com/TruckSim-GPS/trucksim-gps-server/releases)
2. **Run the installer** — it will:
   - Install the VC++ Redistributable if needed
   - Add a firewall rule to allow PC-mobile communication
   - Optionally create a desktop shortcut and enable start with Windows
3. **Launch** TruckSim GPS Telemetry Server
4. **First-time setup:** The program will auto-detect your game installations and copy required plugins
5. **Keep the server running** while playing ETS2/ATS
6. **Connect your mobile device:**
   - Make sure your mobile device is connected to the same network as your PC
   - Use the local network **Server IP** shown in the server window in your mobile app's connection dialog

The app checks for updates automatically on launch. You can also check manually via **Help > Check for Updates**.

## Troubleshooting

You can test connectivity by opening the **Browser Test URL** on your mobile device.  
If the browser test URL doesn't open on your mobile device, check your PC's firewall settings to ensure the connection is allowed.

## Project Links

🌐 **Official Website:** https://trucksimgps.com/  
💖 **Support us on Patreon:** https://www.patreon.com/TruckSimGPS  
💬 **Join our Discord:** https://discord.gg/RdC99Er37U (discussion, support, feature requests)

---

*"Forged upon the timeless foundations laid by those who paved this path."*

This project is built upon the foundational work of the [Funbit ETS2 Telemetry Server](https://github.com/Funbit/ets2-telemetry-server).  
As open source software licensed under **GPL-3**, you can explore the source code and be confident it's free from malware.