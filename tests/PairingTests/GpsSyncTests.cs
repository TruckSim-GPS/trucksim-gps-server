using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Funbit.Ets.Telemetry.Server.Data;
using Funbit.Ets.Telemetry.Server.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PairingTests
{
    static partial class Program
    {
        class PolicyDriver
        {
            public readonly GpsSyncPolicy Policy = new GpsSyncPolicy();
            public DateTime Now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
            public bool Live = true;
            public bool Paused;
            public bool OnJob;
            public bool App = true;
            public bool GameRoute;
            public string Profile = "ats/local/4D79";
            public float TruckX;
            public GpsWaypointV1 Next;
            public int Presses;

            public PolicyDriver Run(int ms, float distance)
            {
                for (int elapsed = 0; elapsed < ms; elapsed += 100)
                {
                    Now = Now.AddMilliseconds(100);
                    if (Policy.Update(new GpsSyncSample
                    {
                        Now = Now,
                        Live = Live,
                        ProfileKey = Profile,
                        Paused = Paused,
                        RouteDistance = distance,
                        OnJob = OnJob,
                        AppConnected = App,
                        AppWantsGameRoute = GameRoute,
                        TruckX = TruckX,
                        NextWaypoint = Next
                    }))
                        Presses++;
                }
                return this;
            }
        }

        static void GpsSyncPolicyRules()
        {
            var d = new PolicyDriver().Run(5000, 0);
            Check(!d.Policy.RouteUp && d.Presses == 0 && d.Policy.ValidAfter == DateTime.MaxValue, "no route: no epoch, no press, nothing shown");

            d.Run(500, 70000);
            Check(d.Policy.RouteUp && d.Policy.Epoch == 1 && d.Policy.EpochReason == "route appeared", "a route that appears starts an epoch");
            Check(d.Presses == 0, "no press before the route settles");
            d.Run(1000, 70000);
            Check(d.Presses == 1, "one press once the route settled: " + d.Presses);
            Check(d.Policy.ValidAfter == d.Policy.EpochStart, "a new route hides every older save");

            d.Policy.SaveWritten(d.Policy.EpochStart.AddMilliseconds(-1), true);
            d.Run(16000, 69800);
            Check(d.Presses == 1, "a save older than the epoch does not count, but its retry waits for the press window");
            d.Run(14000, 69800);
            Check(d.Presses == 2, "the press is retried when the window ends");
            d.Policy.SaveWritten(d.Now, true);
            d.Run(30000, 69500);
            Check(d.Presses == 2 && !d.Policy.Pending, "a fresh save ends the presses");

            d.Run(100, 0).Run(1000, 69400);
            Check(d.Policy.Epoch == 1 && d.Policy.RouteUp, "a one-frame zero (reroute) keeps the route");
            d.Run(2900, 0);
            Check(d.Policy.RouteUp, "the route survives 2.9 s at zero");
            d.Run(200, 0);
            Check(!d.Policy.RouteUp, "the route is gone after 3 s at zero");

            d = Routed();
            var shownSince = d.Policy.ValidAfter;
            d.Paused = true;
            d.Run(5000, 70000).Run(1000, 52000);
            d.Paused = false;
            d.Run(300, 52000);
            Check(d.Policy.Epoch == 1, "no epoch before the resume settles");
            d.Run(300, 52000);
            Check(d.Policy.Epoch == 2 && d.Policy.EpochReason == "resumed after a pause", "a resume starts an epoch");
            Check(d.Policy.ValidAfter == shownSince, "and keeps the last save shown until a fresh one lands");
            d.Run(1500, 52000);
            Check(d.Presses == 2, "and asks for a press");

            d = Routed();
            d.Paused = true;
            d.Run(3000, 70000);
            d.Paused = false;
            d.Run(2000, 69982);
            Check(d.Policy.Epoch == 2 && d.Presses == 2, "a waypoint added on the route (18 m longer) is read after the pause too");

            d = new PolicyDriver().Run(1500, 0);
            d.Paused = true;
            d.Run(1000, 0);
            d.Paused = false;
            d.Run(2000, 0);
            Check(d.Policy.Epoch == 0 && d.Presses == 0, "a pause without a route starts nothing");

            d = Routed();
            d.Run(100, 69000);
            Check(d.Policy.Epoch == 1, "a 1 km reroute while driving is not a new destination");
            d.Run(100, 85000);
            Check(d.Policy.Epoch == 2 && d.Policy.EpochReason == "route jumped while driving", "a jump over 10 % and 1 km starts an epoch");

            d = Routed();
            shownSince = d.Policy.ValidAfter;
            d.Run(100, 85000).Run(5000, 85000);
            Check(d.Presses == 2 && d.Policy.ValidAfter == shownSince, "a soft epoch keeps the last save shown just after its press");
            d.Run(6100, 85000);
            Check(d.Policy.ValidAfter == d.Policy.EpochStart, "no fresh save 10 s after the press: the last one is hidden");

            d = Routed();
            d.App = false;
            d.Run(100, 85000);
            Check(d.Policy.Epoch == 2 && d.Policy.ValidAfter == d.Policy.EpochStart, "a change seen with no app connected hides the last save at once");

            d = Routed();
            d.Run(100, 85000).Run(77000, 85000);
            Check(d.Presses == 1 + GpsSyncPolicy.MaxPresses, "presses stop after " + GpsSyncPolicy.MaxPresses + " attempts, a press window apart: " + d.Presses);
            Check(!d.Policy.Pending && d.Policy.ValidAfter == d.Policy.EpochStart, "without a fresh save the last one is hidden");

            d = new PolicyDriver { App = false }.Run(3000, 70000);
            Check(d.Policy.RouteUp && d.Presses == 0 && d.Policy.Pending, "no app connected: no press, the epoch waits");
            d.App = true;
            d.Run(200, 70000);
            Check(d.Presses == 1, "the press happens once an app connects (route set before the app)");

            d = new PolicyDriver { OnJob = true }.Run(5000, 70000);
            Check(d.Presses == 0, "no press while a job is active");
            d.Run(100, 0).Run(3000, 0).Run(60000, 51000);
            Check(d.Presses == 0 && d.Policy.RouteUp, "a job without the game-route flag: no press even when a route appears");

            d = new PolicyDriver { OnJob = true, GameRoute = true }.Run(1500, 70000);
            Check(d.Presses == 1, "a job with the game-route flag: one press once the route is up: " + d.Presses);
            d.Policy.SaveWritten(d.Now, true);
            d.Run(30000, 69500);
            Check(d.Presses == 1 && !d.Policy.Pending, "a fresh save ends the presses on a job too");

            d = new PolicyDriver { OnJob = true, GameRoute = true }.Run(5000, 0);
            Check(!d.Policy.RouteUp && d.Presses == 0, "a job with the flag but no in-game route: nothing to press");
            d.Run(500, 51000);
            Check(d.Policy.Epoch == 1 && d.Policy.EpochReason == "route appeared" && d.Presses == 0, "the player sets a waypoint (0 -> 51 km): an epoch");
            d.Run(1000, 51000);
            Check(d.Presses == 1, "and one press for it");

            d = Routed();
            d.OnJob = true;
            d.Run(5000, 69500);
            Check(d.Policy.Epoch == 1 && d.Presses == 1, "a job starting on a route already read: no press");
            d.GameRoute = true;
            d.Run(100, 69500);
            Check(d.Policy.Epoch == 2 && d.Policy.EpochReason == "app asked for the game route" && d.Policy.ValidAfter == d.Policy.EpochStart,
                "the flag rising with a route up starts an epoch that hides older saves");
            d.Run(1000, 69500);
            Check(d.Presses == 2, "and presses once");
            d.Run(5000, 69500);
            Check(d.Policy.Epoch == 2, "the flag staying up starts nothing more");

            d = new PolicyDriver { OnJob = true, GameRoute = true }.Run(1500, 70000);
            d.GameRoute = false;
            d.Run(60000, 70000);
            Check(d.Presses == 1 && d.Policy.Epoch == 1, "the flag dropping stops further presses: " + d.Presses);

            d = Routed();
            d.GameRoute = true;
            d.Run(5000, 69500);
            Check(d.Policy.Epoch == 1 && d.Presses == 1, "the flag off a job changes nothing");
            d.OnJob = true;
            d.Run(100, 69500);
            Check(d.Policy.Epoch == 2 && d.Policy.EpochReason == "app asked for the game route", "a job starting with the flag already up starts an epoch");
            d.Run(1000, 69500);
            d.Policy.SaveWritten(d.Now, true);
            shownSince = d.Policy.ValidAfter;
            d.OnJob = false;
            d.Run(3000, 69500);
            Check(d.Policy.Epoch == 2 && d.Presses == 2 && d.Policy.ValidAfter > shownSince, "the job ending with the flag still up starts no epoch and hides the saves of the job");

            d = Routed();
            d.OnJob = true;
            d.Paused = true;
            d.Run(1000, 69500);
            d.GameRoute = true;
            d.Run(2000, 69500);
            Check(d.Policy.Epoch == 1 && d.Presses == 1, "the flag rising during a pause waits for the resume");
            d.Paused = false;
            d.Run(100, 69500);
            Check(d.Policy.Epoch == 2 && d.Policy.EpochReason == "app asked for the game route", "and starts an epoch on resume");
            d.Run(1000, 69500);
            Check(d.Presses == 2, "and presses once the resume settled");

            d = new PolicyDriver { OnJob = true, GameRoute = true }.Run(1500, 70000);
            var onJobSave = ParseSave(PinSave(1, "_nameless.4"));
            var after = d.Policy.ValidAfter.AddSeconds(1);
            Check(d.Policy.Shows(onJobSave, after, true, true), "a job with the flag: the route of a fresh save is shown");
            Check(!d.Policy.Shows(onJobSave, after, true, false), "a job without the flag: nothing is shown");
            Check(!d.Policy.Shows(onJobSave, d.Policy.ValidAfter, true, true), "a job with the flag: a save from before the epoch is not shown");
            Check(!d.Policy.Shows(onJobSave, after, false, false) && d.Policy.Shows(ParseSave(PinSave(1)), after, false, false),
                "no job in telemetry: a save that holds a job is not shown, a pin is");
            Check(!d.Policy.Shows(ParseSave(PinSave(0, "_nameless.4")), after, true, true), "a job with the flag: a save without waypoints shows nothing");

            d = Routed();
            d.Profile = "ats/local/5E80";
            d.Run(100, 70000);
            Check(d.Policy.Epoch == 2 && d.Policy.EpochReason == "route appeared" && d.Policy.ValidAfter == d.Policy.EpochStart, "a profile change starts a new route");

            d = Routed();
            d.Live = false;
            d.Run(1000, 0);
            Check(!d.Policy.RouteUp && d.Policy.ValidAfter == DateTime.MaxValue, "the plugin going away forgets the route");
            d.Live = true;
            d.Run(1500, 70000);
            Check(d.Policy.Epoch == 2 && d.Presses == 2, "a plugin re-init with a route starts an epoch and presses");

            d = Routed();
            d.Next = new GpsWaypointV1 { X = 1000, Z = 0 };
            d.TruckX = 500;
            d.Run(1000, 70000);
            d.TruckX = 950;
            d.Run(100, 69500);
            Check(d.Policy.Epoch == 1, "coming near the next waypoint starts nothing yet");
            d.TruckX = 1100;
            d.Run(100, 69400);
            Check(d.Policy.Epoch == 1, "still within 150 m of it: nothing");
            d.TruckX = 1200;
            d.Run(100, 69300);
            Check(d.Policy.Epoch == 2 && d.Policy.EpochReason == "waypoint passed" && d.Policy.ValidAfter == d.Policy.EpochStart,
                "leaving the next waypoint behind starts a new route");
            d.Run(1500, 69200);
            Check(d.Presses == 2, "and presses to read the next one");
            d.Run(1000, 69000);
            Check(d.Policy.Epoch == 2, "the same waypoint, still listed, does not fire again from afar");

            d = Routed();
            d.Next = new GpsWaypointV1 { X = 1000, Z = 0 };
            d.TruckX = 945;
            d.Run(1000, 69000);
            Check(d.Policy.Epoch == 1, "55 m short of an Atlas stop: nothing yet");
            d.TruckX = 962;
            d.Run(100, 69000);
            var validBefore = d.Policy.ValidAfter;
            Check(d.Policy.Epoch == 2 && d.Policy.EpochReason == "waypoint reached" && validBefore < d.Policy.EpochStart,
                "38 m from it, past where the game drops it: a new route, the current one still shown");
            d.Run(1500, 69000);
            Check(d.Presses == 2, "and presses to read the next stop");
            d.Policy.SaveWritten(d.Now, true);
            d.TruckX = 990;
            d.Run(5000, 69000);
            Check(d.Policy.Epoch == 2 && d.Presses == 2, "staying there does not fire again");
            d.Next = new GpsWaypointV1 { X = 5000, Z = 0 };
            d.Run(1000, 69000);
            Check(d.Policy.Epoch == 2, "driving off to the next stop starts nothing");

            d = new PolicyDriver { Paused = true }.Run(2000, 70000);
            Check(!d.Policy.RouteUp && d.Presses == 0, "nothing happens while paused");
            d.Paused = false;
            d.Run(1500, 70000);
            Check(d.Policy.Epoch == 1 && d.Presses == 1, "a route set during the first pause appears on resume");
        }

        static PolicyDriver Routed()
        {
            var d = new PolicyDriver().Run(1500, 70000);
            d.Policy.SaveWritten(d.Now, true);
            return d.Run((int)GpsSyncPolicy.PressWindow.TotalMilliseconds, 70000);
        }

        static void GpsSyncPressWindow()
        {
            var d = Routed();
            d.Run(100, 85000).Run(1000, 85000);
            Check(d.Presses == 2, "after a quiet press window an epoch presses after the usual 1 s delay");
            var pressedAt = d.Now;
            d.Policy.SaveWritten(d.Now, true);
            d.Run(5000, 70000).Run(5000, 85000).Run(10000, 70000).Run(9900, 70000);
            Check(d.Policy.Epoch == 5 && d.Presses == 2, "a burst of epochs inside the press window: no press yet");
            d.Run(100, 70000);
            Check(d.Presses == 3 && d.Now == pressedAt + GpsSyncPolicy.PressWindow, "one press for the latest epoch when the window ends");
            d.Run(29900, 70000);
            Check(d.Presses == 3, "its retry without a fresh save waits for the window too");
            d.Run(100, 70000);
            Check(d.Presses == 4, "and comes when it ends");

            var held = ParseSave(PinSave(1));
            d = Routed();
            d.Run(100, 85000).Run(1000, 85000);
            var heldAt = d.Now;
            d.Policy.SaveWritten(heldAt, true);
            d.Run(3100, 0);
            Check(!d.Policy.RouteUp && !d.Policy.Shows(held, heldAt, false, false), "a route gone for 3 s hides the save");
            d.Run(1500, 70000);
            Check(d.Policy.EpochReason == "route appeared" && d.Presses == 2 && d.Policy.Shows(held, heldAt, false, false),
                "a route that reappears inside the press window keeps the last save shown while its press waits");
            d.Run(25400, 70000);
            Check(d.Presses == 3 && d.Now == heldAt + GpsSyncPolicy.PressWindow && d.Policy.Shows(held, heldAt, false, false),
                "the press comes when the window ends, the save still shown");
            Check(d.Policy.Shows(held, d.Now, false, false), "a save from that press shows");
            d.Run(10000, 70000);
            Check(!d.Policy.Shows(held, heldAt, false, false), "no fresh save 10 s after the deferred press: the held save is hidden");

            d = Routed();
            d.Run(100, 85000).Run(1000, 85000);
            heldAt = d.Now;
            d.Policy.SaveWritten(heldAt, true);
            d.Run(100, 70000);
            Check(d.Policy.EpochReason == "route jumped while driving" && d.Policy.Shows(held, heldAt, false, false), "a soft epoch inside the window keeps it too");
            d.Profile = "ats/local/5E80";
            d.Run(100, 70000);
            Check(d.Policy.EpochReason == "route appeared" && !d.Policy.Shows(held, heldAt, false, false), "a profile change inside the window hides the held save");

            d = Routed();
            d.GameRoute = true;
            d.Run(100, 85000).Run(1000, 85000);
            heldAt = d.Now;
            d.Policy.SaveWritten(heldAt, true);
            d.OnJob = true;
            d.Run(100, 85000);
            Check(d.Policy.EpochReason == "app asked for the game route" && d.Presses == 2 && !d.Policy.Shows(held, heldAt, true, true),
                "a job starting inside the window hides the held save");
            Check(d.Policy.Shows(ParseSave(PinSave(1, "_nameless.4")), d.Now.AddSeconds(1), true, true), "a save written after the job started shows while the press waits");

            var jobSave = ParseSave(PinSave(1, "_nameless.4"));
            d = new PolicyDriver { OnJob = true, GameRoute = true }.Run(1500, 70000);
            heldAt = d.Now;
            d.Policy.SaveWritten(heldAt, true);
            d.GameRoute = false;
            d.Run(1000, 70000);
            Check(!d.Policy.Shows(jobSave, heldAt, true, false), "a job without the flag shows nothing");
            d.GameRoute = true;
            d.Run(100, 70000);
            Check(d.Policy.EpochReason == "app asked for the game route" && d.Presses == 1 && d.Policy.Shows(jobSave, heldAt, true, true),
                "the flag rising again inside the window keeps the last job save shown while its press waits");
            d.Run(28400, 70000);
            Check(d.Presses == 1, "no press 29.9 s after the last one");
            d.Run(100, 70000);
            Check(d.Presses == 2, "and one press when the window ends");

            d = Routed();
            d.Run(100, 85000).Run(1000, 85000);
            d.App = false;
            d.Run(3100, 0).Run(100, 70000);
            Check(d.Policy.EpochReason == "route appeared" && d.Policy.ValidAfter == d.Policy.EpochStart,
                "a route that reappears inside the window with no app connected hides the last save at once");

            d = Routed();
            d.Run(100, 85000).Run(1000, 85000);
            d.Run(500, 70000);
            d.Policy.SaveWritten(d.Now, true);
            Check(d.Policy.Epoch == 3 && d.Policy.Pending, "the save of a press made before the epoch began does not answer it");
            d.Run(29500, 70000);
            Check(d.Presses == 3, "so the epoch still presses when the window ends");
            d.Policy.SaveWritten(d.Now.AddSeconds(1), true);
            Check(!d.Policy.Pending, "and the save of that press ends it");

            d = Routed();
            d.Run(100, 85000).Run(1000, 85000);
            d.Policy.PressLost();
            Check(d.Policy.ValidAfter == d.Policy.EpochStart, "a press the game did not take ends the hold at once");
            d.Run(30000, 85000);
            Check(d.Presses == 3, "the epoch still retries when the window ends");

            d = Routed();
            d.Run(100, 85000).Run(1000, 85000);
            d.Policy.PressLost();
            d.Run(500, 70000);
            d.Policy.SaveWritten(d.Now, true);
            Check(d.Policy.Epoch == 3 && !d.Policy.Pending, "a lost press owes no save: the next save answers the epoch after it");
        }

        static void GpsSyncSwallowedPress()
        {
            var d = Routed();
            d.Run(100, 85000).Run(1000, 85000);
            Check(d.Presses == 2, "an epoch presses");
            d.Paused = true;
            d.Run(1000, 85000);
            d.Paused = false;
            d.Run(3900, 85000);
            Check(d.Policy.EpochReason == "resumed after a pause" && d.Presses == 2, "a press the game swallowed without a save: the next epoch waits 5 s after it");
            d.Run(100, 85000);
            Check(d.Presses == 3, "not the whole press window");
            d.Run(29900, 85000);
            Check(d.Presses == 3, "inside that epoch its retry still waits for the press window");
            d.Run(100, 85000);
            Check(d.Presses == 4, "and comes when it ends");

            d = Routed();
            d.Run(100, 85000).Run(1000, 85000);
            d.Policy.PressLost();
            d.Paused = true;
            d.Run(1000, 85000);
            d.Paused = false;
            d.Run(28900, 85000);
            Check(d.Presses == 2, "a press the game did not take still holds the next epoch for the press window");
            d.Run(100, 85000);
            Check(d.Presses == 3, "until it ends");
        }

        static void GpsSyncContract()
        {
            var fields = typeof(GpsSyncZone).GetFields().OrderBy(f => (int)Marshal.OffsetOf(typeof(GpsSyncZone), f.Name)).ToList();
            Check(ScsTelemetryDataReader.GpsSyncOffset == 30720 && GpsTargetWatcher.MinPluginRevision == 14, "sync zone at 30720, plugin revision 14");
            Check(Marshal.SizeOf(typeof(GpsSyncZone)) == 16 && fields.Select(f => (int)Marshal.OffsetOf(typeof(GpsSyncZone), f.Name)).SequenceEqual(new[] { 0, 4, 8, 12 })
                  && ScsTelemetryDataReader.GpsSyncOffset + 16 <= 32 * 1024, "four 4-byte fields that fit the 32 KB map");

            string header = null;
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null && header == null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "trucksim-gps-plugin", "scs-telemetry", "inc", "scs-telemetry-common.hpp");
                if (File.Exists(path))
                    header = path;
            }
            if (header == null)
            {
                Console.WriteLine("  [skip] no trucksim-gps-plugin checkout next to this repo");
                return;
            }
            string text = File.ReadAllText(header);
            var revision = Regex.Match(text, @"#define\s+PLUGIN_REVID\s+(\d+)");
            Check(revision.Success && int.Parse(revision.Groups[1].Value) >= GpsTargetWatcher.MinPluginRevision
                  && Regex.IsMatch(text, $@"#define\s+SCS_GPS_SYNC_OFFSET\s+{ScsTelemetryDataReader.GpsSyncOffset}\b"), "the plugin header: the same offset, a revision the server accepts");
            var names = Regex.Matches(Regex.Match(text, @"typedef struct \{([^}]*)\} scsGpsSync_t;").Groups[1].Value, @"unsigned int (\w+);")
                .Cast<Match>().Select(m => m.Groups[1].Value.ToLowerInvariant());
            Check(names.SequenceEqual(fields.Select(f => f.Name.ToLowerInvariant())), "the plugin header has the same zone fields in the same order");
        }

        // Trimmed from ATS 1.61 saves: only the units the parser reads, plus noise.
        const string FreightJobWithPinsSave = @"SiiNunit
{
economy : _nameless.172.b7b9.0018 {
 stored_online_gps_ahead_waypoints: 0
 stored_gps_behind_waypoints: 1
 stored_gps_behind_waypoints[0]: _nameless.173.7370.e578
 stored_gps_ahead_waypoints: 3
 stored_gps_ahead_waypoints[0]: _nameless.173.7370.e6f8
 stored_gps_ahead_waypoints[1]: _nameless.173.7370.ebf8
 stored_gps_ahead_waypoints[2]: _nameless.173.7370.eef8
 stored_gps_avoid_waypoints: 0
}

player : _nameless.172.bfa7.0a88 {
 current_job: null
 current_car_job: null
 selected_job: _nameless.173.7433.f168
}

company : company.volatile.cm_brx_pln.barstow {
 selected_job: _nameless.172.c434.2228
}

job_info : _nameless.172.c434.2228 {
 cargo: cargo.lumber
 source_company: company.volatile.wal_food_str.reno
}

job_info : _nameless.173.7433.f168 {
 cargo: cargo.mobile_compr
 source_company: company.volatile.cm_brx_pln.barstow
 target_company: company.volatile.avs_met_scr.oakland
 is_cargo_market_job: false
 planned_distance_km: 575
 car: null
 units_count: 2
}

gps_waypoint_storage : _nameless.173.7370.e578 {
 nav_node_position: (-24937488, 7588, 3248513)
 direction: forward
}

gps_waypoint_storage : _nameless.173.7370.e6f8 {
 nav_node_position: (-23567686, 7170, 3698049)
 direction: any
}

gps_waypoint_storage : _nameless.173.7370.ebf8 {
 nav_node_position: (-22056931, 9001, 1991286)
 direction: any
}

gps_waypoint_storage : _nameless.173.7370.eef8 {
 nav_node_position: (-25117497, 6912, 2516393)
 direction: any
}
}
";

        const string DispatchJobSave = @"SiiNunit
{
economy : _nameless.172.b7b9.0018 {
 stored_gps_ahead_waypoints: 1
 stored_gps_ahead_waypoints[0]: _nameless.190.e566.f5a8
}

player : _nameless.190.c21a.6ab8 {
 current_job: null
 current_car_job: null
 selected_job: _nameless.191.77e8.2ba8
}

job_info : _nameless.191.77e8.2ba8 {
 cargo: null
 source_company: company.volatile.lum_car_dlr.san_francisc
 target_company: company.volatile.lum_car_dlr.santa_maria
 is_cargo_market_job: false
 planned_distance_km: 385
 car: _nameless.191.a6d8.0538
 units_count: 1
}

car_offer_data : _nameless.191.a6d8.0538 {
 source: ""lum_car_dlr.san_francisc""
 target: ""lum_car_dlr.santa_maria""
 cargo: car_cargo.dealerdocs
}

gps_waypoint_storage : _nameless.190.e566.f5a8 {
 nav_node_position: (-29391278, 1652, -2612712)
 direction: any
}
}
";

        static string PinSave(int aheadCount, string currentJob = "null") => $@"SiiNunit
{{
economy : _nameless.1 {{
 stored_gps_ahead_waypoints: {aheadCount}
{(aheadCount > 0 ? " stored_gps_ahead_waypoints[0]: _nameless.2" : "")}
}}

player : _nameless.3 {{
 current_job: {currentJob}
 current_car_job: null
 selected_job: null
}}

gps_waypoint_storage : _nameless.2 {{
 nav_node_position: (-4413517, 8013, -9535923)
 direction: any
}}
}}
";

        static SaveGpsState ParseSave(string text)
        {
            using (var reader = new StringReader(text))
                return SaveGpsParser.Parse(reader);
        }

        static bool Near(float actual, double expected) => Math.Abs(actual - expected) < 0.05;

        static void SaveGpsParsing()
        {
            var freight = ParseSave(FreightJobWithPinsSave);
            Check(freight.Waypoints.Count == 3 && Near(freight.Waypoints[0].X, -92061.27) && Near(freight.Waypoints[0].Z, 14445.50),
                "freight job + 2 pins: every ahead waypoint, the first pin first");
            Check(Near(freight.Waypoints[2].X, -98115.22) && Near(freight.Waypoints[2].Y, 27.0) && Near(freight.Waypoints[2].Z, 9829.66),
                "freight job + 2 pins: the pickup last");
            var job = freight.SelectedJob;
            Check(job != null && job.CargoId == "mobile_compr" && job.SourceCompanyId == "cm_brx_pln" && job.SourceCityId == "barstow" &&
                  job.DestinationCompanyId == "avs_met_scr" && job.DestinationCityId == "oakland" && job.PlannedDistanceKm == 575 &&
                  job.UnitCount == 2 && job.Market == "freight_market", "freight job: selected job read from job_info");
            Check(!freight.JobActive, "a selected job is not an active job");

            var dispatch = ParseSave(DispatchJobSave);
            Check(dispatch.Waypoints.Count == 1 && Near(dispatch.Waypoints[0].X, -114809.68) && Near(dispatch.Waypoints[0].Z, -10205.91), "dispatch job: pickup position");
            Check(dispatch.SelectedJob?.Market == "dispatch_job" && dispatch.SelectedJob.CargoId == "dealerdocs" &&
                  dispatch.SelectedJob.SourceCityId == "san_francisc", "dispatch job: market and cargo from the car offer");

            var pin = ParseSave(PinSave(1));
            Check(pin.Waypoints.Count == 1 && Near(pin.Waypoints[0].X, -17240.30) && pin.SelectedJob == null, "map pin or Atlas target: destination, no job");
            Check(ParseSave(PinSave(0)).Waypoints.Count == 0, "no route: no destination");
            Check(ParseSave(PinSave(1, "_nameless.4")).JobActive, "an active job is reported");
            Check(ParseSave("garbage\n}\n").Waypoints.Count == 0, "unreadable text: no destination");
            int lastUnit = FreightJobWithPinsSave.IndexOf("gps_waypoint_storage : _nameless.173.7370.ebf8", StringComparison.Ordinal);
            Check(Throws(() => ParseSave(FreightJobWithPinsSave.Substring(0, lastUnit))), "a save cut after one of its units (caught mid-write) is rejected");
            Check(Throws(() => ParseSave(FreightJobWithPinsSave.TrimEnd().TrimEnd('}'))), "a save without its closing brace is rejected");
        }

        static void GpsSyncJson()
        {
            var empty = JObject.Parse(JsonConvert.SerializeObject(new TelemetryV1(), JsonHelper.RestSettings));
            Check(empty["gpsRoute"]?.Type == JTokenType.Null && empty["selectedJob"]?.Type == JTokenType.Null, "both fields are null by default");

            var state = ParseSave(FreightJobWithPinsSave);
            var route = new GpsRouteV1 { SavedAt = new DateTime(2026, 10, 13, 11, 49, 12, DateTimeKind.Utc), Waypoints = state.Waypoints };
            var json = JObject.Parse(JsonConvert.SerializeObject(new TelemetryV1 { GpsRoute = route, SelectedJob = state.SelectedJob }, JsonHelper.RestSettings));
            Check(json["gpsRoute"]?["savedAt"] != null && json["gpsRoute"]["waypoints"]?.Count() == 3 && json["gpsRoute"]["waypoints"][0]["x"] != null,
                "gpsRoute serializes as savedAt + waypoints of x, y, z");
            Check((string)json["selectedJob"]?["sourceCityId"] == "barstow" && (string)json["selectedJob"]["market"] == "freight_market",
                "selectedJob serializes in camelCase");
        }
    }
}
