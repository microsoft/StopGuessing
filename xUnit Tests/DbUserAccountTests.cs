using Xunit;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Entity;
using Microsoft.Data.Entity.Infrastructure;
//using Microsoft.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using StopGuessing.Azure;
using StopGuessing.Clients;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;

// Since we're testing a live database, make sure we run in unit tests in serial mode
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace xUnit_Tests
{
    public class DbUserAccountTests
    {

        public IConfigurationRoot Configuration { get; set; }
        public CloudStorageAccount _CloudStorageAccount;
        //public DbUserAccountController userAccountController;
        //public DbUserAccountControllerFactory userAccountControllerFactory;
        public DbUserAccountController userAccountController;
        public DbUserAccountControllerFactory userAccountControllerFactory;
        public IUserAccountRepositoryFactory<DbUserAccount> _UserAccountRepositoryFactory;
        public LoginAttemptController<DbUserAccount> _loginAttemptController;
        public BlockingAlgorithmOptions _options;
        private string sqlConnectionString;
        private DbContextOptions<DbUserAccountContext> dbOptions;

        public void Init()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath("")
                .AddJsonFile("appsettings.json");

            builder.AddEnvironmentVariables();
            Configuration = builder.Build();

            sqlConnectionString = Configuration["Data:ConnectionString"];
            string cloudStorageConnectionString = Configuration["Data:StorageConnectionString"];

            _options = new BlockingAlgorithmOptions();

            DbContextOptionsBuilder<DbUserAccountContext> dbOptionsBuilder = new DbContextOptionsBuilder<DbUserAccountContext>();
            dbOptionsBuilder.UseSqlServer(sqlConnectionString);
            dbOptions = dbOptionsBuilder.Options;

            _CloudStorageAccount = CloudStorageAccount.Parse(cloudStorageConnectionString);
            userAccountControllerFactory = new DbUserAccountControllerFactory(_CloudStorageAccount, dbOptions);
            userAccountController = userAccountControllerFactory.CreateDbUserAccountController();

            RemoteHost localHost = new RemoteHost { Uri = new Uri("http://localhost:35358") };
            MaxWeightHashing<RemoteHost> hosts = new MaxWeightHashing<RemoteHost>(Configuration["Data:UniqueConfigurationSecretPhrase"]);

            LoginAttemptClient<DbUserAccount> loginAttemptClient = new LoginAttemptClient<DbUserAccount>(hosts, localHost);

            _UserAccountRepositoryFactory = new DbUserAccountRepositoryFactory(dbOptions);

            BinomialLadderSketch localPasswordBinomialLadderSketch = new BinomialLadderSketch(
                _options.NumberOfElementsInBinomialLadderSketch_N, _options.HeightOfBinomialLadder_H);

            _loginAttemptController = new LoginAttemptController<DbUserAccount>(
                userAccountControllerFactory, _UserAccountRepositoryFactory,
                localPasswordBinomialLadderSketch,
                new MemoryUsageLimiter(), _options);

            //services.AddEntityFramework()
            //    .AddSqlServer()
            //    .AddDbContext<DbUserAccountContext>(opt => opt.UseSqlServer(sqlConnectionString));
            //DbUserAccountContext context = new DbUserAccountContext();

            //var db = new DbContextOptionsBuilder();
            //db.UseInMemoryDatabase();
            //_context = new MyContext(db.Options);

        }
        

        [Fact]
        public async Task CookieUsedInPriorLoginAsync()
        {
            Init();
            string username = "Keyser Söze";
            string password = "Kobaya$hi";
            DbUserAccount account = userAccountController.Create(username, password);
            string randomCookiesHash = Convert.ToBase64String(StrongRandomNumberGenerator.GetBytes(16));
            bool cookieAlreadyPresentOnFirstTest = await
                userAccountController.HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(account, randomCookiesHash);
            Assert.False(cookieAlreadyPresentOnFirstTest);
            await userAccountController.RecordHashOfDeviceCookieUsedDuringSuccessfulLoginAsync(account, randomCookiesHash);
            bool cookieAlreadyPresentOnSecondTest = await
                userAccountController.HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(account, randomCookiesHash);
            Assert.True(cookieAlreadyPresentOnSecondTest);
        }

        [Fact]
        public async Task AddIncorrectPhase2HashAsync()
        {
            Init();
            string username = "Keyser Söze";
            string password = "Kobaya$hi";
            DbUserAccount account = userAccountController.Create(username, password);
            string incorrectPasswordHash = Convert.ToBase64String(StrongRandomNumberGenerator.GetBytes(16));
            bool incorrectPasswordAlreadyPresentOnFirstTest = await
                userAccountController.AddIncorrectPhaseTwoHashAsync(account, incorrectPasswordHash);
            Assert.False(incorrectPasswordAlreadyPresentOnFirstTest);
            // Since the hash is added via a background task to minimize response latency, we'll want to
            // wait to be sure it's added
            Thread.Sleep(1000);
            bool incorrectPasswordPresentOnSecondTest = await
                userAccountController.AddIncorrectPhaseTwoHashAsync(account, incorrectPasswordHash);
            Assert.True(incorrectPasswordPresentOnSecondTest);
        }

        [Fact]
        public async Task CreateAccountUseCreditDestroyAccount()
        {
            Init();
            IRepository<string, DbUserAccount> userAccountRespository = _UserAccountRepositoryFactory.Create();

            using (DbUserAccountContext ctx = new DbUserAccountContext(dbOptions))
            {
                ctx.Database.ExecuteSqlCommand("DELETE FROM DbUserAccount");
                ctx.Database.ExecuteSqlCommand("DELETE FROM DbUserAccountCreditBalance");
            }

            string username = "Keyser Söze";
            string password = "Kobaya$hi";

            // Make sure that LoadAsync retursn null for an account that doesn't exist yet.
            DbUserAccount accountThatShouldNotExist = await userAccountRespository.LoadAsync(username);
            Assert.Null(accountThatShouldNotExist);

            // Create an add a new account to the database
            DbUserAccount newAccount = userAccountController.Create(username, password);
            newAccount.CreditLimit = 1d;

            await userAccountRespository.AddAsync(newAccount);

            // Load that account back in
            DbUserAccount reloadedAccount = await userAccountRespository.LoadAsync(username);
            Assert.NotNull(reloadedAccount);

            // Try to use half of the credit limit
            double credit = await userAccountController.TryGetCreditAsync(reloadedAccount, 0.5);
            Assert.Equal(credit, 0.5d);

            // Try to use 100 times the credit limit of 1, only to receive ~.5 back
            // (you'll get a tiny amount more as the consumed credit has decayed with time)
            double moreCredit = await userAccountController.TryGetCreditAsync(reloadedAccount, 1000);
            Assert.InRange(moreCredit, 0.5d, 0.51d);

            // Clean up
            using (DbUserAccountContext context = new DbUserAccountContext(dbOptions))
            {
                DbUserAccount account =
                    context.DbUserAccounts.FirstOrDefault(a => a.UsernameOrAccountId == username);

                Assert.NotNull(account);
                
                if (account != null)
                    context.DbUserAccounts.Remove(account);

                await context.SaveChangesAsync();

                DbUserAccountCreditBalance balance =
                    context.DbUserAccountCreditBalances.FirstOrDefault(a => a.DbUserAccountId == username);
                if (balance != null)
                {
                    context.DbUserAccountCreditBalances.Remove(balance);
                    await context.SaveChangesAsync();
                }
            }

        }
       

    }
}
