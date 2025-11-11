// Utils/JobController.cs
using System.Threading;

namespace BusiestBox.Utils
{
    internal static class JobController
    {
        private static readonly object _lock = new object();
        private static Thread _fg;

        public static void RegisterForeground(Thread t)
        {
            lock (_lock) _fg = t;
        }

        public static void UnregisterForeground()
        {
            lock (_lock) _fg = null;
        }

        public static bool CancelForeground()
        {
            lock (_lock)
            {
                if (_fg == null) return false;
                try
                {
#pragma warning disable 618
                    _fg.Abort();
#pragma warning restore 618
                    return true;
                }
                catch { return false; }
            }
        }
    }
}
