# TruckSim GPS Telemetry Server v1.0.3

A telemetry server for **Euro Truck Simulator 2** and **American Truck Simulator** that enables real-time communication between your PC and the TruckSim GPS mobile app.

This server runs in the background while you play ETS2 or ATS, allowing your mobile device to receive live truck data for GPS navigation features.

## System Requirements

- **Windows 10/11** (64-bit)
- **Microsoft Visual C++ 2015-2022 Redistributable (x64)** - [Download from Microsoft](https://aka.ms/vs/17/release/vc_redist.x64.exe)

## Compatibility

**Server Version:** 1.0.3
**Compatible with:** TruckSim GPS alpha-4 patch-1 mobile app (v0.4.1)

## How to Use

1. **Download** the latest release
2. **Run** `TruckSimGPS_Server.exe`
3. **First-time setup:** The program will:
   - Add a firewall rule to allow PC-mobile communication[.gitignore](.gitignore)
   - Auto-detect your game installations (or ask you to locate them)
   - Copy required plugins to your game folders
4. **Keep the server running** while playing ETS2/ATS
5. **Connect your mobile device:**
   - Make sure your mobile device is connected to the same network as your PC
   - Use the local network **Server IP** shown in the server window in your mobile app's connection dialog 

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