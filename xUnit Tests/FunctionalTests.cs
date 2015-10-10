using System;
using StopGuessing.Models;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using System.Threading;
using Microsoft.Framework.OptionsModel;
using StopGuessing;
using StopGuessing.Clients;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;

namespace xUnit_Tests
{
    public class FunctionalTests
    {
        public IDistributedResponsibilitySet<RemoteHost> MyResponsibleHosts;
        private IStableStore _stableStore;
        public SelfLoadingCache<IPAddress, IpHistory> MyIpHistoryCache;
        public PasswordPopularityTracker MyPasswordTracker;
        public static FixedSizeLruCache<string, LoginAttempt> MyCacheOfRecentLoginAttempts;
        public Dictionary<string, Task<LoginAttempt>> MyLoginAttemptsInProgress;
        //public LoginAttemptController MyLoginAttemptController;
        public UserAccountController MyUserAccountController;
        public UserAccountClient MyUserAccountClient;
        public LoginAttemptClient MyLoginAttemptClient;
        public SelfLoadingCache<string, UserAccount> MyUserAccountCache;

        public void InitTest()
        {
            LimitPerTimePeriod[] creditLimits = new[]
            {
                // 3 per hour
                new LimitPerTimePeriod(new TimeSpan(1, 0, 0), 3f),
                // 6 per day (24 hours, not calendar day)
                new LimitPerTimePeriod(new TimeSpan(1, 0, 0, 0), 6f),
                // 10 per week
                new LimitPerTimePeriod(new TimeSpan(6, 0, 0, 0), 10f),
                // 15 per month
                new LimitPerTimePeriod(new TimeSpan(30, 0, 0, 0), 15f)
            };

           LoginAttemptController MyLoginAttemptController;

        MyResponsibleHosts = new MaxWeightHashing<RemoteHost>("FIXME-uniquekeyfromconfig");
            MyResponsibleHosts.Add("localhost", new RemoteHost { Uri = new Uri("http://localhost:80"), IsLocalHost = true });
            _stableStore = new MemoryOnlyStableStore();
            MyUserAccountCache =
                new SelfLoadingCache<string, UserAccount>(_stableStore.ReadAccountAsync);
            MyIpHistoryCache = new SelfLoadingCache<IPAddress, IpHistory>(
                (id, cancellationToken) =>
                {
                    return Task.Run(() => new IpHistory(id), cancellationToken);
                }

                ); // FIXME with loader
            MyPasswordTracker = new PasswordPopularityTracker(thresholdRequiredToTrackPreciseOccurrences: 10); // FIXME with param
            MyCacheOfRecentLoginAttempts = new FixedSizeLruCache<string, LoginAttempt>(80000);
            MyLoginAttemptsInProgress = new Dictionary<string, Task<LoginAttempt>>();

            MyUserAccountClient = new UserAccountClient(MyResponsibleHosts);
            MyLoginAttemptClient = new LoginAttemptClient(MyResponsibleHosts);

            List<ConfigureOptions<BlockingAlgorithmOptions>> config =
                new List<ConfigureOptions<BlockingAlgorithmOptions>>
                {
                    new ConfigureOptions<BlockingAlgorithmOptions>(bao => { })
                };
            OptionsManager<BlockingAlgorithmOptions> blockingOptions = new OptionsManager<BlockingAlgorithmOptions>(config);
            MyUserAccountController = new UserAccountController(blockingOptions, _stableStore, MyUserAccountCache, creditLimits);
            MyLoginAttemptController = 
                new LoginAttemptController(blockingOptions, _stableStore, MyPasswordTracker,
                MyCacheOfRecentLoginAttempts, MyLoginAttemptsInProgress, MyIpHistoryCache);

            MyUserAccountController.SetLoginAttemptClient(MyLoginAttemptClient);
            MyUserAccountClient.SetUserAccountController(MyUserAccountController);

            MyLoginAttemptController.SetUserAccountClient(MyUserAccountClient);
            MyLoginAttemptClient.SetLoginAttemptController(MyLoginAttemptController);
            //MyLoginAttemptController
        }

