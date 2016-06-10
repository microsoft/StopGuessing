using System;
using System.Collections.Generic;
using Microsoft.Data.Entity.Migrations;

namespace StopGuessing.Migrations
{
    public partial class DbContextFirstMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DbUserAccount",
                columns: table => new
                {
                    DbUserAccountId = table.Column<string>(nullable: false),
                    CreditHalfLife = table.Column<TimeSpan>(nullable: false),
                    CreditLimit = table.Column<double>(nullable: false),
                    EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1 = table.Column<byte[]>(nullable: true),
                    EcPublicAccountLogKey = table.Column<byte[]>(nullable: true),
                    NumberOfIterationsToUseForPhase1Hash = table.Column<int>(nullable: false),
                    PasswordHashPhase1FunctionName = table.Column<string>(nullable: true),
                    PasswordHashPhase2 = table.Column<string>(nullable: true),
                    SaltUniqueToThisAccount = table.Column<byte[]>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DbUserAccount", x => x.DbUserAccountId);
                });
            migrationBuilder.CreateTable(
                name: "DbUserAccountCreditBalance",
                columns: table => new
                {
                    DbUserAccountId = table.Column<string>(nullable: false),
                    ConsumedCreditsLastUpdatedUtc = table.Column<DateTime>(nullable: true),
                    ConsumedCreditsLastValue = table.Column<double>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DbUserAccountCreditBalance", x => x.DbUserAccountId);
                });
            migrationBuilder.CreateIndex(
                name: "IX_DbUserAccount_DbUserAccountId",
                table: "DbUserAccount",
                column: "DbUserAccountId",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_DbUserAccountCreditBalance_DbUserAccountId",
                table: "DbUserAccountCreditBalance",
                column: "DbUserAccountId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("DbUserAccount");
            migrationBuilder.DropTable("DbUserAccountCreditBalance");
        }
    }
}
