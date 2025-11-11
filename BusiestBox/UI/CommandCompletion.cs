using System;
using System.Linq;

namespace BusiestBox.UI
{
    /// <summary>
    /// Provides completion for the first token (top-level commands).
    /// Holds the master list of supported commands.
    /// </summary>
    public sealed class CommandCompletion : ICompletionProvider
    {
        // Central command list
        public static readonly string[] Commands =
        {
            "cd", "pwd", "ls", "help", "copy", "cp", "move", "mv",
            "rm", "del", "load", "invoke", "assemblies", "bof", "shell",
            "mkdir", "cat", "zip", "unzip", "netopts", "encrypt", "decrypt",
            "hostinfo", "upload", "hash", "jobs", "base64", "exit", "quit"
        };

        public bool TryGetCompletions(CompletionContext ctx, out string[] matches)
        {
            matches = null;

            if (ctx == null) return false;
            if (ctx.TokenStart != 0) return false; // only first token

            string tok = ctx.CurrentToken ?? string.Empty;

            matches = Commands
                .Where(c => c.StartsWith(tok, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return true; // handled
        }
    }
}
