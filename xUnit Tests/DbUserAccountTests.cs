using Xunit;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Entity;
//using Microsoft.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using StopGuessing.Azure;
using StopGuessing.Clients;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;

namespace xUnit_Tests
{
    public class DbUserAccountTests
    {

        public IConfigurationRoot Configuration { get; set; }
        public CloudStorageAccount _CloudStorageAccount;
        //public DbUserAccountController userAccountController;
        //public DbUserAccountControllerFactory userAccountControllerFactory;
        public IUserAccountController<DbUserAccount> userAccountController;
        public IUserAccountControllerFactory<DbUserAccount> userAccountControllerFactory;
        public IUserAccountRepositoryFactory<DbUserAccount> _UserAccountRepositoryFactory;
        public LoginAttemptController<DbUserAccount> _loginAttemptController;

        public void Init()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath("")
                .AddJsonFile("appsettings.json");

            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
            
            string cloudStorageConnectionString = Configuration["Data:StorageConnectionString"];
            _CloudStorageAccount = CloudStorageAccount.Parse(cloudStorageConnectionString);
            userAccountControllerFactory = new DbUserAccountControllerFactory(_CloudStorageAccount);
            userAccountController = userAccountControllerFactory.Create();

            RemoteHost localHost = new RemoteHost { Uri = new Uri("http://localhost:35358") };
            MaxWeightHashing<RemoteHost> hosts = new MaxWeightHashing<RemoteHost>(Configuration["Data:UniqueConfigurationSecretPhrase"]);

            LoginAttemptClient<DbUserAccount> loginAttemptClient = new LoginAttemptClient<DbUserAccount>(hosts, localHost);

            string sqlConnectionString = Configuration["Data:ConnectionString"];
            _UserAccountRepositoryFactory = new DbUserAccountRepositoryFactory(opt => opt.UseSqlServer(sqlConnectionString));

            BinomialLadderSketch localPasswordBinomialLadderSketch =
                    new BinomialLadderSketch(options.NumberOfElementsInBinomialLadderSketch_N, options.HeightOfBinomialLadder_H);

            _loginAttemptController = new LoginAttemptController<DbUserAccount>(
                userAccountControllerFactory, _UserAccountRepositoryFactory, localPasswordBinomialLadderSketch, null, null);

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
            DbUserAccount account = userAccountController.Create(username, "Kobaya$hi");
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
            DbUserAccount account = userAccountController.Create(username, "Kobaya$hi");
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

       

    }
}
