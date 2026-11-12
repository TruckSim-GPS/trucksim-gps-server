using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Funbit.Ets.Telemetry.Server;
using Funbit.Ets.Telemetry.Server.Controllers;
using Funbit.Ets.Telemetry.Server.Helpers;
using Funbit.Ets.Telemetry.Server.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TruckSimGps.VoiceFx;

namespace PairingTests
{
    // Run after a Release build: dotnet run --project tests/PairingTests -c Release
    static partial class Program
    {
        static int _failures;

        static int Main(string[] args)
        {
            Run("crypto vectors", CryptoVectors);
            Run("tunnel codec", TunnelCodec);
            Run("pairing session", () => PairingSession().GetAwaiter().GetResult());
            Run("http routes", () => HttpRoutes().GetAwaiter().GetResult());
            Run("handshake", Handshake);
            Run("standby token freshness", StandbyTokenFreshness);
            Run("throughput meter", ThroughputMeter);
            Run("linked devices order", LinkedDevicesOrder);
            Run("relay selection", () => RelaySelection().GetAwaiter().GetResult());
            Run("reconnect jitter", ReconnectJitter);
            Run("parking rules", ParkingRulesTable);
            Run("standby line", () => StandbyLine().GetAwaiter().GetResult());
            Run("voice clips and cache", VoiceClipsAndCache);
            Run("voice decoder", VoiceDecode);
            Run("voice fx vectors", VoiceFxVectors);
            Run("voice routes", () => VoiceRoutes().GetAwaiter().GetResult());
            Run("gps sync policy", GpsSyncPolicyRules);
            Run("gps sync press window", GpsSyncPressWindow);
            Run("gps sync swallowed press", GpsSyncSwallowedPress);
            Run("gps sync plugin contract", GpsSyncContract);
            Run("save gps parser", SaveGpsParsing);
            Run("gps sync json", GpsSyncJson);

            Console.WriteLine(_failures == 0 ? "ALL PASSED" : $"FAILED ({_failures})");
            return _failures == 0 ? 0 : 1;
        }

        static void Run(string name, Action test)
        {
            Console.WriteLine($"== {name}");
            try
            {
                test();
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine($"  [FAIL] {name}: {ex}");
            }
        }

        static void Check(bool condition, string what)
        {
            Console.WriteLine($"  [{(condition ? "PASS" : "FAIL")}] {what}");
            if (!condition)
                _failures++;
        }

        static async Task RelaySelection()
        {
            using (var good = new FakeApi((m, p, b) => Tuple.Create(200, "{}")))
            using (var spare = new FakeApi((m, p, b) => Tuple.Create(200, "{}")))
            using (var originDown = new FakeApi((m, p, b) => Tuple.Create(521, "error code: 521")))
            using (var http = new HttpClient())
            {
                string dead = "http://127.0.0.1:1";
                string listing = JsonConvert.SerializeObject(new[] { good.BaseUrl, spare.BaseUrl, originDown.BaseUrl, dead }
                    .Select(u => new { url = u + "/", region = "eu" }));
                using (var api = new FakeApi((m, p, b) => p == "/api/relays" ? Tuple.Create(200, listing) : Tuple.Create(404, "{}")))
                {
                    string best = await RelaySelector.MeasureAsync(http, api.BaseUrl, new HashSet<string> { spare.BaseUrl });
                    Check(best == good.BaseUrl, "excluded relay skipped, Cloudflare 52x and refused connection are not live: " + best);
                    Check(originDown.Requests.Count == 1, "a relay that failed a ping gets no more probes: " + originDown.Requests.Count);
                    Check(good.Requests.Count == 3, "a live relay gets three probes");

                    best = await RelaySelector.MeasureAsync(http, api.BaseUrl, new HashSet<string> { good.BaseUrl, spare.BaseUrl, originDown.BaseUrl, dead });
                    Check(best == good.BaseUrl || best == spare.BaseUrl, "every relay excluded: measure them all rather than hand out none: " + best);

                    Check(await RelaySelector.PingAsync(http, good.BaseUrl), "ping: 200 is alive");
                    Check(!await RelaySelector.PingAsync(http, originDown.BaseUrl), "ping: 521 from the CDN is not");
                }
            }
        }

        static void ReconnectJitter()
        {
            var first = Enumerable.Range(0, 200).Select(_ => PresenceClient.ReconnectDelay(0).TotalSeconds).ToList();
            Check(first.All(s => s >= 0.5 && s <= 1.5), "first reconnect within 0.5-1.5 s");
            Check(first.Max() - first.Min() > 0.5, "first reconnects spread out: " + first.Min().ToString("F2") + "-" + first.Max().ToString("F2"));

            var late = Enumerable.Range(0, 200).Select(_ => PresenceClient.ReconnectDelay(42).TotalSeconds).ToList();
            Check(late.All(s => s >= 30 && s <= 90), "later reconnects stay 60 s +/-50%");

            var backoff = Enumerable.Range(0, 200).Select(_ => PresenceClient.Jittered(300).TotalSeconds).ToList();
            Check(backoff.All(s => s >= 150 && s <= 450) && backoff.Max() - backoff.Min() > 100, "registration backoff 300 s +/-50%");
        }

        static void ParkingRulesTable()
        {
            var t0 = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
            var policy = new StandbyPolicy();
            var rules = new ParkingRules();
            Check(!rules.Update(false, WindowPresence.Hidden, policy, t0), "no game, window hidden: not parked");
            Check(rules.Update(true, WindowPresence.Hidden, policy, t0), "game running, window in the tray: parked");
            Check(rules.Update(false, WindowPresence.Hidden, policy, t0.AddMinutes(1)), "game just closed, window in the tray: parked during the grace");
            Check(rules.Update(false, WindowPresence.Hidden, policy, t0.AddMinutes(10.9)), "still inside the 10 min grace");
            Check(!rules.Update(false, WindowPresence.Hidden, policy, t0.AddMinutes(11.1)), "unparked once the grace is over");

            rules = new ParkingRules();
            Check(rules.Update(false, WindowPresence.Front, policy, t0), "no game, window in front: parked");
            Check(rules.Update(false, WindowPresence.Background, policy, t0.AddMinutes(1)), "window loses focus: parked during the grace");
            Check(!rules.Update(false, WindowPresence.Background, policy, t0.AddMinutes(11.1)), "unparked once the grace is over");
            Check(rules.Update(false, WindowPresence.Front, policy, t0.AddMinutes(12)), "focused again: parked");
            Check(!rules.Update(false, WindowPresence.Hidden, policy, t0.AddMinutes(12.1)), "minimized with the game closed: unparked at once");

            rules = new ParkingRules();
            rules.Update(true, WindowPresence.Front, policy, t0);
            Check(rules.Update(false, WindowPresence.Front, policy, t0.AddMinutes(1)), "game closed, window in front: parked");
            Check(!rules.Update(false, WindowPresence.Hidden, policy, t0.AddMinutes(2)), "then minimized: unparked at once, the game's grace too");

            var gameOnly = new StandbyPolicy { ParkWithoutGame = false, GraceMinutes = 3 };
            rules = new ParkingRules();
            Check(!rules.Update(false, WindowPresence.Front, gameOnly, t0), "park without the game off: window in front alone does not park");
            Check(!rules.Update(false, WindowPresence.Background, gameOnly, t0.AddSeconds(1)), "and losing focus starts no grace");
            Check(rules.Update(true, WindowPresence.Background, gameOnly, t0.AddMinutes(1)), "the game still parks");
            Check(rules.Update(false, WindowPresence.Background, gameOnly, t0.AddMinutes(2)), "game closed: parked during the policy's grace");
            Check(rules.Update(false, WindowPresence.Background, gameOnly, t0.AddMinutes(4.9)), "which is 3 min");
            Check(!rules.Update(false, WindowPresence.Background, gameOnly, t0.AddMinutes(5.1)), "then unparked");
        }