        public UserAccount LoginTestCreateAccount(string usernameOrAccountId, string password)
        {
            UserAccount account = MyUserAccountController.CreateUserAccount(usernameOrAccountId, password);
            MyUserAccountController.PutAsync(account.UsernameOrAccountId, account).Wait();
            return account;
        }

        public string[] CreateUserAccounts(int numberOfAccounts)
        {
            string[] usernames = Enumerable.Range(1, numberOfAccounts).Select(x => "testuser" + x.ToString()).ToArray();
            foreach (string username in usernames)
                LoginTestCreateAccount(username, "passwordfor" + username);
            return usernames;
        }

        public async Task<LoginAttempt> AuthenticateAsync(string username, string password,
            IPAddress clientAddress = null,
            IPAddress serverAddress = null,
            string api = "web",
            string cookieProvidedByBrowser = null,
            DateTimeOffset? eventTime = null,
            CancellationToken cancellationToken = default(CancellationToken)
            )
        {
            clientAddress = clientAddress ?? new IPAddress(new byte[] {42, 42, 42, 42});
            serverAddress = serverAddress ?? new IPAddress(new byte[] {127, 1, 1, 1});


            LoginAttempt attempt = new LoginAttempt
            {
                Account = username,
                AddressOfClientInitiatingRequest = clientAddress,
                AddressOfServerThatInitiallyReceivedLoginAttempt = serverAddress,
                TimeOfAttempt = eventTime ?? DateTimeOffset.Now,
                Api = api,
                CookieProvidedByBrowser = cookieProvidedByBrowser
            };

            return await MyLoginAttemptClient.PutAsync(attempt, password, cancellationToken);
        }


        const string Username1 = "user1";
        const string Password1 = "testabcd1234";
        private const string PopularPassword = "p@ssword";
        protected IPAddress ClientsIp = new IPAddress(new byte[] { 42, 42, 42, 42 });
        protected IPAddress AttackersIp = new IPAddress(new byte[] { 66, 66, 66, 66 });
        protected IPAddress AnotherAttackersIp = new IPAddress(new byte[] { 166, 66, 66, 66 });

        [Fact]
        public async Task LoginTestTryCorrectPassword()
        {
            InitTest();

            LoginTestCreateAccount(Username1, Password1);

            LoginAttempt attempt = await AuthenticateAsync(Username1, Password1);
            
            Assert.Equal(AuthenticationOutcome.CredentialsValid, attempt.Outcome);
        }

        [Fact]
        public async Task LoginWithInvalidPassword()
        {
            InitTest();
            LoginTestCreateAccount(Username1, Password1);

            LoginAttempt attempt = await AuthenticateAsync(Username1, "wrong", cookieProvidedByBrowser: "GimmeCookie");

            Assert.Equal(AuthenticationOutcome.CredentialsInvalidIncorrectPassword, attempt.Outcome);

            // Try the same wrong password again.  outcome should be CredentialsInvalidRepeatedIncorrectPassword
            LoginAttempt secondAttempt = await AuthenticateAsync(Username1, "wrong", cookieProvidedByBrowser: "GimmeCookie");

            Assert.Equal(AuthenticationOutcome.CredentialsInvalidRepeatedIncorrectPassword, secondAttempt.Outcome);            
        }

        [Fact]
        public async Task LoginWithInvalidAccount()
        {
            InitTest();
            LoginTestCreateAccount(Username1, Password1);
            
            // Try the right password for user1, for a nonexistent user
            LoginAttempt firstAttempt = await AuthenticateAsync("KeyzerSoze", Password1, cookieProvidedByBrowser: "GimmeCookie");
            
            Assert.Equal(AuthenticationOutcome.CredentialsInvalidNoSuchAccount, firstAttempt.Outcome);

            // Repeat of Try the right password for user1, for a nonexistent user
            LoginAttempt secondAttempt = await AuthenticateAsync("KeyzerSoze", Password1, cookieProvidedByBrowser: "GimmeCookie");
            
            Assert.Equal(AuthenticationOutcome.CredentialsInvalidRepeatedNoSuchAccount, secondAttempt.Outcome);
        }

