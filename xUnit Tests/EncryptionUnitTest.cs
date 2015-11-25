using System;
using StopGuessing.Models;
using Xunit;
using StopGuessing.EncryptionPrimitives;

namespace xUnit_Tests
{
    public class EncryptionUnitTest
    {
        [Fact]
        public void AesEncryptionWithIvProvided()
        {
            string plaintextstring = "testestcorrect";
            byte[] plaintext = System.Text.Encoding.Default.GetBytes(plaintextstring);
            byte[] salt = new byte[] { 3, 21, 3, 4, 2, 2, 3, 1 };
            byte[] iv = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            string password = "testpwd1234";
            byte[] keyfrompwd = Encryption.KeyGenFromPwd(password, salt);
            //          using (StopGuessing.encryption cs = new System.Security.Cryptography.CryptoStream(ciphertext, aes.CreateEncryptor(), System.Security.Cryptography.CryptoStreamMode.WriteAccountToStableStoreAsync))
            //          byte[] encrypteddata = EncryptAesCbc(plaintext, key, null, false);
            //          StopGuessing.ECEncryptedMessage_AES_CBC_HMACSHA256 ecen = new StopGuessing.ECEncryptedMessage_AES_CBC_HMACSHA256(key, plaintext);
            byte[] cipertext = Encryption.EncryptAesCbc(plaintext, keyfrompwd, iv, false);
            byte[] decryptedtext = Encryption.DecryptAescbc(cipertext, keyfrompwd, iv, false);
            string decryptedstring = System.Text.Encoding.Default.GetString(decryptedtext);
            Assert.Equal(plaintextstring, decryptedstring);
        }

        [Fact]
        public void AesEncryptionWithoutIvp()
        {
            string plaintextstring = "testestcorrect";
            byte[] plaintext = System.Text.Encoding.Default.GetBytes(plaintextstring);
            byte[] salt = new byte[] { 3, 21, 3, 4, 2, 2, 3, 1 };
            string password = "testpwd1234";
            byte[] keyfrompwd = Encryption.KeyGenFromPwd(password, salt);
            //          using (StopGuessing.encryption cs = new System.Security.Cryptography.CryptoStream(ciphertext, aes.CreateEncryptor(), System.Security.Cryptography.CryptoStreamMode.WriteAccountToStableStoreAsync))
            //          byte[] encrypteddata = EncryptAesCbc(plaintext, key, null, false);
            //          StopGuessing.ECEncryptedMessage_AES_CBC_HMACSHA256 ecen = new StopGuessing.ECEncryptedMessage_AES_CBC_HMACSHA256(key, plaintext);
            byte[] cipertext = Encryption.EncryptAesCbc(plaintext, keyfrompwd, addHmac: false);
            byte[] decryptedtext = Encryption.DecryptAescbc(cipertext, keyfrompwd, checkAndRemoveHmac: false);
            string decryptedstring = System.Text.Encoding.Default.GetString(decryptedtext);
            Assert.Equal(plaintextstring, decryptedstring);
        }

        [Fact]
        public void AesEncryptionWithoutIvpWithMac()
        {
            string plaintextstring = "testestcorrect";
            byte[] plaintext = System.Text.Encoding.Default.GetBytes(plaintextstring);
            byte[] salt = new byte[] { 3, 21, 3, 4, 2, 2, 3, 1 };
            string password = "testpwd1234";
            byte[] keyfrompwd = Encryption.KeyGenFromPwd(password, salt);
            //          using (StopGuessing.encryption cs = new System.Security.Cryptography.CryptoStream(ciphertext, aes.CreateEncryptor(), System.Security.Cryptography.CryptoStreamMode.WriteAccountToStableStoreAsync))
            //          byte[] encrypteddata = EncryptAesCbc(plaintext, key, null, false);
            //          StopGuessing.ECEncryptedMessage_AES_CBC_HMACSHA256 ecen = new StopGuessing.ECEncryptedMessage_AES_CBC_HMACSHA256(key, plaintext);
            byte[] cipertext = Encryption.EncryptAesCbc(plaintext, keyfrompwd, addHmac: true);
            byte[] decryptedtext = Encryption.DecryptAescbc(cipertext, keyfrompwd, checkAndRemoveHmac: true);
            string decryptedstring = System.Text.Encoding.Default.GetString(decryptedtext);
            Assert.Equal(plaintextstring, decryptedstring);
        }

