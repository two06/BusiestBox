using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using System.Net.Security;

namespace BusiestBox.Utils
{
    internal static class NetUtils
    {
        // ----- Defaults / Options -----
        private static readonly object _lock = new object();
        private static string _userAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/70.0.3538.102 Safari/537.36 Edge/18.19582";

        private static bool _useSystemProxy = false;   // if true, use OS proxy (ignores _proxyUrl unless !null)
        private static string _proxyUrl = null;        // e.g. http://host:port ; null => no explicit proxy
        private static ICredentials _proxyCreds = null;

        private static int _timeoutMs = 30000;         // connect timeout
        private static int _readWriteTimeoutMs = 30000;// read/write timeout
        private static bool _allowAutoRedirect = true;
        private static bool _enableDecompression = true;

        private static bool _insecureTls = false;      // ignore TLS errors
        private static readonly RemoteCertificateValidationCallback _bypassCb =
            (sender, cert, chain, errors) => true;
        private static bool _bypassInstalled = false;

        // Updog mode (HTML smuggling for downloads & uploads)
        private static bool _updogMode = false;

        // ----- Public getters (for UI/printing) -----
        public static string UserAgent { get { lock (_lock) return _userAgent; } }
        public static bool UseSystemProxy { get { lock (_lock) return _useSystemProxy; } }
        public static string ProxyUrl { get { lock (_lock) return _proxyUrl; } }
        public static bool HasProxyCredentials { get { lock (_lock) return _proxyCreds != null; } }
        public static int TimeoutMs { get { lock (_lock) return _timeoutMs; } }
        public static int ReadWriteTimeoutMs { get { lock (_lock) return _readWriteTimeoutMs; } }
        public static bool InsecureTls { get { lock (_lock) return _insecureTls; } }
        public static bool AllowAutoRedirect { get { lock (_lock) return _allowAutoRedirect; } }
        public static bool EnableDecompression { get { lock (_lock) return _enableDecompression; } }
        public static bool UpdogMode { get { lock (_lock) return _updogMode; } }

        // ----- Setters (used by NetOpts) -----
        public static void SetUserAgent(string ua)
        {
            if (string.IsNullOrWhiteSpace(ua)) return;
            lock (_lock) { _userAgent = ua; }
        }

        public static void SetProxy(string proxyUrlOrKeyword)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(proxyUrlOrKeyword) ||
                    proxyUrlOrKeyword.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                    proxyUrlOrKeyword.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    _useSystemProxy = false;
                    _proxyUrl = null;
                    return;
                }

                if (proxyUrlOrKeyword.Equals("system", StringComparison.OrdinalIgnoreCase) ||
                    proxyUrlOrKeyword.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    _useSystemProxy = true;
                    return;
                }

