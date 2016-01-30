using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using StopGuessing;
using StopGuessing.Models;
using StopGuessing.EncryptionPrimitives;


namespace Simulator
{
    /// <summary>
    /// This class generates simulated login attempts to be sent to the StopGuessing algorithms
    /// by the simulator 
    /// </summary>
    public class SimulatedLoginAttemptGenerator
    {
        private readonly SimulatedAccounts _simAccounts;
        private readonly ExperimentalConfiguration _experimentalConfiguration;
        private readonly IpPool _ipPool;
        private readonly SimulatedPasswords _simPasswords;

        public readonly SortedSet<SimulatedLoginAttempt> ScheduledBenignAttempts = new SortedSet<SimulatedLoginAttempt>(
            Comparer<SimulatedLoginAttempt>.Create( (a, b) => 
                a.Attempt.TimeOfAttemptUtc.CompareTo(b.Attempt.TimeOfAttemptUtc)));


        /// <summary>
        /// The attempt generator needs to know about the experimental configuration and have access to the sets of simulated accounts,
        /// IP addresses, and simulated passwords.
        /// </summary>
        /// <param name="experimentalConfiguration"></param>
        /// <param name="simAccounts"></param>
        /// <param name="ipPool"></param>
        /// <param name="simPasswords"></param>
        public SimulatedLoginAttemptGenerator(ExperimentalConfiguration experimentalConfiguration, SimulatedAccounts simAccounts, IpPool ipPool, SimulatedPasswords simPasswords)
        {
            _simAccounts = simAccounts;
            _experimentalConfiguration = experimentalConfiguration;
            _ipPool = ipPool;
            _simPasswords = simPasswords;
        }

        /// <summary>
        /// Add a typo to a password for simulating user typo errors
        /// </summary>
        /// <param name="originalPassword">The original password to add a typo to</param>
        /// <returns>The password modified to contain a typo</returns>
        public static string AddTypoToPassword(string originalPassword)
        {
            // Adding a character will meet the edit distance def. of typo, though if simulating systems that weigh
            // different typos differently one might want to create a variety of typos here
            const string typoAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ./";
            return originalPassword + typoAlphabet[(int) StrongRandomNumberGenerator.Get32Bits(typoAlphabet.Length)];
        }

        /// <summary>
        /// Get a benign login attempt to simulate
        /// </summary>
        /// <returns></returns>
        public SimulatedLoginAttempt BenignLoginAttempt(DateTime eventTimeUtc, IUserAccountContextFactory accountContextFactory)
        {
            // If there is a benign login attempt already scheduled to occur by now,
            // send it instaed
            lock (ScheduledBenignAttempts)
            {
                if (ScheduledBenignAttempts.Count > 0 &&
                    ScheduledBenignAttempts.First().Attempt.TimeOfAttemptUtc < eventTimeUtc)
                {
                    SimulatedLoginAttempt result = ScheduledBenignAttempts.First();
                    ScheduledBenignAttempts.Remove(result);
                    return result;
                }
            }

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
                account.ClientAddresses.Add(clientIp = _ipPool.GetNewRandomBenignIp());
            }
            else
            {
                // Use one of the user's existing IP Addresses selected at random
                clientIp = account.ClientAddresses.ToArray()[(int)StrongRandomNumberGenerator.Get32Bits(account.ClientAddresses.Count)];
            }
                        
            string password = account.Password;

            //
            // Add benign failures

            // An automated client begins a string of login attempts using an old (stale) password 
            if (StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfLongRepeatOfStalePassword)
            {
                // To cause this client to be out of date, we'll change the password here.
                string newPassword = _simPasswords.GetPasswordFromWeightedDistribution();
                accountContextFactory.Get().ReadAsync(account.UniqueId).Result.SetPassword(newPassword, account.Password);
                account.Password = newPassword;
                mistake += "StalePassword";

                // Schedule all the future failed attempts a fixed distance aparat
                lock (ScheduledBenignAttempts)
                {
                    double additionalMistakes = 0;
                    for (additionalMistakes = 1; additionalMistakes < _experimentalConfiguration.LengthOfLongRepeatOfOldPassword; additionalMistakes++)                        
                    {
                        DateTime futureMistakeEventTimeUtc = eventTimeUtc.AddSeconds(
                            _experimentalConfiguration.MinutesBetweenLongRepeatOfOldPassword * additionalMistakes);
                        ScheduledBenignAttempts.Add(new SimulatedLoginAttempt(
                            account, AddTypoToPassword(password), false, false, clientIp, cookie, mistake,
                                eventTimeUtc.AddSeconds(_experimentalConfiguration.MinutesBetweenLongRepeatOfOldPassword * additionalMistakes)));
                    }
                }
            }

