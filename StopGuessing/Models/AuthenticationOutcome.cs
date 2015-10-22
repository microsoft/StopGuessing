namespace StopGuessing.Models
{
    public enum AuthenticationOutcome { 
        Undetermined = 0,
        CredentialsValid = 1,
        CredentialsValidButBlocked = -1,
        CredentialsInvalidNoSuchAccount = -2,
        CredentialsInvalidRepeatedNoSuchAccount = -3,
        CredentialsInvalidRepeatedIncorrectPassword = -4,
        CredentialsInvalidIncorrectPassword = -5,
        CredentialsInvalidIncorrectPasswordTypoUnlikely = -6,
        CredentialsInvalidIncorrectPasswordTypoLikely = -7
    }

}
