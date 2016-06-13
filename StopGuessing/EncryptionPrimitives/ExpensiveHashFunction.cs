using System.Collections.Generic;

namespace StopGuessing.EncryptionPrimitives
{

    public delegate byte[] ExpensiveHashFunction(string password, byte[] saltBytes, int iterations);

    public static class ExpensiveHashFunctionFactory
    {

        public const string DefaultFunctionName = "Rfc2898";
        public const int DefaultNumberOfIterations = 10000;

        private static readonly Dictionary<string, ExpensiveHashFunction> ExpensiveHashFunctions = new Dictionary<string, ExpensiveHashFunction>
            {
                {DefaultFunctionName, (password, salt, iterations) =>
                new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, iterations).GetBytes(16)}
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