        [Fact]
        public async Task LoginWithIpWithBadReputationAsync()
        {
            InitTest();
            string[] usernames = CreateUserAccounts(200);
            LoginTestCreateAccount(Username1, Password1);

            // Have one attacker make the password popular by attempting to login to every account with it.
            foreach (string username in usernames.Skip(10))
                await AuthenticateAsync(username, PopularPassword, clientAddress: AttackersIp);

            LoginAttempt firstAttackersAttempt = await AuthenticateAsync(Username1, Password1, clientAddress: AttackersIp);

            Assert.Equal(AuthenticationOutcome.CredentialsValidButBlocked, firstAttackersAttempt.Outcome);

            // Now the second attacker should be flagged after using that password 10 times on different accounts.
            foreach (string username in usernames.Skip(1).Take(9))
                await AuthenticateAsync(username, PopularPassword, AnotherAttackersIp);
        
            await AuthenticateAsync(usernames[0], PopularPassword, AnotherAttackersIp);

            LoginAttempt anotherAttackersAttempt = await AuthenticateAsync(Username1, Password1, clientAddress: AnotherAttackersIp);

            Assert.Equal(AuthenticationOutcome.CredentialsValidButBlocked, anotherAttackersAttempt.Outcome);
        }

        [Fact]
        public async Task LoginWithIpWithBadReputationParallelLoadAsync()
        {
            InitTest();
            string[] usernames = CreateUserAccounts(200);
            LoginTestCreateAccount(Username1, Password1);

            // Have one attacker make the password popular by attempting to login to every account with it.
            Parallel.ForEach(usernames.Skip(10), username =>
                AuthenticateAsync(username, PopularPassword, clientAddress: AttackersIp).Wait());

            LoginAttempt firstAttackersAttempt = await AuthenticateAsync(Username1, Password1, clientAddress: AttackersIp);

            Assert.Equal(AuthenticationOutcome.CredentialsValidButBlocked, firstAttackersAttempt.Outcome);

            // Now the second attacker should be flagged after using that password 10 times on different accounts.
            foreach (string username in usernames.Skip(1).Take(9))
                await AuthenticateAsync(username, PopularPassword, AnotherAttackersIp);

            await AuthenticateAsync(usernames[0], PopularPassword, AnotherAttackersIp);

            LoginAttempt anotherAttackersAttempt = await AuthenticateAsync(Username1, Password1, clientAddress: AnotherAttackersIp);

            Assert.Equal(AuthenticationOutcome.CredentialsValidButBlocked, anotherAttackersAttempt.Outcome);
        }

        [Fact]
        public async Task LoginWithIpWithMixedReputationAsync()
        {
            InitTest();
            string[] usernames = CreateUserAccounts(500);
            LoginTestCreateAccount(Username1, Password1);

            // Have one attacker make the password popular by attempting to login to every account with it.
            foreach (string username in usernames.Skip(100))
                await AuthenticateAsync(username, PopularPassword, clientAddress: AttackersIp);

            // Now have our client get the correct password half the time, and the popular incorrect password half the time.
            bool shouldGuessPopular = true;
            foreach (string username in usernames.Take(50))
            {
                await AuthenticateAsync(username, shouldGuessPopular ? PopularPassword : "passwordfor" + username, ClientsIp);
                shouldGuessPopular = !shouldGuessPopular;
            }
            
            LoginAttempt anotherAttackersAttempt = await AuthenticateAsync(Username1, Password1, clientAddress: AnotherAttackersIp);
            Assert.Equal(AuthenticationOutcome.CredentialsValid, anotherAttackersAttempt.Outcome);
        }
        