            // The benign user may mistype her password causing a typo 
            if (StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfBenignPasswordTypo)
            {
                mistake += "Typo";
                // Typos tend to come in clusters, and are hopefully followed by a correct login
                // Add additional typos to the schedule of future benign attempts and then a submission of the correct password
                lock (ScheduledBenignAttempts)
                {
                    double additionalMistakes = 0;
                    while (StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfRepeatTypo)
                    {
                        ScheduledBenignAttempts.Add(new SimulatedLoginAttempt(
                            account, AddTypoToPassword(password), false, false, clientIp, cookie, mistake,
                            eventTimeUtc.AddSeconds(_experimentalConfiguration.DelayBetweenRepeatBenignErrorsInSeconds * ++additionalMistakes)));
                    }
                    // Add a correct login after the string of typos
                    ScheduledBenignAttempts.Add(new SimulatedLoginAttempt(
                        account, password, false, false, clientIp, cookie, "", eventTimeUtc.AddSeconds(
                            _experimentalConfiguration.DelayBetweenRepeatBenignErrorsInSeconds*(1 + additionalMistakes))));

                }
                // Put the typo into the password for the first typo failure, to be returned by this function.
                password = AddTypoToPassword(password);
            }

            // The benign user may mistakenly use a password for another of her accounts, which we draw from same distribution
            // we used to generate user account passwords
            if (StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfAccidentallyUsingAnotherAccountPassword)
            {
                mistake += "WrongPassword";

                // Choices of the wrong account password may come in clusters, and are hopefully followed by a correct login
                // Add additional typos to the schedule of future benign attempts and then a submission of the correct password
                lock (ScheduledBenignAttempts)
                {
                    double additionalMistakes = 0;
                    while(StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfRepeatUseOfPasswordFromAnotherAccount) {
                        ScheduledBenignAttempts.Add(new SimulatedLoginAttempt(
                            account, _simPasswords.GetPasswordFromWeightedDistribution(), false, false, clientIp, cookie,
                            mistake, eventTimeUtc.AddSeconds(_experimentalConfiguration.DelayBetweenRepeatBenignErrorsInSeconds*++additionalMistakes)));
                    }
                    // Add a correct login after mistakes
                    ScheduledBenignAttempts.Add(new SimulatedLoginAttempt(
                        account, password, false, false, clientIp, cookie, "", eventTimeUtc.AddSeconds(
                        _experimentalConfiguration.DelayBetweenRepeatBenignErrorsInSeconds * (additionalMistakes+1))));
                }

                // Make the current request have the wrong password
                password = _simPasswords.GetPasswordFromWeightedDistribution();
            }

            // The benign user may mistype her account name, and land on someone else's account name
            if (StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfBenignAccountNameTypoResultingInAValidUserName)
            {
                mistake += "WrongAccountName";

                // Choices of the wrong account password may come in clusters, and are hopefully followed by a correct login
                // Add additional typos to the schedule of future benign attempts and then a submission of the correct password
                lock (ScheduledBenignAttempts)
                {
                    double additionalMistakes = 0;
                    while (StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.ChanceOfRepeatWrongAccountName)
                    {
                        ScheduledBenignAttempts.Add(new SimulatedLoginAttempt(
                            _simAccounts.GetBenignAccountAtRandomUniform(), password, false, false, clientIp, cookie, mistake, eventTimeUtc.AddSeconds(
                            _experimentalConfiguration.DelayBetweenRepeatBenignErrorsInSeconds * ++additionalMistakes)));
                    }
                    // Add a correct login after mistakes
                    ScheduledBenignAttempts.Add(new SimulatedLoginAttempt(
                        account, password, false, false, clientIp, cookie, "", eventTimeUtc.AddSeconds(
                        _experimentalConfiguration.DelayBetweenRepeatBenignErrorsInSeconds * (additionalMistakes + 1))));

                    // Make the current request have the wrong account name
                    account = _simAccounts.GetBenignAccountAtRandomUniform();
                }
            }

