using System;
using System.Net;
using System.Threading;
using StopGuessing.Models;
using StopGuessing.EncryptionPrimitives;


namespace Simulator
{
    public class SimulatedLoginAttempt
    {
        public LoginAttempt Attempt;
        public string Password;
        public bool IsPasswordValid;
        public bool IsFromAttacker;
        public bool IsGuess;

        public SimulatedLoginAttempt(SimulatedAccount account,
            string password,
            bool isFromAttacker,
            bool isGuess,
            IPAddress clientAddress,
            string cookieProvidedByBrowser,
            DateTimeOffset eventTime
        )
        {
            string accountId = account != null ? account.UniqueId : StrongRandomNumberGenerator.Get64Bits().ToString();
            bool isPasswordValid = account != null && account.Password == password;

            Attempt = new LoginAttempt
            {
                UsernameOrAccountId = accountId,
                AddressOfClientInitiatingRequest = clientAddress,
                AddressOfServerThatInitiallyReceivedLoginAttempt = new IPAddress(new byte[] {127, 1, 1, 1}),
                TimeOfAttempt = eventTime,
                Api = "web",
                CookieProvidedByBrowser = cookieProvidedByBrowser
            };
            Password = password;
            IsPasswordValid = isPasswordValid;
            IsFromAttacker = isFromAttacker;
            IsGuess = isGuess;
        }
    }

    public partial class Simulator
    {
        /// <summary>
        /// Send one benign login attempts
        /// </summary>
        /// <returns></returns>
        public SimulatedLoginAttempt BenignLoginAttempt()
        {
            //1. Pick a user at random
            SimulatedAccount account = BenignAccountSelector.GetItemByWeightedRandom();

            //2. Deal with cookies
            string cookie;
            // Add a new cookie if there are no cookies, or with if we haven't reached the max number of cookies and lose a roll of the dice
            if (account.Cookies.Count == 0 ||
                (account.Cookies.Count < MyExperimentalConfiguration.MaxCookiesPerUserAccount && StrongRandomNumberGenerator.GetFraction() < MyExperimentalConfiguration.ChanceOfCoookieReUse))
            {
                // We'll use the decimal represenation of a 64-bit unsigned integer as our cookie 
                cookie = StrongRandomNumberGenerator.Get64Bits().ToString();
                account.Cookies.Add(cookie);
            }
            else
            {
                // Use one of the user's existing cookies selected at random
                cookie = account.Cookies.ToArray()[(int)StrongRandomNumberGenerator.Get32Bits(account.Cookies.Count)];
            }

            //Console.WriteLine("The user currently has " + account.Cookies.Count + " cookies.  Using: " + cookie);

            //3. Choose an IP address for the login
            // 1/3 of times login with the primary IP address, otherwise, choose an IP randomly from the benign IP pool
            IPAddress clientIp;
            if (account.ClientAddresses.Count == 0 ||
                (account.ClientAddresses.Count < MyExperimentalConfiguration.MaxIpPerUserAccount && StrongRandomNumberGenerator.GetFraction() < MyExperimentalConfiguration.ChanceOfIpReUse))
            {
                // Use a new IP for the user
                account.ClientAddresses.Add(clientIp = GetNewRandomBenignIp());
            }
            else
            {
                // Use one of the user's existing IP Addresses selected at random
                clientIp = account.ClientAddresses.ToArray()[(int)StrongRandomNumberGenerator.Get32Bits(account.ClientAddresses.Count)];
            }
                        
            string password = account.Password;

            //
            // Add benign failures

            // The benign user may mistype her password causing a typo (Adding a z will meet the edit distance def. of typo)
            if (StrongRandomNumberGenerator.GetFraction() < MyExperimentalConfiguration.ChanceOfBenignPasswordTypo)
            {
                password += "z";
            }
            // The benign user may mistakenly use a password for another of her accounts, which we draw from same distribution
            // we used to generate user account passwords
            if (StrongRandomNumberGenerator.GetFraction() < MyExperimentalConfiguration.ChanceOfAccidentallyUsingAnotherAccountPassword)
            {
                password = GetPasswordFromWeightedDistribution();
            }
            // The benign user may mistype her account name, and land on someone else's account name
            if (StrongRandomNumberGenerator.GetFraction() < MyExperimentalConfiguration.ChanceOfBenignAccountNameTypoResultingInAValidUserName)
            { //2% username typo
                account = GetBenignAccountAtRandomUniform();
            }

            return new SimulatedLoginAttempt(account, password, false, false, clientIp, cookie, DateTimeOffset.Now);

        }