        static async Task StandbyLine()
        {
            using (var full = new FakeRelay { Full = true })
            using (var relay = new FakeRelay())
            {
                var settings = StandbySettings();
                var handshakes = new List<StandbyHandshake>();
                using (var client = new StandbyClient(settings, () => Task.FromResult<IReadOnlyList<string>>(new[] { full.Url, relay.Url }),
                    h => { lock (handshakes) handshakes.Add(h); return Task.CompletedTask; }, TimeSpan.FromSeconds(4), _ => TimeSpan.FromMilliseconds(300)))
                {
                    client.Update(true, false, WindowPresence.Front);
                    Check(await Until(() => client.IsParked && client.ParkedRelay == relay.Url), "a full relay (4003) is skipped for the next one");
                    Check(full.Connects == 1 && relay.Authorization == "Bearer token-1", "parks with the token in Authorization");

                    await relay.Send("ping");
                    Check(await Until(() => { lock (relay.Received) return relay.Received.Contains("pong"); }), "answers the relay's ping");

                    await relay.Send("{\"t\":\"handshake\",\"sessionId\":\"s1\",\"deviceId\":\"d1\",\"nonceA\":\"n\",\"env\":\"e\",\"pcTicket\":\"p\",\"standbyToken\":\"token-2\"}");
                    Check(await Until(() => { lock (handshakes) return handshakes.Count == 1; }), "a ring reaches the handshake path");
                    Check(await client.SendReply("s1", true, "envR", "https://relay") && await Until(() => { lock (relay.Received) return relay.Received.Any(f => f.Contains("\"reply\"") && f.Contains("\"s1\"")); }),
                        "the reply goes back on the same socket");

                    client.Update(true, false, WindowPresence.Hidden);
                    Check(await Until(() => !client.IsParked && relay.Closed), "minimized with no game: unparks at once");
                    client.Update(true, false, WindowPresence.Front);
                    Check(await Until(() => client.IsParked && relay.Connects == 2), "back in front: parks again at once");

                    await relay.CloseCurrent(4001);
                    await Task.Delay(1000);
                    Check(!client.IsParked && relay.Connects == 2, "4001 replaced: no reconnect on its own");
                    client.Update(true, false, WindowPresence.Hidden);
                    client.Update(true, false, WindowPresence.Front);
                    Check(await Until(() => client.IsParked && relay.Connects == 3), "the next time the window comes to the front it parks again");

                    await relay.CloseCurrent(1001);
                    Check(await Until(() => client.IsParked && relay.Connects == 4), "a drop reconnects after the backoff");

                    await Task.Delay(5000);
                    Check(await Until(() => client.IsParked && relay.Connects == 5), "a line silent past the limit is replaced");

                    await relay.Send("{\"t\":\"policy\",\"parkWithoutGame\":false,\"graceMinutes\":3}");
                    Check(await Until(() => !settings.StandbyPolicy.ParkWithoutGame && settings.StandbyPolicy.GraceMinutes == 3), "a policy frame updates the settings");
                    Check(await Until(() => { var saved = Settings.Load(settings.FilePath).StandbyPolicy; return !saved.ParkWithoutGame && saved.GraceMinutes == 3; }), "and it is saved");
                    Check(await Until(() => !client.IsParked), "park without the game off: the window alone no longer holds the line");
                }
            }

            using (var wrongKey = new FakeRelay { RefuseWith = 401 })
            using (var relay = new FakeRelay())
            {
                var settings = StandbySettings();
                using (var client = new StandbyClient(settings, () => Task.FromResult<IReadOnlyList<string>>(new[] { wrongKey.Url, relay.Url }), _ => Task.CompletedTask,
                    TimeSpan.FromSeconds(4), _ => TimeSpan.FromMilliseconds(300)))
                {
                    client.Update(true, true, WindowPresence.Hidden);
                    Check(await Until(() => client.IsParked && client.ParkedRelay == relay.Url) && settings.StandbyToken == "token-1",
                        "one relay's 401 does not drop the token: the next relay takes it");
                }
            }

            using (var relay = new FakeRelay { RefuseWith = 401 })
            {
                var settings = StandbySettings();
                using (var client = new StandbyClient(settings, () => Task.FromResult<IReadOnlyList<string>>(new[] { relay.Url }), _ => Task.CompletedTask,
                    TimeSpan.FromSeconds(2), _ => TimeSpan.FromMilliseconds(300)))
                {
                    client.Update(true, true, WindowPresence.Hidden);
                    Check(await Until(() => settings.StandbyToken == null), "401 drops the token");
                    await Task.Delay(1000);
                    Check(relay.Connects == 1, "401: no retry without a token");
                }
            }
        }

        static Settings StandbySettings()
        {
            var settings = Settings.Load(Path.Combine(Path.GetTempPath(), "standby-test-" + Guid.NewGuid().ToString("N") + ".json"));
            settings.StandbyToken = "token-1";
            return settings;
        }

        static async Task<bool> Until(Func<bool> condition)
        {
            for (int i = 0; i < 50 && !condition(); i++)
                await Task.Delay(100);
            return condition();
        }

        sealed class FakeRelay : IDisposable
        {
            readonly HttpListener _listener = new HttpListener();
            System.Net.WebSockets.WebSocket _current;
            int _connects;

            public int RefuseWith { get; set; }
            public bool Full { get; set; }
            public bool Closed { get; private set; }
            public string Authorization { get; private set; }
            public List<string> Received { get; } = new List<string>();
            public int Connects => Volatile.Read(ref _connects);
            public string Url { get; }

            public FakeRelay()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                int port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                Url = "http://localhost:" + port;
                _listener.Prefixes.Add(Url + "/");
                _listener.Start();
                _ = Task.Run(Accept);
            }

