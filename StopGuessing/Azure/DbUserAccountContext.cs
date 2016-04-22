using Microsoft.Data.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StopGuessing.Azure
{
    public class DbUserAccountContext : DbContext
    {
        public DbSet<DbUserAccount> DbUserAccounts { get; set; }
        public DbSet<SuccessfulLoginCookieEntity> SuccessfulLoginCookies { get; set; }
        public DbSet<IncorrectPhaseTwoHashEntity> IncorrectPhaseTwoHashes { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Make Blog.Url required
            modelBuilder.Entity<DbUserAccount>()
                .Property(b => b.UsernameOrAccountId)
                .IsRequired();
        }
    }
}