        /// <summary>
        /// Attacker issues one guess by picking an benign account at random and picking a password by weighted distribution
        /// </summary>
        public SimulatedLoginAttempt MaliciousLoginAttemptWeighted()
        {
            SimulatedAccount targetBenignAccount =
                (StrongRandomNumberGenerator.GetFraction() <  MyExperimentalConfiguration.ProbabilityThatAttackerChoosesAnInvalidAccount)
                    ? null : GetBenignAccountAtRandomUniform();

            return new SimulatedLoginAttempt(
                targetBenignAccount,
                GetPasswordFromWeightedDistribution(),
                true, true,
                GetRandomMaliciousIp(),
                StrongRandomNumberGenerator.Get64Bits().ToString(),
                DateTimeOffset.Now);
        }

        private readonly Object _breadthFirstLock = new object();
        private ulong _breadthFirstAttemptCounter;

        /// <summary>
        /// Attacker issues one guess by picking an benign account at random and picking a password by weighted distribution
        /// </summary>
        public SimulatedLoginAttempt MaliciousLoginAttemptBreadthFirst()
        {
            ulong breadthFirstAttemptCount;
            lock (_breadthFirstLock)
            {
                breadthFirstAttemptCount = _breadthFirstAttemptCounter++;
            }

            // Start with the most common password and walk through all the accounts,
            // then move on to the next most common password.
            int passwordIndex = (int) (breadthFirstAttemptCount/(ulong) BenignAccounts.Count);
            int accountIndex = (int) (breadthFirstAttemptCount%(ulong) BenignAccounts.Count);
            string password = OrderedListOfMostCommonPasswords[passwordIndex];
            SimulatedAccount targetBenignAccount = BenignAccounts[accountIndex];

            // Sometimes the attacker will miss and generate an invalid account name;
            if (StrongRandomNumberGenerator.GetFraction() <
                MyExperimentalConfiguration.ProbabilityThatAttackerChoosesAnInvalidAccount)
                targetBenignAccount = null;


            //SimulationTest _simulationtest = new SimulationTest();
            return new SimulatedLoginAttempt(targetBenignAccount, password,
                true, true,
                GetRandomMaliciousIp(),
                StrongRandomNumberGenerator.Get64Bits().ToString(),
                DateTimeOffset.Now);
        }

        /// <summary>
        /// Attacker login with correct accounts he has, trying to fool our service into thinking his IP is benign
        /// </summary>
        /// <returns></returns>
        public SimulatedLoginAttempt MaliciousAttemptToSantiizeIpViaAValidLogin(IPAddress ipAddressToSanitizeThroughLogin)
        {
            SimulatedAccount simAccount = GetMaliciousAccountAtRandomUniform();

            return new SimulatedLoginAttempt(simAccount, simAccount.Password,
                true, false,
                ipAddressToSanitizeThroughLogin, StrongRandomNumberGenerator.Get64Bits().ToString(),
                DateTimeOffset.Now);
        }


        /// <summary>
        /// Send login requests to the stopguessing service
        /// </summary>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <param name="clientAddress"></param>
        /// <param name="cookieProvidedByBrowser"></param>
        /// <param name="eventTime"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Tuple<LoginAttempt,string> CreateLoginAttempt(string username, string password,
            IPAddress clientAddress,
            string cookieProvidedByBrowser,
            DateTimeOffset eventTime,
            CancellationToken cancellationToken = default(CancellationToken)
            )
        {
            return new Tuple<LoginAttempt, string>(new LoginAttempt
            {
                UsernameOrAccountId = username,
                AddressOfClientInitiatingRequest = clientAddress,
                AddressOfServerThatInitiallyReceivedLoginAttempt = new IPAddress(new byte[] { 127, 1, 1, 1 }),
                TimeOfAttempt = eventTime,
                Api = "web",
                CookieProvidedByBrowser = cookieProvidedByBrowser
            }, password);
        }
    }
}