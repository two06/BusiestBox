#!/usr/bin/env python3
import argparse, hmac, hashlib, os
from Crypto.Cipher import AES
from Crypto.Util.Padding import pad
from Crypto.Random import get_random_bytes

FORMAT_VERSION = 1
AES_BLOCK_SIZE = 16
AES_KEY_SIZE = 32
HMAC_KEY_SIZE = 32
PBKDF2_SALT_SIZE = 16
PBKDF2_ITERATIONS = 5000

def derive_keys(passphrase: str, salt: bytes):
    material = hashlib.pbkdf2_hmac(
        "sha1", passphrase.encode("utf-8"), salt,
        PBKDF2_ITERATIONS, dklen=AES_KEY_SIZE + HMAC_KEY_SIZE
    )
    return material[:AES_KEY_SIZE], material[AES_KEY_SIZE:], material

def encrypt_file(passphrase, infile, outfile, debug=False):
    with open(infile, "rb") as f:
        plaintext = f.read()

    salt = get_random_bytes(PBKDF2_SALT_SIZE)
    iv = get_random_bytes(AES_BLOCK_SIZE)
    enc_key, mac_key, material = derive_keys(passphrase, salt)

    cipher = AES.new(enc_key, AES.MODE_CBC, iv)
    ciphertext = cipher.encrypt(pad(plaintext, AES_BLOCK_SIZE))

    # HMAC over salt || iv || ciphertext (version byte excluded, same as C#)
    tag = hmac.new(mac_key, salt + iv + ciphertext, hashlib.sha256).digest()

    if debug:
        print(f"[DEBUG] Passphrase bytes: {passphrase.encode('utf-8').hex()}")
        print(f"[DEBUG] Salt length: {len(salt)}")
        print(f"[DEBUG] Salt: {salt.hex()}")
        print(f"[DEBUG] IV: {iv.hex()}")
        print(f"[DEBUG] Derived key material: {material.hex()}")
        print(f"[DEBUG]  - Encryption key: {enc_key.hex()}")
        print(f"[DEBUG]  - MAC key: {mac_key.hex()}")
        print(f"[DEBUG] Ciphertext length: {len(ciphertext)}")
        print(f"[DEBUG] Generated tag: {tag.hex()}")

    # Final blob: [ver][salt][iv][ciphertext][hmac]
    blob = bytes([FORMAT_VERSION]) + salt + iv + ciphertext + tag

    with open(outfile, "wb") as f:
        f.write(blob)

    print(f"[*] Encrypted {infile} -> {outfile}")

def main():
    parser = argparse.ArgumentParser(
        description="Encrypt a file using PBKDF2-HMAC-SHA1 + AES-256-CBC + HMAC-SHA256 "
                    "(compatible with BusiestBox.Crypto.EncryptBytes)."
    )
    parser.add_argument("passphrase", help="Passphrase used to derive encryption and HMAC keys")
    parser.add_argument("input", help="Path to the plaintext input file")
    parser.add_argument("output", help="Path to write the encrypted output file")
    parser.add_argument("--debug", action="store_true", help="Print internal key/IV/HMAC values for troubleshooting")
    args = parser.parse_args()

    encrypt_file(args.passphrase, args.input, args.output, debug=args.debug)

if __name__ == "__main__":
    main()
