using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace BusiestBox.Utils
{
    internal class ZipSupport
    {
        public static bool ZipSupported()
        {
            try
            {
                // Try common identities for ZipArchive
                Type zipType =
                    Type.GetType("System.IO.Compression.ZipArchive", false) ??
                    Type.GetType("System.IO.Compression.ZipArchive, System.IO.Compression", false) ??
                    Type.GetType("System.IO.Compression.ZipArchive, System.IO.Compression, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", false);

                // If not found, try to load the assembly explicitly and retry
                if (zipType == null)
                {
                    try { Assembly.Load("System.IO.Compression"); } catch { /* ignore */ }
                    zipType =
                        Type.GetType("System.IO.Compression.ZipArchive", false) ??
                        Type.GetType("System.IO.Compression.ZipArchive, System.IO.Compression", false);
                }

                if (zipType == null) return false;

                // Get the ZipArchiveMode enum from the same assembly
                Type modeType = zipType.Assembly.GetType("System.IO.Compression.ZipArchiveMode", throwOnError: false);
                if (modeType == null) return false;

                // Prefer ctor(Stream, ZipArchiveMode, bool), else use the 4-arg overload with Encoding
                ConstructorInfo ctor3 = zipType.GetConstructor(new[] { typeof(Stream), modeType, typeof(bool) });
                ConstructorInfo ctor4 = (ctor3 == null)
                    ? zipType.GetConstructor(new[] { typeof(Stream), modeType, typeof(bool), typeof(Encoding) })
                    : null;

                if (ctor3 == null && ctor4 == null) return false;

                // Try to instantiate with Mode=Create over a MemoryStream
                object modeCreate = Enum.Parse(modeType, "Create", ignoreCase: true);
                using (var ms = new MemoryStream())
                {
                    object za = (ctor3 != null)
                        ? ctor3.Invoke(new object[] { ms, modeCreate, true })
                        : ctor4.Invoke(new object[] { ms, modeCreate, true, Encoding.UTF8 });

                    (za as IDisposable)?.Dispose();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
