using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using StopGuessing;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;

namespace Simulator
{
    /// <summary>
    /// Track all of the information about a login attempt that is not stored in the LoginAttempt record.
    /// We need this because the actual LoginAttempt does not track the plaintext password nor does it
    /// have any ground truth about the circumstances behind the attempt: was the attempt made by an attacker,
    /// was it a guess, and what type of mistakes might the simulator have tried to emulate when constructing
    /// the attempt.
    /// </summary>
    public class SimulatedLoginAttempt
    {
        public SimulatedAccount SimAccount;
        public string UserNameOrAccountId;
        public IPAddress AddressOfClientInitiatingRequest { get; set; }
        public DateTime TimeOfAttemptUtc { get; set; }
        public string CookieProvidedByBrowser { get; set; }
        public bool DeviceCookieHadPriorSuccessfulLoginForThisAccount { get; set; }
        public bool IsFrequentlyGuessedPassword = false;
        public bool IsRepeatFailure;
        public string Password;
        public bool IsPasswordValid;
        public bool IsFromAttacker;
        public bool IsGuess;
        public string MistakeType;

        public SimulatedLoginAttempt(SimulatedAccount account,
            string password,
            bool isFromAttacker,
            bool isGuess,
            IPAddress clientAddress,
            string cookieProvidedByBrowser,
            string mistakeType,
            DateTime eventTimeUtc
            )
        {
            SimAccount = account;
            UserNameOrAccountId = account != null ? account.UniqueId : StrongRandomNumberGenerator.Get64Bits().ToString();
            IsPasswordValid = account != null && account.Password == password;
            AddressOfClientInitiatingRequest = clientAddress;
            TimeOfAttemptUtc = eventTimeUtc;
            CookieProvidedByBrowser = cookieProvidedByBrowser;
            Password = password;
            IsFromAttacker = isFromAttacker;
            IsGuess = isGuess;
            MistakeType = mistakeType;
        }

        public void UpdateSimulatorState(Simulator simulator, SimIpHistory ipHistory)
        {

            IsRepeatFailure = (SimAccount == null)
                ? simulator._recentIncorrectPasswords.AddMember(UserNameOrAccountId + "\n" + Password)
                : simulator._userAccountController.AddIncorrectPhaseTwoHashAsync( SimAccount.Account, Password, TimeOfAttemptUtc).Result;

            int passwordsHeightOnBinomialLadder = IsPasswordValid
                ? simulator._binomialLadderSketch.GetHeight(Password)
                : simulator._binomialLadderSketch.Step(Password);

            IsFrequentlyGuessedPassword = passwordsHeightOnBinomialLadder + 1 >=
                                          simulator._binomialLadderSketch.MaxHeight;


            if (SimAccount != null)
            {
                DeviceCookieHadPriorSuccessfulLoginForThisAccount =
                    simulator._userAccountController.HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(
                        SimAccount.Account, CookieProvidedByBrowser).Result;
            }

            if (IsPasswordValid)
            {
                // Determine if any of the outcomes for login attempts from the client IP for this request were the result of typos,
                // as this might impact our decision about whether or not to block this client IP in response to its past behaviors.
                ipHistory.AdjustBlockingScoreForPastTyposTreatedAsFullFailures(simulator, SimAccount, TimeOfAttemptUtc,
                    Password);
                if (SimAccount != null && SimAccount.Account != null)
                    simulator._userAccountController.RecordHashOfDeviceCookieUsedDuringSuccessfulLoginBackground(
                        SimAccount.Account, CookieProvidedByBrowser, TimeOfAttemptUtc);
            }

            if (!IsPasswordValid && !IsRepeatFailure && SimAccount != null)
            {
                ipHistory.RecentPotentialTypos.Add(new SimLoginAttemptSummaryForTypoAnalysis()
                {
                    WhenUtc = TimeOfAttemptUtc,
                    Password = Password,
                    UsernameOrAccountId = UserNameOrAccountId,
                    WasPasswordFrequent = IsFrequentlyGuessedPassword
                });
            }

            DecayingDouble decayingOneFromThisInstant = new DecayingDouble(1, TimeOfAttemptUtc);
            TimeSpan halfLife = simulator._experimentalConfiguration.BlockingOptions.BlockScoreHalfLife;
            if (IsPasswordValid)
            {
                ipHistory.SuccessfulLogins.AddInPlace(halfLife, decayingOneFromThisInstant);
            } else if (SimAccount == null)
            {
                if (IsRepeatFailure)
                {
                    if (IsFrequentlyGuessedPassword)
                        ipHistory.RepeatAccountFailuresFrequentPassword.AddInPlace(halfLife, decayingOneFromThisInstant);
                    else
                        ipHistory.RepeatAccountFailuresInfrequentPassword.AddInPlace(halfLife, decayingOneFromThisInstant);
                }
                else
                {
                    if (IsFrequentlyGuessedPassword)
                        ipHistory.AccountFailuresFrequentPassword.AddInPlace(halfLife, decayingOneFromThisInstant);
                    else
                        ipHistory.AccountFailuresInfrequentPassword.AddInPlace(halfLife, decayingOneFromThisInstant);
                }
            }
            else
            {
                if (IsRepeatFailure)
                {
                    if (IsFrequentlyGuessedPassword)
                        ipHistory.RepeatPasswordFailuresNoTypoFrequentPassword.AddInPlace(halfLife, decayingOneFromThisInstant);
                    else
                        ipHistory.RepeatPasswordFailuresNoTypoInfrequentPassword.AddInPlace(halfLife, decayingOneFromThisInstant);
                }
                else
                {
                    if (IsFrequentlyGuessedPassword)
                        ipHistory.PasswordFailuresNoTypoFrequentPassword.AddInPlace(halfLife, decayingOneFromThisInstant);
                    else
                        ipHistory.PasswordFailuresNoTypoInfrequentPassword.AddInPlace(halfLife, decayingOneFromThisInstant);
                }
            }

        }

        
    }
}
