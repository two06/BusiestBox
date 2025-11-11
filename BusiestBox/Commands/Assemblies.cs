using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BusiestBox.Commands
{
    internal class Assemblies
    {
        public static void Execute()
        {
            try
            {
                var asms = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .ToArray();

                // prepare rows
                var rows = new System.Collections.Generic.List<Row>();
                for (int i = 0; i < asms.Length; i++)
                {
                    var a = asms[i];
                    var an = SafeGetName(a);
                    string name = (an != null && an.Name != null) ? an.Name : "(unknown)";
                    string version = (an != null && an.Version != null) ? an.Version.ToString() : "N/A";
                    string displayName = version != "N/A" ? (name + " (" + version + ")") : name;

                    string mvidShort = "";
                    try
                    {
                        string m = a.ManifestModule.ModuleVersionId.ToString();
                        int dash = m.IndexOf('-');
                        mvidShort = (dash > 0 ? m.Substring(0, dash) : m);
                    }
                    catch { mvidShort = "????????"; }

                    string location;
                    long size = -1;
                    try
                    {
                        location = a.Location;
                        if (!string.IsNullOrEmpty(location) && File.Exists(location))
                        {
                            try { size = new FileInfo(location).Length; } catch { size = -1; }
                        }
                        else
                        {
                            location = "[dynamic]";
                            size = -1;
                        }
                    }
                    catch
                    {
                        location = "<Unknown>";
                        size = -1;
                    }

                    rows.Add(new Row { Name = name, DisplayName = displayName, ShortMvid = mvidShort, Size = size, Location = location });
                }

                // assign instance index per (Name, ShortMvid)
                var seen = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < rows.Count; i++)
                {
                    string key = rows[i].Name + "|" + rows[i].ShortMvid;
                    int idx;
                    if (!seen.TryGetValue(key, out idx)) idx = 0;
                    idx++;
                    seen[key] = idx;
                    rows[i].Instance = idx;
                }

                // compute column widths
                int nameCol = "Assembly Name".Length;
                int mvidCol = "ShortMVID".Length;
                int instCol = "Inst".Length;

                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].DisplayName != null && rows[i].DisplayName.Length > nameCol) nameCol = rows[i].DisplayName.Length;
                    if (rows[i].ShortMvid != null && rows[i].ShortMvid.Length > mvidCol) mvidCol = rows[i].ShortMvid.Length;
                    if (rows[i].Instance.ToString().Length > instCol) instCol = rows[i].Instance.ToString().Length;
                }

                long maxSize = 0;
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i].Size > maxSize) maxSize = rows[i].Size;
                int sizeCol = Math.Max("Size".Length, maxSize.ToString().Length);

                Console.WriteLine();
                Console.WriteLine("  Assemblies loaded in current process");
                Console.WriteLine();
                Console.WriteLine(
                    "Assembly Name".PadRight(nameCol) + "   " +
                    "ShortMVID".PadRight(mvidCol) + "   " +
                    "Inst".PadLeft(instCol) + "   " +
                    "Size".PadLeft(sizeCol) + "   " +
                    "Filepath"
                );
                Console.WriteLine(
                    new string('=', nameCol) + "   " +
                    new string('=', mvidCol) + "   " +
                    new string('=', instCol) + "   " +
                    new string('=', sizeCol) + "   " +
                    new string('=', 8)
                );

                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    string sizeStr = (r.Size < 0) ? new string('.', sizeCol) : r.Size.ToString().PadLeft(sizeCol);
                    Console.WriteLine(
                        (r.DisplayName ?? "").PadRight(nameCol) + "   " +
                        (r.ShortMvid ?? "").PadRight(mvidCol) + "   " +
                        r.Instance.ToString().PadLeft(instCol) + "   " +
                        sizeStr + "   " +
                        (r.Location ?? "")
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] Error listing assemblies: " + ex.Message);
            }
        }

        private sealed class Row
        {
            public string Name;
            public string DisplayName;
            public string ShortMvid;
            public int Instance;
            public long Size;
            public string Location;
        }

        private static AssemblyName SafeGetName(Assembly a)
        {
            try { return a.GetName(); } catch { return null; }
        }

    }
}
