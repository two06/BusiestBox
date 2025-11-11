using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Security.Principal;
using System.Runtime.InteropServices;

namespace BusiestBox.Commands
{
    internal static class HostInfo
    {
        // Usage:
        //   hostinfo            -> prints host details
        //   hostinfo <id>       -> same, but includes "ID: <id>" (handy if you want to tag the record)
        public static void Execute()
        {
            string hostname = Safe(() => Environment.MachineName, "unknown");
            string os = Safe(() => Environment.OSVersion.VersionString, "unknown");
            string ipaddr = Safe(GetLocalIPv4Summary, "n/a");
            bool elevated = Safe(High, false);
            string user = Safe(() => WindowsIdentity.GetCurrent().Name, "unknown");
            string pid = Safe(() => Process.GetCurrentProcess().Id.ToString(), "n/a");
            string procPath = Safe(GetProcessPath, "n/a");
            string arch = Safe(GetProcessArch, Environment.Is64BitProcess ? "x64" : "x86");

            Console.WriteLine("Hostname        : {0}", hostname);
            Console.WriteLine("User            : {0}{1}", elevated ? "*" : "", user);
            Console.WriteLine("PID             : {0}", pid);
            Console.WriteLine("Process         : {0}", procPath);
            Console.WriteLine("Process Arch    : {0}", arch);
            Console.WriteLine("OS              : {0}", os);
            Console.WriteLine("IP(s)           : {0}", ipaddr);
        }

        // ----- helpers -----

        private static T Safe<T>(Func<T> f, T fallback)
        {
            try { return f(); } catch { return fallback; }
        }

        private static string GetProcessPath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule.FileName;
            }
            catch
            {
                // Fallback if MainModule is restricted
                return Process.GetCurrentProcess().ProcessName;
            }
        }

        private static string GetProcessArch()
        {
            try
            {
                // Prefer RuntimeInformation when available
                return RuntimeInformation.ProcessArchitecture.ToString();
            }
            catch
            {
                return Environment.Is64BitProcess ? "X64" : "X86";
            }
        }

        private static bool High()
        {
            try
            {
                var wi = WindowsIdentity.GetCurrent();
                var wp = new WindowsPrincipal(wi);
                return wp.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static string GetLocalIPv4Summary()
        {
            try
            {
                var addrs =
                    NetworkInterface.GetAllNetworkInterfaces()
                        .Where(nic =>
                            nic.OperationalStatus == OperationalStatus.Up &&
                            nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                        .Where(ua =>
                            ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(ua.Address) &&
                            !ua.Address.ToString().StartsWith("169.254."))  // skip APIPA
                        .Select(ua => ua.Address.ToString())
                        .Distinct()
                        .ToArray();

                return addrs.Length > 0 ? string.Join(", ", addrs) : "n/a";
            }
            catch
            {
                // Simple DNS fallback
                try
                {
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    var ips = host.AddressList
                        .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(a => a.ToString())
                        .ToArray();
                    return ips.Length > 0 ? string.Join(", ", ips) : "n/a";
                }
                catch { return "n/a"; }
            }
        }
    }
}
