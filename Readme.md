# TruckSim GPS Telemetry Server

An open-source telemetry server for **Euro Truck Simulator 2** and **American Truck Simulator** that enables real-time communication between your PC and the [TruckSim GPS](https://trucksimgps.com/) mobile app.

The server runs in the background while you play ETS2 or ATS, reading game data via shared memory and serving it over a local REST API and WebSocket connection. The API is open and documented below, so it can also be used by third-party dashboards, overlays, or other tools.

The telemetry plugin that reads game data is also open source under the MIT license: [trucksim-gps-plugin](https://github.com/TruckSim-GPS/trucksim-gps-plugin).

This server is fully functional on its own and does not require the mobile app to operate. All of its functionality is available through the open API documented below, with no features restricted or gated in any way. Third-party developers can create their own clients that consume the server's telemetry data.

## System Requirements

- **Windows 10/11** (64-bit)
- **Microsoft Visual C++ 2015-2022 Redistributable (x64)** - [Download from Microsoft](https://aka.ms/vs/17/release/vc_redist.x64.exe)

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

## REST API

The server exposes a JSON endpoint on port `31377` (configurable). Any HTTP client on the local network can poll it.

### `GET /api/ets2/telemetry`

Returns a JSON object with all available telemetry data. No authentication required. CORS is enabled for all origins.

**Example request:**
```
curl http://192.168.1.100:31377/api/ets2/telemetry
```

**Example response (abbreviated):**
```json
{
  "game": {
    "connected": true,
    "gameName": "ETS2",
    "paused": false,
    "time": "0001-01-08T21:09:00Z",
    "timeScale": 19.0,
    "version": "1.10",
    "telemetryPluginVersion": "4"
  },
  "truck": {
    "id": "man",
    "make": "MAN",
    "model": "TGX",
    "speed": 53.82,
    "gear": 10,
    "engineRpm": 1337.18,
    "fuel": 683.82,
    "fuelCapacity": 700.0,
    "placement": {
      "x": 13475.57,
      "y": 67.36,
      "z": 14618.62,
      "heading": 0.185,
      "pitch": -0.006,
      "roll": -0.0002
    }
  },
  "trailers": [
    {
      "attached": true,
      "id": "derrick",
      "name": "Derrick",
      "placement": { "x": 13483.32, "y": 67.73, "z": 14622.12, "heading": 0.181, "pitch": -0.005, "roll": -0.0001 }
    }
  ],
  "job": {
    "income": 2316,
    "sourceCity": "Linz",
    "sourceCompany": "DPD",
    "destinationCity": "Salzburg",
    "destinationCompany": "JCB",
    "plannedDistanceKm": 132
  },
  "navigation": {
    "estimatedTime": "0001-01-01T03:01:40Z",
    "estimatedDistance": 132500,
    "speedLimit": 90
  },
  "gameplay": {
    "onJob": true,
    "jobFinished": false,
    "jobCancelled": false,
    "jobDelivered": true,
    "fined": false,
    "tollgate": true,
    "ferry": false,
    "train": false,
    "refuel": false,
    "refuelPayed": false,
    "jobDeliveredDetails": {
      "revenue": 23160,
      "earnedXp": 120,
      "cargoDamage": 0.02,
      "distanceKm": 132.5,
      "deliveryTime": 480,
      "autoParked": false,
      "autoLoaded": true
    },
    "jobCancelledDetails": { "penalty": 0 },
    "finedDetails": { "amount": 0, "offence": "NoValue" },
    "tollgateDetails": { "payAmount": 12 },
    "ferryDetails": { "payAmount": 0, "sourceName": null, "targetName": null },
    "trainDetails": { "payAmount": 0, "sourceName": null, "targetName": null },
    "refuelDetails": { "amount": 0.0 }
  }
}
```

The response includes the following top-level objects:

| Object | Contents |
|--------|----------|
| `game` | Connection state, game name (ETS2/ATS), time, pause state, version |
| `truck` | Vehicle info, speed, RPM, fuel, gear, lights, inputs, 3D placement |
| `trailers` | Array of attached trailers with placement and wear data |
| `job` | Active job details: cities, companies, cargo, income, deadline |
| `navigation` | In-game GPS: estimated time, distance, current speed limit |
| `gameplay` | Game events with toggle flags and detail objects (see below) |

For the full list of fields, see [TelemetryV1.cs](source/Funbit.Ets.Telemetry.Server/Data/TelemetryV1.cs).

### Gameplay Events

The `gameplay` object exposes game events (job delivery, cancellation, fines, tolls, ferries, trains, refueling) using **toggle flags** and **detail objects**.

**Event detection via XOR toggle:** The boolean flags (`jobDelivered`, `jobCancelled`, `fined`, `tollgate`, `ferry`, `train`) use a toggle pattern inherited from the game's telemetry plugin. Each time an event fires, the flag **flips** (`false` → `true` → `false` → ...). To detect events, compare the current value against the previous poll — a **change** in value means the event fired. The specific value (`true` or `false`) is irrelevant; only the transition matters.

```
Poll 1: jobDelivered = false   (store as previous)
Poll 2: jobDelivered = false   (no change — no event)
Poll 3: jobDelivered = true    (changed! — delivery event fired)
Poll 4: jobDelivered = true    (no change — no event)
Poll 5: jobDelivered = false   (changed! — another delivery event fired)
```

**Exceptions:**
- `onJob` is a persistent state flag (`true` while a job is active), not a toggle.
- `refuel` is a persistent flag (`true` while refueling is in progress, `false` when done).
- `refuelPayed` is set to `true` when refueling completes.

**Detail objects** (`jobDeliveredDetails`, `finedDetails`, etc.) contain data from the most recent event of that type. This data persists in shared memory until overwritten by the next event, so it is always available alongside the toggle flag when a change is detected.

| Event | Toggle Flag | Detail Object | Key Fields |
|-------|------------|---------------|------------|
| Job delivered | `jobDelivered` | `jobDeliveredDetails` | `revenue`, `earnedXp`, `cargoDamage`, `distanceKm`, `deliveryTime`, `autoParked`, `autoLoaded` |
| Job cancelled | `jobCancelled` | `jobCancelledDetails` | `penalty` |
| Fined | `fined` | `finedDetails` | `amount`, `offence` |
| Tollgate | `tollgate` | `tollgateDetails` | `payAmount` |
| Ferry | `ferry` | `ferryDetails` | `payAmount`, `sourceName`, `targetName` |
| Train | `train` | `trainDetails` | `payAmount`, `sourceName`, `targetName` |
| Refuel | `refuel` | `refuelDetails` | `amount` |

### WebSocket (SignalR)

For real-time streaming, connect to the SignalR hub at:

```
http://<server-ip>:31377/signalr
```

Call `RequestData()` on the `Ets2TelemetryHub` to receive telemetry updates via the `updateData` callback. The hub uses the same JSON format as the REST endpoint.

## Troubleshooting

You can test connectivity by opening `http://<server-ip>:31377/api/ets2/telemetry` in a browser on any device on your network. If it doesn't load, check your PC's firewall settings.

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
Open source software licensed under **GPL-3.0**. You can explore the source code and be confident it's free from malware.
