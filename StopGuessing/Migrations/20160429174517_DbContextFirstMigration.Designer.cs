using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using StopGuessing.AccountStorage.Sql;

namespace StopGuessing.Migrations
{
    [DbContext(typeof(DbUserAccountContext))]
    [Migration("20160429174517_DbContextFirstMigration")]
    partial class DbContextFirstMigration
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "7.0.0-rc1-16348")
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("StopGuessing.Azure.DbUserAccount", b =>
                {
                    b.Property<string>("DbUserAccountId");

                    b.Property<TimeSpan>("CreditHalfLife");

                    b.Property<double>("CreditLimit");

                    b.Property<byte[]>("EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1");

                    b.Property<byte[]>("EcPublicAccountLogKey");

                    b.Property<int>("NumberOfIterationsToUseForPhase1Hash");

                    b.Property<string>("PasswordHashPhase1FunctionName");

                    b.Property<string>("PasswordHashPhase2");

                    b.Property<byte[]>("SaltUniqueToThisAccount");

                    b.HasKey("DbUserAccountId");

                    b.HasIndex("DbUserAccountId")
                        .IsUnique();
                });

            modelBuilder.Entity("StopGuessing.Azure.DbUserAccountCreditBalance", b =>
                {
                    b.Property<string>("DbUserAccountId");

                    b.Property<DateTime?>("ConsumedCreditsLastUpdatedUtc")
                        .IsConcurrencyToken();

                    b.Property<double>("ConsumedCreditsLastValue")
                        .IsConcurrencyToken();

                    b.HasKey("DbUserAccountId");

                    b.HasIndex("DbUserAccountId")
                        .IsUnique();
                });
        }
    }
}
