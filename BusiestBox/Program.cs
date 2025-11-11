using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;

using BusiestBox.Commands;
using BusiestBox.Utils;
using BusiestBox.UI;

namespace BusiestBox
{
    public class Program
    {
        static int lastRenderLen = 0;

        // History (used by TabComplete)
        static List<string> CommandHistory = new List<string>();
        static int historyIndex = -1;

        static int _handlingCtrlC = 0;

        static readonly bool ZipAvailable = ZipSupport.ZipSupported();

        public static void Main(string[] args)
        {
            CompletionRegistry.Register(new CommandCompletion());
            CompletionRegistry.Register(new NetOptsCompletion());
            CompletionRegistry.Register(new Base64Completion());
            CompletionRegistry.Register(new FilePathCompletion());

            Console.TreatControlCAsInput = false;

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true; // don't exit process

                if (Interlocked.Exchange(ref _handlingCtrlC, 1) == 1)
                    return;

                try
                {
                    bool cancelled = JobManager.CancelForeground(); // ✅ replaced JobController

                    if (cancelled)
                    {
                        Console.WriteLine("\n[*] Foreground job canceled via Ctrl+C.");
                    }
                    else
                    {
                        Console.WriteLine("\n[*] Ctrl+C detected. Use 'exit' to quit!");
                    }

                    TabComplete.NotifyExternalInterrupt(); // redraw prompt on next key
                }
                finally
                {
                    Interlocked.Exchange(ref _handlingCtrlC, 0);
                }
            };

            string currentDirectory = Directory.GetCurrentDirectory();

