using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

using BusiestBox.Utils;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Invoke
    {
        // Overload used by Program.cs – we keep this signature, but now pass currentDirectory through
        public static void Execute(string currentDirectory, params string[] args)
        {
            ExecuteImpl(currentDirectory, args);
        }

        // Convenience overload – still present for any callers that don't pass currentDirectory
        public static void Execute(params string[] args)
        {
            ExecuteImpl(Directory.GetCurrentDirectory(), args);
        }

        private static void ExecuteImpl(string currentDirectory, string[] args)
        {
            if (args == null || args.Length == 0) { PrintUsage(); return; }

            string selector = null;       // assembly selector (simple name or short MVID[#N])
            string methodName = "Main";   // default method
            string typeName = null;       // optional type constraint
            string outFileRaw = null;     // optional tee destination (FS or vfs://)
            bool runInBackground = false;

            // Parse flags until we hit the selector
            int i = 0;
            for (; i < args.Length; i++)
            {
                string tok = args[i];

                // First non-flag token becomes the selector; stop parsing flags
                if (!tok.StartsWith("--", StringComparison.Ordinal))
                {
                    selector = tok;
                    i++; // step past selector; remaining tokens are method args
                    break;
                }

                string flag = tok;
                string val = null;
                int eq = tok.IndexOf('=');
                if (eq > 2) { flag = tok.Substring(0, eq); val = tok.Substring(eq + 1); }

                switch (flag.ToLowerInvariant())
                {
                    case "--bg":
                    case "--background":
                        runInBackground = true;
                        break;

                    case "--method":
                        if (val == null)
                        {
                            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                            {
                                Console.WriteLine("[!] invoke: --method requires a value");
                                return;
                            }
                            val = args[++i];
                        }
                        methodName = val;
                        break;

                    case "--type":
                        if (val == null)
                        {
                            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                            {
                                Console.WriteLine("[!] invoke: --type requires a value");
                                return;
                            }
                            val = args[++i];
                        }
                        typeName = val;
                        break;

                    case "--outfile":
                        if (val == null)
                        {
                            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                            {
                                Console.WriteLine("[!] invoke: --outfile requires a path");
                                return;
                            }
                            val = args[++i];
                        }
                        outFileRaw = val;
                        break;

                    default:
                        Console.WriteLine("[!] invoke: unknown option {0}", flag);
                        return;
                }
            }

            if (string.IsNullOrEmpty(selector))
            {
                PrintUsage();
                return;
            }

            string[] methodArgs = (i < args.Length) ? args.Skip(i).ToArray() : Array.Empty<string>();

            try
            {
                Assembly asm = ResolveLoadedAssembly(selector);
                if (asm == null)
                {
                    Console.WriteLine("[!] invoke: Could not resolve assembly from: " + selector);
                    Console.WriteLine("Hint: run 'assemblies' to see names and short MVIDs.");
                    return;
                }

                if (runInBackground)
                {
                    string capType = typeName;
                    string capMethod = methodName;
                    string[] capArgs = methodArgs;
                    string capCwd = currentDirectory;
                    string capOut = outFileRaw;

                    int jobId = 0; // declare first

                    jobId = JobManager.StartBackgroundJob(
                        JobType.Assembly,
                        selector,
                        capOut,
                        () =>
                        {
                            int capturedJobId = jobId;

                            using (CreateOutTee(capOut, capCwd))
                            {
                                Console.WriteLine("[bg {0}] starting {1}::{2}", capturedJobId, capType ?? "(search)", capMethod);
                                RunInvocation(asm, capType, capMethod, capArgs, "[bg " + capturedJobId + "] ");
                                Console.WriteLine("[bg {0}] done", capturedJobId);
                            }
                        }
                    );

                    Console.WriteLine("[*] invoke scheduled in background (job {0})", jobId);
                    return;
                }

                RunForegroundInvocation(asm, selector, typeName, methodName, methodArgs, outFileRaw, currentDirectory);
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException;
                Console.WriteLine("[!] invoke: Method threw " + (inner != null ? inner.GetType().Name + ": " + inner.Message : ex.Message));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] invoke: " + ex.Message);
            }
        }

        // ---------- Foreground wrapper (Ctrl+C cancellable via JobManager) ----------
        private static void RunForegroundInvocation(Assembly asm, string selector, string typeName, string methodName, string[] methodArgs, string outFileRaw, string currentDirectory)
        {
            var done = new ManualResetEvent(false);
            Exception captured = null;

            var worker = new Thread(delegate ()
            {
                try
                {
                    using (CreateOutTee(outFileRaw, currentDirectory))
                    {
                        RunInvocation(asm, typeName, methodName, methodArgs, null);
                    }
                }
#pragma warning disable 618
                catch (ThreadAbortException)
                {
                    try { Thread.ResetAbort(); } catch { }
                    Console.WriteLine("[!] Invocation aborted by Ctrl+C.");
                }
#pragma warning restore 618
                catch (Exception ex)
                {
                    captured = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            worker.IsBackground = true;
            try { worker.SetApartmentState(ApartmentState.MTA); } catch { }

            string targetDisplay = selector;
            if (!string.Equals(methodName, "Main", StringComparison.OrdinalIgnoreCase))
                targetDisplay = selector + "::" + methodName;

            int jobId = JobManager.RegisterForegroundJob(
                JobType.Assembly,
                targetDisplay,
                worker,
                outFileRaw
            );

            worker.Start();
            done.WaitOne();

            JobManager.MarkJobCompleted(jobId);
            JobManager.UnregisterForegroundJob();

            if (captured != null)
                Console.WriteLine("[!] invoke: " + captured.Message);
        }

        // ---------- Core invocation wrapper ----------
        private static void RunInvocation(Assembly asm, string typeName, string methodName, string[] methodArgs, string prefix)
        {
            bool ok = !string.IsNullOrEmpty(typeName)
                ? InvokeOnType(asm, typeName, methodName, methodArgs, prefix)
                : InvokeMethodAcrossTypes(asm, methodName, methodArgs, prefix);

            if (!ok)
                WriteLine(prefix, "[!] invoke: Method '" + methodName + "' not found or not invocable.");
        }

        // ---------- Assembly selection ----------
        private static Assembly ResolveLoadedAssembly(string selector)
        {
            if (string.IsNullOrEmpty(selector)) return null;

            string token = selector;
            int wantedIndex = -1;
            int hashPos = selector.LastIndexOf('#');
            if (hashPos > 0 && hashPos < selector.Length - 1)
            {
                string idxStr = selector.Substring(hashPos + 1);
                if (int.TryParse(idxStr, out int idx) && idx >= 1)
                {
                    wantedIndex = idx;
                    token = selector.Substring(0, hashPos);
                }
            }

            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            var matches = new System.Collections.Generic.List<Assembly>();

            if (IsShortHex(token))
            {
                for (int i = 0; i < loaded.Length; i++)
                {
                    try
                    {
                        string mvid = loaded[i].ManifestModule.ModuleVersionId.ToString();
                        int dash = mvid.IndexOf('-');
                        string shortId = dash > 0 ? mvid.Substring(0, dash) : mvid;
                        if (string.Equals(shortId, token, StringComparison.OrdinalIgnoreCase))
                            matches.Add(loaded[i]);
                    }
                    catch { }
                }
            }
            else
            {
                for (int i = 0; i < loaded.Length; i++)
                {
                    var an = SafeGetName(loaded[i]);
                    if (an != null && string.Equals(an.Name, token, StringComparison.OrdinalIgnoreCase))
                        matches.Add(loaded[i]);
                }
            }

            if (matches.Count == 0) return null;
            if (wantedIndex >= 1) return wantedIndex <= matches.Count ? matches[wantedIndex - 1] : null;
            return matches[matches.Count - 1];
        }

        // ---------- Invocation helpers ----------
        private static bool InvokeMethodAcrossTypes(Assembly asm, string methodName, string[] args, string prefix)
        {
            var types = SafeGetTypes(asm);
            for (int i = 0; i < types.Length; i++)
            {
                var t = types[i];
                var m = FindBestMethod(t, methodName, args);
                if (m != null && TryInvoke(t, m, args, prefix))
                    return true;
            }
            return false;
        }

        private static bool InvokeOnType(Assembly asm, string typeName, string methodName, string[] args, string prefix)
        {
            var t = asm.GetType(typeName, false, true);
            if (t == null)
            {
                var all = SafeGetTypes(asm);
                for (int i = 0; i < all.Length; i++)
                {
                    if (string.Equals(all[i].FullName, typeName, StringComparison.OrdinalIgnoreCase))
                    { t = all[i]; break; }
                }
            }
            if (t == null) return false;

            var m = FindBestMethod(t, methodName, args);
            if (m == null) return false;

            return TryInvoke(t, m, args, prefix);
        }

        private static MethodInfo FindBestMethod(Type t, string methodName, string[] args)
        {
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static);

            MethodInfo[] named = new MethodInfo[methods.Length];
            int n = 0;
            for (int i = 0; i < methods.Length; i++)
                if (string.Equals(methods[i].Name, methodName, StringComparison.Ordinal))
                    named[n++] = methods[i];

            if (n == 0) return null;

            for (int i = 0; i < n; i++)
            {
                var p = named[i].GetParameters();
                if (p.Length == 1 && p[0].ParameterType == typeof(string[]))
                    return named[i];
            }
            for (int i = 0; i < n; i++)
            {
                var p = named[i].GetParameters();
                if (p.Length == 0)
                    return named[i];
            }
            for (int i = 0; i < n; i++)
            {
                var p = named[i].GetParameters();
                if (p.Length == args.Length && AllStringParams(p))
                    return named[i];
            }

            return named[0];
        }

        private static bool TryInvoke(Type t, MethodInfo m, string[] args, string prefix)
        {
            object instance = null;
            if (!m.IsStatic)
            {
                if (t.IsAbstract) return false;
                try { instance = Activator.CreateInstance(t); } catch { return false; }
            }

            var pars = m.GetParameters();
            object result = null;

            if (pars.Length == 1 && pars[0].ParameterType == typeof(string[]))
            {
                WriteLine(prefix, "[*] Invoking " + t.FullName + "." + m.Name + "(string[] args)");
                result = m.Invoke(instance, new object[] { args });
            }
            else if (pars.Length == 0)
            {
                WriteLine(prefix, "[*] Invoking " + t.FullName + "." + m.Name + "()");
                result = m.Invoke(instance, new object[0]);
            }
            else if (pars.Length == args.Length && AllStringParams(pars))
            {
                WriteLine(prefix, "[*] Invoking " + t.FullName + "." + m.Name + "(" + args.Length + " string args)");
                object[] callArgs = new object[args.Length];
                for (int i = 0; i < args.Length; i++) callArgs[i] = args[i];
                result = m.Invoke(instance, callArgs);
            }
            else
            {
                return false;
            }

            if (m.ReturnType != typeof(void))
                WriteLine(prefix, result != null ? result.ToString() : "(null)");

            return true;
        }

        // ---------- Tee (Console.Out -> console + file/VFS) ----------
        private sealed class OutTeeScope : IDisposable
        {
            private readonly TextWriter _original;
            private readonly TextWriter _fileWriter;
            private readonly string _vfsTarget;
            private readonly StringWriter _vfsBuffer;

            public OutTeeScope(TextWriter original, TextWriter fileWriter, string vfsTarget, StringWriter vfsBuffer)
            {
                _original = original;
                _fileWriter = fileWriter;
                _vfsTarget = vfsTarget;
                _vfsBuffer = vfsBuffer;
            }

            public void Dispose()
            {
                try { Console.Out.Flush(); } catch { }
                Console.SetOut(_original);

                if (_fileWriter != null)
                {
                    try { _fileWriter.Flush(); } catch { }
                    try { _fileWriter.Dispose(); } catch { }
                }

                if (!string.IsNullOrEmpty(_vfsTarget) && _vfsBuffer != null)
                {
                    try
                    {
                        EnsureParentDirForResolvedPath(_vfsTarget);
                        VfsLayer.WriteAllText(_vfsTarget, _vfsBuffer.ToString(), Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        try { Console.Error.WriteLine("[!] invoke: failed writing outfile: " + ex.Message); } catch { }
                    }
                }
            }
        }

        private static OutTeeScope CreateOutTee(string outFileRaw, string currentDirectory)
        {
            if (string.IsNullOrEmpty(outFileRaw))
                return new OutTeeScope(Console.Out, null, null, null);

            string resolved;
            try
            {
                resolved = VfsLayer.ResolvePath(currentDirectory, outFileRaw);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] invoke: cannot resolve outfile '{0}': {1}", outFileRaw, ex.Message);
                return new OutTeeScope(Console.Out, null, null, null);
            }

            var originalOut = Console.Out;

            if (resolved.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
            {
                var vfsBuffer = new StringWriter(new StringBuilder());
                try { EnsureParentDirForResolvedPath(resolved); } catch { }

                Console.SetOut(TextWriter.Synchronized(new DelegatingWriter(
                    c => { originalOut.Write(c); vfsBuffer.Write(c); },
                    () => { try { originalOut.Flush(); } catch { } try { vfsBuffer.Flush(); } catch { } }
                )));

                return new OutTeeScope(originalOut, null, resolved, vfsBuffer);
            }

            try
            {
                EnsureParentDirForResolvedPath(resolved);
                var sw = new StreamWriter(resolved, append: true, Encoding.UTF8) { AutoFlush = true };

                Console.SetOut(TextWriter.Synchronized(new DelegatingWriter(
                    c => { originalOut.Write(c); sw.Write(c); },
                    () => { try { originalOut.Flush(); } catch { } try { sw.Flush(); } catch { } }
                )));

                return new OutTeeScope(originalOut, sw, null, null);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] Could not redirect output to file: {0}", resolved);
                Console.Error.WriteLine(ex.Message);
                return new OutTeeScope(originalOut, null, null, null);
            }
        }

        private static void EnsureParentDirForResolvedPath(string resolved)
        {
            if (resolved.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
            {
                string p = resolved.Substring("vfs://".Length).TrimEnd('/');
                int idx = p.LastIndexOf('/');
                string parent = (idx > 0) ? ("vfs://" + p.Substring(0, idx)) : "vfs://";
                VfsLayer.CreateDirectory(parent);
            }
            else
            {
                string dir = Path.GetDirectoryName(resolved);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
        }

        private sealed class DelegatingWriter : TextWriter
        {
            private readonly Action<char> _writeChar;
            private readonly Action _flush;

            public DelegatingWriter(Action<char> writeChar, Action flush)
            {
                _writeChar = writeChar;
                _flush = flush;
            }

            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value) => _writeChar(value);
            public override void Flush() => _flush();
        }

        private static void WriteLine(string prefix, string text)
        {
            if (prefix == null) Console.WriteLine(text);
            else Console.WriteLine(prefix + text);
        }

        private static AssemblyName SafeGetName(Assembly a)
        {
            try { return a.GetName(); } catch { return null; }
        }

        private static Type[] SafeGetTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e)
            {
                var list = e.Types;
                if (list == null) return new Type[0];
                int count = 0;
                for (int i = 0; i < list.Length; i++) if (list[i] != null) count++;
                var arr = new Type[count];
                int j = 0;
                for (int i = 0; i < list.Length; i++) if (list[i] != null) arr[j++] = list[i];
                return arr;
            }
            catch { return new Type[0]; }
        }

        private static bool AllStringParams(ParameterInfo[] p)
        {
            for (int i = 0; i < p.Length; i++)
                if (p[i].ParameterType != typeof(string)) return false;
            return true;
        }

        private static bool IsShortHex(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Length < 6 || s.Length > 12) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool hex = (c >= '0' && c <= '9') ||
                           (c >= 'a' && c <= 'f') ||
                           (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  invoke [--outfile <path>] [--method <name>] [--type <type>] [--bg] <assembly_name|short_mvid[#N]> [args...]");
            Console.WriteLine("Notes:");
            Console.WriteLine("  • All options must come BEFORE the selector; everything AFTER the selector is passed to the invoked method.");
            Console.WriteLine("  • Selector may be a simple name or short MVID (first GUID chunk). Use '#N' to pick the Nth match.");
            Console.WriteLine("  • Default method: Main");
        }
    }
}
