using System.Collections.Generic;
using System.Linq;

namespace StopGuessing.DataStructures
{
    /// <summary>
    /// A class representing a proportion, or fraction, of numerator over denominator.
    /// This comes in handy when one might care about the actual magnitude of the numerator/denominator,
    /// which is list in a representation that divides the numerator by the denominator.
    /// </summary>
    public struct Proportion
    {
        public ulong Numerator { get; }
        public ulong Denominator { get; }

        public double AsDouble { get; }
        

        public Proportion(ulong numerator, ulong denominator)
        {
            Numerator = numerator;
            Denominator = denominator;
            AsDouble = Denominator == 0 ? 0 : Numerator / ((double)Denominator);
        }
        

        /// <summary>
        /// Return this proportion modified to have a denominator at least as large as a value specified
        /// in the parameter.
        /// </summary>
        /// <param name="minDenominator">The denominator to use if the proportions denominator is less than this value.</param>
        /// <returns>If the minDenominator is less than this proportion's Denominator, the return valuee will be this proportion.
        /// Otherwise, it will be a new proportion with the same Numerator but with the Denominator set to <paramref name="minDenominator"/>.</returns>
        public Proportion MinDenominator(ulong minDenominator)
        {
            return Denominator >= minDenominator ? this : new Proportion(Numerator, minDenominator);
        }

        public static Proportion GetLargest(IEnumerable<Proportion> proportions)
        {
            Proportion max = new Proportion(0,ulong.MaxValue);
            foreach (Proportion p in proportions)
            {
                if (p.AsDouble > max.AsDouble)
                    max = p;
            }
            return max;
        }

    }
}
