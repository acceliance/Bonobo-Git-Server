using System.Data.Entity.ModelConfiguration;

namespace Bonobo.Git.Server.Data.Mapping
{
    public class SshKeyMap : EntityTypeConfiguration<SshKey>
    {
        public SshKeyMap()
        {
            SetPrimaryKey();
            SetProperties();
            SetRelationships();
            SetTableAndColumnMappings();
        }


        private void SetTableAndColumnMappings()
        {
            ToTable("SshKey");
            Property(t => t.Id).HasColumnName("Id");
            Property(t => t.UserId).HasColumnName("User_Id");
            Property(t => t.Name).HasColumnName("Name");
            Property(t => t.KeyType).HasColumnName("KeyType");
            Property(t => t.Fingerprint).HasColumnName("Fingerprint");
            Property(t => t.PublicKey).HasColumnName("PublicKey");
            Property(t => t.KeySize).HasColumnName("KeySize");
            Property(t => t.CreatedAt).HasColumnName("CreatedAt");
            Property(t => t.LastUsed).HasColumnName("LastUsed");
        }

        private void SetRelationships()
        {
            HasRequired(t => t.User)
                .WithMany(t => t.SshKeys)
                .HasForeignKey(t => t.UserId)
                .WillCascadeOnDelete(true);
        }

        private void SetProperties()
        {
            Property(t => t.Name)
                .IsRequired()
                .HasMaxLength(255);

            Property(t => t.KeyType)
                .IsRequired()
                .HasMaxLength(64);

            // The SHA256 form is 50 characters, but leave room for anything longer in future
            Property(t => t.Fingerprint)
                .IsRequired()
                .HasMaxLength(255);

            Property(t => t.PublicKey)
                .IsRequired();
        }

        private void SetPrimaryKey()
        {
            HasKey(t => t.Id);
        }
    }
}
