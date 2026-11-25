using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DshTray
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); }
            catch { }
            ConfigureConsoleEncoding();

            CliOptions options;
            try
            {
                options = CliOptions.Parse(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }

            if (options.ShowHelp)
            {
                CliRunner.PrintHelp();
                return 0;
            }

            string dataDir;
            try
            {
                dataDir = AppPaths.ResolveDataDir(options.DataDir);
                AppPaths.EnsureDirectory(dataDir);
            }
            catch (Exception ex)
            {
                return Fail("\u65e0\u6cd5\u521b\u5efa\u6570\u636e\u76ee\u5f55\uff1a" + ex.Message);
            }

            // A tray started from a restricted shell/sandbox cannot write its data
            // directory; it used to run invisibly while holding the single-instance
            // mutex, so shortcut launches then silently forwarded and exited.
            if (options.Action == "tray" && !AppPaths.CanWriteDirectory(dataDir))
            {
                try
                {
                    File.AppendAllText(
                        Path.Combine(Path.GetTempPath(), "DSHTray-startup-error.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " read-only data dir: " + dataDir + Environment.NewLine,
                        Util.Utf8);
                }
                catch
                {
                }
                return Fail("\u65e0\u6cd5\u5199\u5165\u6570\u636e\u76ee\u5f55\uff1a\r\n" + dataDir
                    + "\r\n\r\n\u8bf7\u4ece\u684c\u9762\u300cDeepSeek Harness\u300d\u5feb\u6377\u65b9\u5f0f\u542f\u52a8\uff0c"
                    + "\u4e0d\u8981\u5728\u53d7\u9650\u7ec8\u7aef\u6216\u6c99\u7bb1\u91cc\u8fd0\u884c\u3002");
            }

            Config config = Config.Load(AppPaths.ConfigFile(dataDir));
            if (options.Port > 0) config.Port = options.Port;
            Logger log = new Logger(dataDir);

            if (options.Action == "status" || options.Action == "diagnose")
            {
                int code = CliRunner.RunInspection(options, config, dataDir, log);
                log.Dispose();
                return code;
            }

            if (options.Action != "tray")
            {
                int code = CliRunner.RunControl(options, config, dataDir, log);
                log.Dispose();
                return code;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, AppPaths.MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    log.Info("another tray instance is already running; forwarding request");
                    CliRunner.SignalExisting(options);
                    log.Dispose();
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    log.Error("unhandled UI exception: " + e.Exception);
                    try
                    {
                        MessageBox.Show("\u542f\u52a8\u5668\u9047\u5230\u9519\u8bef\uff1a\r\n" + e.Exception.Message, AppPaths.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch
                    {
                    }
                };

                try
                {
                    ServerController controller = new ServerController(config, log, dataDir);
                    controller.SuppressAutoOpen = options.Silent || options.Autostart;
                    controller.AutoOpenOnAdopt = !options.Silent && !options.Autostart;
                    TrayContext context = new TrayContext(config, log, controller, AppPaths.WindowTitle, options.Silent, Environment.GetCommandLineArgs()[0]);
                    log.Info("launcher started pid=" + Process.GetCurrentProcess().Id + " data=" + dataDir);
                    Application.Run(context);
                }
                catch (Exception ex)
                {
                    log.Error("fatal: " + ex);
                    try
                    {
                        MessageBox.Show("\u542f\u52a8\u5668\u65e0\u6cd5\u542f\u52a8\uff1a\r\n" + ex.Message, AppPaths.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch
                    {
                    }
                    return 1;
                }
                finally
                {
                    log.Dispose();
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        private static void ConfigureConsoleEncoding()
        {
            try
            {
                // With redirected output there is no console code page to follow, so
                // emit UTF-8 and let scripts / log captures read Chinese correctly.
                IntPtr handle = Native.GetStdHandle(-11);
                uint mode;
                bool redirected = handle != IntPtr.Zero
                    && handle != new IntPtr(-1)
                    && !Native.GetConsoleMode(handle, out mode);
                if (redirected)
                {
                    StreamWriter writer = new StreamWriter(Console.OpenStandardOutput(), Util.Utf8);
                    writer.AutoFlush = true;
                    Console.SetOut(writer);
                    StreamWriter errors = new StreamWriter(Console.OpenStandardError(), Util.Utf8);
                    errors.AutoFlush = true;
                    Console.SetError(errors);
                }
            }
            catch
            {
            }
        }

        private static int Fail(string message)
        {
            try { Console.Error.WriteLine(message); } catch { }
            try
            {
                if (Environment.UserInteractive)
                {
                    MessageBox.Show(message, AppPaths.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch
            {
            }
            return 1;
        }
    }
}