        [Fact]
        public void Testserilization()
        {
            DateTime utcNow = DateTime.UtcNow;
            
            LoginAttempt attempt = new LoginAttempt()
            {
                TimeOfAttemptUtc = utcNow
            };
            string serialized = Newtonsoft.Json.JsonConvert.SerializeObject(attempt);
            LoginAttempt deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<LoginAttempt>(serialized);
            DateTimeOffset deserializedTimeOfAttempt = deserialized.TimeOfAttemptUtc;
            Assert.Equal(utcNow, deserializedTimeOfAttempt);
        }


        //      public UserAccount<UserAccountIdentifier, LoginAPIType, PasswordPolicyType> CreateTestAccountAsync<UserAccountIdentifier, LoginAPIType, PasswordPolicyType>(UserAccountIdentifier UsernameOrAccountID, string password) where UserAccountIdentifier : IComparable
        //      {
        //          StopGuessing.UserAccount<UserAccountIdentifier, LoginAPIType, PasswordPolicyType> Account1 = new StopGuessing.UserAccount<UserAccountIdentifier, LoginAPIType, PasswordPolicyType>();
        //          //Account1.UsernameOrAccountID = "user1";
        //         // string password = "testabcd1234";
        //          Account1.SaltUniqueToThisAccount = new byte[] { 0, 21, 3, 4, 2, 2, 3, 1 };
        //          byte[] KeyFromPWD = StopGuessing.Encryption.KeyGenFromPWD(password, Account1.SaltUniqueToThisAccount);
        //          ECDiffieHellmanCng ecdha = new ECDiffieHellmanCng(CngKey.Create(CngAlgorithm.ECDiffieHellmanP256, null, new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport }));
        //          ECDiffieHellmanCng ecdhb = new ECDiffieHellmanCng(CngKey.Create(CngAlgorithm.ECDiffieHellmanP256, null, new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport }));
        //          byte[] aprivateKey = ecdha.Key.Export(CngKeyBlobFormat.EccPrivateBlob);
        //          byte[] apublicKey = ecdha.Key.Export(CngKeyBlobFormat.EccPublicBlob);
        //          byte[] bprivateKey = ecdha.Key.Export(CngKeyBlobFormat.EccPrivateBlob);
        //          byte[] bpublicKey = ecdha.Key.Export(CngKeyBlobFormat.EccPublicBlob);
        //          //Account1.ECPublicAccountLogKey = ECDiffieHellmanCngPublicKey.FromByteArray(apublicKey, CngKeyBlobFormat.EccPublicBlob);
        //          ECDiffieHellmanCng ecdhacng = new ECDiffieHellmanCng();
        //          Account1.ECPublicAccountLogKey = ecdhacng.PublicKey;
        //          AccountsPasswordVerificationFailure AccoutFailure = new AccountsPasswordVerificationFailure();
        //          Account1.PasswordVerificationFailures =
        //              new SequenceTest<EncryptedAccountPasswordVerificationFailure>(100);
        //          //    new SequenceTest<EncryptedAccountPasswordVerificationFailure>(Account1.ECPublicAccountLogKey, AccoutFailure);
        //          /*
        //          EncryptedAccountPasswordVerificationFailure failure = new EncryptedAccountPasswordVerificationFailure(
        //                  ECPublicAccountLogKey,
        //                  new AccountsPasswordVerificationFailure() { ClientIPAddress = ClientsIP, PasswordProvidedByClient = PasswordProvidedByClient }
        //                  );
        //          PasswordVerificationFailures.Add(failure);
        //*/

        //          byte[] iv = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        //          Account1.ECPrivateAccountLogKeyEncryptedWithPasswordHashPhase1 = StopGuessing.Encryption.EncryptAesCbc(aprivateKey, KeyFromPWD, iv, false);