        //[Fact]

        //public void LoginTestMoreCorrect()
        //{

        //    //InitializeData();
        //    int i = 0;
        //    PasswordPopularityTracker passwordTracker = new PasswordPopularityTracker();
        //    IpTracker<byte[], byte[]> ipTracking = new IpTracker<byte[], byte[]>();

        //    for (i = 0; i < 10; i++)
        //    {
        //        System.Net.IPAddress clientsIp = IPAddress.Parse("192.168.1.1");
        //        string browserCookie = null;
        //        AuthenticationOutcome result = TryCorrectPassword(clientsIp, browserCookie, passwordTracker, ipTracking);
        //        Assert.Equal(AuthenticationOutcome.CredentialsValid, result, "False positive for password verification");
        //    }
        //    //Parallel.For(0, 100, j =>
        //    //{
        //    //    System.Net.IPAddress ClientsIP = IPAddress.Parse("192.168.1.1");
        //    //    string BrowserCookie = null;
        //    //    bool result = LoginTestTrywrongPassword(ClientsIP, BrowserCookie);
        //    //    Assert.Equal(false, result, "False negative for password verification");
        //    //});
        //    //for (i = 0; i < 1; i++)
        //    //{
        //    //    System.Net.IPAddress ClientsIP = IPAddress.Parse("192.168.1.1");
        //    //    string BrowserCookie = null;
        //    //    bool result = LoginTestTryCorrectPassword(ClientsIP, BrowserCookie);
        //    //    Assert.Equal(true, result, "False positive for password verification");
        //    //}   

        //}


        //[Fact]
        //public void LoginTestMoreWrong()
        //{

        //    //InitializeData();
        //    int i = 0;
        //    PasswordPopularityTracker passwordTracker = new PasswordPopularityTracker();
        //    IpTracker<byte[], byte[]> ipTracking = new IpTracker<byte[], byte[]>();

        //    //Parallel.For(0, 100, j =>
        //    //{
        //    //    System.Net.IPAddress ClientsIP = IPAddress.Parse("192.168.1.1");
        //    //    string BrowserCookie = null;
        //    //    bool result = LoginTestTrywrongPassword(ClientsIP, BrowserCookie);
        //    //    Assert.Equal(false, result, "False negative for password verification");
        //    //});
        //    for (i = 0; i < 10; i++)
        //    {
        //        System.Net.IPAddress clientsIp = IPAddress.Parse("192.168.1.1");
        //        string browserCookie = null;
        //        AuthenticationOutcome result = TryWrongPassword(clientsIp, browserCookie, passwordTracker, ipTracking);
        //        Assert.Equal(AuthenticationOutcome.CredentialsInvalidIncorrectPassword, result, "False negative for password verification");
        //    }

        //}

        //[Fact]
        //public void DuplicateCorrectLogin()
        //{

        //    string usernameOrAccountId = RandomString();
        //    string password = RandomString();
        //    PasswordPopularityTracker passwordTracker = new PasswordPopularityTracker();
        //    IpTracker<byte[], byte[]> ipTracking = new IpTracker<byte[], byte[]>();
        //    System.Net.IPAddress clientsIp = IPAddress.Parse("192.168.1.1");
        //    string browserCookie = null;

        //    UserAccountTracker<byte[], byte[]> userAccountTracker =
        //        new UserAccountTracker<byte[], byte[]>();
        //    UserAccount<byte[], byte[]> account1 = new UserAccount<byte[], byte[]>(usernameOrAccountId, password: password);
        //    userAccountTracker.Add(usernameOrAccountId, account1);
        //    string passwordProvidedByClient = password;


        //    byte[] api = null;



        //    account1.AuthenticateClient(passwordProvidedByClient, clientsIp, api, browserCookie, passwordTracker, userAccountTracker, ipTracking);
        //    account1.AuthenticateClient(passwordProvidedByClient, clientsIp, api, browserCookie, passwordTracker, userAccountTracker, ipTracking);


