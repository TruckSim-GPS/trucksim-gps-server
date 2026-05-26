using System;
using System.Configuration;
using System.IO;
using System.Text;
using Funbit.Ets.Telemetry.Server.Data;
using Funbit.Ets.Telemetry.Server.Helpers;
using Newtonsoft.Json;

namespace Funbit.Ets.Telemetry.Server.Controllers
{
    /// <summary>
    /// Endpoint helpers consumed by <see cref="MinimalHttpServer"/>.
    /// Kept as static methods after the OWIN/SignalR/WebApi stack was removed.
    /// </summary>
    public static class TelemetryEndpoints
    {
        public const string TelemetryAppUriPath = "/";
        public const string TelemetryApiUriPath = "/api/ets2/telemetry";

        const string TestTelemetryJsonFileName = "Ets2TestTelemetry.json";

        static readonly bool UseTestTelemetryData = Convert.ToBoolean(
            ConfigurationManager.AppSettings["UseEts2TestTelemetryData"]);

        public static string GetEts2TelemetryJson()
        {
            if (UseTestTelemetryData)
            {
                using (var file = File.Open(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TestTelemetryJsonFileName),
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite))
                using (var reader = new StreamReader(file, Encoding.UTF8))
                    return reader.ReadToEnd();
            }

            var v1 = ScsTelemetryDataReader.Instance.Read();
            return JsonConvert.SerializeObject(v1, JsonHelper.RestSettings);
        }

        const string StatusPageHtmlTemplate = @"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>TruckSim GPS Telemetry Server</title>
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            margin: 0;
            padding: 0;
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .container {
            background: white;
            border-radius: 12px;
            box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
            padding: 40px;
            text-align: center;
            max-width: 500px;
            margin: 20px;
        }
        .status-icon {
            font-size: 64px;
            margin-bottom: 20px;
            color: #4CAF50;
        }
        h1 {
            color: #333;
            margin-bottom: 16px;
            font-size: 28px;
        }
        .status-text {
            color: #4CAF50;
            font-size: 20px;
            font-weight: 600;
            margin-bottom: 24px;
        }
        .info {
            color: #666;
            line-height: 1.6;
            margin-bottom: 20px;
        }
        .version {
            color: #999;
            font-size: 14px;
            border-top: 1px solid #eee;
            padding-top: 20px;
            margin-top: 20px;
        }
        .api-info {
            background: #f8f9fa;
            border-radius: 6px;
            padding: 16px;
            margin: 20px 0;
            font-family: 'Courier New', monospace;
            font-size: 14px;
            color: #495057;
        }
        .bypass-notice {
            background: #fff3cd;
            color: #856404;
            padding: 12px;
            border-radius: 6px;
            margin: 20px 0;
            font-size: 13px;
        }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""status-icon"">✅</div>
        <h1>TruckSim GPS Telemetry Server</h1>
        <div class=""status-text"">Connection Successful!</div>
        <div class=""info"">
            <p>The telemetry server is running and accessible from this device.
            You can now use this IP address in your TruckSim GPS mobile application to connect.</p>

            <p>To use this connection in the app:</p>
            <ol style=""text-align: left; display: inline-block;"">
                <li>Open the TruckSim GPS app on your mobile device</li>
                <li>Go to connection settings</li>
                <li>Enter this server's IP address</li>
                <li>Start Euro Truck Simulator 2 or American Truck Simulator</li>
            </ol>
        </div>

        <div class=""api-info"">
            Telemetry API: <strong>/api/ets2/telemetry</strong>
        </div>

        {BYPASS_NOTICE}

        <div class=""version"">Version {VERSION}</div>
    </div>
</body>
</html>";

        public static string GetStatusPageHtml(bool showBypassNotice = false)
        {
            string bypassNotice = showBypassNotice
                ? @"<div class=""bypass-notice"">⚙️ Using custom HTTP server (KB5066835/KB5065789 workaround)</div>"
                : "";

            return StatusPageHtmlTemplate
                .Replace("{VERSION}", AssemblyHelper.Version)
                .Replace("{BYPASS_NOTICE}", bypassNotice);
        }
    }
}
