using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Funbit.Ets.Telemetry.Server
{
    public sealed class UpdateForm : Form
    {
        readonly string _changelogUrl;
        string _tempHtmlPath;

        public UpdateForm(string currentVersion, string installedVersion, string releaseBody, string changelogUrl)
        {
            _changelogUrl = changelogUrl;
            BuildForm(currentVersion, installedVersion, releaseBody);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (_tempHtmlPath != null && File.Exists(_tempHtmlPath))
            {
                try { File.Delete(_tempHtmlPath); }
                catch { /* best effort cleanup */ }
            }
        }

        void BuildForm(string currentVersion, string installedVersion, string releaseBody)
        {
            SuspendLayout();

            Text = "Update Available \u2013 TruckSim GPS Telemetry Server";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            ClientSize = new Size(560, 500);
            MinimumSize = new Size(450, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9.75f);

            var headerLabel = new Label
            {
                Text = "A new version is available!",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30),
                AutoSize = false,
                Size = new Size(520, 28),
                Location = new Point(20, 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var versionLabel = new Label
            {
                Text = $"v{TrimVersion(installedVersion)}  \u2192  v{currentVersion}",
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(80, 80, 80),
                AutoSize = false,
                Size = new Size(520, 22),
                Location = new Point(20, 46),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var separator = new Label
            {
                AutoSize = false,
                Size = new Size(520, 1),
                Location = new Point(20, 76),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.Fixed3D
            };

            var changelogLabel = new Label
            {
                Text = "Release Notes",
                Font = new Font("Segoe UI Semibold", 9.75f),
                ForeColor = Color.FromArgb(60, 60, 60),
                AutoSize = true,
                Location = new Point(20, 84)
            };

            var webBrowser = new WebBrowser
            {
                Location = new Point(20, 108),
                Size = new Size(520, 310),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                IsWebBrowserContextMenuEnabled = false,
                ScriptErrorsSuppressed = true,
                WebBrowserShortcutsEnabled = false
            };

            webBrowser.Navigating += (s, e) =>
            {
                // Allow file:// navigation for our temp file, block everything else except links
                if (e.Url.Scheme == "http" || e.Url.Scheme == "https")
                {
                    e.Cancel = true;
                    Process.Start(new ProcessStartInfo(e.Url.ToString()) { UseShellExecute = true });
                }
            };

            // Write HTML to temp file with UTF-8 BOM so IE correctly handles emoji
            string html = BuildHtml(releaseBody ?? "No release notes available.");
            _tempHtmlPath = Path.Combine(Path.GetTempPath(), $"trucksimgps_update_{Guid.NewGuid():N}.html");
            File.WriteAllText(_tempHtmlPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            webBrowser.Navigate(_tempHtmlPath);

            var bottomPanel = new Panel
            {
                Size = new Size(560, 56),
                Dock = DockStyle.Bottom
            };

            var githubLink = new LinkLabel
            {
                Text = "View full release on GitHub",
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Location = new Point(20, 18)
            };
            githubLink.LinkClicked += (s, e) =>
            {
                Process.Start(new ProcessStartInfo(_changelogUrl) { UseShellExecute = true });
            };

            var laterButton = new Button
            {
                Text = "Later",
                Font = new Font("Segoe UI", 9.75f),
                Size = new Size(90, 34),
                Location = new Point(340, 12),
                Anchor = AnchorStyles.Right,
                FlatStyle = FlatStyle.System
            };
            laterButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            var updateButton = new Button
            {
                Text = "Update Now",
                Font = new Font("Segoe UI", 9.75f, FontStyle.Bold),
                Size = new Size(110, 34),
                Location = new Point(436, 12),
                Anchor = AnchorStyles.Right,
                FlatStyle = FlatStyle.System
            };
            updateButton.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            AcceptButton = updateButton;
            CancelButton = laterButton;

            bottomPanel.Controls.AddRange(new Control[] { githubLink, laterButton, updateButton });
            Controls.AddRange(new Control[]
            {
                headerLabel, versionLabel, separator, changelogLabel, webBrowser, bottomPanel
            });

            ResumeLayout();
        }

        static string BuildHtml(string markdown)
        {
            string body = MarkdownToHtml(markdown);

            return @"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"" />
<meta http-equiv=""X-UA-Compatible"" content=""IE=edge"" />
<style>
body {
  font-family: 'Segoe UI', 'Segoe UI Emoji', 'Segoe UI Symbol', sans-serif;
  font-size: 14px;
  color: #222;
  margin: 8px 12px;
  line-height: 1.6;
}
h2 {
  font-size: 16px;
  font-weight: 600;
  color: #1a1a1a;
  margin: 1em 0 0.4em 0;
  padding-bottom: 4px;
  border-bottom: 1px solid #e0e0e0;
}
h2:first-child { margin-top: 0; }
h3 { font-size: 15px; font-weight: 600; margin: 0.8em 0 0.3em 0; }
ul, ol { padding-left: 22px; margin: 0.3em 0; }
ul { list-style-type: disc; }
ul ul { list-style-type: disc; margin: 2px 0; padding-left: 20px; }
li { margin: 4px 0; }
code {
  background: #f4f4f4;
  padding: 1px 5px;
  border-radius: 3px;
  font-family: Consolas, monospace;
  font-size: 13px;
}
a { color: #0066cc; text-decoration: none; }
a:hover { text-decoration: underline; }
p { margin: 0.4em 0; }
strong { font-weight: 600; }
hr { border: none; border-top: 1px solid #e0e0e0; margin: 12px 0; }
</style>
</head>
<body>" + body + @"</body>
</html>";
        }

        static string MarkdownToHtml(string markdown)
        {
            var sb = new StringBuilder();
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            bool inUl = false;
            bool inOl = false;
            int ulDepth = 0;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.TrimStart();

                bool isUlItem = Regex.IsMatch(trimmed, @"^[-*+]\s");
                bool isOlItem = Regex.IsMatch(trimmed, @"^\d+\.\s");
                int indent = line.Length - trimmed.Length;

                // Close lists when transitioning out
                if (inUl && !isUlItem && trimmed.Length > 0)
                {
                    while (ulDepth > 0) { sb.Append("</ul>"); ulDepth--; }
                    inUl = false;
                }
                if (inOl && !isOlItem && trimmed.Length > 0)
                {
                    sb.Append("</ol>");
                    inOl = false;
                }

                // Headers
                var hMatch = Regex.Match(trimmed, @"^(#{1,3})\s+(.+)");
                if (hMatch.Success)
                {
                    if (inUl) { while (ulDepth > 0) { sb.Append("</ul>"); ulDepth--; } inUl = false; }
                    if (inOl) { sb.Append("</ol>"); inOl = false; }
                    int level = hMatch.Groups[1].Length;
                    sb.Append($"<h{level}>{Inline(hMatch.Groups[2].Value.Trim())}</h{level}>");
                    continue;
                }

                // Horizontal rule
                if (Regex.IsMatch(trimmed, @"^[-*_]{3,}\s*$")) { sb.Append("<hr/>"); continue; }

                // Unordered list
                if (isUlItem)
                {
                    var m = Regex.Match(trimmed, @"^[-*+]\s+(.+)");
                    if (m.Success)
                    {
                        // Each 2 spaces of indent = one nesting level
                        int targetDepth = (indent / 2) + 1;
                        if (!inUl) { sb.Append("<ul>"); ulDepth = 1; inUl = true; }
                        while (ulDepth < targetDepth) { sb.Append("<ul>"); ulDepth++; }
                        while (ulDepth > targetDepth) { sb.Append("</ul>"); ulDepth--; }
                        sb.Append($"<li>{Inline(m.Groups[1].Value)}</li>");
                    }
                    continue;
                }

                // Ordered list
                if (isOlItem)
                {
                    var m = Regex.Match(trimmed, @"^\d+\.\s+(.+)");
                    if (m.Success)
                    {
                        if (!inOl) { sb.Append("<ol>"); inOl = true; }
                        sb.Append($"<li>{Inline(m.Groups[1].Value)}</li>");
                    }
                    continue;
                }

                // Empty line
                if (trimmed.Length == 0) continue;

                // Paragraph
                sb.Append($"<p>{Inline(trimmed)}</p>");
            }

            if (inUl) { while (ulDepth > 0) { sb.Append("</ul>"); ulDepth--; } }
            if (inOl) sb.Append("</ol>");

            return sb.ToString();
        }

        static string Inline(string text)
        {
            text = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            text = Regex.Replace(text, @"__(.+?)__", "<strong>$1</strong>");
            text = Regex.Replace(text, @"(?<!\*)\*([^*]+?)\*(?!\*)", "<em>$1</em>");
            text = Regex.Replace(text, @"(?<!\w)_([^_]+?)_(?!\w)", "<em>$1</em>");
            text = Regex.Replace(text, @"`(.+?)`", "<code>$1</code>");
            text = Regex.Replace(text, @"\[(.+?)\]\((.+?)\)", "<a href=\"$2\">$1</a>");
            return text;
        }

        static string TrimVersion(string version)
        {
            var parts = version.Split('.');
            if (parts.Length >= 3)
                return $"{parts[0]}.{parts[1]}.{parts[2]}";
            return version;
        }
    }
}
