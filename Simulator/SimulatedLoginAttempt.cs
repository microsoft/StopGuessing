using System;
using System.Net;
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
        public LoginAttempt Attempt;
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
            string accountId = account != null ? account.UniqueId : StrongRandomNumberGenerator.Get64Bits().ToString();
            bool isPasswordValid = account != null && account.Password == password;

            Attempt = new LoginAttempt
            {
                UsernameOrAccountId = accountId,
                AddressOfClientInitiatingRequest = clientAddress,
                AddressOfServerThatInitiallyReceivedLoginAttempt = new IPAddress(new byte[] { 127, 1, 1, 1 }),
                TimeOfAttemptUtc = eventTimeUtc,
                Api = "web",
                CookieProvidedByBrowser = cookieProvidedByBrowser
            };
            Password = password;
            IsPasswordValid = isPasswordValid;
            IsFromAttacker = isFromAttacker;
            IsGuess = isGuess;
            MistakeType = mistakeType;
        }
    }
}
