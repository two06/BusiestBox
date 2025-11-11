#!/usr/bin/env python3
import argparse, hmac, hashlib
from Crypto.Cipher import AES
from Crypto.Util.Padding import unpad
import binascii

FORMAT_VERSION = 1
AES_BLOCK_SIZE = 16
AES_KEY_SIZE = 32
HMAC_KEY_SIZE = 32
PBKDF2_SALT_SIZE = 16
HMAC_SIZE = 32
PBKDF2_ITERATIONS = 5000

def derive_keys(passphrase: str, salt: bytes):
    material = hashlib.pbkdf2_hmac(
        "sha1", passphrase.encode("utf-8"), salt,
        PBKDF2_ITERATIONS, dklen=AES_KEY_SIZE + HMAC_KEY_SIZE
    )
    return material[:AES_KEY_SIZE], material[AES_KEY_SIZE:], material

def decrypt_file(passphrase, infile, outfile, debug=False):
    with open(infile, "rb") as f:
        blob = f.read()

    if len(blob) < 1 + PBKDF2_SALT_SIZE + AES_BLOCK_SIZE + HMAC_SIZE:
        raise ValueError("Ciphertext blob too short or corrupt")

    offset = 0
    version = blob[offset]; offset += 1
    if version != FORMAT_VERSION:
        raise ValueError("Unsupported ciphertext version")

    salt = blob[offset:offset+PBKDF2_SALT_SIZE]; offset += PBKDF2_SALT_SIZE
    iv = blob[offset:offset+AES_BLOCK_SIZE]; offset += AES_BLOCK_SIZE
    ciphertext = blob[offset:-HMAC_SIZE]
    tag = blob[-HMAC_SIZE:]

    if debug:
        print(f"[DEBUG] Passphrase bytes: {passphrase.encode('utf-8').hex()}")
        print(f"[DEBUG] Salt length: {len(salt)}")

    enc_key, mac_key, material = derive_keys(passphrase, salt)
    expected_tag = hmac.new(mac_key, salt + iv + ciphertext, hashlib.sha256).digest()

    if debug:
        print(f"[DEBUG] Salt: {salt.hex()}")
        print(f"[DEBUG] IV: {iv.hex()}")
        print(f"[DEBUG] Derived key material: {material.hex()}")
        print(f"[DEBUG]  - Encryption key: {enc_key.hex()}")
        print(f"[DEBUG]  - MAC key: {mac_key.hex()}")
        print(f"[DEBUG] Ciphertext length: {len(ciphertext)}")
        print(f"[DEBUG] File tag: {tag.hex()}")
        print(f"[DEBUG] Expected tag: {expected_tag.hex()}")

    if not hmac.compare_digest(tag, expected_tag):
        raise ValueError("Invalid passphrase or data has been tampered with")

    cipher = AES.new(enc_key, AES.MODE_CBC, iv)
    plaintext = unpad(cipher.decrypt(ciphertext), AES_BLOCK_SIZE)

    with open(outfile, "wb") as f:
        f.write(plaintext)

    print(f"[*] Decrypted {infile} -> {outfile}")

def main():
    parser = argparse.ArgumentParser(
        description="Decrypt a file produced by BusiestBox.Crypto.EncryptBytes "
                    "(PBKDF2-HMAC-SHA1 + AES-256-CBC + HMAC-SHA256)."
    )
    parser.add_argument("passphrase", help="Passphrase used to derive encryption and HMAC keys")
    parser.add_argument("input", help="Path to the encrypted input file")
    parser.add_argument("output", help="Path to write the decrypted output file")
    parser.add_argument("--debug", action="store_true", help="Print internal key/IV/HMAC values for troubleshooting")
    args = parser.parse_args()

    decrypt_file(args.passphrase, args.input, args.output, debug=args.debug)

if __name__ == "__main__":
    main()
