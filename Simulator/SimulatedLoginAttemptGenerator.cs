using System;
using System.Net;
using System.Threading;
using StopGuessing.Models;
using StopGuessing.EncryptionPrimitives;


namespace Simulator
{
    
    public class SimulatedLoginAttemptGenerator
    {
        private readonly SimulatedAccounts _simAccounts;
        private readonly ExperimentalConfiguration _experimentalConfiguration;
        private readonly IpPool _ipPool;
        private readonly SimulatedPasswords _simPasswords;

        public SimulatedLoginAttemptGenerator(ExperimentalConfiguration experimentalConfiguration, SimulatedAccounts simAccounts, IpPool ipPool, SimulatedPasswords simPasswords)
        {
            _simAccounts = simAccounts;
            _experimentalConfiguration = experimentalConfiguration;
            _ipPool = ipPool;
            _simPasswords = simPasswords;
        }

        /// <summary>
        /// Send one benign login attempts
        /// </summary>
        /// <returns></returns>
        public SimulatedLoginAttempt BenignLoginAttempt()
        {
            string mistake = "";
            //1. Pick a user at random
            SimulatedAccount account = _simAccounts.BenignAccountSelector.GetItemByWeightedRandom();

            //2. Deal with cookies
            string cookie;
            // Add a new cookie if there are no cookies, or with if we haven't reached the max number of cookies and lose a roll of the dice
            if (account.Cookies.Count == 0 ||
                (account.Cookies.Count < _experimentalConfiguration.MaxCookiesPerUserAccount && StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfCoookieReUse))
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
                (account.ClientAddresses.Count < _experimentalConfiguration.MaxIpPerUserAccount && StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfIpReUse))
            {
                // Use a new IP for the user
                account.ClientAddresses.Add(clientIp = _ipPool.GetNewRandomBenignIp(account.UniqueId));
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
            if (StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfBenignPasswordTypo)
            {
                password += "z";
                mistake += "Typo";
            }
            // The benign user may mistakenly use a password for another of her accounts, which we draw from same distribution
            // we used to generate user account passwords
            if (StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfAccidentallyUsingAnotherAccountPassword)
            {
                password = _simPasswords.GetPasswordFromWeightedDistribution();
                mistake = "WrongPassword";
            }
            // The benign user may mistype her account name, and land on someone else's account name
            if (StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfBenignAccountNameTypoResultingInAValidUserName)
            {
                account = _simAccounts.GetBenignAccountAtRandomUniform();
                mistake += "WrongAccountName";
            }

            return new SimulatedLoginAttempt(account, password, false, false, clientIp, cookie, mistake, DateTime.UtcNow);

        }

        /// <summary>
        /// Attacker issues one guess by picking an benign account at random and picking a password by weighted distribution
        /// </summary>
        public SimulatedLoginAttempt MaliciousLoginAttemptWeighted()
        {
            SimulatedAccount targetBenignAccount =
                (StrongRandomNumberGenerator.GetFraction() <  _experimentalConfiguration.ProbabilityThatAttackerChoosesAnInvalidAccount)
                    ? null : _simAccounts.GetBenignAccountAtRandomUniform();

            return new SimulatedLoginAttempt(
                targetBenignAccount,
                _simPasswords.GetPasswordFromWeightedDistribution(),
                true, true,
                _ipPool.GetRandomMaliciousIp(),
                StrongRandomNumberGenerator.Get64Bits().ToString(),
                "",
                DateTime.UtcNow);
        }

        private readonly Object _breadthFirstLock = new object();
        private ulong _breadthFirstAttemptCounter;

        /// <summary>
        /// Attacker issues one guess by picking an benign account at random and picking a password by weighted distribution
        /// </summary>
        public SimulatedLoginAttempt MaliciousLoginAttemptBreadthFirst()
        {
            string mistake = "";
            string password;

            SimulatedAccount targetBenignAccount;

            // Sometimes the attacker will miss and generate an invalid account name;
            if (StrongRandomNumberGenerator.GetFraction() <
                _experimentalConfiguration.ProbabilityThatAttackerChoosesAnInvalidAccount)
            {
                targetBenignAccount = null;
                mistake = "BadAccount";
                password = _simPasswords.OrderedListOfMostCommonPasswords[(int)(_breadthFirstAttemptCounter / (ulong)_simAccounts.BenignAccounts.Count)];
            }
            else
            {
                ulong breadthFirstAttemptCount;
                lock (_breadthFirstLock)
                {
                    breadthFirstAttemptCount = _breadthFirstAttemptCounter++;
                }

                // Start with the most common password and walk through all the accounts,
                // then move on to the next most common password.
                int passwordIndex = (int) (breadthFirstAttemptCount/(ulong) _simAccounts.BenignAccounts.Count);
                int accountIndex = (int) (breadthFirstAttemptCount%(ulong) _simAccounts.BenignAccounts.Count);
                targetBenignAccount = _simAccounts.BenignAccounts[accountIndex];
                password = _simPasswords.OrderedListOfMostCommonPasswords[passwordIndex];
            }

            //SimulationTest _simulationtest = new SimulationTest();
            return new SimulatedLoginAttempt(targetBenignAccount, password,
                true, true,
                _ipPool.GetRandomMaliciousIp(),
                StrongRandomNumberGenerator.Get64Bits().ToString(),
                mistake,
                DateTime.UtcNow);
        }

        /// <summary>
        /// Attacker login with correct accounts he has, trying to fool our service into thinking his IP is benign
        /// </summary>
        /// <returns></returns>
        public SimulatedLoginAttempt MaliciousAttemptToSantiizeIpViaAValidLogin(IPAddress ipAddressToSanitizeThroughLogin)
        {
            SimulatedAccount simAccount = _simAccounts.GetMaliciousAccountAtRandomUniform();

            return new SimulatedLoginAttempt(simAccount, simAccount.Password,
                true, false,
                ipAddressToSanitizeThroughLogin, StrongRandomNumberGenerator.Get64Bits().ToString(), "",
                DateTime.UtcNow);
        }



    }
}