        //}

        //[Fact]

        //public void ParallelLoginTestWrong()
        //{

        //    //InitializeData();
        //    PasswordPopularityTracker passwordTracker = new PasswordPopularityTracker();
        //    IpTracker<byte[], byte[]> ipTracking = new IpTracker<byte[], byte[]>();

        //    Parallel.For(0, 1000, j =>
        //    {
        //        System.Net.IPAddress clientsIp = IPAddress.Parse("192.168.1.1");
        //        string browserCookie = null;
        //        AuthenticationOutcome result = TryWrongPassword(clientsIp, browserCookie, passwordTracker, ipTracking);
        //        Assert.Equal(AuthenticationOutcome.CredentialsInvalidIncorrectPassword, result, "False negative for password verification");
        //    });


        //}

        //[Fact]
        //public void ParallelLoginTestCorrect()
        //{

        //    //InitializeData();
        //    PasswordPopularityTracker passwordTracker = new PasswordPopularityTracker();
        //    IpTracker<byte[], byte[]> ipTracking = new IpTracker<byte[], byte[]>();

        //    Parallel.For(0, 1500, j =>
        //    {
        //        System.Net.IPAddress clientsIp = IPAddress.Parse("192.168.1.1");
        //        string browserCookie = null;
        //        AuthenticationOutcome result = TryCorrectPassword(clientsIp, browserCookie, passwordTracker, ipTracking);
        //        Assert.Equal(AuthenticationOutcome.CredentialsValid, result, "False positive for password verification");
        //    });

        //}


        //[Fact]
        //public void CreateUserAccountFromPasswordDistribution()
        //{

        //    Dictionary<string, int> passwordDistribution = new Dictionary<string, int>();
        //    Dictionary<long, string> userIdPassword = new Dictionary<long, string>();
        //    //StopGuessing.BruteDetection<string, byte[], byte[]>.UserAccoutPool = new Dictionary<string, UserAccount<byte[], byte[]>>();
        //    //UserIDPassword = new Dictionary<long, string>();

        //    string line;
        //    using (System.IO.StreamReader file = new System.IO.StreamReader(@"..\..\testsmall.txt"))
        //    {
        //        while ((line = file.ReadLine()) != null)
        //        {
        //            string[] words = line.Split(' ');
        //            Console.WriteLine(line);
        //            Console.WriteLine(words[2]);
        //            Console.WriteLine(words[3]);
        //            passwordDistribution.Add(words[3], Int32.Parse(words[2]));

        //        }


        //    }
        //    int counter = 0;
        //    int counterall = 0;
        //    UserAccountTracker<byte[], byte[]> userAccountTracker =
        //    new UserAccountTracker<byte[], byte[]>();
        //    foreach (KeyValuePair<string, int> entry in passwordDistribution)
        //    {
        //        int passwordnumber = entry.Value;

        //        for (counter = 0; counter < passwordnumber / 1000; counter++)
        //        {

        //            userIdPassword.Add(counterall, entry.Key);
        //            string usernameOrAccountId = counterall.ToString();
        //            string password = entry.Key;
        //            UserAccount<byte[], byte[]> account1 = new UserAccount<byte[], byte[]>(usernameOrAccountId, password: password);
        //            userAccountTracker.Add(usernameOrAccountId, account1);
        //            counterall++;

        //        }


        //    }


        //string UsernameOrAccountID = RandomString();
        //string password = RandomString();
        //StopGuessing.UserAccountTracker<byte[], byte[]> UserAccountTracker =
        //    new StopGuessing.UserAccountTracker<byte[], byte[]>();
        //UserAccount<byte[], byte[]> Account1 = new UserAccount<byte[], byte[]>(UsernameOrAccountID, password);
        //UserAccountTracker.Add(Account1);
        //string PasswordProvidedByClient = password;           


        //}




    }
}
