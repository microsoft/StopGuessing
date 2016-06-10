namespace StopGuessing.Models
{
    /// <summary>
    /// Possible outcomes of an authentication attempt.  The only information ever 
    /// revealed to an untrusted client is whether the outcome is CredentialsValid,
    /// in which case the client should be logged in, or some other value, in which case
    /// the client should only be told the credentials were invalid (but not given any more
    /// specific information).
    /// </summary>
    public enum AuthenticationOutcome { 
        /// <summary>
        /// Default value before the authentication has occurred.
        /// </summary>
        Undetermined = 0,
        /// <summary>
        /// The credentials were valid and the user should be allowed to login.
        /// </summary>
        CredentialsValid = 1,
        /// <summary>
        /// The credentials were valid but the client has been guessing so much that
        /// the authentication system should act as if the credentials were invalid.
        /// </summary>
        CredentialsValidButBlocked = -1,
        /// <summary>
        /// The username or account ID provided does not map to a valid account.
        /// </summary>
        CredentialsInvalidNoSuchAccount = -2,
        /// <summary>
        /// The username or account ID provided does not map to a valid account,
        /// and we've seen this same mistake made recently.
        /// </summary>
        CredentialsInvalidRepeatedNoSuchAccount = -3,
        /// <summary>
        /// The password provided is not the correct password for this account,
        /// and we've seen the same account name/password pair fail recently.
        /// </summary>
        CredentialsInvalidRepeatedIncorrectPassword = -4,
        /// <summary>
        /// The password provided is not the correct password for this account,
        /// </summary>
        CredentialsInvalidIncorrectPassword = -5,
        /// <summary>
        /// The password provided is not the correct password for this account,
        /// and its not even close (by measure of edit distance).
        /// </summary>
        CredentialsInvalidIncorrectPasswordTypoUnlikely = -6,
        /// <summary>
        /// The password provided is not the correct password for this account,
        /// but it is close enough (by measure of edit distance) that it was
        /// more likely a typo than a guess.
        /// </summary>
        CredentialsInvalidIncorrectPasswordTypoLikely = -7
    }

}