        //          byte[] hash = SHA256.Create().ComputeHash(KeyFromPWD);
        //          Account1.PasswordHashPhase2 = hash;
        //          Account1.PublicKey = ECDiffieHellmanCngPublicKey.FromByteArray(apublicKey, CngKeyBlobFormat.EccPublicBlob);
        //          return Account1;
        //      }

        //[Fact]
        //public void LoginTestTryCorrectPassword()
        //{

        //    //InitializeData();
        //    string usernameOrAccountId = "user1";
        //    string password = "testabcd1234";
        //    //UserAccount<byte[], byte[]> Account1 = CreateTestAccountAsync<string, byte[], byte[]>(UsernameOrAccountID,password);
        //    //StopGuessing.BruteDetection<string, byte[], byte[]> BruteDetectionObject = new StopGuessing.BruteDetection<string, byte[], byte[]>();
        //    UserAccountTracker<byte[], byte[]> userAccountTracker =
        //        new UserAccountTracker<byte[], byte[]>();
        //    UserAccount<byte[], byte[]> account1 = new UserAccount<byte[], byte[]>(usernameOrAccountId, password: password);
        //    userAccountTracker.Add(usernameOrAccountId, account1);
        //    string passwordProvidedByClient = "testabcd1234";
        //    System.Net.IPAddress clientsIp = IPAddress.Parse("192.168.1.1");
        //    //StopGuessing.PasswordPopularityTracker PasswordTracker = new StopGuessing.PasswordPopularityTracker();
        //    //PasswordTracker.ApproximateOccurrencesOfFailedPassword = new StopGuessing.Sketch(100,100,100);
        //    //PasswordTracker.PreciseOccurrencesOfFailedUnsaltedHashedPassword = new Dictionary<byte[], int>();
        //    //PasswordTracker.PreciseOccurrencesOfFailedUnsaltedHashedPassword.Add(System.Text.Encoding.Default.GetBytes(password), 0);
        //    //PasswordTracker.MapOfHighlyPopularUnsaltedHashedPasswordsToPlaintextPasswords = new Dictionary<byte[], string>();


        //    byte[] api = null;
        //    string browserCookie = null;

        //    //StopGuessing.IPTracker<byte[]> IPTracking = new StopGuessing.IPTracker<byte[]>();
        //    //IPTracking.IPBehaviors = new Dictionary<System.Net.IPAddress, IPBehaviorHistory<string, byte[]>> ();
        //    //IPBehaviorHistory<string, byte[]> IPBehavior = new IPBehaviorHistory<string,byte[]>();
        //    //IPBehavior.IsIPAKnownAggregatorThatWeCannotBlock = false;
        //    //IPTracking.IPBehaviors.Add(ClientsIP, IPBehavior);
        //    //  Account1.VerifyPassword(PasswordProvidedByClient, ClientsIP, API, BrowserCookie,
        //    // null, null);
        //    //  Account1.SetPassword("newpwd","testabcd1234");
        //    PasswordPopularityTracker passwordTracker = new PasswordPopularityTracker();
        //    IpTracker<byte[], byte[]> ipTracking = new IpTracker<byte[], byte[]>();
        //    AuthenticationOutcome result = account1.AuthenticateClient(passwordProvidedByClient, clientsIp, api, browserCookie, passwordTracker, userAccountTracker, ipTracking);
        //    Assert.Equal(AuthenticationOutcome.CredentialsValid, result, "False positive for password verification");
        //}

        //StopGuessing.PasswordPopularityTracker PasswordTracker = new StopGuessing.PasswordPopularityTracker();
        //StopGuessing.IPTracker<byte[], byte[]> IPTracking = new StopGuessing.IPTracker<byte[], byte[]>();
        /*
        public void InitializeData()
        {
            PasswordTracker.ApproximateOccurrencesOfFailedPassword = new StopGuessing.Sketch(100, 100, 100);
            PasswordTracker.PreciseOccurrencesOfFailedUnsaltedHashedPassword = new Dictionary<byte[], int>();
            PasswordTracker.MapOfHighlyPopularUnsaltedHashedPasswordsToPlaintextPasswords = new Dictionary<byte[], string>();
            IPTracking.IPBehaviors = new Dictionary<System.Net.IPAddress, IPBehaviorHistory<string, byte[]>>();
            IPBehaviorHistory<string, byte[]> IPBehavior = new IPBehaviorHistory<string, byte[]>(10);    
        
        }

        */

