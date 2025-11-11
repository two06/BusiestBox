using System;
using System.Linq;

namespace BusiestBox.UI
{
    public sealed class NetOptsCompletion : ICompletionProvider
    {
        private static readonly string[] Subs = new[]
        {
            "ua","user-agent","proxy","proxy-cred","proxy-creds","timeout",
            "insecure","redirect","decompress","compression","updog_mode","updog"
        };

        private static readonly string[] OnOff = new[] { "on", "off" };

        private static readonly string[] ProxyVals = new[]
        {
            "off","system","http://","https://","socks5://","socks4://"
        };

        public bool TryGetCompletions(CompletionContext ctx, out string[] matches)
        {
            matches = null;
            if (ctx == null || string.IsNullOrEmpty(ctx.FullLine)) return false;

            // We only care when the first token is (or is becoming) "netopts"
            bool caretInFirst = (ctx.TokenStart == 0);
            var upto = ctx.FullLine.Substring(0, Math.Max(0, ctx.CursorPos));
            var args = BusiestBox.Utils.TabComplete.SplitArgsPreservingQuotes(upto);
            if (args.Length == 0) return false;

            bool firstIsNetopts = args[0].Equals("netopts", StringComparison.OrdinalIgnoreCase);
            if (!(firstIsNetopts || (caretInFirst && "netopts".StartsWith(ctx.CurrentToken ?? "", StringComparison.OrdinalIgnoreCase))))
                return false;

            // Which arg index are we completing now?
            bool atBoundary = upto.Length > 0 && char.IsWhiteSpace(upto[upto.Length - 1]);
            int argIndex = atBoundary ? args.Length : Math.Max(0, args.Length - 1);

            // If still typing the first token, let core command list handle it
            if (caretInFirst && !firstIsNetopts)
            {
                matches = new string[0];
                return true;
            }

            // Subcommand
            if (argIndex == 1)
            {
                matches = Subs.Where(s => s.StartsWith(ctx.CurrentToken ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
                return true;
            }

            string sub = args.Length >= 2 ? args[1].ToLowerInvariant() : "";
            string tok = ctx.CurrentToken ?? "";

            switch (sub)
            {
                case "ua":
                case "user-agent":
                    matches = new string[0]; // free text
                    return true;

                case "proxy":
                    if (argIndex == 2)
                        matches = ProxyVals.Where(s => s.StartsWith(tok, StringComparison.OrdinalIgnoreCase)).ToArray();
                    else matches = new string[0];
                    return true;

                case "proxy-cred":
                case "proxy-creds":
                    if (argIndex == 2)
                        matches = new[] { "clear" }.Where(s => s.StartsWith(tok, StringComparison.OrdinalIgnoreCase)).ToArray();
                    else matches = new string[0]; // user/pw free text
                    return true;

                case "timeout":
                    if (argIndex == 2)
                        matches = new[] { "5000", "10000", "15000", "30000", "60000" }
                                  .Where(s => s.StartsWith(tok, StringComparison.OrdinalIgnoreCase)).ToArray();
                    else matches = new string[0];
                    return true;

                case "insecure":
                case "redirect":
                case "decompress":
                case "compression":
                case "updog_mode":
                case "updog":
                    if (argIndex == 2)
                        matches = OnOff.Where(s => s.StartsWith(tok, StringComparison.OrdinalIgnoreCase)).ToArray();
                    else matches = new string[0];
                    return true;

                default:
                    matches = new string[0];
                    return true; // handled (no suggestions)
            }
        }
    }
}
