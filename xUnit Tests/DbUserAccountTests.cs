using Xunit;
using System;
using System.Threading;
using System.Threading.Tasks;
//using Microsoft.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using StopGuessing.Azure;
using StopGuessing.Controllers;
using StopGuessing.EncryptionPrimitives;

namespace xUnit_Tests
{
    public class DbUserAccountTests
    {

        public IConfigurationRoot Configuration { get; set; }
        public CloudStorageAccount _CloudStorageAccount;
        public DbUserAccountController _DbUserAccountController;

        public void Init()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath("")
                .AddJsonFile("appsettings.json");

            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
            
            string cloudStorageConnectionString = Configuration["Data:StorageConnectionString"];
            _CloudStorageAccount = CloudStorageAccount.Parse(cloudStorageConnectionString);
            _DbUserAccountController = new DbUserAccountController(_CloudStorageAccount);
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
            DbUserAccount account = _DbUserAccountController.Create(username, "Kobaya$hi");
            string randomCookiesHash = Convert.ToBase64String(StrongRandomNumberGenerator.GetBytes(16));
            bool cookieAlreadyPresentOnFirstTest = await
                _DbUserAccountController.HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(account, randomCookiesHash);
            Assert.False(cookieAlreadyPresentOnFirstTest);
            await _DbUserAccountController.RecordHashOfDeviceCookieUsedDuringSuccessfulLoginAsync(account, randomCookiesHash);
            bool cookieAlreadyPresentOnSecondTest = await
                _DbUserAccountController.HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(account, randomCookiesHash);
            Assert.True(cookieAlreadyPresentOnSecondTest);
        }

        [Fact]
        public async Task AddIncorrectPhase2HashAsync()
        {
            Init();
            string username = "Keyser Söze";
            DbUserAccount account = _DbUserAccountController.Create(username, "Kobaya$hi");
            string incorrectPasswordHash = Convert.ToBase64String(StrongRandomNumberGenerator.GetBytes(16));
            bool incorrectPasswordAlreadyPresentOnFirstTest = await
                _DbUserAccountController.AddIncorrectPhaseTwoHashAsync(account, incorrectPasswordHash);
            Assert.False(incorrectPasswordAlreadyPresentOnFirstTest);
            // Since the hash is added via a background task to minimize response latency, we'll want to
            // wait to be sure it's added
            Thread.Sleep(1000);
            bool incorrectPasswordPresentOnSecondTest = await
                _DbUserAccountController.AddIncorrectPhaseTwoHashAsync(account, incorrectPasswordHash);
            Assert.True(incorrectPasswordPresentOnSecondTest);
        }

       

    }
}
