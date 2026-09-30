using Bonobo.Git.Server.Models;
using System;
using System.Collections.Generic;

namespace Bonobo.Git.Server.Data
{
    public interface ISshKeyRepository
    {
        /// <summary>Every key in the system, which is what the authorized_keys file is built from</summary>
        IList<SshKeyModel> GetAllKeys();

        IList<SshKeyModel> GetKeysForUser(Guid userId);

        SshKeyModel GetKey(Guid keyId);

        /// <summary>Used to tell the user that a key they are adding already belongs to somebody</summary>
        SshKeyModel GetKeyByFingerprint(string fingerprint);

        /// <summary>False if the fingerprint is already present - a key may only belong to one user</summary>
        bool AddKey(SshKeyModel key);

        bool RemoveKey(Guid keyId);

        /// <summary>
        /// Records that a key was just used to authenticate. Best-effort: a failure here must never
        /// stop the git operation that triggered it.
        /// </summary>
        void RecordKeyUsed(Guid keyId, DateTime timestamp);
    }
}
