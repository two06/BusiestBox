using System;

namespace BusiestBox.Coff
{
    /// <summary>
    /// Minimal validator for COFF object files used by BOFs.
    /// Validates (a) file looks like a COFF object (not PE, not LIB/archive),
    /// and (b) architecture matches the current process (x86 vs x64).
    /// </summary>
    internal static class Validator
    {
        public enum CoffArch
        {
            Unknown = 0,
            X86,
            X64,
            Arm64,
            Other
        }

        // COFF "Machine" values
        private const ushort IMAGE_FILE_MACHINE_I386 = 0x014c; // x86
        private const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664; // x64
        private const ushort IMAGE_FILE_MACHINE_ARM64 = 0xAA64; // arm64

        /// <summary>
        /// Validate bytes look like a COFF object file (not PE, not archive).
        /// Returns false with a reason if not.
        /// </summary>
        public static bool TryValidateCoff(byte[] data, out CoffArch arch, out string reason)
        {
            arch = CoffArch.Unknown;
            reason = null;

            if (data == null || data.Length < 20)
            {
                reason = "File too small to be a COFF object.";
                return false;
            }

            // Reject PE/EXE/DLL (those begin with "MZ")
            if (data.Length >= 2 && data[0] == (byte)'M' && data[1] == (byte)'Z')
            {
                reason = "This is a PE image (MZ), not a COFF object.";
                return false;
            }

            // Reject UNIX archive (.lib) – starts with "!<arch>\n"
            if (data.Length >= 8 &&
                data[0] == (byte)'!' && data[1] == (byte)'<' && data[2] == (byte)'a' && data[3] == (byte)'r' &&
                data[4] == (byte)'c' && data[5] == (byte)'h' && data[6] == (byte)'>' && data[7] == (byte)'\n')
            {
                reason = "This is an archive (.lib), not a single COFF object.";
                return false;
            }

            // A COFF object begins directly with IMAGE_FILE_HEADER (20 bytes)
            // WORD Machine; WORD NumberOfSections; DWORD TimeDateStamp; DWORD PointerToSymbolTable;
            // DWORD NumberOfSymbols; WORD SizeOfOptionalHeader; WORD Characteristics;
            ushort machine = ReadU16(data, 0);
            ushort numSecs = ReadU16(data, 2);
            // ushort sizeOpt = ReadU16(data, 16); // usually 0 for objects, but not strictly required

            if (numSecs == 0 || numSecs > 1024)
            {
                reason = "Invalid COFF header: unreasonable section count.";
                return false;
            }

            arch = MapMachine(machine);
            if (arch == CoffArch.Other || arch == CoffArch.Unknown)
            {
                reason = $"Unsupported COFF machine value 0x{machine:X4}.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validate COFF object and ensure its architecture matches the current process.
        /// </summary>
        public static bool ValidateMatchesCurrentProcess(byte[] data, out CoffArch fileArch, out string reason)
        {
            if (!TryValidateCoff(data, out fileArch, out reason))
                return false;

            bool isProc64 = Environment.Is64BitProcess;
            switch (fileArch)
            {
                case CoffArch.X64:
                    if (!isProc64) { reason = "x64 COFF cannot run in a 32-bit process."; return false; }
                    return true;

                case CoffArch.X86:
                    if (isProc64) { reason = "x86 COFF cannot run in a 64-bit process."; return false; }
                    return true;

                case CoffArch.Arm64:
                    reason = "ARM64 COFF is not supported by this loader.";
                    return false;

                default:
                    reason = "Unknown/unsupported COFF architecture.";
                    return false;
            }
        }

        public static string ArchToString(CoffArch a)
        {
            switch (a)
            {
                case CoffArch.X86: return "x86";
                case CoffArch.X64: return "x64";
                case CoffArch.Arm64: return "arm64";
                default: return "unknown";
            }
        }

        private static CoffArch MapMachine(ushort machine)
        {
            if (machine == IMAGE_FILE_MACHINE_I386) return CoffArch.X86;
            if (machine == IMAGE_FILE_MACHINE_AMD64) return CoffArch.X64;
            if (machine == IMAGE_FILE_MACHINE_ARM64) return CoffArch.Arm64;
            return CoffArch.Other;
        }

        private static ushort ReadU16(byte[] d, int o)
            => (ushort)(d[o] | (d[o + 1] << 8));
    }
}
