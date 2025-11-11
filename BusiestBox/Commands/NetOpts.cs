using System;
using System.Linq;
using BusiestBox.Utils;

namespace BusiestBox.Commands
{
    internal class NetOpts
    {
        // Usage:
        //   netopts                         -> show current settings
        //   netopts ua <string...>          -> set User-Agent
        //   netopts proxy off|system|<url>  -> set proxy mode or URL
        //   netopts proxy-cred <user> <pw>  -> set proxy credentials
        //   netopts proxy-cred clear        -> clear proxy credentials
        //   netopts timeout <ms>            -> set timeout (ms)
        //   netopts insecure on|off         -> ignore TLS cert errors (global)
        //   netopts redirect on|off         -> follow HTTP redirects
        //   netopts decompress on|off       -> enable gzip/deflate auto-decompression
        //   netopts updog_mode on|off       -> enable HTML-smuggled uploads/downloads decoding
        public static void Execute(params string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Show();
                return;
            }

            var cmd = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToArray();

            try
            {
                switch (cmd)
                {
                    case "ua":
                    case "user-agent":
                        if (rest.Length == 0) { Console.WriteLine("Usage: netopts ua <string...>"); return; }
                        NetUtils.SetUserAgent(string.Join(" ", rest));
                        Console.WriteLine("[*] UA set.");
                        break;

                    case "proxy":
                        if (rest.Length != 1) { Console.WriteLine("Usage: netopts proxy off|system|<url>"); return; }
                        NetUtils.SetProxy(rest[0]);
                        Console.WriteLine("[*] Proxy set: {0}", rest[0]);
                        break;

                    case "proxy-cred":
                    case "proxy-creds":
                        if (rest.Length == 1 && rest[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
                        {
                            NetUtils.ClearProxyCredentials();
                            Console.WriteLine("[*] Proxy credentials cleared.");
                        }
                        else if (rest.Length == 2)
                        {
                            NetUtils.SetProxyCredentials(rest[0], rest[1]);
                            Console.WriteLine("[*] Proxy credentials set.");
                        }
                        else
                        {
                            Console.WriteLine("Usage: netopts proxy-cred <user> <password> | clear");
                        }
                        break;

                    case "timeout":
                        if (rest.Length != 1 || !int.TryParse(rest[0], out int ms) || ms <= 0)
                        { Console.WriteLine("Usage: netopts timeout <milliseconds>"); return; }
                        NetUtils.SetTimeoutMs(ms);
                        Console.WriteLine("[*] Timeout set to {0} ms.", ms);
                        break;

                    case "insecure":
                        if (rest.Length != 1 || !ParseOnOff(rest[0], out bool insecure))
                        { Console.WriteLine("Usage: netopts insecure on|off"); return; }
                        NetUtils.SetInsecureTls(insecure);
                        Console.WriteLine("[*] Insecure TLS {0}.", insecure ? "enabled (WARNING)" : "disabled");
                        break;

                    case "redirect":
                        if (rest.Length != 1 || !ParseOnOff(rest[0], out bool follow))
                        { Console.WriteLine("Usage: netopts redirect on|off"); return; }
                        NetUtils.SetAutoRedirect(follow);
                        Console.WriteLine("[*] Auto-redirect {0}.", follow ? "enabled" : "disabled");
                        break;

                    case "decompress":
                    case "compression":
                        if (rest.Length != 1 || !ParseOnOff(rest[0], out bool decomp))
                        { Console.WriteLine("Usage: netopts decompress on|off"); return; }
                        NetUtils.SetDecompression(decomp);
                        Console.WriteLine("[*] Auto-decompression {0}.", decomp ? "enabled" : "disabled");
                        break;

                    case "updog_mode":
                    case "updog":
                        if (rest.Length != 1 || !ParseOnOff(rest[0], out bool updog_mode))
                        { Console.WriteLine("Usage: netopts updog_mode on|off"); return; }
                        NetUtils.SetUpdogMode(updog_mode);
                        Console.WriteLine("[*] Updog mode {0}.", updog_mode ? "enabled" : "disabled");
                        break;

                    case "help":
                    case "--help":
                    case "-h":
                        PrintUsage();
                        break;

                    default:
                        Console.WriteLine("[!] Unknown netopts subcommand: {0}", cmd);
                        PrintUsage();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] netopts: {0}", ex.Message);
            }
        }

        private static void Show()
        {
            Console.WriteLine("Network Options:");
            Console.WriteLine("  User-Agent            : {0}", NetUtils.UserAgent);
            Console.WriteLine("  Proxy Mode            : {0}", NetUtils.UseSystemProxy ? "system" :
                                                 string.IsNullOrEmpty(NetUtils.ProxyUrl) ? "off" : "explicit");
            Console.WriteLine("  Proxy URL             : {0}", string.IsNullOrEmpty(NetUtils.ProxyUrl) ? "(none)" : NetUtils.ProxyUrl);
            Console.WriteLine("  Proxy Credentials     : {0}", NetUtils.HasProxyCredentials ? "(set)" : "(none)");
            Console.WriteLine("  Timeout (ms)          : {0}", NetUtils.TimeoutMs);
            Console.WriteLine("  Insecure TLS          : {0}", NetUtils.InsecureTls ? "ON (unsafe)" : "OFF");
            Console.WriteLine("  Auto-Redirect         : {0}", NetUtils.AllowAutoRedirect ? "ON" : "OFF");
            Console.WriteLine("  Auto-Decompression    : {0}", NetUtils.EnableDecompression ? "ON" : "OFF");
            Console.WriteLine("  Updog Mode            : {0}", NetUtils.UpdogMode ? "ON" : "OFF");
            Console.WriteLine();
            PrintUsage();
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  netopts # show current settings");
            Console.WriteLine("  netopts ua <string...>");
            Console.WriteLine("  netopts proxy off|system|<url>");
            Console.WriteLine("  netopts proxy-cred <user> <password> | clear");
            Console.WriteLine("  netopts timeout <milliseconds>");
            Console.WriteLine("  netopts insecure on|off");
            Console.WriteLine("  netopts redirect on|off");
            Console.WriteLine("  netopts decompress on|off");
            Console.WriteLine("  netopts updog_mode on|off");
        }

        private static bool ParseOnOff(string s, out bool value)
        {
            value = false;
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("1", StringComparison.OrdinalIgnoreCase))
            { value = true; return true; }
            if (s.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("0", StringComparison.OrdinalIgnoreCase))
            { value = false; return true; }
            return false;
        }
    }
}