        //[Fact]
        //public void LoginTestTrywrongPassword()
        //{
        //    //InitializeData();
        //    string usernameOrAccountId = "user1";
        //    string password = "testabcd1234";
        //    //UserAccount<byte[], byte[]> Account1 = CreateTestAccountAsync<string, byte[], byte[]>(UsernameOrAccountID, password);
        //    UserAccountTracker<byte[], byte[]> userAccountTracker =
        //       new UserAccountTracker<byte[], byte[]>();
        //    UserAccount<byte[], byte[]> account1 = new UserAccount<byte[], byte[]>(usernameOrAccountId, password: password);
        //    userAccountTracker.Add(usernameOrAccountId, account1);
        //    string passwordProvidedByClient = "abcd1234";
        //    System.Net.IPAddress clientsIp = IPAddress.Parse("192.168.1.1");

        //    byte[] api = null;
        //    string browserCookie = null;

        //    PasswordPopularityTracker passwordTracker = new PasswordPopularityTracker();
        //    IpTracker<byte[], byte[]> ipTracking = new IpTracker<byte[], byte[]>();
        //    AuthenticationOutcome result = account1.AuthenticateClient(passwordProvidedByClient, clientsIp, api, browserCookie, passwordTracker, userAccountTracker, ipTracking);
        //    Assert.Equal(AuthenticationOutcome.CredentialsInvalidIncorrectPassword, result, "False negative for password verification");
        //}



        public string RandomString()
        {
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()-_+=";
            var stringChars = new char[8];
            var random = new Random();

            for (int i = 0; i < stringChars.Length; i++)
            {
                stringChars[i] = chars[random.Next(chars.Length)];
            }

            string finalString = new String(stringChars);
            return finalString;

        }

        //public AuthenticationOutcome TryCorrectPassword(System.Net.IPAddress clientsIp, string browserCookie, PasswordPopularityTracker passwordTracker, IpTracker<byte[], byte[]> ipTracking)
        //{

        //    //InitializeData();
        //    Random rnd1 = new Random(1);
        //    Random rnd2 = new Random(2);
        //    byte[] bytes1 = new byte[10];
        //    byte[] bytes2 = new byte[10];
        //    rnd1.NextBytes(bytes1);
        //    rnd2.NextBytes(bytes2);
        //    string usernameOrAccountId = bytes1.ToString();
        //    string password = bytes2.ToString();
        //    //string UsernameOrAccountID = RandomString();
        //    //string password = RandomString();
        //    UserAccountTracker<byte[], byte[]> userAccountTracker =
        //        new UserAccountTracker<byte[], byte[]>();
        //    UserAccount<byte[], byte[]> account1 = new UserAccount<byte[], byte[]>(usernameOrAccountId, password: password);
        //    userAccountTracker.Add(usernameOrAccountId, account1);
        //    string passwordProvidedByClient = password;


        //    byte[] api = null;

        //    AuthenticationOutcome result = account1.AuthenticateClient(passwordProvidedByClient, clientsIp, api, browserCookie, passwordTracker, userAccountTracker, ipTracking);
        //    return result;

        //}


        //public AuthenticationOutcome TryWrongPassword(System.Net.IPAddress clientsIp, string browserCookie, PasswordPopularityTracker passwordTracker, IpTracker<byte[], byte[]> ipTracking)
        //{

        //    //InitializeData();
        //    string usernameOrAccountId = RandomString();
        //    string password = RandomString();

        //    UserAccountTracker<byte[], byte[]> userAccountTracker =
        //        new UserAccountTracker<byte[], byte[]>();
        //    UserAccount<byte[], byte[]> account1 = new UserAccount<byte[], byte[]>(usernameOrAccountId, password: password);
        //    userAccountTracker.Add(usernameOrAccountId, account1);

