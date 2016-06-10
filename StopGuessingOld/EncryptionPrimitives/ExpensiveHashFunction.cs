using System.Collections.Generic;
using Microsoft.AspNet.Cryptography.KeyDerivation;

namespace StopGuessing.EncryptionPrimitives
{

    public delegate byte[] ExpensiveHashFunction(string password, byte[] saltBytes, int iterations);

    public static class ExpensiveHashFunctionFactory
    {

        public const string DefaultFunctionName = "PBKDF2_SHA256";
        public const int DefaultNumberOfIterations = 10000;

        private static readonly Dictionary<string, ExpensiveHashFunction> ExpensiveHashFunctions = new Dictionary<string, ExpensiveHashFunction>
            {
                {DefaultFunctionName, (password, salt, iterations) =>
                KeyDerivation.Pbkdf2(
                    password: password,
                    salt: salt,
                    prf: KeyDerivationPrf.HMACSHA256,
                    iterationCount: iterations,
                    numBytesRequested: 16)}
            };


        public static ExpensiveHashFunction Get(string hashFunctionName)
        {
            return ExpensiveHashFunctions[hashFunctionName];
        }

        public static void Add(string name, ExpensiveHashFunction function)
        {
            ExpensiveHashFunctions[name] = function;
        }
    }
}
