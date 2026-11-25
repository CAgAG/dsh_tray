using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DshTray
{
    internal sealed class ProbeResult
    {
        public string Url = "";
        public bool Reachable;
        public int StatusCode;
        public string Body = "";
        public bool IsDsh;
        public bool PortInUse;
        public string Error = "";
    }

    /// <summary>HTTP/TCP probing used for "is DSH up?" decisions and port conflict detection.</summary>
    internal static class Probe
    {
        public static ProbeResult Run(string url, int timeoutMs)
        {
            ProbeResult result = new ProbeResult();
            result.Url = url ?? "";
            if (string.IsNullOrEmpty(url)) return result;

            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                result.Error = "URL \u65e0\u6548";
                return result;
            }

            int port = uri.Port;
            string host = uri.Host;

            // Cheap fast path: a closed port cannot host DSH, and probing it over
            // HTTP would cost seconds on some systems.
            if (!TcpListening(host, port, Math.Min(600, timeoutMs)))
            {
                return result;
            }
            result.PortInUse = true;

            HttpWebRequest request = null;
            try
            {
                request = (HttpWebRequest)WebRequest.Create(uri);
                request.Method = "GET";
                request.Timeout = Math.Max(500, timeoutMs);
                request.ReadWriteTimeout = Math.Max(500, timeoutMs);
                request.AllowAutoRedirect = false;
                request.Proxy = null;
                request.KeepAlive = false;
                request.UserAgent = "DSHTray/" + AppPaths.Version;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    result.Reachable = true;
                    result.StatusCode = (int)response.StatusCode;
                    result.Body = ReadBody(response);
                }
            }
            catch (WebException we)
            {
                HttpWebResponse errorResponse = we.Response as HttpWebResponse;
                if (errorResponse != null)
                {
                    result.Reachable = true;
                    result.StatusCode = (int)errorResponse.StatusCode;
                    result.Body = ReadBody(errorResponse);
                    errorResponse.Dispose();
                }
                else
                {
                    result.Error = we.Message;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            if (result.Reachable) result.PortInUse = true;

            result.IsDsh = LooksLikeDsh(result);
            return result;
        }

        public static bool LooksLikeDsh(ProbeResult result)
        {
            if (result == null || !result.Reachable) return false;
            string body = result.Body ?? "";
            bool marker = ContainsIgnoreCase(body, "dsh web") || ContainsIgnoreCase(body, "DeepSeek Harness");
            if (result.StatusCode == 401) return ContainsIgnoreCase(body, "dsh web");
            if (result.StatusCode == 200) return marker;
            if (result.StatusCode == 303 || result.StatusCode == 302) return true;
            return false;
        }

        private static bool ContainsIgnoreCase(string text, string needle)
        {
            return text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ReadBody(HttpWebResponse response)
        {
            try
            {
                using (Stream stream = response.GetResponseStream())
                {
                    if (stream == null) return "";
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        char[] buffer = new char[4096];
                        int read = reader.Read(buffer, 0, buffer.Length);
                        return read > 0 ? new string(buffer, 0, read) : "";
                    }
                }
            }
            catch
            {
                return "";
            }
        }

        public static bool TcpListening(string host, int port, int timeoutMs)
        {
            if (port <= 0) return false;
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult async = client.BeginConnect(host, port, null, null);
                    bool finished = async.AsyncWaitHandle.WaitOne(Math.Max(200, timeoutMs));
                    if (!finished) return false;
                    client.EndConnect(async);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Best-effort PID of the process listening on a local port.</summary>
        public static int FindPortOwner(int port)
        {
            if (port <= 0) return 0;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netstat.exe", "-ano -p tcp");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process process = Process.Start(psi))
                {
                    if (process == null) return 0;
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(4000);
                    string needle = ":" + port.ToString(CultureInfo.InvariantCulture);
                    foreach (string raw in output.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0) continue;
                        string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 5) continue;
                        if (!string.Equals(parts[0], "TCP", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!parts[1].EndsWith(needle, StringComparison.Ordinal)) continue;
                        if (!string.Equals(parts[3], "LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                        int pid;
                        if (int.TryParse(parts[4], out pid)) return pid;
                    }
                }
            }
            catch
            {
            }
            return 0;
        }

        public static string StripQuery(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            try
            {
                Uri uri = new Uri(url);
                UriBuilder builder = new UriBuilder(uri);
                builder.Query = "";
                builder.Fragment = "";
                return builder.Uri.AbsoluteUri;
            }
            catch
            {
                int index = url.IndexOf('?');
                return index >= 0 ? url.Substring(0, index) : url;
            }
        }

        public static bool SameUrl(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string left = StripQuery(a).TrimEnd('/');
            string right = StripQuery(b).TrimEnd('/');
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