                // explicit URL
                _useSystemProxy = false;
                _proxyUrl = proxyUrlOrKeyword;
            }
        }

        public static void ClearProxy()
        {
            lock (_lock)
            {
                _useSystemProxy = false;
                _proxyUrl = null;
            }
        }

        public static void SetProxyCredentials(string username, string password)
        {
            lock (_lock)
            {
                _proxyCreds = (username == null && password == null)
                    ? null
                    : new NetworkCredential(username ?? "", password ?? "");
            }
        }

        public static void ClearProxyCredentials()
        {
            lock (_lock) { _proxyCreds = null; }
        }

        public static void SetTimeoutMs(int ms)
        {
            if (ms <= 0) return;
            lock (_lock) { _timeoutMs = ms; }
        }

        public static void SetReadWriteTimeoutMs(int ms)
        {
            if (ms <= 0) return;
            lock (_lock) { _readWriteTimeoutMs = ms; }
        }

        public static void SetInsecureTls(bool on)
        {
            lock (_lock)
            {
                _insecureTls = on;
                try
                {
                    if (on && !_bypassInstalled)
                    {
                        ServicePointManager.ServerCertificateValidationCallback += _bypassCb;
                        _bypassInstalled = true;
                    }
                    else if (!on && _bypassInstalled)
                    {
                        ServicePointManager.ServerCertificateValidationCallback -= _bypassCb;
                        _bypassInstalled = false;
                    }
                }
                catch { /* best-effort */ }
            }
        }

        public static void SetAutoRedirect(bool on)
        {
            lock (_lock) { _allowAutoRedirect = on; }
        }

        public static void SetDecompression(bool on)
        {
            lock (_lock) { _enableDecompression = on; }
        }

        public static void SetUpdogMode(bool on)
        {
            lock (_lock) { _updogMode = on; }
        }

        // ----- Compatibility shim (old callers) -----
        public static WebClient CreateWebClient()
        {
            // snapshot options under lock to keep a stable client
            string ua; bool useSys; string purl; ICredentials creds;
            int to; int rw; bool redir; bool decomp; bool insecure;
            lock (_lock)
            {
                ua = _userAgent;
                useSys = _useSystemProxy;
                purl = _proxyUrl;
                creds = _proxyCreds;
                to = _timeoutMs;
                rw = _readWriteTimeoutMs;
                redir = _allowAutoRedirect;
                decomp = _enableDecompression;
                insecure = _insecureTls;
            }

            return new OptionsWebClient(ua, useSys, purl, creds, to, rw, redir, decomp, insecure);
        }

        public static byte[] DownloadDataSmart(string url, out string suggestedFileName)
        {
            return DownloadDataSmart(url, out suggestedFileName, CancellationToken.None);
        }
        public static byte[] DownloadDataSmart(string url, out string suggestedFileName, CancellationToken ct)
        {
            suggestedFileName = null;
            ct.ThrowIfCancellationRequested();

            // Snapshot options under lock
            string ua; bool useSys; string purl; ICredentials creds;
            int to; int rw; bool redir; bool decomp;
            lock (_lock)
            {
                ua = _userAgent;
                useSys = _useSystemProxy;
                purl = _proxyUrl;
                creds = _proxyCreds;
                to = _timeoutMs;
                rw = _readWriteTimeoutMs;
                redir = _allowAutoRedirect;
                decomp = _enableDecompression;
            }

            var uri = new Uri(url);
            var req = (HttpWebRequest)WebRequest.Create(uri);

            // Apply options
            if (!string.IsNullOrEmpty(ua)) req.UserAgent = ua;
            if (to > 0) req.Timeout = to;
            if (rw > 0) req.ReadWriteTimeout = rw;
            req.AllowAutoRedirect = redir;
            try
            {
                req.AutomaticDecompression = decomp
                    ? DecompressionMethods.GZip | DecompressionMethods.Deflate
                    : DecompressionMethods.None;
            }
            catch { /* best-effort */ }

            try
            {
                if (useSys)
                {
                    req.Proxy = WebRequest.DefaultWebProxy;
                }
                else if (!string.IsNullOrWhiteSpace(purl))
                {
                    var proxy = new WebProxy(purl) { BypassProxyOnLocal = false };
                    if (creds != null) proxy.Credentials = creds;
                    req.Proxy = proxy;
                }
                else
                {
                    req.Proxy = null;
                }
            }
            catch { /* best-effort */ }

            // Wire cancellation: Abort() the request if the token is cancelled
            using (ct.Register(() => { try { req.Abort(); } catch { } }))
            {
                HttpWebResponse resp = null;
                try
                {
                    resp = (HttpWebResponse)req.GetResponse();
                }
                catch
                {
                    if (ct.IsCancellationRequested)
                        throw new OperationCanceledException(ct);
                    throw; // propagate network error
                }

                using (resp)
                using (var rs = resp.GetResponseStream())
                {
                    if (rs == null) throw new IOException("No response stream.");

                    // Try to infer filename from Content-Disposition
                    try
                    {
                        var cd = resp.Headers?["Content-Disposition"];
                        var name = TryParseContentDispositionFilename(cd);
                        if (!string.IsNullOrEmpty(name))
                            suggestedFileName = name;
                    }
                    catch { }

                    // Stream into memory with cancellation checks
                    var ms = new MemoryStream();
                    var buf = new byte[81920];
                    int n;
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        try { n = rs.Read(buf, 0, buf.Length); }
                        catch (IOException) when (ct.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(ct);
                        }
                        if (n <= 0) break;
                        ms.Write(buf, 0, n);
                    }

                    var raw = ms.ToArray();

                    if (!UpdogMode)
                    {
                        if (string.IsNullOrEmpty(suggestedFileName))
                            suggestedFileName = LeafFromUrl(url);
                        return raw;
                    }

                    // Updog mode ON: decode smuggled payload
                    string html = BytesToStringUtf8(raw);
                    if (!TryDecodeHtmlSmuggledPage(html, out byte[] decoded, out string fname))
                        throw new InvalidOperationException("Updog mode is enabled, but the response does not contain a smuggled payload.");

                    if (!string.IsNullOrEmpty(fname))
                        suggestedFileName = fname;
                    else if (string.IsNullOrEmpty(suggestedFileName))
                        suggestedFileName = LeafFromUrl(url);

                    return decoded;
                }
            }
        }

        // ----- WebClient implementation that applies all options -----
        private sealed class OptionsWebClient : WebClient
        {
            private readonly string _ua;
            private readonly bool _useSystemProxy;
            private readonly string _proxyUrl;
            private readonly ICredentials _proxyCreds;
            private readonly int _timeoutMs;
            private readonly int _readWriteTimeoutMs;
            private readonly bool _allowAutoRedirect;
            private readonly bool _enableDecompression;
            private readonly bool _insecureTls;

            public OptionsWebClient(
                string ua, bool useSystemProxy, string proxyUrl, ICredentials proxyCreds,
                int timeoutMs, int readWriteTimeoutMs, bool allowAutoRedirect, bool enableDecompression, bool insecureTls)
            {
                _ua = ua;
                _useSystemProxy = useSystemProxy;
                _proxyUrl = proxyUrl;
                _proxyCreds = proxyCreds;
                _timeoutMs = timeoutMs;
                _readWriteTimeoutMs = readWriteTimeoutMs;
                _allowAutoRedirect = allowAutoRedirect;
                _enableDecompression = enableDecompression;
                _insecureTls = insecureTls;

                Encoding = Encoding.UTF8;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                var req = base.GetWebRequest(address);

                // HTTP(S)
                var http = req as HttpWebRequest;
                if (http != null)
                {
                    // UA
                    if (!string.IsNullOrEmpty(_ua))
                        http.UserAgent = _ua;

                    // Timeouts
                    if (_timeoutMs > 0) http.Timeout = _timeoutMs;
                    if (_readWriteTimeoutMs > 0) http.ReadWriteTimeout = _readWriteTimeoutMs;

                    // Redirects
                    http.AllowAutoRedirect = _allowAutoRedirect;

                    // Compression
                    try
                    {
                        http.AutomaticDecompression = _enableDecompression
                            ? DecompressionMethods.GZip | DecompressionMethods.Deflate
                            : DecompressionMethods.None;
                    }
                    catch { }

                    // Proxy
                    try
                    {
                        if (_useSystemProxy)
                        {
                            http.Proxy = WebRequest.DefaultWebProxy;
                        }
                        else if (!string.IsNullOrWhiteSpace(_proxyUrl))
                        {
                            var proxy = new WebProxy(_proxyUrl);
                            proxy.BypassProxyOnLocal = false;
                            if (_proxyCreds != null) proxy.Credentials = _proxyCreds;
                            http.Proxy = proxy;
                        }
                        else
                        {
                            http.Proxy = null; // direct
                        }
                    }
                    catch { }

                    return http;
                }

                // FTP (if you ever use it)
                var ftp = req as FtpWebRequest;
                if (ftp != null)
                {
                    if (_timeoutMs > 0) ftp.Timeout = _timeoutMs;
                    if (_readWriteTimeoutMs > 0) ftp.ReadWriteTimeout = _readWriteTimeoutMs;

                    try
                    {
                        if (_useSystemProxy)
                        {
                            ftp.Proxy = WebRequest.DefaultWebProxy;
                        }
                        else if (!string.IsNullOrWhiteSpace(_proxyUrl))
                        {
                            var proxy = new WebProxy(_proxyUrl);
                            if (_proxyCreds != null) proxy.Credentials = _proxyCreds;
                            ftp.Proxy = proxy;
                        }
                        else
                        {
                            ftp.Proxy = null;
                        }
                    }
                    catch { }
                }

                return req;
            }
        }

        // ----- Smuggled-page decoder (downloads) -----

        // Matches from not_updog's smuggled download page:
        //   const d = "BASE64_ENCRYPTED_BYTES";
        //   const k = atob("BASE64_KEY");
        //   const n = atob("BASE64_MASKED_FILENAME");
        private static readonly Regex RxD = new Regex(@"const\s+d\s*=\s*[""'](?<d>[^""']+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex RxK = new Regex(@"const\s+k\s*=\s*atob\(\s*[""'](?<k>[^""']+)[""']\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex RxN = new Regex(@"const\s+n\s*=\s*atob\(\s*[""'](?<n>[^""']+)[""']\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static bool TryDecodeHtmlSmuggledPage(string html, out byte[] data, out string fileName)
        {
            data = null; fileName = null;
            if (string.IsNullOrEmpty(html)) return false;

            var mD = RxD.Match(html);
            var mK = RxK.Match(html);
            if (!mD.Success || !mK.Success) return false;

            try
            {
                byte[] enc = Convert.FromBase64String(mD.Groups["d"].Value);
                byte[] key = Convert.FromBase64String(mK.Groups["k"].Value);

                // Optional filename
                var mN = RxN.Match(html);
                byte[] nameMasked = null;
                if (mN.Success)
                {
                    nameMasked = Convert.FromBase64String(mN.Groups["n"].Value);
                }

                // XOR-decode payload
                byte[] plain = new byte[enc.Length];
                for (int i = 0; i < enc.Length; i++)
                    plain[i] = (byte)(enc[i] ^ key[i % key.Length]);

                data = plain;

                if (nameMasked != null && nameMasked.Length > 0)
                {
                    byte[] nameBytes = new byte[nameMasked.Length];
                    for (int i = 0; i < nameMasked.Length; i++)
                        nameBytes[i] = (byte)(nameMasked[i] ^ key[i % key.Length]);

                    // Filenames are sent as UTF-8 in the server snippet
                    fileName = SafeFileName(Encoding.UTF8.GetString(nameBytes));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ----- Updog upload helpers (client-side XOR) -----

        // Matches the upload page's embedded key:
        //   const xorKey = atob("BASE64_KEY");
        private static readonly Regex RxUploadKey = new Regex(@"const\s+xorKey\s*=\s*atob\(\s*[""'](?<k>[^""']+)[""']\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Try to retrieve the XOR key from the Updog HTML page at the given URL.
        // NOTE: Only meaningful when UpdogMode == true. Returns false if the page lacks the key.
        public static bool TryGetUpdogUploadKey(string url, out byte[] key)
        {
            key = null;
            if (!UpdogMode) return false;

            using (var wc = CreateWebClient())
            {
                byte[] b = wc.DownloadData(url);
                string html = BytesToStringUtf8(b);
                var m = RxUploadKey.Match(html);
                if (!m.Success) return false;

                try
                {
                    key = Convert.FromBase64String(m.Groups["k"].Value);
                    return true;
                }
                catch { return false; }
            }
        }

        // XOR-mask a buffer with the given key, continuing from a specified rolling offset (for chunked uploads)
        public static void XorMaskInPlace(byte[] buffer, int count, byte[] key, ref int xorOffset)
        {
            if (buffer == null || key == null || key.Length == 0) return;
            int klen = key.Length;
            for (int i = 0; i < count; i++)
            {
                buffer[i] = (byte)(buffer[i] ^ key[(xorOffset + i) % klen]);
            }
            xorOffset += count;
        }

        // Convenience: for single-shot uploads (non-streaming). Throws if UpdogMode is on and key cannot be discovered.
        public static byte[] PrepareUploadDataForUpdog(string url, byte[] data)
        {
            if (!UpdogMode) return data ?? new byte[0];

            if (!TryGetUpdogUploadKey(url, out var key) || key == null || key.Length == 0)
                throw new InvalidOperationException("Updog mode is enabled, but the upload page did not provide an xorKey.");

            var copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);
            int off = 0;
            XorMaskInPlace(copy, copy.Length, key, ref off);
            return copy;
        }

        // ----- Small helpers -----

        private static string BytesToStringUtf8(byte[] b, int index = 0, int count = -1)
        {
            if (b == null) return "";
            if (count < 0) count = b.Length - index;
            try { return Encoding.UTF8.GetString(b, index, count); } catch { return ""; }
        }

        private static string TryParseContentDispositionFilename(string cd)
        {
            // Very simple parser for: attachment; filename="name.ext"
            // Also supports RFC5987: filename*=UTF-8''urlencoded
            try
            {
                if (string.IsNullOrEmpty(cd)) return null;

                // filename*=
                int idxStar = IndexOfIgnoreCase(cd, "filename*=");
                if (idxStar >= 0)
                {
                    string rest = cd.Substring(idxStar + "filename*=".Length).Trim();
                    int q1 = rest.IndexOf('\''); // charset'
                    if (q1 > 0)
                    {
                        int q2 = rest.IndexOf('\'', q1 + 1); // language'
                        if (q2 > q1)
                        {
                            string urlEnc = rest.Substring(q2 + 1);
                            string decoded = Uri.UnescapeDataString(urlEnc);
                            return SafeFileName(decoded);
                        }
                    }
                }

                // filename=
                int idx = IndexOfIgnoreCase(cd, "filename=");
                if (idx >= 0)
                {
                    string rest = cd.Substring(idx + "filename=".Length).Trim();
                    if (rest.StartsWith("\""))
                    {
                        int end = rest.IndexOf('"', 1);
                        if (end > 1)
                            return SafeFileName(rest.Substring(1, end - 1));
                    }
                    else
                    {
                        int semi = rest.IndexOf(';');
                        string token = semi > 0 ? rest.Substring(0, semi) : rest;
                        return SafeFileName(token.Trim());
                    }
                }
            }
            catch { }
            return null;
        }

        private static int IndexOfIgnoreCase(string s, string needle)
        {
            return s.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }

        private static string LeafFromUrl(string url)
        {
            try
            {
                var u = new Uri(url);
                string leaf = Path.GetFileName(u.AbsolutePath);
                if (string.IsNullOrEmpty(leaf)) leaf = "download";
                return SafeFileName(leaf);
            }
            catch { return "download"; }
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "download";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