            while (true)
            {
                string prompt = "BusiestBox [" + PathUtils.ToPromptPath(currentDirectory) + "]> ";
                WritePrompt(prompt);

                string input = TabComplete.ReadLineWithTabCompletion(
                    currentDirectory,
                    prompt,
                    CommandCompletion.Commands,
                    CommandHistory,
                    ref historyIndex,
                    ref lastRenderLen);

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                var parts = TabComplete.SplitArgsPreservingQuotes(input.Trim());
                var command = parts[0].ToLower();
                var argsOnly = parts.Skip(1).ToArray();

                var expanded = Glob.ExpandArgs(currentDirectory, argsOnly);

                // Shorthand: !command
                if (input.StartsWith("!"))
                {
                    Shell.ExecuteShorthand(input, currentDirectory);
                    continue;
                }

                switch (command)
                {
                    case "cd":
                    var target = (expanded.Length < 1 || string.IsNullOrWhiteSpace(expanded[0]))
                        ? "~"
                        : expanded[0];
                    Cd.Execute(ref currentDirectory, target);
                    break;

                    case "pwd":
                        Pwd.Execute(currentDirectory);
                        break;

                    case "ls":
                        if (argsOnly.Length < 1)
                            Ls.Execute(currentDirectory);
                        else
                            Ls.Execute(expanded[0]);
                        break;

                    case "help":
                        Help.Execute();
                        break;

                    case "copy":
                    case "cp":
                        if (expanded.Length < 2)
                        {
                            Console.WriteLine("Usage: copy [-r] <source1> [source2 ...] <destination>");
                            break;
                        }
                        try
                        {
                            Copy.Execute(currentDirectory, expanded);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[!] Copy failed: {ex.Message}");
                        }
                        break;

                    case "move":
                    case "mv":
                        if (expanded.Length < 2)
                        {
                            Console.WriteLine("Usage: mv [-r] <source1> [source2 ...] <destination>");
                            break;
                        }
                        try
                        {
                            Move.Execute(currentDirectory, expanded);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[!] Copy failed: {ex.Message}");
                        }
                        break;

                    case "mkdir":
                        Mkdir.Execute(currentDirectory, expanded);
                        break;

                    case "cat":
                        Cat.Execute(currentDirectory, expanded);
                        break;

                    case "rm":
                    case "del":
                        Rm.Execute(currentDirectory, expanded);
                        break;

                    case "hash":
                        // Keep raw args to preserve URLs; Hash expands FS/VFS tokens itself.
                        Hash.Execute(currentDirectory, argsOnly);
                        break;

                    case "assemblies":
                        Assemblies.Execute();
                        break;

                    case "jobs":
                        Jobs.Execute(argsOnly);
                        break;

                    case "load":
                        Load.Execute(currentDirectory, expanded);
                        break;

                    case "invoke":
                        try
                        {
                            if (argsOnly.Length == 0)
                            {
                                Console.WriteLine("Usage: invoke [--outfile <path>] [--method <name>] [--type <type>] [--bg] <assembly_name|short_mvid> [args...]");
                                break;
                            }
                            Invoke.Execute(currentDirectory, argsOnly);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[!] Error in invoke: " + ex.Message);
                        }
                        break;

                    case "netopts":
                        NetOpts.Execute(argsOnly);
                        break;

                    case "zip":
                        if (ZipAvailable)
                        {
                            if (expanded.Length < 2)
                            {
                                Console.WriteLine("Usage: zip [-r] <output.zip> <input1> [input2 ...]");
                                break;
                            }
                            try
                            {
                                Zip.Execute(currentDirectory, expanded);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("[!] zip failed: " + ex.Message);
                            }
                        }
                        else
                        {
                            Console.WriteLine("[!] Zip and Unzip disabled on this platform!");
                        }
                        break;

                    case "unzip":
                        if (ZipAvailable)
                        {
                            if (expanded.Length < 1)
                            {
                                Console.WriteLine("Usage: unzip <zipfile_or_url> [destination]");
                                break;
                            }
                            try
                            {
                                Unzip.Execute(currentDirectory, expanded);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("[!] unzip failed: " + ex.Message);
                            }
                        }
                        else
                        {
                            Console.WriteLine("[!] Zip and Unzip disabled on this platform!");
                        }
                        break;

                    case "encrypt":
                        try
                        {
                            if (argsOnly.Length != 3)
                            {
                                Console.WriteLine("Usage: encrypt <passphrase> <inputFile|url> <outputFile>");
                                break;
                            }

                            // Leave passphrase unexpanded
                            string passphrase = argsOnly[0];

                            // Expand only file arguments
                            var expandedFiles = Glob.ExpandArgs(currentDirectory, argsOnly.Skip(1).ToArray());
                            string inputFile = expandedFiles.Length > 0 ? expandedFiles[0] : argsOnly[1];
                            string outputFile = expandedFiles.Length > 1 ? expandedFiles[1] : argsOnly[2];

                            Encrypt.Execute(new string[] { passphrase, inputFile, outputFile });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[!] encrypt failed: " + ex.Message);
                        }
                        break;

                    case "decrypt":
                        try
                        {
                            if (argsOnly.Length != 3)
                            {
                                Console.WriteLine("Usage: decrypt <passphrase> <inputFile|url> <outputFile>");
                                break;
                            }

                            // Leave passphrase unexpanded
                            string passphrase = argsOnly[0];

                            // Expand only file arguments (input, output)
                            var expandedFiles = Glob.ExpandArgs(currentDirectory, argsOnly.Skip(1).ToArray());
                            string inputFile = expandedFiles.Length > 0 ? expandedFiles[0] : argsOnly[1];
                            string outputFile = expandedFiles.Length > 1 ? expandedFiles[1] : argsOnly[2];

                            Decrypt.Execute(new string[] { passphrase, inputFile, outputFile });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[!] decrypt failed: " + ex.Message);
                        }
                        break;

                    case "shell":
                        Shell.Execute(currentDirectory, parts.Skip(1).ToArray());
                        break;

                    case "bof":
                        try
                        {
                            // Do not glob-expand BOF args; Bof.Execute handles URLs/VFS/FS and packing itself.
                            Bof.Execute(currentDirectory, argsOnly);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[!] bof: " + ex.Message);
                        }
                        break;

                    case "upload":
                        {
                            if (argsOnly.Length < 2)
                            {
                                Upload.Execute(currentDirectory, argsOnly);
                                break;
                            }

                            var expandedSources = Glob.ExpandArgs(currentDirectory, argsOnly.Skip(1));
                            var rebuilt = (new[] { argsOnly[0] })        // keep URL untouched
                                          .Concat(expandedSources)       // expand only sources
                                          .ToArray();

                            Upload.Execute(currentDirectory, rebuilt);
                            break;
                        }

                    case "base64":
                        Base64.Execute(currentDirectory, argsOnly);
                        break;

                    case "hostinfo":
                        HostInfo.Execute();
                        break;

                    case "exit":
                    case "quit":
                        return;

                    default:
                        Console.WriteLine($"Unknown command: {command}");
                        break;
                }
            }
        }

        static void WritePrompt(string prompt)
        {
            TabComplete.MarkPromptAnchor();
            lastRenderLen = 0;
            Console.Write(prompt);
        }

    }
}
