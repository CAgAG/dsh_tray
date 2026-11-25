using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DshTray
{
    internal static class Util
    {
        public static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        public static string Truncate(string value, int max)
        {
            if (value == null) return "";
            if (max <= 0) return "";
            return value.Length <= max ? value : value.Substring(0, max - 1) + "\u2026";
        }

        public static string SingleLine(string value)
        {
            if (value == null) return "";
            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        public static bool ParseBool(string raw, bool fallback)
        {
            if (string.IsNullOrEmpty(raw)) return fallback;
            string v = raw.Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on" || v == "\u662f" || v == "\u5f00") return true;
            if (v == "0" || v == "false" || v == "no" || v == "off" || v == "\u5426" || v == "\u5173") return false;
            return fallback;
        }

        public static int ParseInt(string raw, int fallback)
        {
            int value;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return value;
            return fallback;
        }

        public static void SafeDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        /// <summary>Formats a URL for display without the query string.</summary>
        public static string DisplayUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            try
            {
                Uri uri = new Uri(url);
                string text = uri.Host + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/") text += uri.AbsolutePath;
                return text;
            }
            catch
            {
                return url;
            }
        }
    }
}