            async Task Accept()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch (Exception)
                    {
                        return;
                    }
                    Interlocked.Increment(ref _connects);
                    Authorization = context.Request.Headers["Authorization"];
                    if (RefuseWith != 0 || context.Request.Url.AbsolutePath != "/standby")
                    {
                        context.Response.StatusCode = RefuseWith != 0 ? RefuseWith : 404;
                        context.Response.Close();
                        continue;
                    }
                    var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
                    if (Full)
                    {
                        await socket.CloseOutputAsync((System.Net.WebSockets.WebSocketCloseStatus)4003, "full", CancellationToken.None);
                        continue;
                    }
                    _current = socket;
                    Closed = false;
                    await Send("{\"t\":\"policy\",\"parkWithoutGame\":true,\"graceMinutes\":10}");
                    _ = Task.Run(() => Serve(socket));
                }
            }

            async Task Serve(System.Net.WebSockets.WebSocket socket)
            {
                var buffer = new byte[4096];
                try
                {
                    while (socket.State == System.Net.WebSockets.WebSocketState.Open)
                    {
                        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                        {
                            Closed = true;
                            await socket.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                            return;
                        }
                        lock (Received)
                            Received.Add(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                }
                catch (Exception)
                {
                }
            }

            public Task Send(string text)
            {
                return _current.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
            }

            public Task CloseCurrent(int code)
            {
                return _current.CloseOutputAsync((System.Net.WebSockets.WebSocketCloseStatus)code, "test", CancellationToken.None);
            }

            public void Dispose()
            {
                _listener.Close();
            }
        }

        static void ThroughputMeter()
        {
            var meter = new ThroughputMeter();
            var t0 = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
            meter.Sample(0, 0, t0);
            Check(meter.UpBytesPerSecond == 0 && meter.DownBytesPerSecond == 0, "single sample reads zero");
            meter.Sample(2048, 512, t0.AddSeconds(1));
            Check(meter.UpBytesPerSecond == 2048 && meter.DownBytesPerSecond == 512, "two samples one second apart give bytes per second");
            for (int i = 2; i <= 5; i++)
                meter.Sample(2048L * i, 512L * i, t0.AddSeconds(i));
            Check(meter.UpBytesPerSecond == 2048 && meter.DownBytesPerSecond == 512, "steady stream over the full 5 s window");
            meter.Sample(2048L * 5, 512L * 5, t0.AddSeconds(6));
            Check(Math.Abs(meter.UpBytesPerSecond - 2048 * 4 / 5.0) < 0.001, "oldest sample drops out once it is older than 5 s: " + meter.UpBytesPerSecond);
            for (int i = 7; i <= 12; i++)
                meter.Sample(2048L * 5, 512L * 5, t0.AddSeconds(i));
            Check(meter.UpBytesPerSecond == 0 && meter.DownBytesPerSecond == 0, "idle tunnel decays to zero within the window");
            meter.Sample(2048L * 5 + 100, 512L * 5, t0.AddSeconds(12));
            Check(meter.UpBytesPerSecond == 20 && meter.DownBytesPerSecond == 0, "a second sample at the same tick keeps the window span");
        }

        static void LinkedDevicesOrder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tsgps-pairing-tests-" + Guid.NewGuid().ToString("N"));
            var settings = Settings.Load(Path.Combine(dir, "Settings.json"));
            var devices = new LinkedDeviceStore(settings);
            var now = DateTime.UtcNow;
            settings.LinkedDevices.AddRange(new[]
            {
                new LinkedDevice { DeviceId = "idle-old", AddedAt = now.AddDays(-9), LastSeenAt = now.AddDays(-3) },
                new LinkedDevice { DeviceId = "phone", AddedAt = now.AddDays(-5), LastSeenAt = now },
                new LinkedDevice { DeviceId = "idle-recent", AddedAt = now.AddDays(-1), LastSeenAt = now.AddHours(-1) },
                new LinkedDevice { DeviceId = "tablet", AddedAt = now.AddDays(-8), LastSeenAt = now.AddSeconds(-2) },
            });
            string order = string.Join(",", devices.Sorted().Select(d => d.DeviceId));
            Check(order == "tablet,phone,idle-recent,idle-old", "connected first in link order, then idle by last seen: " + order);
            devices.Touch("tablet", null, ConnectionMethod.Direct);
            order = string.Join(",", devices.Sorted().Select(d => d.DeviceId));
            Check(order == "tablet,phone,idle-recent,idle-old", "a request from a connected device does not move it: " + order);
            Directory.Delete(dir, true);
        }

        static void CryptoVectors()
        {
            var doc = JObject.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crypto_vectors.json")));
            foreach (var v in doc["hkdf"])
            {
                var okm = Crypto.Hkdf(Crypto.FromHex((string)v["ikm"]), Crypto.FromHex((string)v["salt"]),
                    Crypto.FromHex((string)v["infoHex"]), (int)v["length"]);
                Check(Crypto.ToHex(okm) == (string)v["okm"], "hkdf " + v["name"]);
            }
            foreach (var v in doc["aesGcm"])
            {
                var key = Crypto.FromHex((string)v["key"]);
                var nonce = Crypto.FromHex((string)v["nonce"]);
                var plaintext = Crypto.FromHex((string)v["plaintext"]);
                var expected = (string)v["ciphertext"] + (string)v["tag"];
                Check(Crypto.ToHex(Crypto.GcmEncrypt(key, nonce, plaintext)) == expected, "gcm encrypt " + v["name"]);
                Check(Crypto.ToHex(Crypto.GcmDecrypt(key, nonce, Crypto.FromHex(expected))) == (string)v["plaintext"], "gcm decrypt " + v["name"]);
            }
            var env = doc["aesGcm"].First(x => (string)x["name"] == "env_under_deviceKey");
            string envelope = Crypto.Base64Url(Crypto.Concat(Crypto.FromHex((string)env["nonce"]), Crypto.FromHex((string)env["ciphertext"] + (string)env["tag"])));
            Check(Crypto.OpenEnvelope(Crypto.FromHex((string)env["key"]), envelope) == Encoding.UTF8.GetString(Crypto.FromHex((string)env["plaintext"])), "envelope open matches vector");
            var tampered = Crypto.FromBase64Url(envelope);
            tampered[tampered.Length - 1] ^= 1;
            Check(Throws(() => Crypto.OpenEnvelope(Crypto.FromHex((string)env["key"]), Crypto.Base64Url(tampered))), "tampered envelope rejected");
            var voice = doc["hkdf"].First(x => (string)x["name"] == "voice_hello_key");
            Check(Crypto.ToHex(Crypto.VoiceHelloKey(Crypto.FromHex((string)voice["ikm"]))) == (string)voice["okm"], "voice hello key = HKDF(deviceKey, empty, tsgps-voice-v1)");
            Check(Crypto.ToHex(Crypto.TunnelNonce(2, 1)) == "000000020000000000000001", "tunnel nonce layout");
            Check(Crypto.ToHex(Crypto.TunnelNonce(1, ulong.MaxValue)) == "00000001ffffffffffffffff", "tunnel nonce max counter");
        }

        static bool Throws(Action action)
        {
            try { action(); return false; } catch (Exception) { return true; }
        }

        static void TunnelCodec()
        {
            var key = Crypto.RandomBytes(32);
            var encoder = new TunnelEncoder(key, TunnelEncoder.AppToPc);
            var decoder = new TunnelDecoder(key, TunnelEncoder.AppToPc);
            var payloads = new List<byte[]>();
            var rng = new Random(7);
            for (int i = 0; i < 50; i++)
            {
                var payload = new byte[rng.Next(0, 3000)];
                for (int j = 0; j < payload.Length; j++)
                    payload[j] = (byte)"GET /api/ets2/telemetry HTTP/1.1\r\n"[j % 34];
                payloads.Add(payload);
            }
            var wires = payloads.Select((p, i) => encoder.Encode(TunnelFrame.Build((ushort)(i % 7), (byte)(i % 3), p, 0, p.Length))).ToList();
            bool allMatch = true;
            long wireBytes = 0, plainBytes = 0;
            for (int i = 0; i < wires.Count; i++)
            {
                var frame = decoder.Decode(wires[i]);
                allMatch &= TunnelFrame.StreamId(frame) == i % 7 && frame[2] == i % 3 &&
                            frame.Skip(3).SequenceEqual(payloads[i]);
                wireBytes += wires[i].Length;
                plainBytes += payloads[i].Length;
            }
            Check(allMatch, $"50 frames round trip in order ({plainBytes} B plain -> {wireBytes} B wire)");
            Check(wireBytes < plainBytes / 2, "repetitive payload compresses with the shared context");
            ulong counter = 0;
            for (int i = 0; i < 8; i++)
                counter = (counter << 8) | wires[0][i];
            Check(counter == 0 && wires[1][7] == 1, "counters start at 0 and increment");
            Check(Throws(() => decoder.Decode(wires[10])), "replayed counter rejected");
            var other = new TunnelDecoder(key, TunnelEncoder.PcToApp);
            Check(Throws(() => other.Decode(encoder.Encode(TunnelFrame.Build(1, 0, new byte[4], 0, 4)))), "wrong direction id fails authentication");
        }

        static async Task PairingSession()
        {
            int conflicts = 1;
            using (var api = new FakeApi((method, path, body) =>
            {
                if (method == "POST" && path == "/api/pair/sessions")
                {
                    if (conflicts-- > 0)
                        return Tuple.Create(409, "{}");
                    var json = JObject.Parse(body);
                    bool valid = ((string)json["codeHash"]).Length == 64 && ((string)json["pcId"]).Length == 32
                                 && Crypto.FromBase64Url((string)json["salt"]).Length == 16 && ((string)json["blob"]).Length > 40;
                    return valid ? Tuple.Create(201, "{}") : Tuple.Create(400, "{}");
                }
                if (method == "DELETE" && path.StartsWith("/api/pair/sessions/"))
                    return Tuple.Create(204, "");
                return Tuple.Create(404, "");
            }))
            {
                string pcId = Crypto.RandomHexId();
                var candidates = new List<string> { "192.168.1.42/24", "127.0.0.1/8" };
                var states = new List<PairingState>();
                using (var service = new PairingSessionService(api.BaseUrl, pcId, 31377, () => candidates, TimeSpan.FromSeconds(2)))
                {
                    service.StateChanged += () => states.Add(service.State);
                    await service.GenerateAsync();
                    Check(service.State == PairingState.Active && states.SequenceEqual(new[] { PairingState.Generating, PairingState.Active }), "generate: Generating -> Active");
                    Check(api.Requests.Count(r => r.StartsWith("POST /api/pair/sessions")) == 2, "409 code clash retried with a new code");
                    var session = service.Current;
                    Check(session.Code.Length == 6 && session.Code.All(c => PairingSessionService.CodeAlphabet.Contains(c)), "code is 6 chars of the alphabet: " + session.Code);
                    Check(session.CodeHash == Crypto.ToHex(Crypto.Sha256(Encoding.UTF8.GetBytes(session.Code))), "codeHash = sha256(code)");

                    var uri = new Uri(session.QrPayload);
                    var query = uri.Query.TrimStart('?').Split('&').Select(p => p.Split(new[] { '=' }, 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
                    Check(uri.Scheme == "trucksimgps" && uri.Host == "connect" && query["v"] == "2", "QR payload is trucksimgps://connect?v=2");
                    Check(query["pc"] == pcId && query["p"] == "31377" && query["i"] == "192.168.1.42/24,127.0.0.1/8" && query["n"] == Environment.MachineName, "QR carries pcId, port, candidates, name");
                    Check(Crypto.FromBase64Url(query["s"]).SequenceEqual(session.Secret), "QR carries S");
                    var hash = Crypto.Sha256(Encoding.UTF8.GetBytes(session.Code)).Take(12).ToArray();
                    Check(query["h"] == Crypto.Base64Url(hash), "QR h = first 12 bytes of sha256(code)");

                    var posted = JObject.Parse(api.Bodies.Last());
                    var blobKey = Crypto.Hkdf(Encoding.UTF8.GetBytes(session.Code), Crypto.FromBase64Url((string)posted["salt"]), Crypto.PairInfo, 32);
                    var blob = JObject.Parse(Crypto.OpenEnvelope(blobKey, (string)posted["blob"]));
                    Check((string)blob["pcId"] == pcId && (int)blob["port"] == 31377 && (string)blob["s"] == query["s"]
                          && blob["candidates"].Select(c => (string)c).SequenceEqual(candidates), "blob decrypts under HKDF(code, salt) with pcId/name/candidates/port/s");

                    string deviceId = Crypto.RandomHexId();
                    var nonce = Crypto.RandomBytes(16);
                    byte[] secret;
                    Check(!service.TryComplete(deviceId, Crypto.Base64Url(Crypto.RandomBytes(32)), Crypto.Base64Url(nonce), out secret), "wrong proof rejected");
                    Check(service.State == PairingState.Active, "session still live after a wrong proof");
                    var proof = Crypto.HmacSha256(session.Secret, Crypto.Concat(Encoding.UTF8.GetBytes(deviceId), nonce));
                    Check(service.TryComplete(deviceId, Crypto.Base64Url(proof), Crypto.Base64Url(nonce), out secret) && secret.SequenceEqual(session.Secret), "correct proof completes and yields S");
                    Check(service.State == PairingState.Idle && service.Live == null, "completed session is consumed (Idle)");
                    Check(!service.TryComplete(deviceId, Crypto.Base64Url(proof), Crypto.Base64Url(nonce), out secret), "single use: second completion rejected");
                    await Task.Delay(300);
                    Check(api.Requests.Any(r => r == "DELETE /api/pair/sessions/" + session.CodeHash), "session deleted on the cloud after completion");

                    await service.GenerateAsync();
                    Check(service.State == PairingState.Active, "second session active");
                    await Task.Delay(2600);
                    Check(service.State == PairingState.Expired && service.Live == null, "session expires after the TTL");
                    Check(!service.TryComplete(deviceId, Crypto.Base64Url(proof), Crypto.Base64Url(nonce), out secret), "expired session cannot complete");
                    service.Cancel();
                    Check(service.State == PairingState.Idle, "cancel returns to Idle");
                }

                using (var offline = new PairingSessionService("http://127.0.0.1:9", pcId, 31377, () => candidates, TimeSpan.FromMinutes(2)))
                {
                    await offline.GenerateAsync();
                    Check(offline.State == PairingState.Error, "unreachable cloud -> Error state");
                }
            }
        }

        static async Task HttpRoutes()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tsgps-pairing-tests-" + Guid.NewGuid().ToString("N"));
            var settings = Settings.Load(Path.Combine(dir, "Settings.json"));
            Check(Crypto.IsHexId(settings.PcId, 32) && File.Exists(settings.FilePath), "fresh settings mint a 32-hex pcId and persist it");
            var devices = new LinkedDeviceStore(settings);
            using (var api = new FakeApi((m, p, b) => Tuple.Create(m == "POST" ? 201 : 204, "{}")))
            using (var pairing = new PairingSessionService(api.BaseUrl, settings.PcId, 31377, () => new List<string> { "127.0.0.1/8" }, TimeSpan.FromMinutes(2)))
            {
                int port = FreePort();
                using (var presence = new PresenceClient("http://127.0.0.1:9", settings, devices, pairing, () => new List<string>(), port))
                using (var server = new MinimalHttpServer(port, pairing, devices, new VoiceEndpoints(devices, new VoiceCache(Path.Combine(dir, "voice")), new FakeVoiceAudio()), presence))
                using (var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") })
                {
                    server.Start();
                    var hello = JObject.Parse(await http.GetStringAsync("/api/pair/hello"));
                    Check((string)hello["pcId"] == settings.PcId && (string)hello["name"] == Environment.MachineName && (int)hello["serverVersion"] == 7 && !(bool)hello["standby"] && (bool)hello["standbyRenew"], "GET /api/pair/hello -> {pcId, name, serverVersion: 7, standby: false, standbyRenew: true}");

                    var telemetry = JObject.Parse(await http.GetStringAsync("/api/ets2/telemetry"));
                    Check((int)telemetry["serverVersion"] == 7, "telemetry serverVersion is 7");

                    string deviceId = Crypto.RandomHexId();
                    var missing = await http.PostAsync("/api/pair/complete", Json(new { deviceId, deviceName = "Pixel 8", proof = "AA", nonce = "AA" }));
                    Check(missing.StatusCode == HttpStatusCode.NotFound, "complete without a live session -> 404");

                    await pairing.GenerateAsync();
                    var session = pairing.Current;
                    var nonce = Crypto.RandomBytes(16);
                    var proof = Crypto.HmacSha256(session.Secret, Crypto.Concat(Encoding.UTF8.GetBytes(deviceId), nonce));
                    var bad = await http.PostAsync("/api/pair/complete", Json(new { deviceId, deviceName = "Pixel 8", proof = Crypto.Base64Url(Crypto.RandomBytes(32)), nonce = Crypto.Base64Url(nonce) }));
                    Check(bad.StatusCode == HttpStatusCode.NotFound, "complete with a bad proof -> 404");
                    var ok = await http.PostAsync("/api/pair/complete", Json(new { deviceId, deviceName = "Pixel 8", proof = Crypto.Base64Url(proof), nonce = Crypto.Base64Url(nonce) }));
                    Check(ok.StatusCode == HttpStatusCode.OK, "complete with the right proof -> 200");
                    var reply = JObject.Parse(Crypto.OpenEnvelope(session.Secret, (string)JObject.Parse(await ok.Content.ReadAsStringAsync())["env"]));
                    var deviceKey = Crypto.FromBase64Url((string)reply["deviceKey"]);
                    Check(deviceKey.Length == 32 && (string)reply["pcId"] == settings.PcId && (string)reply["name"] == Environment.MachineName, "reply env decrypts under S to {deviceKey (32 B), pcId, name}");
                    var linked = devices.Get(deviceId);
                    Check(linked != null && linked.Name == "Pixel 8" && devices.GetKey(deviceId).SequenceEqual(deviceKey) && linked.LastMethod == ConnectionMethod.Direct, "device stored with the same key, name, method direct");
                    Check(pairing.State == PairingState.Idle, "pairing session consumed");

                    var request = new HttpRequestMessage(HttpMethod.Get, "/api/ets2/telemetry");
                    request.Headers.Add("X-TSGPS-Device", deviceId);
                    request.Headers.Add("X-TSGPS-Device-Name", "Pixel 8");
                    var tracked = await http.SendAsync(request);
                    Check(!tracked.Headers.Contains("X-TSGPS-Pair"), "linked device request carries no pair offer");

                    Check(!devices.AppState().WantsGameRoute, "no device asks for the game route by default");
                    request = WithDevice(HttpMethod.Get, "/api/ets2/telemetry", deviceId);
                    request.Headers.Add("X-TSGPS-Game-Route", "1");
                    await http.SendAsync(request);
                    Check(devices.AppState().WantsGameRoute, "a telemetry poll with X-TSGPS-Game-Route: 1 asks for the game route");
                    await http.SendAsync(WithDevice(HttpMethod.Get, "/api/pair/hello", deviceId));
                    Check(devices.AppState().WantsGameRoute, "other requests leave the game-route flag alone");
                    await http.SendAsync(WithDevice(HttpMethod.Get, "/api/ets2/telemetry", deviceId));
                    Check(!devices.AppState().WantsGameRoute, "a telemetry poll without the header drops it");
                    request = WithDevice(HttpMethod.Get, "/api/ets2/telemetry", deviceId);
                    request.Headers.Add("X-TSGPS-Game-Route", "1");
                    await http.SendAsync(request);
                    lock (settings.Sync)
                        devices.Get(deviceId).GameRouteWantedAt -= LinkedDeviceStore.ConnectedWindow;
                    Check(!devices.AppState().WantsGameRoute, "the game-route flag expires like the connection does");

                    string manualId = Crypto.RandomHexId();
                    request = new HttpRequestMessage(HttpMethod.Get, "/api/ets2/telemetry");
                    request.Headers.Add("X-TSGPS-Device", manualId);
                    request.Headers.Add("X-TSGPS-Device-Name", "Galaxy Tab");
                    var offered = await http.SendAsync(request);
                    var manual = devices.Get(manualId);
                    Check(offered.Headers.Contains("X-TSGPS-Pair") && offered.Headers.GetValues("X-TSGPS-Pair").First() == "offer", "unknown device header -> X-TSGPS-Pair: offer");
                    Check(manual != null && manual.DeviceKey == null && manual.Name == "Galaxy Tab" && manual.LastMethod == ConnectionMethod.Manual, "unknown device auto-registered as manual");

                    var lan = await http.PostAsync("/api/pair/complete-lan", Json(new { deviceId = manualId, deviceName = "Galaxy Tab", nonce = Crypto.Base64Url(Crypto.RandomBytes(16)) }));
                    var lanReply = JObject.Parse(await lan.Content.ReadAsStringAsync());
                    Check(lan.StatusCode == HttpStatusCode.OK && Crypto.FromBase64Url((string)lanReply["deviceKey"]).Length == 32 && (string)lanReply["pcId"] == settings.PcId, "complete-lan -> plaintext {deviceKey, pcId, name}");
                    Check(devices.GetKey(manualId).SequenceEqual(Crypto.FromBase64Url((string)lanReply["deviceKey"])), "manual device now linked with that key");

                    request = new HttpRequestMessage(HttpMethod.Get, "/api/ets2/telemetry");
                    request.Headers.Add("X-TSGPS-Device", manualId);
                    var after = await http.SendAsync(request);
                    Check(!after.Headers.Contains("X-TSGPS-Pair") && devices.Get(manualId).LastMethod == ConnectionMethod.Direct, "after complete-lan: no offer, method direct");

                    var anonymousWarmup = await http.PostAsync("/api/pair/warmup", null);
                    Check(anonymousWarmup.StatusCode == HttpStatusCode.NotFound && !presence.WarmingUp, "warmup without a linked device -> 404, pairing line stays closed");
                    var warmup = await http.SendAsync(WithDevice(HttpMethod.Post, "/api/pair/warmup", manualId));
                    Check(warmup.StatusCode == HttpStatusCode.OK && !(bool)JObject.Parse(await warmup.Content.ReadAsStringAsync())["ready"] && presence.WarmingUp,
                        "warmup from a linked device opens the pairing line for a minute, ready: false until it registers");
                    settings.StandbyToken = StandbyToken(settings.PcId, DateTime.UtcNow.AddDays(30));
                    hello = JObject.Parse(await http.GetStringAsync("/api/pair/hello"));
                    Check((bool)hello["standby"] && !(bool)hello["standbyRenew"], "a fresh token: standby true, no renewal asked");
                    settings.StandbyToken = StandbyToken(settings.PcId, DateTime.UtcNow.AddDays(3));
                    hello = JObject.Parse(await http.GetStringAsync("/api/pair/hello"));
                    Check((bool)hello["standby"] && (bool)hello["standbyRenew"], "a token within a week of expiry still parks but asks for a renewal");
                    settings.StandbyToken = null;

                    var plain = await http.GetAsync("/api/ets2/telemetry");
                    Check(plain.StatusCode == HttpStatusCode.OK && !plain.Headers.Contains("X-TSGPS-Pair") && devices.Sorted().Count == 2, "request without device headers is untouched");

                    var invalid = await http.PostAsync("/api/pair/complete-lan", Json(new { deviceId = "nope" }));
                    Check(invalid.StatusCode == HttpStatusCode.BadRequest, "malformed deviceId -> 400");

                    devices.Remove(manualId);
                    Check(devices.Get(manualId) == null && Settings.Load(settings.FilePath).LinkedDevices.Count == 1, "remove persists");

                    await RemovedDevices(http, devices, pairing, settings, deviceId, deviceKey);
                }
            }
            Directory.Delete(dir, true);
        }

        static async Task RemovedDevices(HttpClient http, LinkedDeviceStore devices, PairingSessionService pairing, Settings settings, string deviceId, byte[] deviceKey)
        {
            var revoked = new List<string>();
            devices.Revoked += id => revoked.Add(id);
            devices.Remove(deviceId);
            var stored = Settings.Load(settings.FilePath);
            var tombstone = stored.RemovedDevices.FirstOrDefault(r => r.DeviceId == deviceId);
            Check(devices.Get(deviceId) == null && devices.IsRemoved(deviceId) && tombstone != null
                  && tombstone.Until > DateTime.UtcNow + TimeSpan.FromHours(23), "removing a linked device writes a 24 h tombstone");
            Check(revoked.SequenceEqual(new[] { deviceId }), "remove raises Revoked for the device");
            Check(new LinkedDeviceStore(stored).IsRemoved(deviceId), "tombstone survives a restart");

            var refused = await http.SendAsync(WithDevice(HttpMethod.Get, "/api/ets2/telemetry", deviceId));
            Check(refused.StatusCode == HttpStatusCode.Forbidden && (string)JObject.Parse(await refused.Content.ReadAsStringAsync())["error"] == "device_removed", "removed device: telemetry -> 403 device_removed");
            var refusedProfile = await http.SendAsync(WithDevice(HttpMethod.Get, "/api/game/state?game=ets2", deviceId));
            Check(refusedProfile.StatusCode == HttpStatusCode.Forbidden && (string)JObject.Parse(await refusedProfile.Content.ReadAsStringAsync())["error"] == "device_removed", "removed device: the gate sits ahead of every route");
            Check(devices.Get(deviceId) == null, "removed device is not registered again");

            var hello = await http.SendAsync(WithDevice(HttpMethod.Get, "/api/pair/hello", deviceId));
            Check(hello.StatusCode == HttpStatusCode.OK && hello.Headers.GetValues("X-TSGPS-Pair").First() == "removed", "removed device: hello answers with X-TSGPS-Pair: removed");

            var nonceA = Crypto.RandomBytes(16);
            string env = Crypto.SealEnvelope(deviceKey, Newtonsoft.Json.JsonConvert.SerializeObject(new { deviceName = "Pixel 8", nonceA = Crypto.Base64Url(nonceA), ts = 1789000000 }));
            Check(!HandshakeResponder.Answer(devices.GetKey(deviceId), new List<string>(), "", Crypto.Base64Url(nonceA), env).Ok, "removed device: handshake under its old key is rejected");

            var unlink = await http.PostAsync("/api/pair/unlink", Json(new { deviceId, nonce = "AA", proof = "AA" }));
            Check(unlink.StatusCode == HttpStatusCode.NotFound, "removed device: unlink -> 404 not_linked");
            var garbage = await http.PostAsync("/api/pair/unlink", Json(new { deviceId, nonce = "!!", proof = "!!" }));
            Check(garbage.StatusCode == HttpStatusCode.NotFound, "unlink with unparseable base64 -> 404, not 500");

            var fresh = WithDevice(HttpMethod.Get, "/api/ets2/telemetry", deviceId);
            fresh.Headers.Add("X-TSGPS-Pair", "new");
            var accepted = await http.SendAsync(fresh);
            Check(accepted.StatusCode == HttpStatusCode.OK && accepted.Headers.GetValues("X-TSGPS-Pair").First() == "offer" && !devices.IsRemoved(deviceId)
                  && devices.Get(deviceId)?.LastMethod == ConnectionMethod.Manual, "X-TSGPS-Pair: new clears the tombstone, registers manual, offers");
            var again = await http.SendAsync(WithDevice(HttpMethod.Get, "/api/ets2/telemetry", deviceId));
            Check(again.StatusCode == HttpStatusCode.OK, "after the new marker plain requests pass again");

            devices.Link(deviceId, "Pixel 8", ConnectionMethod.Direct);
            devices.Remove(deviceId);
            Check(devices.IsRemoved(deviceId), "linked again and removed again: tombstone back");
            await pairing.GenerateAsync();
            var session = pairing.Current;
            var nonce = Crypto.RandomBytes(16);
            var proof = Crypto.HmacSha256(session.Secret, Crypto.Concat(Encoding.UTF8.GetBytes(deviceId), nonce));
            var complete = await http.SendAsync(WithDevice(HttpMethod.Post, "/api/pair/complete", deviceId, Json(new { deviceId, deviceName = "Pixel 8", proof = Crypto.Base64Url(proof), nonce = Crypto.Base64Url(nonce) })));
            Check(complete.StatusCode == HttpStatusCode.OK && !devices.IsRemoved(deviceId) && devices.GetKey(deviceId) != null
                  && !complete.Headers.Contains("X-TSGPS-Pair"), "pairing completion bypasses and clears the tombstone, and its reply carries no removed header");
            var linkedAgain = await http.SendAsync(WithDevice(HttpMethod.Get, "/api/ets2/telemetry", deviceId));
            Check(linkedAgain.StatusCode == HttpStatusCode.OK && !linkedAgain.Headers.Contains("X-TSGPS-Pair"), "re-paired device polls without an offer");

            var key = devices.GetKey(deviceId);
            var unlinkNonce = Crypto.RandomBytes(16);
            var badProof = await http.SendAsync(WithDevice(HttpMethod.Post, "/api/pair/unlink", deviceId, Json(new { deviceId, nonce = Crypto.Base64Url(unlinkNonce), proof = Crypto.Base64Url(Crypto.RandomBytes(32)) })));
            Check(badProof.StatusCode == HttpStatusCode.NotFound && devices.Get(deviceId) != null, "unlink with a wrong proof changes nothing");
            int revokedBefore = revoked.Count;
            var unlinkProof = Crypto.HmacSha256(key, Crypto.Concat(Encoding.UTF8.GetBytes(deviceId), unlinkNonce));
            var unlinked = await http.SendAsync(WithDevice(HttpMethod.Post, "/api/pair/unlink", deviceId, Json(new { deviceId, nonce = Crypto.Base64Url(unlinkNonce), proof = Crypto.Base64Url(unlinkProof) })));
            Check(unlinked.StatusCode == HttpStatusCode.OK && devices.Get(deviceId) == null && !devices.IsRemoved(deviceId), "unlink with the device proof deletes the row without a tombstone, and its own device headers do not re-register it");
            Check(revoked.Count == revokedBefore + 1 && revoked.Last() == deviceId, "unlink raises Revoked for the device");
            var offeredAgain = await http.SendAsync(WithDevice(HttpMethod.Get, "/api/ets2/telemetry", deviceId));
            Check(offeredAgain.StatusCode == HttpStatusCode.OK && offeredAgain.Headers.Contains("X-TSGPS-Pair"), "unlinked device is offered a pairing again");

            string keyless = Crypto.RandomHexId();
            await http.SendAsync(WithDevice(HttpMethod.Get, "/api/ets2/telemetry", keyless));
            devices.Remove(keyless);
            var keylessAgain = await http.SendAsync(WithDevice(HttpMethod.Get, "/api/ets2/telemetry", keyless));
            Check(!devices.IsRemoved(keyless) && keylessAgain.StatusCode == HttpStatusCode.OK && keylessAgain.Headers.Contains("X-TSGPS-Pair"), "removing a keyless manual row just deletes it");

            devices.Link(deviceId, "Pixel 8", ConnectionMethod.Direct);
            devices.Remove(deviceId);
            lock (settings.Sync)
                settings.RemovedDevices.First(r => r.DeviceId == deviceId).Until = DateTime.UtcNow - TimeSpan.FromSeconds(1);
            Check(!devices.IsRemoved(deviceId) && Settings.Load(settings.FilePath).RemovedDevices.All(r => r.DeviceId != deviceId), "expired tombstone is ignored and pruned");
            var expired = await http.SendAsync(WithDevice(HttpMethod.Get, "/api/ets2/telemetry", deviceId));
            Check(expired.StatusCode == HttpStatusCode.OK, "device with an expired tombstone is served again");
        }

        static HttpRequestMessage WithDevice(HttpMethod method, string path, string deviceId, HttpContent content = null)
        {
            var request = new HttpRequestMessage(method, path) { Content = content };
            request.Headers.Add("X-TSGPS-Device", deviceId);
            request.Headers.Add("X-TSGPS-Device-Name", "Pixel 8");
            return request;
        }

        static StringContent Json(object body)
        {
            return new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
        }

        static void Handshake()
        {
            var deviceKey = Crypto.RandomBytes(32);
            var nonceA = Crypto.RandomBytes(16);
            string nonceAText = Crypto.Base64Url(nonceA);
            string env = Crypto.SealEnvelope(deviceKey, Newtonsoft.Json.JsonConvert.SerializeObject(new { deviceName = "Pixel 8", nonceA = nonceAText, ts = 1789000000 }));
            var candidates = new List<string> { "192.168.1.42/24", "127.0.0.1/8" };

            var answer = HandshakeResponder.Answer(deviceKey, candidates, "https://eu1.relay.example", nonceAText, env);
            Check(answer.Ok && answer.DeviceName == "Pixel 8", "valid env under the device key is accepted");
            var envR = JObject.Parse(Crypto.OpenEnvelope(deviceKey, answer.EnvR));
            var nonceB = Crypto.FromBase64Url((string)envR["nonceB"]);
            Check(nonceB.Length == 16 && (string)envR["relayUrl"] == "https://eu1.relay.example" && envR["candidates"].Select(c => (string)c).SequenceEqual(candidates), "envR = {candidates, relayUrl, nonceB}");
            var okm = Crypto.Hkdf(deviceKey, Crypto.Concat(nonceA, nonceB), Crypto.RelayInfo, 128);
            Check(okm.Take(32).SequenceEqual(answer.KeyAppToPc) && okm.Skip(32).Take(32).SequenceEqual(answer.KeyPcToApp), "session keys = HKDF(deviceKey, nonceA||nonceB, tsgps-relay-v1)[0..64]");

            Check(!HandshakeResponder.Answer(null, candidates, "", nonceAText, env).Ok, "unknown device (no key) rejected");
            Check(!HandshakeResponder.Answer(Crypto.RandomBytes(32), candidates, "", nonceAText, env).Ok, "wrong key rejected");
            Check(!HandshakeResponder.Answer(deviceKey, candidates, "", Crypto.Base64Url(Crypto.RandomBytes(16)), env).Ok, "nonceA mismatch rejected");
            Check(!HandshakeResponder.Answer(deviceKey, candidates, "", nonceAText, "not-base64!").Ok, "garbage env rejected");

            Check(!answer.Warmup, "a plain handshake is no warm-up");
            string warmupEnv = Crypto.SealEnvelope(deviceKey, Newtonsoft.Json.JsonConvert.SerializeObject(new { deviceName = "Pixel 8", nonceA = nonceAText, ts = 1789000000, warmup = true }));
            var warmup = HandshakeResponder.Answer(deviceKey, candidates, "https://eu1.relay.example", nonceAText, warmupEnv);
            Check(warmup.Ok && warmup.Warmup, "env warmup: true is accepted and flagged, so the PC opens no relay leg");
        }

        static void StandbyTokenFreshness()
        {
            var now = DateTime.UtcNow;
            string pcId = Crypto.RandomHexId();
            Check(StandbyClient.IsFreshToken(StandbyToken(pcId, now.AddDays(30)), now), "token valid for 30 days is fresh");
            Check(!StandbyClient.IsFreshToken(StandbyToken(pcId, now.AddDays(6)), now), "token within a week of expiry is not fresh");
            Check(!StandbyClient.IsFreshToken(StandbyToken(pcId, now.AddDays(-1)), now), "expired token is not fresh");
            Check(!StandbyClient.IsFreshToken(null, now) && !StandbyClient.IsFreshToken("", now), "no token is not fresh");
            Check(!StandbyClient.IsFreshToken("!!", now) && !StandbyClient.IsFreshToken(Crypto.Base64Url(Crypto.RandomBytes(40)), now), "unreadable token is not fresh");
        }

        static string StandbyToken(string pcId, DateTime expires)
        {
            var bytes = Crypto.RandomBytes(56);
            Buffer.BlockCopy(Crypto.FromHex(pcId), 0, bytes, 0, 16);
            long exp = new DateTimeOffset(expires).ToUnixTimeSeconds();
            for (int i = 0; i < 8; i++)
                bytes[23 - i] = (byte)(exp >> (8 * i));
            return Crypto.Base64Url(bytes);
        }

        static VoiceClip PcmClip(string id, int samples, short value = 1000)
        {
            var payload = new byte[samples * 2];
            for (int i = 0; i < samples; i++)
            {
                payload[2 * i] = (byte)value;
                payload[2 * i + 1] = (byte)(value >> 8);
            }
            return new VoiceClip { Id = id, Codec = VoiceCodec.Pcm16, SampleRate = 44100, SkipSamples = 0, ValidSamples = samples, Payload = payload };
        }

        static void VoiceClipsAndCache()
        {
            var key = Crypto.RandomBytes(32);
            string id = Crypto.RandomHexId();
            var blob = VoiceClip.Seal(key, PcmClip(id, 441));
            var opened = VoiceClip.Open(key, blob);
            Check(opened.Id == id && opened.Codec == VoiceCodec.Pcm16 && opened.ValidSamples == 441 && opened.Payload.Length == 882, "clip seal/open round trip");
            var tampered = (byte[])blob.Clone();
            tampered[tampered.Length - 20] ^= 1;
            Check(Throws(() => VoiceClip.Open(key, tampered)), "tampered clip rejected");
            Check(Throws(() => VoiceClip.Open(Crypto.RandomBytes(32), blob)), "clip under another key rejected");

            string dir = Path.Combine(Path.GetTempPath(), "tsgps-voice-tests-" + Guid.NewGuid().ToString("N"));
            var cache = new VoiceCache(dir, 2500);
            string deviceId = Crypto.RandomHexId();
            Check(Throws(() => cache.Store(deviceId, id, blob)), "store before hello throws");
            Check(cache.Hello(deviceId, key).Count == 0 && cache.HasKey(deviceId), "first hello: empty cache, key held");
            cache.Store(deviceId, id, blob);
            Check(Throws(() => cache.Store(deviceId, Crypto.RandomHexId(), blob)), "blob stored under another id rejected");
            Check(cache.Load(deviceId, id).ValidSamples == 441, "stored clip loads");
            Check(cache.Load(deviceId, Crypto.RandomHexId()) == null, "unknown clip loads as null");
            Check(new VoiceCache(dir).Hello(deviceId, key).SequenceEqual(new[] { id }), "a restarted cache lists the clip for the same key");
            var stored = File.ReadAllBytes(Directory.GetFiles(Path.Combine(dir, deviceId), "*.clip")[0]);
            Check(Encoding.ASCII.GetString(stored).IndexOf("TSGC", StringComparison.Ordinal) < 0, "clips are stored encrypted");

            string second = Crypto.RandomHexId();
            Thread.Sleep(20);
            cache.Store(deviceId, second, VoiceClip.Seal(key, PcmClip(second, 441)));
            string third = Crypto.RandomHexId();
            Thread.Sleep(20);
            cache.Store(deviceId, third, VoiceClip.Seal(key, PcmClip(third, 441)));
            var held = cache.Hello(deviceId, key);
            Check(held.Count == 2 && !held.Contains(id) && held.Contains(third), "size cap drops the oldest clip first");

            File.WriteAllBytes(Path.Combine(dir, deviceId, third + ".clip"), new byte[40]);
            Check(cache.Load(deviceId, third) == null && !File.Exists(Path.Combine(dir, deviceId, third + ".clip")), "an unreadable clip is deleted on load");

            Check(cache.Hello(deviceId, Crypto.RandomBytes(32)).Count == 0, "a new key wipes the device's clips");
            cache.Forget(deviceId);
            Check(!cache.HasKey(deviceId) && !Directory.Exists(Path.Combine(dir, deviceId)), "forget drops the key and the folder");
            Directory.Delete(dir, true);
        }

        static void VoiceDecode()
        {
            var tone = new VoiceClip
            {
                Id = Crypto.RandomHexId(), Codec = VoiceCodec.Mp3, SampleRate = 44100, SkipSamples = 1105, ValidSamples = 22050,
                Payload = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voice_tone.mp3"))
            };
            var pcm = new float[22050];
            VoiceDecoder.DecodeInto(tone, pcm, 0);
            double rms = Math.Sqrt(pcm.Skip(2000).Take(18000).Average(x => (double)x * x));
            Check(rms > 0.05 && rms < 0.15, "MP3 clip decodes to the tone (rms " + rms.ToString("0.000") + ")");
            tone.ValidSamples = 30000;
            Check(Throws(() => VoiceDecoder.DecodeInto(tone, new float[30000], 0)), "frames shorter than the declared length throw");

            int rate;
            var joined = VoiceDecoder.Join(new[] { PcmClip("a", 441, 16384), PcmClip("b", 882, 16384) }, PcmClip("c", 441, 16384), 300, out rate);
            Check(rate == 44100 && joined.Length == 441 + 882 + 13230 + 441 && joined[1323] == 0f, "join = clips + pause + then, no lead-in");
            var prompt = VoiceDecoder.ToPcm16(joined, 100, rate, 0.5f);
            Check(prompt.Length == 4410 + joined.Length && prompt[0] == 0 && prompt[4409] == 0 && prompt[4410] == 8192 && prompt[4410 + 1323] == 0 && prompt[prompt.Length - 1] == 8192,
                "lead-in silence goes in front, gain applied");
            var loud = VoiceDecoder.ToPcm16(VoiceDecoder.Join(new[] { PcmClip("a", 10, 30000) }, null, 0, out rate), 0, rate, 2f);
            Check(loud.All(x => x == short.MaxValue), "gain clips at full scale");
            Check(VoiceDecoder.ToPcm16(100.6f / 32767) == 101 && VoiceDecoder.ToPcm16(-100.6f / 32767) == -101 && VoiceDecoder.ToPcm16(-2f) == -32767,
                "PCM16 rounds like the app's Pcm16.FromFloat (full scale 32767)");
        }

        static JObject FxVectors()
        {
            return JObject.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voice_fx_vectors.json")));
        }

        static short[] Unpack(string base64)
        {
            using (var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress))
            using (var raw = new MemoryStream())
            {
                deflate.CopyTo(raw);
                var bytes = raw.ToArray();
                var pcm = new short[bytes.Length / 2];
                Buffer.BlockCopy(bytes, 0, pcm, 0, bytes.Length);
                return pcm;
            }
        }

        static VoiceClip VectorClip(string id, short[] pcm, int sampleRate)
        {
            var payload = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, payload, 0, payload.Length);
            return new VoiceClip { Id = id, Codec = VoiceCodec.Pcm16, SampleRate = sampleRate, SkipSamples = 0, ValidSamples = pcm.Length, Payload = payload };
        }

        static int MaxError(short[] expected, short[] actual)
        {
            if (expected.Length != actual.Length)
                return int.MaxValue;
            int max = 0;
            for (int i = 0; i < expected.Length; i++)
                max = Math.Max(max, Math.Abs(expected[i] - actual[i]));
            return max;
        }

        static void VoiceFxVectors()
        {
            var vectors = FxVectors();
            int rate = (int)vectors["sampleRate"];
            var jobs = vectors["jobs"].ToList();
            Check((int)vectors["catalogVersion"] == VoiceFxCatalog.Version, "vectors are for catalog version " + VoiceFxCatalog.Version);
            Check(vectors["sounds"].Children<JProperty>().Select(p => p.Name).OrderBy(x => x).SequenceEqual(VoiceFxCatalog.Sounds.OrderBy(x => x)),
                "vectors carry every catalog sound");
            Check(VoiceFxCatalog.All.All(e => jobs.Any(j => (string)j["name"] == e.Id + ".defaults")), "every effect has a job at its defaults");

            var voice = VoiceDecoder.Decode(VectorClip("voice", Unpack((string)vectors["voice"]), rate));
            var sounds = new VoiceFxSounds(rate, vectors["sounds"].Children<JProperty>()
                .ToDictionary(p => p.Name, p => VoiceDecoder.Decode(VectorClip(p.Name, Unpack((string)p.Value), rate))));
            foreach (var job in jobs)
            {
                int jobRate = (int?)job["sampleRate"] ?? rate;
                var inputs = vectors["rates"]?[jobRate.ToString()];
                var jobVoice = jobRate == rate ? voice : VoiceDecoder.Decode(VectorClip("voice", Unpack((string)inputs["voice"]), jobRate));
                var jobSounds = jobRate == rate ? sounds : new VoiceFxSounds(jobRate, ((JObject)inputs["sounds"] ?? new JObject()).Properties()
                    .ToDictionary(p => p.Name, p => VoiceDecoder.Decode(VectorClip(p.Name, Unpack((string)p.Value), jobRate))));
                var result = VoiceFxRenderer.Render(jobVoice, jobRate, (string)job["effect"], job["values"].ToObject<Dictionary<string, int>>(), (uint)job["seed"], jobSounds);
                var expected = Unpack((string)job["pcm16"]);
                int max = MaxError(expected, VoiceDecoder.ToPcm16(result.Samples, 0, jobRate, 1f));
                Check(expected.Length == (int)job["length"] && result.PreRollSamples == (int)job["preRollSamples"] && max <= 1,
                    job["name"] + ": " + result.Samples.Length + " samples, pre-roll " + result.PreRollSamples + ", max error " + max + " LSB");
            }

            var mp3 = vectors["mp3"];
            var tone = new VoiceClip
            {
                Id = Crypto.RandomHexId(), Codec = VoiceCodec.Mp3, SampleRate = (int)mp3["sampleRate"], SkipSamples = (int)mp3["skipSamples"],
                ValidSamples = (int)mp3["validSamples"], Payload = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, (string)mp3["file"]))
            };
            int mp3Max = MaxError(Unpack((string)mp3["pcm16"]), VoiceDecoder.ToPcm16(VoiceDecoder.Decode(tone), 0, tone.SampleRate, 1f));
            Check(mp3Max <= 1, "MP3 fixture decodes to the app's PCM16, max error " + mp3Max + " LSB");
        }

        sealed class FakeVoiceAudio : IVoiceAudio
        {
            public readonly List<string> Calls = new List<string>();
            public short[] LastPrompt;

            public void PlayPrompt(string reqId, short[] pcm, int sampleRate)
            {
                LastPrompt = pcm;
                Calls.Add("play " + reqId + " " + pcm.Length);
            }

            public void StopPrompt(string reqId, int fadeMs)
            {
                Calls.Add("stop " + reqId + " " + fadeMs);
            }

            public void PlayEffect(short[] pcm, int sampleRate)
            {
                Calls.Add("effect " + pcm.Length);
            }
        }

        static HttpRequestMessage VoicePost(string route, string deviceId, object body)
        {
            return WithDevice(HttpMethod.Post, "/api/voice/" + route, deviceId, Json(body));
        }

        static async Task VoiceRoutes()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tsgps-voice-routes-" + Guid.NewGuid().ToString("N"));
            var settings = Settings.Load(Path.Combine(dir, "Settings.json"));
            var devices = new LinkedDeviceStore(settings);
            var audio = new FakeVoiceAudio();
            var voiceDir = Path.Combine(dir, "voice");
            using (var api = new FakeApi((m, p, b) => Tuple.Create(204, "{}")))
            using (var pairing = new PairingSessionService(api.BaseUrl, settings.PcId, 31377, () => new List<string> { "127.0.0.1/8" }, TimeSpan.FromMinutes(2)))
            {
                int port = FreePort();
                using (var presence = new PresenceClient("http://127.0.0.1:9", settings, devices, pairing, () => new List<string>(), port))
                using (var server = new MinimalHttpServer(port, pairing, devices, new VoiceEndpoints(devices, new VoiceCache(voiceDir), audio), presence))
                using (var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") })
                {
                    server.Start();
                    string deviceId = Crypto.RandomHexId();
                    var lan = await http.PostAsync("/api/pair/complete-lan", Json(new { deviceId, deviceName = "Pixel 8", nonce = "AA" }));
                    var deviceKey = Crypto.FromBase64Url((string)JObject.Parse(await lan.Content.ReadAsStringAsync())["deviceKey"]);
                    var cacheKey = Crypto.RandomBytes(32);
                    Func<byte[], object> hello = k => new
                    {
                        pcId = settings.PcId,
                        env = Crypto.SealEnvelope(Crypto.VoiceHelloKey(deviceKey), JsonConvert.SerializeObject(new { cacheKey = Crypto.Base64Url(k) }))
                    };

                    string a = Crypto.RandomHexId(), b = Crypto.RandomHexId(), then = Crypto.RandomHexId();
                    var play = new { reqId = "r1", ids = new[] { a, b }, thenId = then, leadInMs = 0, thenPauseMs = 300, gain = 1f };
                    var early = await http.SendAsync(VoicePost("play", deviceId, play));
                    Check(early.StatusCode == HttpStatusCode.Unauthorized, "play before hello -> 401 hello_required");

                    var anonymous = await http.PostAsync("/api/voice/hello", Json(hello(cacheKey)));
                    Check(anonymous.StatusCode == HttpStatusCode.Forbidden, "hello without a device header -> 403");
                    var wrongPc = await http.SendAsync(VoicePost("hello", deviceId, new { pcId = Crypto.RandomHexId(), env = "x" }));
                    Check(wrongPc.StatusCode == HttpStatusCode.Forbidden, "hello for another PC -> 403");
                    var forged = await http.SendAsync(VoicePost("hello", deviceId,
                        new { pcId = settings.PcId, env = Crypto.SealEnvelope(Crypto.RandomBytes(32), JsonConvert.SerializeObject(new { cacheKey = "AA" })) }));
                    Check(forged.StatusCode == HttpStatusCode.Forbidden, "hello sealed under another key -> 403");

                    var first = await http.SendAsync(VoicePost("hello", deviceId, hello(cacheKey)));
                    var firstBody = JObject.Parse(await first.Content.ReadAsStringAsync());
                    Check(first.StatusCode == HttpStatusCode.OK && (int)firstBody["v"] == 1 && !firstBody["ids"].Any(), "hello -> 200 {v: 1, ids: []}");

                    var missing = await http.SendAsync(VoicePost("play", deviceId, play));
                    var missingIds = JObject.Parse(await missing.Content.ReadAsStringAsync())["ids"].Select(x => (string)x).OrderBy(x => x);
                    Check((int)missing.StatusCode == 409 && missingIds.SequenceEqual(new[] { a, b, then }.OrderBy(x => x)), "play without the clips -> 409 with every missing id");

                    var anonymousClips = await http.PostAsync("/api/voice/clips", Json(new { clips = new[] { new { id = a, blob = "AA" } } }));
                    Check(anonymousClips.StatusCode == HttpStatusCode.Unauthorized, "clips without a device key -> 401");
                    var clips = new[] { a, b, then }.Select(id => new { id, blob = Crypto.Base64Url(VoiceClip.Seal(cacheKey, PcmClip(id, id == b ? 30000 : 441))) }).ToArray();
                    var upload = await http.SendAsync(VoicePost("clips", deviceId, new { clips }));
                    var uploadBody = JObject.Parse(await upload.Content.ReadAsStringAsync());
                    Check(upload.StatusCode == HttpStatusCode.OK && uploadBody["stored"].Count() == 3 && !uploadBody["rejected"].Any() && !audio.Calls.Any(),
                        "clips (a body over 64 KB) are stored, acked, and nothing plays");
                    var full = await http.SendAsync(VoicePost("play", deviceId, play));
                    var fullBody = JObject.Parse(await full.Content.ReadAsStringAsync());
                    int expected = 441 + 30000 + 13230 + 441;
                    Check(full.StatusCode == HttpStatusCode.OK && audio.LastPrompt.Length == expected && (int)fullBody["durationMs"] == expected * 1000 / 44100,
                        "play with ids only -> 200, joined prompt, durationMs");

                    var bulk = Enumerable.Range(0, 25).Select(i => Crypto.RandomHexId())
                        .Select(id => new { id, blob = Crypto.Base64Url(VoiceClip.Seal(cacheKey, PcmClip(id, 44100))) }).ToArray();
                    var bulkUpload = await http.SendAsync(VoicePost("clips", deviceId, new { clips = bulk }));
                    Check(bulkUpload.StatusCode == HttpStatusCode.OK && JObject.Parse(await bulkUpload.Content.ReadAsStringAsync())["stored"].Count() == 25,
                        "a pack-sized upload (~3 MB body) fits under the 4 MB cap");

                    var again = await http.SendAsync(VoicePost("play", deviceId, new { reqId = "r2", ids = new[] { a }, gain = 1f }));
                    Check(again.StatusCode == HttpStatusCode.OK && audio.Calls.Last() == "play r2 441", "cached clips play with ids only");
                    var rehello = await http.SendAsync(VoicePost("hello", deviceId, hello(cacheKey)));
                    Check(JObject.Parse(await rehello.Content.ReadAsStringAsync())["ids"].Count() == 28, "hello again lists every cached clip");

                    string other = Crypto.RandomHexId();
                    var swapped = await http.SendAsync(VoicePost("clips", deviceId, new { clips = new[] { new { id = other, clips[0].blob } } }));
                    var swappedBody = JObject.Parse(await swapped.Content.ReadAsStringAsync());
                    Check(swapped.StatusCode == HttpStatusCode.OK && (string)swappedBody["rejected"].Single() == other && !swappedBody["stored"].Any(),
                        "a clip uploaded under another id is rejected, not stored");

                    await http.SendAsync(VoicePost("stop", deviceId, new { reqId = "r4", fadeMs = 100 }));
                    Check(audio.Calls.Last() == "stop r4 100", "stop reaches the audio with its fade");
                    var foreign = await http.SendAsync(VoicePost("stop", Crypto.RandomHexId(), new { fadeMs = 0 }));
                    Check(foreign.StatusCode == HttpStatusCode.Unauthorized && audio.Calls.Last() == "stop r4 100", "a device without a voice key cannot stop prompts -> 401");
                    await http.SendAsync(VoicePost("stop", deviceId, new { reqId = "r5", fadeMs = int.MaxValue }));
                    Check(audio.Calls.Last() == "stop r5 2000", "stop fades are capped at 2 s");
                    var late = await http.SendAsync(VoicePost("play", deviceId, new { reqId = "r4", ids = new[] { a } }));
                    Check((int)late.StatusCode == 410 && !audio.Calls.Any(c => c.StartsWith("play r4")), "a play the device already gave up on is dropped -> 410");

                    string chime = Crypto.RandomHexId();
                    var noChime = await http.SendAsync(VoicePost("effect", deviceId, new { id = chime, gain = 1f }));
                    Check((int)noChime.StatusCode == 409, "an effect the PC lacks -> 409");
                    await http.SendAsync(VoicePost("clips", deviceId,
                        new { clips = new[] { new { id = chime, blob = Crypto.Base64Url(VoiceClip.Seal(cacheKey, PcmClip(chime, 16000))) } } }));
                    var effect = await http.SendAsync(VoicePost("effect", deviceId, new { id = chime, gain = 1f }));
                    Check(effect.StatusCode == HttpStatusCode.OK && audio.Calls.Last() == "effect 16000", "effect plays on its own channel");

                    await VoiceFxRoutes(http, deviceId, cacheKey, audio);

                    var restarted = new VoiceEndpoints(devices, new VoiceCache(voiceDir), audio);
                    Check(restarted.Play(deviceId, JsonConvert.SerializeObject(new { ids = new[] { a } })).Status == 401, "after a PC Telemetry restart the key is gone: play -> 401");

                    var newKey = await http.SendAsync(VoicePost("hello", deviceId, hello(Crypto.RandomBytes(32))));
                    Check(!JObject.Parse(await newKey.Content.ReadAsStringAsync())["ids"].Any(), "a hello with a new key (re-pair) starts an empty cache");

                    devices.Remove(deviceId);
                    Check(!Directory.Exists(Path.Combine(voiceDir, deviceId)), "removing the device deletes its voice cache");

                    var get = await http.GetAsync("/api/voice/hello");
                    Check(get.StatusCode == HttpStatusCode.MethodNotAllowed, "GET on a voice route -> 405");
                }
            }
            Directory.Delete(dir, true);
        }

        static async Task VoiceFxRoutes(HttpClient http, string deviceId, byte[] cacheKey, FakeVoiceAudio audio)
        {
            var vectors = FxVectors();
            int rate = (int)vectors["sampleRate"];
            var jobs = vectors["jobs"].ToList();
            var longest = jobs.Where(j => j["sampleRate"] == null).OrderByDescending(j => (int)j["preRollSamples"]).ThenByDescending(j => (int)j["length"]).First();
            string voice = Crypto.RandomHexId();
            var sounds = VoiceFxCatalog.Sounds.ToDictionary(s => s, s => Crypto.RandomHexId());
            string withheld = VoiceFxRenderer.SoundsUsed((string)longest["effect"], longest["values"].ToObject<Dictionary<string, int>>())[0];
            Func<string, short[], object> upload = (id, pcm) => new { id, blob = Crypto.Base64Url(VoiceClip.Seal(cacheKey, VectorClip(id, pcm, rate))) };
            var clips = new[] { upload(voice, Unpack((string)vectors["voice"])) }
                .Concat(VoiceFxCatalog.Sounds.Where(s => s != withheld).Select(s => upload(sounds[s], Unpack((string)vectors["sounds"][s])))).ToArray();
            await http.SendAsync(VoicePost("clips", deviceId, new { clips }));
            var voices = new Dictionary<int, string>();
            var rateSounds = new Dictionary<int, Dictionary<string, string>>();
            foreach (var r in ((JObject)vectors["rates"]).Properties())
            {
                int at = int.Parse(r.Name);
                var pcm = new Dictionary<string, short[]> { [voices[at] = Crypto.RandomHexId()] = Unpack((string)r.Value["voice"]) };
                rateSounds[at] = ((JObject)r.Value["sounds"] ?? new JObject()).Properties().ToDictionary(p => p.Name, p => Crypto.RandomHexId());
                foreach (var p in ((JObject)r.Value["sounds"] ?? new JObject()).Properties())
                    pcm[rateSounds[at][p.Name]] = Unpack((string)p.Value);
                await http.SendAsync(VoicePost("clips", deviceId, new { clips = pcm.Select(c => new { id = c.Key,
                    blob = Crypto.Base64Url(VoiceClip.Seal(cacheKey, VectorClip(c.Key, c.Value, at))) }).ToArray() }));
            }
            Func<JToken, object> fxOf = job => new { id = (string)job["effect"], values = job["values"], seed = (uint)job["seed"], sounds };
            Func<object, int, object> play = (fx, leadInMs) => new { reqId = "fx", ids = new[] { voice }, leadInMs, gain = 1f, fx };

            var lacking = await http.SendAsync(VoicePost("play", deviceId, play(fxOf(longest), 0)));
            var lackingIds = JObject.Parse(await lacking.Content.ReadAsStringAsync())["ids"].Select(x => (string)x).ToList();
            Check((int)lacking.StatusCode == 409 && lackingIds.SequenceEqual(new[] { sounds[withheld] }), "fx with a sound the PC lacks -> 409 listing that sound's clip id");
            await http.SendAsync(VoicePost("clips", deviceId, new { clips = new[] { upload(sounds[withheld], Unpack((string)vectors["sounds"][withheld])) } }));

            var dry = await http.SendAsync(VoicePost("play", deviceId, play(null, 0)));
            int dryLength = audio.LastPrompt.Length;
            Check(dry.StatusCode == HttpStatusCode.OK && audio.LastPrompt.SequenceEqual(Unpack((string)vectors["voice"])), "play without fx is the dry voice, sample for sample");

            int worst = 0;
            var wrong = new List<string>();
            foreach (var job in jobs)
            {
                int? jobRate = (int?)job["sampleRate"];
                var reply = await http.SendAsync(VoicePost("play", deviceId, jobRate == null ? play(fxOf(job), 0)
                    : new { reqId = "fx", ids = new[] { voices[jobRate.Value] }, leadInMs = 0, gain = 1f,
                        fx = new { id = (string)job["effect"], values = job["values"], seed = (uint)job["seed"], sounds = rateSounds[jobRate.Value] } }));
                int error = reply.StatusCode == HttpStatusCode.OK ? MaxError(Unpack((string)job["pcm16"]), audio.LastPrompt) : int.MaxValue;
                worst = Math.Max(worst, error);
                if (error > 1)
                    wrong.Add((string)job["name"]);
            }
            Check(wrong.Count == 0, "every vector job through the play route matches the app (" + jobs.Count + " jobs, max error " + worst + " LSB)"
                + (wrong.Count == 0 ? "" : ": " + string.Join(", ", wrong)));

            await http.SendAsync(VoicePost("play", deviceId, play(fxOf(longest), 0)));
            var wetPcm = audio.LastPrompt;
            Check(wetPcm.Length > dryLength && (int)longest["preRollSamples"] > 0, longest["name"] + " renders longer than dry (" + wetPcm.Length + " vs " + dryLength
                + " samples) with a pre-roll of " + longest["preRollSamples"]);
            var lead = await http.SendAsync(VoicePost("play", deviceId, play(fxOf(longest), 120)));
            var leadPcm = audio.LastPrompt;
            int leadIn = rate * 120 / 1000;
            Check(leadPcm.Length == leadIn + wetPcm.Length && leadPcm.Take(leadIn).All(x => x == 0) && leadPcm.Skip(leadIn).SequenceEqual(wetPcm)
                && (int)JObject.Parse(await lead.Content.ReadAsStringAsync())["durationMs"] == leadPcm.Length * 1000 / rate,
                "lead-in is silence in front of the pre-roll; durationMs covers lead-in, pre-roll and tail");

            var effect = VoiceFxCatalog.All[0];
            Func<object, Task<int>> status = async fx => (int)(await http.SendAsync(VoicePost("play", deviceId, play(fx, 0)))).StatusCode;
            Check(await status(new { id = "not_an_effect", seed = 1, sounds }) == 400, "unknown effect -> 400");
            Check(await status(new { id = effect.Id, sounds }) == 400, "fx without a seed -> 400");
            Check(await status(new { id = effect.Id, seed = 1, values = new JObject { ["in"] = 1 },
                    sounds = sounds.Where(kv => kv.Key != VoiceFxCatalog.RadioIn[1]).ToDictionary(kv => kv.Key, kv => kv.Value) }) == 400,
                "fx without a sound it plays -> 400");
            Check(await status(new { id = effect.Id, seed = 1, sounds, values = new JObject { [effect.Params[0].Id] = 50.5 } }) == 400, "a fractional value -> 400");
            Check(await status(new { id = effect.Id, seed = -1, sounds }) == 400, "a negative seed -> 400");
            Check(await status(effect.Id) == 400, "fx that is not an object -> 400");
        }

        static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        sealed class FakeApi : IDisposable
        {
            readonly TcpListener _listener;
            readonly Func<string, string, string, Tuple<int, string>> _handler;
            public readonly List<string> Requests = new List<string>();
            public readonly List<string> Bodies = new List<string>();
            public string BaseUrl { get; }

            public FakeApi(Func<string, string, string, Tuple<int, string>> handler)
            {
                _handler = handler;
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
                Task.Run(Accept);
            }

            async Task Accept()
            {
                while (true)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(); }
                    catch (Exception) { return; }
                    _ = Task.Run(() => Serve(client));
                }
            }

            async Task Serve(TcpClient client)
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[64 * 1024];
                    int total = 0;
                    int headerEnd = -1;
                    while (headerEnd < 0)
                    {
                        int n = await stream.ReadAsync(buffer, total, buffer.Length - total);
                        if (n <= 0) return;
                        total += n;
                        headerEnd = Encoding.ASCII.GetString(buffer, 0, total).IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    }
                    string head = Encoding.ASCII.GetString(buffer, 0, headerEnd);
                    var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var parts = lines[0].Split(' ');
                    int contentLength = lines.Skip(1).Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        .Select(l => int.Parse(l.Substring(15).Trim())).FirstOrDefault();
                    int bodyStart = headerEnd + 4;
                    while (total - bodyStart < contentLength)
                    {
                        int n = await stream.ReadAsync(buffer, total, buffer.Length - total);
                        if (n <= 0) return;
                        total += n;
                    }
                    string body = Encoding.UTF8.GetString(buffer, bodyStart, contentLength);
                    lock (Requests)
                    {
                        Requests.Add(parts[0] + " " + parts[1]);
                        Bodies.Add(body);
                    }
                    var result = _handler(parts[0], parts[1], body);
                    var payload = Encoding.UTF8.GetBytes(result.Item2);
                    var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {result.Item1} X\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(response, 0, response.Length);
                    await stream.WriteAsync(payload, 0, payload.Length);
                }
            }

            public void Dispose()
            {
                _listener.Stop();
            }
        }
    }
}
