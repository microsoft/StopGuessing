using System;
using System.Net;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;

namespace Simulator
{
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
