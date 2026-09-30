using Bonobo.Git.Server.App_GlobalResources;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Bonobo.Git.Server.Security
{
    /// <summary>
    /// A public key which has been checked and understood - the only thing which is ever
    /// stored against a user or written into an authorized_keys file.
    /// </summary>
    public class ParsedSshKey
    {
        /// <summary>The key algorithm, exactly as it appears in the key file, e.g. 'ssh-ed25519'</summary>
        public string KeyType { get; internal set; }

        /// <summary>The base64 blob, with no whitespace</summary>
        public string Base64Data { get; internal set; }

        /// <summary>The trailing comment, usually user@host. May be empty, never null.</summary>
        public string Comment { get; internal set; }

        /// <summary>OpenSSH-style SHA256 fingerprint, e.g. 'SHA256:xyz...' - this is what we show the user</summary>
        public string Fingerprint { get; internal set; }

        /// <summary>Legacy MD5 fingerprint, e.g. 'MD5:aa:bb:...', kept because some clients still print it</summary>
        public string LegacyFingerprint { get; internal set; }

        /// <summary>Key size in bits</summary>
        public int KeySize { get; internal set; }

        /// <summary>
        /// The canonical single-line form, which is what we store and what goes into authorized_keys.
        /// Deliberately excludes any options the user may have pasted.
        /// </summary>
        public string Text
        {
            get { return String.IsNullOrEmpty(Comment) ? KeyType + " " + Base64Data : KeyType + " " + Base64Data + " " + Comment; }
        }
    }

    /// <summary>
    /// Parses OpenSSH public keys in the one-line authorized_keys format: 'type base64 [comment]'.
    ///
    /// We do not simply trust the type token at the front of the line - the base64 blob carries its
    /// own copy of the algorithm name, and we insist that the two agree. Anything we do not fully
    /// understand is rejected rather than passed through to sshd.
    /// </summary>
    public static class SshKeyParser
    {
        /// <summary>Matches the minimum that OpenSSH itself will generate</summary>
        public const int MinimumRsaKeySize = 2048;

        private const int MaximumKeyLength = 16384;

        /// <summary>Key type to key size in bits, or 0 when the size has to be read out of the blob</summary>
        private static readonly Dictionary<string, int> AcceptedKeyTypes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "ssh-ed25519", 256 },
            { "ssh-rsa", 0 },
            { "ecdsa-sha2-nistp256", 256 },
            { "ecdsa-sha2-nistp384", 384 },
            { "ecdsa-sha2-nistp521", 521 },
            { "sk-ssh-ed25519@openssh.com", 256 },
            { "sk-ecdsa-sha2-nistp256@openssh.com", 256 },
        };

        public static IEnumerable<string> SupportedKeyTypes
        {
            get { return AcceptedKeyTypes.Keys; }
        }

        public static bool TryParse(string input, out ParsedSshKey key, out string error)
        {
            key = null;
            error = null;

            if (String.IsNullOrWhiteSpace(input))
            {
                error = Resources.Validation_SshKey_Empty;
                return false;
            }

            if (input.Length > MaximumKeyLength)
            {
                error = Resources.Validation_SshKey_Malformed;
                return false;
            }

            // A pasted private key is a common and dangerous mistake, so say so plainly rather
            // than letting it fall through to the generic malformed message.
            if (input.IndexOf("PRIVATE KEY", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                error = Resources.Validation_SshKey_PrivateKey;
                return false;
            }

            string trimmed = input.Trim();

            // Public key files end with a newline, which Trim has just dealt with. Anything still
            // containing a line break is either several keys at once or a wrapped paste.
            if (trimmed.IndexOf('\n') >= 0 || trimmed.IndexOf('\r') >= 0)
            {
                error = Resources.Validation_SshKey_MultipleLines;
                return false;
            }

            string[] parts = trimmed.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                error = Resources.Validation_SshKey_Malformed;
                return false;
            }

            string keyType = parts[0];
            string base64Data = parts[1];
            string comment = parts.Length > 2 ? parts[2].Trim() : String.Empty;

            if (String.Equals(keyType, "ssh-dss", StringComparison.Ordinal))
            {
                error = Resources.Validation_SshKey_DsaNotSupported;
                return false;
            }

            int declaredKeySize;
            if (!AcceptedKeyTypes.TryGetValue(keyType, out declaredKeySize))
            {
                // Most likely the line starts with authorized_keys options, or it is not a key at all.
                error = String.Format(Resources.Validation_SshKey_UnsupportedType, keyType, String.Join(", ", AcceptedKeyTypes.Keys));
                return false;
            }

            byte[] blob;
            try
            {
                blob = Convert.FromBase64String(base64Data);
            }
            catch (FormatException)
            {
                error = Resources.Validation_SshKey_Malformed;
                return false;
            }

            var reader = new SshBlobReader(blob);
            byte[] embeddedType;
            if (!reader.TryReadString(out embeddedType))
            {
                error = Resources.Validation_SshKey_Malformed;
                return false;
            }

            // The blob knows what it is. If that disagrees with the text in front of it then the key
            // has been mangled, and the fingerprint we showed the user would be misleading.
            if (!String.Equals(Encoding.ASCII.GetString(embeddedType), keyType, StringComparison.Ordinal))
            {
                error = Resources.Validation_SshKey_TypeMismatch;
                return false;
            }

            int keySize = declaredKeySize;
            if (keyType == "ssh-rsa")
            {
                byte[] exponent;
                byte[] modulus;
                if (!reader.TryReadString(out exponent) || !reader.TryReadString(out modulus))
                {
                    error = Resources.Validation_SshKey_Malformed;
                    return false;
                }

                keySize = GetBitLength(modulus);
                if (keySize < MinimumRsaKeySize)
                {
                    error = String.Format(Resources.Validation_SshKey_RsaTooSmall, MinimumRsaKeySize);
                    return false;
                }
            }

            key = new ParsedSshKey
            {
                KeyType = keyType,
                Base64Data = base64Data,
                Comment = comment,
                KeySize = keySize,
                Fingerprint = GetSha256Fingerprint(blob),
                LegacyFingerprint = GetMd5Fingerprint(blob),
            };
            return true;
        }

        /// <summary>
        /// 'SHA256:' followed by unpadded base64, which is what 'ssh-keygen -lf' prints, so that the
        /// user can compare what we show against their own machine by eye.
        /// </summary>
        private static string GetSha256Fingerprint(byte[] blob)
        {
            using (var sha256 = SHA256.Create())
            {
                return "SHA256:" + Convert.ToBase64String(sha256.ComputeHash(blob)).TrimEnd('=');
            }
        }

        private static string GetMd5Fingerprint(byte[] blob)
        {
            using (var md5 = MD5.Create())
            {
                return "MD5:" + BitConverter.ToString(md5.ComputeHash(blob)).Replace('-', ':').ToLowerInvariant();
            }
        }

        /// <summary>
        /// Bit length of an SSH mpint, which may carry a leading zero byte to keep it positive.
        /// </summary>
        private static int GetBitLength(byte[] mpint)
        {
            int firstNonZero = 0;
            while (firstNonZero < mpint.Length && mpint[firstNonZero] == 0)
            {
                firstNonZero++;
            }

            if (firstNonZero == mpint.Length)
            {
                return 0;
            }

            int bits = (mpint.Length - firstNonZero - 1) * 8;
            for (byte b = mpint[firstNonZero]; b != 0; b = (byte)(b >> 1))
            {
                bits++;
            }
            return bits;
        }

        /// <summary>
        /// Reads the length-prefixed strings of RFC 4251 section 5, without trusting any of the lengths.
        /// </summary>
        private sealed class SshBlobReader
        {
            private readonly byte[] _data;
            private int _position;

            public SshBlobReader(byte[] data)
            {
                _data = data;
            }

            public bool TryReadString(out byte[] value)
            {
                value = null;

                if (_data.Length - _position < 4)
                {
                    return false;
                }

                long length = ((long)_data[_position] << 24)
                            | ((long)_data[_position + 1] << 16)
                            | ((long)_data[_position + 2] << 8)
                            | _data[_position + 3];
                _position += 4;

                if (length < 0 || length > _data.Length - _position)
                {
                    return false;
                }

                value = new byte[length];
                Buffer.BlockCopy(_data, _position, value, 0, (int)length);
                _position += (int)length;
                return true;
            }
        }
    }
}
