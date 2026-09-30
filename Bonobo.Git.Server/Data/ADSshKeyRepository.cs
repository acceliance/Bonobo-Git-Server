using Bonobo.Git.Server.Configuration;
using Bonobo.Git.Server.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonobo.Git.Server.Data
{
    /// <summary>
    /// SSH keys for the Active Directory backend.
    ///
    /// Unlike users and teams, keys are not mirrored from AD - they are uploaded through Bonobo and
    /// belong to it. They therefore live in their own store alongside the other AD backend files,
    /// rather than being refreshed by ADBackend's periodic sync.
    /// </summary>
    public class ADSshKeyRepository : ISshKeyRepository
    {
        private static readonly Lazy<ADBackendStore<SshKeyModel>> Store =
            new Lazy<ADBackendStore<SshKeyModel>>(() => new ADBackendStore<SshKeyModel>(ActiveDirectorySettings.BackendPath, "SshKeys"));

        private static readonly object WriteLock = new object();

        public IList<SshKeyModel> GetAllKeys()
        {
            return Store.Value.ToList();
        }

        public IList<SshKeyModel> GetKeysForUser(Guid userId)
        {
            return Store.Value
                .Where(key => key.UserId == userId)
                .OrderBy(key => key.CreatedAt)
                .ToList();
        }

        public SshKeyModel GetKey(Guid keyId)
        {
            return Store.Value[keyId];
        }

        public SshKeyModel GetKeyByFingerprint(string fingerprint)
        {
            return Store.Value.FirstOrDefault(key => String.Equals(key.Fingerprint, fingerprint, StringComparison.Ordinal));
        }

        public bool AddKey(SshKeyModel key)
        {
            lock (WriteLock)
            {
                if (GetKeyByFingerprint(key.Fingerprint) != null)
                {
                    return false;
                }

                if (key.Id == Guid.Empty)
                {
                    key.Id = Guid.NewGuid();
                }

                return Store.Value.Add(key);
            }
        }

        public bool RemoveKey(Guid keyId)
        {
            lock (WriteLock)
            {
                return Store.Value.Remove(keyId);
            }
        }

        public void RecordKeyUsed(Guid keyId, DateTime timestamp)
        {
            try
            {
                lock (WriteLock)
                {
                    var key = Store.Value[keyId];
                    if (key == null)
                    {
                        return;
                    }

                    key.LastUsed = timestamp;
                    Store.Value.AddOrUpdate(key);
                }
            }
            catch (Exception ex)
            {
                // Bookkeeping only - never fail a push or fetch because of it
                Log.Warning(ex, "SSH: Could not record last-used time for key {KeyId}", keyId);
            }
        }
    }
}
