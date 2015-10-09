using System;
using System.Text;

namespace xUnit_Tests
{
    class Pseudorandom
    {
        private Random _random;

        public Pseudorandom(int seed = 42)
        {
            _random = new Random(seed);
        }

        public void Seed(int seed)
        {
            _random = new Random(seed);
        }

        static readonly char[] Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()-_+=".ToCharArray();

        public string GetString(int length = 12)
        {
            //System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create();

            StringBuilder s = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                s.Append(Alphabet[_random.Next(Alphabet.Length)]);
                //byte[] r = new byte[16];
                //rng.GetBytes(r);
                //s.Append(Convert.ToBase64String(r));
            }

            return s.ToString();
        }
    }
}