            return new SimulatedLoginAttempt(account, password, false, false, clientIp, cookie, mistake, eventTimeUtc);

        }

        /// <summary>
        /// Attacker issues one guess by picking an benign account at random and picking a password by weighted distribution
        /// </summary>
        public SimulatedLoginAttempt MaliciousLoginAttemptWeighted(DateTime eventTimeUtc)
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
                eventTimeUtc);
        }

        private readonly Object _breadthFirstLock = new object();
        private ulong _breadthFirstAttemptCounter;

        /// <summary>
        /// Attacker issues one guess by picking an benign account at random and picking a password by weighted distribution
        /// </summary>
        public SimulatedLoginAttempt MaliciousLoginAttemptBreadthFirst(DateTime eventTimeUtc)
        {
            // Sometimes the attacker will miss and generate an invalid account name;
            bool invalidAccount = (StrongRandomNumberGenerator.GetFraction() <
                                   _experimentalConfiguration.ProbabilityThatAttackerChoosesAnInvalidAccount);

            ulong breadthFirstAttemptCount;
            lock (_breadthFirstLock)
            {
                breadthFirstAttemptCount = _breadthFirstAttemptCounter;
                if (!invalidAccount)
                    _breadthFirstAttemptCounter++;
            }

            // Start with the most common password and walk through all the accounts,
            // then move on to the next most common password.
            int passwordIndex = (int)(breadthFirstAttemptCount / (ulong)_simAccounts.BenignAccounts.Count);
            int accountIndex = (int)(breadthFirstAttemptCount % (ulong)_simAccounts.BenignAccounts.Count);

            string mistake = invalidAccount ? "BadAccount" : "";
            SimulatedAccount targetBenignAccount = invalidAccount ? null : _simAccounts.BenignAccounts[accountIndex];
            string password = _simPasswords.OrderedListOfMostCommonPasswords[passwordIndex];

            //SimulationTest _simulationtest = new SimulationTest();
            return new SimulatedLoginAttempt(targetBenignAccount, password,
                true, true,
                _ipPool.GetRandomMaliciousIp(),
                StrongRandomNumberGenerator.Get64Bits().ToString(),
                mistake,
                eventTimeUtc);
        }


        /// <summary>
        /// Attacker issues one guess by picking an benign account at random and picking a password by weighted distribution
        /// </summary>
        public SimulatedLoginAttempt MaliciousLoginAttemptBreadthFirstAvoidMakingPopular(DateTime eventTimeUtc)
        {
            // Sometimes the attacker will miss and generate an invalid account name;
            bool invalidAccount = (StrongRandomNumberGenerator.GetFraction() <
                                   _experimentalConfiguration.ProbabilityThatAttackerChoosesAnInvalidAccount);

            ulong breadthFirstAttemptCount;
            lock (_breadthFirstLock)
            {
                breadthFirstAttemptCount = _breadthFirstAttemptCounter;
                if (!invalidAccount)
                    _breadthFirstAttemptCounter++;
            }

            // Start with the most common password and walk through all the accounts,
            // then move on to the next most common password.
            int passwordIndex = (int)(breadthFirstAttemptCount / (ulong)_experimentalConfiguration.MaxAttackerGuessesPerPassword);
            int accountIndex = (int)(breadthFirstAttemptCount % (ulong)_simAccounts.BenignAccounts.Count);

            string mistake = invalidAccount ? "BadAccount" : "";
            SimulatedAccount targetBenignAccount = invalidAccount ? null : _simAccounts.BenignAccounts[accountIndex];
            string password = _simPasswords.OrderedListOfMostCommonPasswords[passwordIndex];

            //SimulationTest _simulationtest = new SimulationTest();
            return new SimulatedLoginAttempt(targetBenignAccount, password,
                true, true,
                _ipPool.GetRandomMaliciousIp(),
                StrongRandomNumberGenerator.Get64Bits().ToString(),
                mistake,
                eventTimeUtc);
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