        //    string passwordProvidedByClient = RandomString();
        //    //StopGuessing.PasswordPopularityTracker PasswordTracker = new StopGuessing.PasswordPopularityTracker();
        //    //PasswordTracker.ApproximateOccurrencesOfFailedPassword = new StopGuessing.Sketch(100,100,100);
        //    //PasswordTracker.PreciseOccurrencesOfFailedUnsaltedHashedPassword = new Dictionary<byte[], int>();
        //    //PasswordTracker.PreciseOccurrencesOfFailedUnsaltedHashedPassword.Add(System.Text.Encoding.Default.GetBytes(password), 0);
        //    //PasswordTracker.MapOfHighlyPopularUnsaltedHashedPasswordsToPlaintextPasswords = new Dictionary<byte[], string>();


        //    byte[] api = null;


        //    //StopGuessing.IPTracker<byte[]> IPTracking = new StopGuessing.IPTracker<byte[]>();
        //    //IPTracking.IPBehaviors = new Dictionary<System.Net.IPAddress, IPBehaviorHistory<string, byte[]>> ();
        //    //IPBehaviorHistory<string, byte[]> IPBehavior = new IPBehaviorHistory<string,byte[]>();
        //    //IPBehavior.IsIPAKnownAggregatorThatWeCannotBlock = false;
        //    //IPTracking.IPBehaviors.Add(ClientsIP, IPBehavior);
        //    //  Account1.VerifyPassword(PasswordProvidedByClient, ClientsIP, API, BrowserCookie,
        //    // null, null);
        //    //  Account1.SetPassword("newpwd","testabcd1234");
        //    AuthenticationOutcome result = account1.AuthenticateClient(passwordProvidedByClient, clientsIp, api, browserCookie, passwordTracker, userAccountTracker, ipTracking);
        //    return result;

        //}

        /*
        public bool LoginTestTrywrongAccount(System.Net.IPAddress ClientsIP, string BrowserCookie)
        {

            //InitializeData();
            string UsernameOrAccountID = RandomString();
            string password = RandomString();
            UserAccount<byte[], byte[]> Account1 = CreateTestAccountAsync<string, byte[], byte[]>(UsernameOrAccountID, password);
            string PasswordProvidedByClient = RandomString();
            //StopGuessing.PasswordPopularityTracker PasswordTracker = new StopGuessing.PasswordPopularityTracker();
            //PasswordTracker.ApproximateOccurrencesOfFailedPassword = new StopGuessing.Sketch(100,100,100);
            //PasswordTracker.PreciseOccurrencesOfFailedUnsaltedHashedPassword = new Dictionary<byte[], int>();
            //PasswordTracker.PreciseOccurrencesOfFailedUnsaltedHashedPassword.Add(System.Text.Encoding.Default.GetBytes(password), 0);
            //PasswordTracker.MapOfHighlyPopularUnsaltedHashedPasswordsToPlaintextPasswords = new Dictionary<byte[], string>();


            byte[] API = null;


            //StopGuessing.IPTracker<byte[]> IPTracking = new StopGuessing.IPTracker<byte[]>();
            //IPTracking.IPBehaviors = new Dictionary<System.Net.IPAddress, IPBehaviorHistory<string, byte[]>> ();
            //IPBehaviorHistory<string, byte[]> IPBehavior = new IPBehaviorHistory<string,byte[]>();
            //IPBehavior.IsIPAKnownAggregatorThatWeCannotBlock = false;
            //IPTracking.IPBehaviors.Add(ClientsIP, IPBehavior);
            //  Account1.VerifyPassword(PasswordProvidedByClient, ClientsIP, API, BrowserCookie,
            // null, null);
            //  Account1.SetPassword("newpwd","testabcd1234");
            bool result = Account1.VerifyPassword(PasswordProvidedByClient, ClientsIP, API, BrowserCookie, PasswordTracker, IPTracking);
            return result;

        //}
        */

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
        //public void BloomFilterTest()
        //{
        //    int capacity = 2000000;
        //    StopGuessing.DataStructures.Filter<string>  filter = new StopGuessing.DataStructures.Filter<string>(capacity);
        //    filter.Add("content");
        //    bool result = filter.Contains("content");
        //    Assert.Equal(true, result, "False negative for password verification");


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
