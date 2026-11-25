using System;
using System.Globalization;

namespace DshTray
{
    /// <summary>
    /// Minimal UI localization. Text is written inline as Chinese/English pairs so
    /// every call site stays readable and no resource files are needed.
    /// </summary>
    internal static class Strings
    {
        private static bool _english = DetectEnglish();

        public static bool IsEnglish { get { return _english; } }

        public static string LanguageTag { get { return _english ? "en" : "zh"; } }

        /// <summary>
        /// Selects the UI language. <c>auto</c> (or empty) follows Windows UI
        /// culture, with <c>DSHTRAY_LANG</c> as an explicit override.
        /// </summary>
        public static void Initialize(string setting)
        {
            string value = (setting ?? "").Trim().ToLowerInvariant();
            if (value.Length == 0 || value == "auto" || value == "system")
            {
                string env = Environment.GetEnvironmentVariable("DSHTRAY_LANG");
                if (!string.IsNullOrEmpty(env))
                {
                    Apply(env);
                    return;
                }
                _english = DetectEnglish();
                return;
            }
            Apply(value);
        }

        private static void Apply(string value)
        {
            string tag = (value ?? "").Trim().ToLowerInvariant();
            if (tag == "en" || tag == "english" || tag.StartsWith("en-", StringComparison.Ordinal))
            {
                _english = true;
            }
            else if (tag == "zh" || tag == "chinese" || tag.StartsWith("zh-", StringComparison.Ordinal))
            {
                _english = false;
            }
            else
            {
                _english = DetectEnglish();
            }
        }

        public static string T(string chinese, string english)
        {
            return _english ? english : chinese;
        }

        public static string Format(string chinese, string english, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, _english ? english : chinese, args);
        }

        private static bool DetectEnglish()
        {
            try
            {
                return !string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
    }
}
