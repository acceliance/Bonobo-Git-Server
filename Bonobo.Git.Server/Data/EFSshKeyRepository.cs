using Bonobo.Git.Server.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Linq;
using Unity;

namespace Bonobo.Git.Server.Data
{
    public class EFSshKeyRepository : ISshKeyRepository
    {
        [Dependency]
        public Func<BonoboGitServerContext> CreateContext { get; set; }

        public IList<SshKeyModel> GetAllKeys()
        {
            using (var db = CreateContext())
            {
                return db.SshKeys.ToList().Select(key => key.ToModel()).ToList();
            }
        }

        public IList<SshKeyModel> GetKeysForUser(Guid userId)
        {
            using (var db = CreateContext())
            {
                return db.SshKeys
                    .Where(key => key.UserId == userId)
                    .ToList()
                    .Select(key => key.ToModel())
                    .OrderBy(key => key.CreatedAt)
                    .ToList();
            }
        }

        public SshKeyModel GetKey(Guid keyId)
        {
            using (var db = CreateContext())
            {
                var key = db.SshKeys.FirstOrDefault(k => k.Id == keyId);
                return key == null ? null : key.ToModel();
            }
        }

        public SshKeyModel GetKeyByFingerprint(string fingerprint)
        {
            using (var db = CreateContext())
            {
                var key = db.SshKeys.FirstOrDefault(k => k.Fingerprint == fingerprint);
                return key == null ? null : key.ToModel();
            }
        }

        public bool AddKey(SshKeyModel model)
        {
            using (var db = CreateContext())
            {
                if (db.SshKeys.Any(k => k.Fingerprint == model.Fingerprint))
                {
                    return false;
                }

                db.SshKeys.Add(new SshKey
                {
                    Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
                    UserId = model.UserId,
                    Name = model.Name,
                    KeyType = model.KeyType,
                    Fingerprint = model.Fingerprint,
                    PublicKey = model.PublicKey,
                    KeySize = model.KeySize,
                    CreatedAt = model.CreatedAt,
                    LastUsed = model.LastUsed,
                });

                try
                {
                    db.SaveChanges();
                }
                catch (DbUpdateException ex)
                {
                    // The unique index on Fingerprint is the real guard against two users claiming
                    // the same key; the check above only saves us from the common case.
                    Log.Warning(ex, "SSH: Could not add key {Fingerprint} for user {UserId}", model.Fingerprint, model.UserId);
                    return false;
                }

                return true;
            }
        }

        public bool RemoveKey(Guid keyId)
        {
            using (var db = CreateContext())
            {
                var key = db.SshKeys.FirstOrDefault(k => k.Id == keyId);
                if (key == null)
                {
                    return false;
                }

                db.SshKeys.Remove(key);
                db.SaveChanges();
                return true;
            }
        }

        public void RecordKeyUsed(Guid keyId, DateTime timestamp)
        {
            try
            {
                using (var db = CreateContext())
                {
                    var key = db.SshKeys.FirstOrDefault(k => k.Id == keyId);
                    if (key == null)
                    {
                        return;
                    }

                    key.LastUsed = timestamp;
                    db.SaveChanges();
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
