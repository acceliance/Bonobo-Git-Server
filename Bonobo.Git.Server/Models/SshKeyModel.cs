using Bonobo.Git.Server.Data;
using System;

namespace Bonobo.Git.Server.Models
{
    /// <summary>
    /// A public key belonging to a user. The key itself is not a secret, but the set of keys is what
    /// grants access, so this is treated as security-relevant data throughout.
    /// </summary>
    public class SshKeyModel : INameProperty
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        /// <summary>The label the user gave the key, e.g. 'Work laptop'</summary>
        public string Name { get; set; }

        /// <summary>Key algorithm, e.g. 'ssh-ed25519'</summary>
        public string KeyType { get; set; }

        /// <summary>The canonical one-line public key, as it is written into authorized_keys</summary>
        public string PublicKey { get; set; }

        /// <summary>OpenSSH SHA256 fingerprint, e.g. 'SHA256:xyz...'</summary>
        public string Fingerprint { get; set; }

        public int KeySize { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>Null until the key has been used to authenticate at least once</summary>
        public DateTime? LastUsed { get; set; }

        public string DisplayName
        {
            get { return Name; }
        }
    }
}
