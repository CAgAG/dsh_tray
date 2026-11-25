using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DshTray
{
    internal sealed class LaunchCommand
    {
        public string FileName = "";
        public string Arguments = "";
        public string Description = "";
        public bool Fallback;
        public string NodePath = "";
        public string DshEntry = "";
        public string DshVersion = "";
    }

    /// <summary>Finds node.exe and the @deepseek-ai/dsh CLI, preferring the highest installed version.</summary>
    internal sealed class Resolver
    {
        private static readonly Regex VersionRegex = new Regex("\"version\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex BinObjectRegex = new Regex("\"bin\"\\s*:\\s*\\{([^}]*)\\}", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex BinStringRegex = new Regex("\"bin\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex BinDshRegex = new Regex("\"dsh\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        private readonly Config _cfg;
        private readonly Logger _log;

        public Resolver(Config cfg, Logger log)
        {
            _cfg = cfg;
            _log = log;
        }

        public LaunchCommand Build(int port)
        {
            string node = ResolveNode();
            string version;
            string source;
            string entry = ResolveDshEntry(out version, out source);

            if (node != null && entry != null)
            {
                LaunchCommand command = new LaunchCommand();
                command.FileName = node;
                command.Arguments = BuildWebArguments(entry, port);
                command.NodePath = node;
                command.DshEntry = entry;
                command.DshVersion = version;
                command.Description = "node " + NodeVersion(node) + " + @deepseek-ai/dsh " + version + " (" + source + ")";
                return command;
            }

            string npx = ResolveNpx(node);
            if (npx != null)
            {
                LaunchCommand command = new LaunchCommand();
                command.Fallback = true;
                command.FileName = Environment.GetEnvironmentVariable("COMSPEC");
                if (string.IsNullOrEmpty(command.FileName) || !File.Exists(command.FileName))
                {
                    command.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                }
                string inner = Util.Quote(npx) + " --yes @deepseek-ai/dsh@latest " + BuildProfileArguments(port);
                command.Arguments = "/d /s /c \"" + inner + "\"";
                command.NodePath = node ?? "";
                command.Description = "npx @deepseek-ai/dsh@latest\uff08\u672a\u627e\u5230\u672c\u5730\u5b89\u88c5\uff0c\u9996\u6b21\u4f1a\u8054\u7f51\u4e0b\u8f7d\uff09";
                _log.Warn("no local @deepseek-ai/dsh package found; falling back to npx: " + command.Description);
                return command;
            }

            throw new InvalidOperationException(
                "\u672a\u627e\u5230 Node.js \u6216 @deepseek-ai/dsh\u3002\u8bf7\u5148\u5728\u547d\u4ee4\u884c\u8fd0\u884c\u4e00\u6b21 npx @deepseek-ai/dsh web\uff0c"
                + "\u6216\u5728\u914d\u7f6e\u6587\u4ef6\u4e2d\u6307\u5b9a nodePath \u548c dshEntry\u3002");
        }

        private string BuildWebArguments(string entry, int port)
        {
            return Util.Quote(entry) + " " + BuildProfileArguments(port);
        }

        private string BuildProfileArguments(int port)
        {
            StringBuilder sb = new StringBuilder();
            string profile = string.IsNullOrEmpty(_cfg.Profile) ? "web" : _cfg.Profile.Trim();
            sb.Append(profile);
            sb.Append(" --no-open");
            if (!string.IsNullOrEmpty(_cfg.Host)) sb.Append(" --host ").Append(_cfg.Host.Trim());
            sb.Append(" --port ").Append(port.ToString(CultureInfo.InvariantCulture));
            string extra = (_cfg.ExtraArgs ?? "").Trim();
            if (extra.Length > 0) sb.Append(' ').Append(extra);
            return sb.ToString();
        }

        public string ResolveNode()
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrEmpty(_cfg.NodePath)) candidates.Add(_cfg.NodePath);

            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(';'))
            {
                string trimmed = dir.Trim();
                if (trimmed.Length > 0) candidates.Add(Path.Combine(trimmed, "node.exe"));
            }

            AddIfNotEmpty(candidates, Environment.GetEnvironmentVariable("NVM_SYMLINK"), "node.exe");
            AddIfNotEmpty(candidates, Environment.GetEnvironmentVariable("NVM_HOME"), "node.exe");
            AddIfNotEmpty(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
            AddIfNotEmpty(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe");
            AddIfNotEmpty(candidates, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe");
            AddIfNotEmpty(candidates, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Volta", "bin", "node.exe");
            AddIfNotEmpty(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nvm", "current", "node.exe");
            AddIfNotEmpty(candidates, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "nodejs", "current", "node.exe");

            // nvm-windows version folders (%~APPDATA%\nvm\vX.Y.Z\node.exe)
            string nvmHome = Environment.GetEnvironmentVariable("NVM_HOME");
            if (string.IsNullOrEmpty(nvmHome)) nvmHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nvm");
            foreach (string dir in SafeDirectories(nvmHome))
            {
                candidates.Add(Path.Combine(dir, "node.exe"));
            }

            // fnm installation folders
            string fnm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fnm", "node-versions");
            foreach (string dir in SafeDirectories(fnm))
            {
                candidates.Add(Path.Combine(dir, "installation", "node.exe"));
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
                catch
                {
                }
            }
            return null;
        }

        public string ResolveDshEntry(out string version, out string source)
        {
            version = "";
            source = "";

            if (!string.IsNullOrEmpty(_cfg.DshEntry))
            {
                string configured = _cfg.DshEntry.Trim();
                try
                {
                    if (File.Exists(configured))
                    {
                        version = ReadVersion(Path.GetDirectoryName(configured));
                        source = "\u914d\u7f6e\u6307\u5b9a";
                        return Path.GetFullPath(configured);
                    }
                    if (Directory.Exists(configured))
                    {
                        string entry;
                        if (TryReadPackage(configured, out version, out entry)) { source = "\u914d\u7f6e\u6307\u5b9a"; return entry; }
                    }
                }
                catch
                {
                }
                _log.Warn("configured dshEntry is unusable: " + configured);
            }

            List<DshCandidate> candidates = CollectCandidates();
            if (candidates.Count == 0) return null;

            DshCandidate best = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                if (CompareVersions(candidates[i].Version, best.Version) > 0) best = candidates[i];
            }
            version = best.Version;
            source = best.Source;
            return best.Entry;
        }

        private List<DshCandidate> CollectCandidates()
        {
            List<PackageRef> packageDirs = new List<PackageRef>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            AddPackageDir(packageDirs, seen, Path.Combine(appData, "npm", "node_modules"), "npm \u5168\u5c40");
            AddPackageDir(packageDirs, seen, Path.Combine(localAppData, "npm", "node_modules"), "npm \u5168\u5c40");
            AddPackageDir(packageDirs, seen, Path.Combine(programFiles, "nodejs", "node_modules"), "nodejs \u5168\u5c40");

            List<string> cacheRoots = new List<string>();
            cacheRoots.Add(Path.Combine(localAppData, "npm-cache"));
            cacheRoots.Add(Path.Combine(appData, "npm-cache"));
            cacheRoots.Add(Path.Combine(userProfile, ".npm"));
            string envCache = Environment.GetEnvironmentVariable("npm_config_cache");
            if (!string.IsNullOrEmpty(envCache)) cacheRoots.Add(envCache);

            foreach (string cache in cacheRoots)
            {
                string npxRoot = Path.Combine(cache, "_npx");
                if (!Directory.Exists(npxRoot)) continue;
                foreach (string dir in SafeDirectories(npxRoot))
                {
                    AddPackageDir(packageDirs, seen, Path.Combine(dir, "node_modules"), "npx \u7f13\u5b58");
                }
            }

            List<DshCandidate> result = new List<DshCandidate>();
            foreach (PackageRef package in packageDirs)
            {
                string version;
                string entry;
                if (TryReadPackage(package.Dir, out version, out entry))
                {
                    DshCandidate candidate = new DshCandidate();
                    candidate.Version = version;
                    candidate.Entry = entry;
                    candidate.Source = package.Source;
                    result.Add(candidate);
                }
            }
            return result;
        }

        private static void AddPackageDir(List<PackageRef> packageDirs, HashSet<string> seen, string nodeModulesRoot, string source)
        {
            if (string.IsNullOrEmpty(nodeModulesRoot)) return;
            try
            {
                string packageDir = Path.GetFullPath(Path.Combine(nodeModulesRoot, "@deepseek-ai", "dsh"));
                if (!Directory.Exists(packageDir)) return;
                if (seen.Add(packageDir))
                {
                    PackageRef reference = new PackageRef();
                    reference.Dir = packageDir;
                    reference.Source = source;
                    packageDirs.Add(reference);
                }
            }
            catch
            {
            }
        }

        private sealed class PackageRef
        {
            public string Dir = "";
            public string Source = "";
        }

        private bool TryReadPackage(string packageDir, out string version, out string entry)
        {
            version = "";
            entry = "";
            try
            {
                string manifest = Path.Combine(packageDir, "package.json");
                if (!File.Exists(manifest)) return false;
                string text = File.ReadAllText(manifest);
                Match vm = VersionRegex.Match(text);
                if (!vm.Success) return false;
                version = vm.Groups[1].Value;

                string binRelative = "lib/bin.js";
                Match bm = BinObjectRegex.Match(text);
                if (bm.Success)
                {
                    Match dm = BinDshRegex.Match(bm.Groups[1].Value);
                    if (dm.Success) binRelative = dm.Groups[1].Value;
                }
                else
                {
                    Match sm = BinStringRegex.Match(text);
                    if (sm.Success) binRelative = sm.Groups[1].Value;
                }

                string candidate = Path.Combine(packageDir, binRelative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                if (!File.Exists(candidate)) candidate = Path.Combine(packageDir, "lib", "bin.js");
                if (!File.Exists(candidate)) return false;
                entry = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string ReadVersion(string packageDir)
        {
            string version;
            string entry;
            return TryReadPackage(packageDir, out version, out entry) ? version : "";
        }

        private string ResolveNpx(string node)
        {
            if (!string.IsNullOrEmpty(node))
            {
                try
                {
                    string candidate = Path.Combine(Path.GetDirectoryName(node), "npx.cmd");
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(';'))
            {
                string trimmed = dir.Trim();
                if (trimmed.Length == 0) continue;
                try
                {
                    string candidate = Path.Combine(trimmed, "npx.cmd");
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }
            return null;
        }

        private static string NodeVersion(string node)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(node, "-v");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
                {
                    if (process == null) return "";
                    string text = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(3000);
                    return text;
                }
            }
            catch
            {
                return "";
            }
        }

        private static void AddIfNotEmpty(List<string> candidates, string root, params string[] parts)
        {
            if (string.IsNullOrEmpty(root)) return;
            string path = root;
            foreach (string part in parts) path = Path.Combine(path, part);
            candidates.Add(path);
        }

        private static IEnumerable<string> SafeDirectories(string root)
        {
            try
            {
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) return Directory.GetDirectories(root);
            }
            catch
            {
            }
            return new string[0];
        }

        /// <summary>Numeric-aware comparison of semver-ish strings; prereleases sort before releases.</summary>
        public static int CompareVersions(string left, string right)
        {
            string[] a = (left ?? "").Split(new char[] { '-', '+' });
            string[] b = (right ?? "").Split(new char[] { '-', '+' });
            string[] aCore = a[0].Split('.');
            string[] bCore = b[0].Split('.');
            int length = Math.Max(aCore.Length, bCore.Length);
            for (int i = 0; i < length; i++)
            {
                int av = i < aCore.Length ? Util.ParseInt(aCore[i], 0) : 0;
                int bv = i < bCore.Length ? Util.ParseInt(bCore[i], 0) : 0;
                if (av != bv) return av < bv ? -1 : 1;
            }
            bool aPre = a.Length > 1;
            bool bPre = b.Length > 1;
            if (aPre == bPre) return 0;
            return aPre ? -1 : 1;
        }

        private sealed class DshCandidate
        {
            public string Version = "";
            public string Entry = "";
            public string Source = "";
        }
    }
}
