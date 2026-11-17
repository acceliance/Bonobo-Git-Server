using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Bonobo.Git.Server.Git.Lfs
{
    public enum LfsSaveResult
    {
        Stored,
        AlreadyStored,
        HashMismatch,
        SizeMismatch
    }

    /// <summary>
    /// Content-addressed storage of the Git LFS objects of ONE repository, inside the bare repository directory:
    ///   {repository}/lfs/objects/ab/cd/abcd...(64 hex)     the objects
    ///   {repository}/lfs/tmp/                              uploads in progress
    /// This is the layout git-lfs itself uses in a .git directory. Deleting or renaming the repository takes its objects with it.
    /// </summary>
    public class LfsObjectStore
    {
        private static readonly Regex OidPattern = new Regex("^[0-9a-f]{64}$", RegexOptions.Compiled);

        private readonly string _objectsDirectory;
        private readonly string _tempDirectory;

        public LfsObjectStore(string repositoryDirectory)
        {
            if (string.IsNullOrEmpty(repositoryDirectory))
            {
                throw new ArgumentException("repositoryDirectory is required", "repositoryDirectory");
            }
            _objectsDirectory = Path.Combine(repositoryDirectory, "lfs", "objects");
            _tempDirectory = Path.Combine(repositoryDirectory, "lfs", "tmp");
        }

        /// <summary>
        /// An object id is the lowercase hex SHA-256 of the content. Anything else is refused BEFORE it is used to build a path.
        /// </summary>
        public static bool IsValidOid(string oid)
        {
            return oid != null && OidPattern.IsMatch(oid);
        }

        public string GetObjectPath(string oid)
        {
            if (!IsValidOid(oid))
            {
                throw new ArgumentException("Invalid LFS object id", "oid");
            }
            return Path.Combine(_objectsDirectory, oid.Substring(0, 2), oid.Substring(2, 2), oid);
        }

        public bool TryGetSize(string oid, out long size)
        {
            size = 0;
            if (!IsValidOid(oid))
            {
                return false;
            }
            var file = new FileInfo(GetObjectPath(oid));
            if (!file.Exists)
            {
                return false;
            }
            size = file.Length;
            return true;
        }

        /// <summary>
        /// Stores the content under <paramref name="oid"/>. The content goes to a temporary file first, and only an object whose
        /// SHA-256 (and size, when <paramref name="expectedSize"/> is not negative) matches is moved to its final place:
        /// a broken or interrupted upload never leaves a corrupt object behind.
        /// </summary>
        public LfsSaveResult Save(string oid, Stream content, long expectedSize)
        {
            string finalPath = GetObjectPath(oid);   // validates the oid

            Directory.CreateDirectory(_tempDirectory);
            string tempPath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N") + ".part");
            try
            {
                long length = 0;
                string actualOid;
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            hash.AppendData(buffer, 0, read);
                            file.Write(buffer, 0, read);
                            length += read;
                        }
                    }
                    actualOid = ToHex(hash.GetHashAndReset());
                }

                if (!string.Equals(actualOid, oid, StringComparison.Ordinal))
                {
                    return LfsSaveResult.HashMismatch;
                }
                if (expectedSize >= 0 && length != expectedSize)
                {
                    return LfsSaveResult.SizeMismatch;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
                if (File.Exists(finalPath))
                {
                    return LfsSaveResult.AlreadyStored;
                }
                try
                {
                    File.Move(tempPath, finalPath);
                }
                catch (IOException)
                {
                    // Another request stored the same object between the check and the move: same content, so this is fine
                    if (File.Exists(finalPath))
                    {
                        return LfsSaveResult.AlreadyStored;
                    }
                    throw;
                }
                return LfsSaveResult.Stored;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (IOException)
                {
                    // left in lfs/tmp, harmless
                }
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var chars = new char[bytes.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 0xF];
            }
            return new string(chars);
        }
    }
}
