from http.server import HTTPServer, BaseHTTPRequestHandler
import os
import urllib
import traceback
import argparse
import base64
import random
import ssl

# Generate XOR key once at startup
XOR_KEY = os.urandom(16)
SCRIPT_ABSPATH = os.path.abspath(__file__)
CHUNK_SIZE = 4 * 1024 * 1024  # 4MB 

def xor_mask(data: bytes, key: bytes, offset: int = 0) -> bytes:
    return bytes(b ^ key[(i + offset) % len(key)] for i, b in enumerate(data))

class SimpleFileServer(BaseHTTPRequestHandler):
    def list_directory(self, rel_path='.'):  # rel_path is relative to cwd
        path = os.path.join(os.getcwd(), rel_path)
        try:
            entries = os.listdir(path)
            entries.sort()
        except Exception as e:
            self.send_error(500, f"Unable to list directory: {e}")
            return

        output = ['<html><body><h2>Upload File</h2>']

        if not self.server.disable_html_smuggling:
            key_b64 = base64.b64encode(XOR_KEY).decode()
            output.append(f"""
            <form id="uploadForm">
                <input type="file" id="fileInput" />
                <input type="submit" value="Upload"/>
            </form>
            <script>
            const xorKey = atob("{key_b64}");
            document.getElementById('uploadForm').onsubmit = async function(e) {{
                e.preventDefault();
                const file = document.getElementById('fileInput').files[0];
                if (!file) return;

                const reader = new FileReader();
                reader.onload = function() {{
                    const data = new Uint8Array(reader.result);
                    const encrypted = new Uint8Array(data.length);
                    for (let i = 0; i < data.length; i++) {{
                        encrypted[i] = data[i] ^ xorKey.charCodeAt(i % xorKey.length);
                    }}

                    const blob = new Blob([encrypted]);
                    const form = new FormData();
                    form.append('file', blob, file.name);
                    fetch('/', {{
                        method: 'POST',
                        body: form
                    }}).then(() => window.location.reload());
                }};
                reader.readAsArrayBuffer(file);
            }};
            </script>
            """)
        else:
            output.append("""
            <form enctype="multipart/form-data" method="post">
                <input name="file" type="file"/>
                <input type="submit" value="Upload"/>
            </form>
            """)


        if os.path.abspath(rel_path) != os.path.abspath('.'):
            parent = os.path.normpath(os.path.join(rel_path, '..'))
            output.append(f'<li>[ D ] <a href="{urllib.parse.quote(parent)}">.. (up)</a></li>')

        for entry in entries:
            full_path = os.path.join(path, entry)
            # Block download access to specific file types
            if entry.startswith('.') or entry.endswith('.partial') or os.path.abspath(full_path) == SCRIPT_ABSPATH:
                continue
            link_path = os.path.join(rel_path, entry)
            label = '[ D ]' if os.path.isdir(full_path) else '[ F ]'
            output.append(f'<li>{label} <a href="{urllib.parse.quote(link_path)}">{entry}</a></li>')

        output.append('</ul></body></html>')
        response = '\n'.join(output).encode('utf-8')
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(response)))
        self.end_headers()
        self.wfile.write(response)

    def xor_encrypt(self, data: bytes) -> bytes:
        return xor_mask(data, XOR_KEY)

    def xor_decrypt(self, data: bytes) -> bytes:
        return xor_mask(data, XOR_KEY)

    def serve_smuggled_html(self, local_path, filename):
        with open(local_path, 'rb') as f:
            raw_data = f.read()

        encrypted = self.xor_encrypt(raw_data)
        b64_encoded = base64.b64encode(encrypted).decode()
        b64_key = base64.b64encode(XOR_KEY).decode()

        filename_masked = xor_mask(filename.encode(), XOR_KEY)
        b64_fname = base64.b64encode(filename_masked).decode()

        html_template = f"""
        <html><body>
        <script>
        const d = "{b64_encoded}";
        const k = atob("{b64_key}");
        const n = atob("{b64_fname}");

        function Decode(input, mask) {{
            let result = new Uint8Array(input.length);
            let m = mask.length;
            for (let j = 0; j < input.length; j++) {{
                result[j] = input[j] ^ mask.charCodeAt(j % m);
            }}
            return result;
        }}

        let a = Uint8Array.from(atob(d), x => x.charCodeAt(0));
        let b = Decode(a, k);
        let f = Decode(Uint8Array.from(n, c => c.charCodeAt(0)), k);
        let blob = new Blob([b]);
        let l = document.createElement('a');
        l.href = URL.createObjectURL(blob);
        l.download = new TextDecoder().decode(f);
        l.click();
        </script>
        <p>Preparing download...</p>
        </body></html>
        """
        encoded = html_template.encode()
        self.send_response(200)
        self.send_header("Content-Type", "text/html")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def do_GET(self):
        requested_path = urllib.parse.unquote(self.path)
        safe_path = os.path.normpath(requested_path.lstrip('/'))
        local_path = os.path.join(os.getcwd(), safe_path)

        print(f"[*] GET {self.path} → local path: {local_path}")

        if not os.path.abspath(local_path).startswith(os.getcwd()):
            self.send_error(403, "Forbidden")
            return

        if os.path.abspath(local_path) == SCRIPT_ABSPATH:
            self.send_error(403, "Access to this file is forbidden")
            return

        if os.path.isdir(local_path):
            self.list_directory(safe_path)
        elif os.path.isfile(local_path):
            if self.server.disable_html_smuggling:
                try:
                    with open(local_path, 'rb') as f:
                        data = f.read()
                    self.send_response(200)
                    self.send_header('Content-Type', 'application/octet-stream')
                    self.send_header('Content-Disposition', f'attachment; filename="{os.path.basename(local_path)}"')
                    self.send_header('Content-Length', str(len(data)))
                    self.end_headers()
                    self.wfile.write(data)
                    print(f"[+] Served download: {local_path} ({len(data)} bytes)")
                except Exception as e:
                    print(f"[ERROR] Failed to serve file {local_path}: {e}")
                    self.send_error(500, f"Error reading file: {e}")
            else:
                self.serve_smuggled_html(local_path, os.path.basename(local_path))
        else:
            self.send_error(404, "File not found")

    def do_POST(self):
        print("[*] Incoming POST request")
        content_type = self.headers.get('Content-Type', '')
        if 'multipart/form-data' not in content_type:
            self.send_response(400)
            self.end_headers()
            self.wfile.write(b"Expected multipart/form-data.")
            return

        try:
            boundary = content_type.split('boundary=')[-1].encode()
            delimiter = b'--' + boundary
            end_delimiter = delimiter + b'--'
            content_length = int(self.headers.get('Content-Length', 0))
            line = self.rfile.readline()
            content_length -= len(line)

            if not line.startswith(delimiter):
                self.send_response(400)
                self.end_headers()
                self.wfile.write(b"Malformed multipart data.")
                return

            headers = b""
            while True:
                line = self.rfile.readline()
                content_length -= len(line)
                if line in (b"\r\n", b"\n", b""):
                    break
                headers += line

            filename = None
            for header_line in headers.decode(errors='replace').splitlines():
                if header_line.lower().startswith('content-disposition:'):
                    parts = header_line.split(';')
                    for p in parts:
                        if p.strip().startswith('filename='):
                            filename = p.strip().split('=')[1].strip('"')
                            break

            if not filename:
                self.send_response(400)
                self.end_headers()
                self.wfile.write(b"Filename not provided.")
                return

            temp_filename = filename + '.partial'
            with open(temp_filename, 'wb') as out_file:
                xor_offset = 0
                buffer = b''
                while True:
                    line = self.rfile.readline()
                    if line.startswith(b'--' + boundary):
                        break  # Stop at the boundary line
                    if not line:
                        break
                    buffer += line
                    while len(buffer) >= CHUNK_SIZE:
                        chunk, buffer = buffer[:CHUNK_SIZE], buffer[CHUNK_SIZE:]
                        if not self.server.disable_html_smuggling:
                            chunk = xor_mask(chunk, XOR_KEY, xor_offset)
                        out_file.write(chunk)
                        xor_offset += len(chunk)
                        print(f"[chunk] wrote {len(chunk)} bytes to {temp_filename}")

                # Remove trailing CRLF if it's before the boundary and only padding
                if buffer.endswith(b"\r\n"):
                    buffer = buffer[:-2]
                elif buffer.endswith(b"\n"):
                    buffer = buffer[:-1]

                if buffer:
                    if not self.server.disable_html_smuggling:
                        buffer = xor_mask(buffer, XOR_KEY, xor_offset)
                    out_file.write(buffer)
                    print(f"[chunk] wrote {len(buffer)} bytes")

            os.rename(temp_filename, filename)
            print(f"[+] Uploaded: {filename}")
            self.send_response(303)
            self.send_header('Location', '/')
            self.end_headers()

        except Exception as e:
            traceback.print_exc()
            self.send_response(500)
            self.end_headers()
            self.wfile.write(b"Server error.")

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="Simple HTML smuggling file server")
    parser.add_argument('--port', type=int, help='Port to listen on (default: 80 or 443 if --ssl)')
    parser.add_argument('--no-html-smuggling', action='store_true', help='Disable HTML smuggling')
    parser.add_argument('--ssl', action='store_true', help='Enable SSL')
    parser.add_argument('--certfile', type=str, default='cert.pem', help='SSL certificate file')
    parser.add_argument('--keyfile', type=str, default='key.pem', help='SSL private key file')
    args = parser.parse_args()

    port = args.port or (443 if args.ssl else 80)

    print(f"[*] Serving on {'https' if args.ssl else 'http'}://0.0.0.0:{port}")
    print("[+] HTML smuggling mode ENABLED" if not args.no_html_smuggling else "[-] HTML smuggling mode DISABLED")

    class CustomHTTPServer(HTTPServer):
        def __init__(self, server_address, RequestHandlerClass):
            super().__init__(server_address, RequestHandlerClass)
            self.disable_html_smuggling = args.no_html_smuggling

    HTTPServer.allow_reuse_address = True
    httpd = CustomHTTPServer(("", port), SimpleFileServer)

    if args.ssl:
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(certfile=args.certfile, keyfile=args.keyfile)
        httpd.socket = context.wrap_socket(httpd.socket, server_side=True)

    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n[!] Server interrupted by user, shutting down...")
        httpd.server_close()

