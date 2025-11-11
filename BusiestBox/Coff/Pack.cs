using System;
using System.IO;
using System.Text;

namespace BusiestBox.Coff
{
    /// <summary>
    /// Generic argument packer.
    /// Format chars:
    ///   'b' : binary blob (byte[] or path string)  -> writes [u32 lenIncludingTerminator][data][0x00]
    ///   'i' : int32 (little-endian)
    ///   's' : int16 (little-endian)
    ///   'z' : UTF-8 string (zero-terminated)       -> [u32 lenIncludingTerminator][utf8][0x00]
    ///   'Z' : UTF-16LE string (zero-terminated)    -> [u32 lenIncludingTerminator][utf16le][0x00,0x00]
    /// Finally, the whole payload is prefixed with a 32-bit little-endian total length.
    /// </summary>
    public static class Pack
    {
        public static byte[] PackArgs(string format, params object[] args)
        {
            if (format == null) throw new ArgumentNullException(nameof(format));
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (format.Length != args.Length)
                throw new ArgumentException(
                    $"Format length must match args length (format={format.Length}, args={args.Length}).");

            using (var payload = new MemoryStream())
            using (var bw = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
            {
                for (int i = 0; i < format.Length; i++)
                {
                    char f = format[i];
                    object a = args[i];

                    switch (f)
                    {
                        case 'i':
                            bw.Write(ConvertToInt32(a));
                            break;

                        case 's':
                            bw.Write(ConvertToInt16(a));
                            break;

                        case 'z':
                            {
                                byte[] utf8 = ToUtf8Bytes(a);
                                uint lenWithTerm = (uint)(utf8.Length + 1);
                                bw.Write(lenWithTerm);
                                bw.Write(utf8);
                                bw.Write((byte)0x00); // NUL terminator
                                break;
                            }

                        case 'Z':
                            {
                                byte[] u16 = ToUtf16LeBytes(a);
                                uint lenWithTerm = (uint)(u16.Length + 2);
                                bw.Write(lenWithTerm);
                                bw.Write(u16);
                                bw.Write((byte)0x00); // 2-byte NUL terminator (LE)
                                bw.Write((byte)0x00);
                                break;
                            }

                        case 'b':
                            {
                                byte[] blob = ToBinaryBytes(a);
                                uint lenWithTerm = (uint)(blob.Length + 1);
                                bw.Write(lenWithTerm);
                                bw.Write(blob);
                                bw.Write((byte)0x00); // trailing pad
                                break;
                            }

                        default:
                            // point a caret at the bad char, similar to your Python error
                            string msg = "Invalid character in format string: " + format + Environment.NewLine +
                                         new string(' ', "Invalid character in format string: ".Length + i) + "^";
                            throw new ArgumentException(msg);
                    }
                }

                // Prepend total size (u32, little-endian)
                bw.Flush();
                byte[] body = payload.ToArray();
                using (var result = new MemoryStream(4 + body.Length))
                using (var header = new BinaryWriter(result, Encoding.UTF8, leaveOpen: true))
                {
                    header.Write((uint)body.Length);
                    header.Write(body);
                    header.Flush();
                    return result.ToArray();
                }
            }
        }

        // ---- helpers ----

        private static int ConvertToInt32(object v)
        {
            if (v == null) return 0;
            if (v is int i) return i;
            if (v is short s) return (int)s;
            if (v is long l) return checked((int)l);
            if (v is uint ui) return checked((int)ui);
            if (v is ushort us) return us;
            if (v is byte b) return b;
            if (v is string str) return int.Parse(str.Trim());
            return Convert.ToInt32(v);
        }

        private static short ConvertToInt16(object v)
        {
            if (v == null) return 0;
            if (v is short s) return s;
            if (v is int i) return checked((short)i);
            if (v is long l) return checked((short)l);
            if (v is ushort us) return checked((short)us);
            if (v is byte b) return b;
            if (v is string str) return short.Parse(str.Trim());
            return Convert.ToInt16(v);
        }

        private static byte[] ToUtf8Bytes(object v)
        {
            if (v == null) return Array.Empty<byte>();
            if (v is byte[] b) return b; // if user passed bytes already
            return Encoding.UTF8.GetBytes(v.ToString());
        }

        private static byte[] ToUtf16LeBytes(object v)
        {
            if (v == null) return Array.Empty<byte>();
            if (v is byte[] b) return b; // if user passed UTF-16LE bytes already
            return Encoding.Unicode.GetBytes(v.ToString()); // UTF-16 LE in .NET
        }

        /// <summary>
        /// Accepts byte[], Stream, or a file path string; otherwise uses UTF-8 bytes of ToString().
        /// </summary>
        private static byte[] ToBinaryBytes(object v)
        {
            if (v == null) return Array.Empty<byte>();

            if (v is byte[] b) return b;

            if (v is Stream s)
            {
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }

            if (v is string path)
            {
                try
                {
                    if (File.Exists(path))
                        return File.ReadAllBytes(path);
                }
                catch
                {
                    // fall through to treat as raw text
                }
                return Encoding.UTF8.GetBytes(path);
            }

            return Encoding.UTF8.GetBytes(v.ToString());
        }
    }
}
