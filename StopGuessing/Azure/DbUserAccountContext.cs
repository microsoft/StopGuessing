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
        public DbSet<DbUserAccountCreditBalance> DbUserAccountCreditBalances { get; set; }
        //public DbSet<SuccessfulLoginCookieEntity> SuccessfulLoginCookies { get; set; }
        //public DbSet<IncorrectPhaseTwoHashEntity> IncorrectPhaseTwoHashes { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DbUserAccount>().HasIndex(e => e.DbUserAccountId).IsUnique(true);
            modelBuilder.Entity<DbUserAccount>().HasKey(e => e.DbUserAccountId);
            modelBuilder.Entity<DbUserAccount>()
                .Property(e => e.DbUserAccountId)
                .IsRequired();
            modelBuilder.Entity<DbUserAccountCreditBalance>().HasIndex(e => e.DbUserAccountId).IsUnique(true);
            modelBuilder.Entity<DbUserAccountCreditBalance>().HasKey(e => e.DbUserAccountId);
            modelBuilder.Entity<DbUserAccountCreditBalance>()
                .Property(e => e.DbUserAccountId)
                .IsRequired();
            modelBuilder.Entity<DbUserAccountCreditBalance>()
                .Property(e => e.ConsumedCreditsLastValue).IsConcurrencyToken(true);
            modelBuilder.Entity<DbUserAccountCreditBalance>()
                .Property(e => e.ConsumedCreditsLastUpdatedUtc).IsConcurrencyToken(true);
        }
    }
}
