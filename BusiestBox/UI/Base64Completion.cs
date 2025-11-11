using System;
using System.Linq;
using BusiestBox.Utils;

namespace BusiestBox.UI
{
    public sealed class Base64Completion : ICompletionProvider
    {
        private static readonly string[] Modes = new[] { "encode", "decode" };

        public bool TryGetCompletions(CompletionContext ctx, out string[] matches)
        {
            matches = null;
            if (ctx == null || string.IsNullOrEmpty(ctx.FullLine))
                return false;

            // --- Tokenize up to cursor ---
            string upto = ctx.FullLine.Substring(0, Math.Max(0, ctx.CursorPos));
            var args = TabComplete.SplitArgsPreservingQuotes(upto);
            if (args.Length == 0) return false;

            bool caretInFirst = (ctx.TokenStart == 0);
            bool firstIsBase64 = args[0].Equals("base64", StringComparison.OrdinalIgnoreCase);

            // If caret is in first token and not fully typed "base64", let global command completer handle it
            if (caretInFirst && !firstIsBase64)
            {
                matches = null;
                return false;
            }

            // If the first token isn't "base64", don't handle this at all
            if (!firstIsBase64)
                return false;

            // Determine arg index being completed
            bool atBoundary = upto.Length > 0 && char.IsWhiteSpace(upto[upto.Length - 1]);
            int argIndex = atBoundary ? args.Length : Math.Max(0, args.Length - 1);

            string tok = ctx.CurrentToken ?? string.Empty;

            // --- Arg 1: encode/decode ---
            if (argIndex == 1)
            {
                matches = Modes.Where(m => m.StartsWith(tok, StringComparison.OrdinalIgnoreCase)).ToArray();
                return true;
            }

            // --- Arg 2: input file ---
            if (argIndex == 2)
            {
                matches = FilePathCompletionHelper(tok, ctx.CurrentDirectory);
                return true;
            }

            // --- Arg 3: output file ---
            if (argIndex == 3)
            {
                matches = FilePathCompletionHelper(tok, ctx.CurrentDirectory);
                return true;
            }

            // No suggestions beyond arg 3
            matches = Array.Empty<string>();
            return true;
        }

        /// <summary>
        /// Delegates to FilePathCompletion to reuse its path suggestion logic.
        /// </summary>
        private static string[] FilePathCompletionHelper(string token, string currentDirectory)
        {
            var dummyCtx = new CompletionContext
            {
                CurrentToken = token,
                CurrentDirectory = currentDirectory,
                TokenStart = 1, // not first token
                CursorPos = token.Length,
                FullLine = "base64 " + token
            };

            string[] outMatches;
            new FilePathCompletion().TryGetCompletions(dummyCtx, out outMatches);
            return outMatches ?? Array.Empty<string>();
        }
    }
}
