using Bonobo.Git.Server.Models;
using System;

namespace Bonobo.Git.Server.Data
{
    public partial class SshKey
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Name { get; set; }
        public string KeyType { get; set; }
        public string Fingerprint { get; set; }
        public string PublicKey { get; set; }
        public int KeySize { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUsed { get; set; }

        public virtual User User { get; set; }

        public SshKeyModel ToModel()
        {
            return new SshKeyModel
            {
                Id = Id,
                UserId = UserId,
                Name = Name,
                KeyType = KeyType,
                Fingerprint = Fingerprint,
                PublicKey = PublicKey,
                KeySize = KeySize,
                CreatedAt = CreatedAt,
                LastUsed = LastUsed,
            };
        }
    }
}
