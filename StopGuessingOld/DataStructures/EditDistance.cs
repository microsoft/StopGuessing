using System;

namespace StopGuessing.DataStructures
{

    /// <summary>
    /// A static class for calculating the edit distance between two strings,
    /// factoring in not just the standard additions, deletions, and subtstitutions,
    /// but also case changes ('a'->'A') and transpositions ('typo'->'tpyo').
    /// The caller may set the cost of each type of edit and the calculator will
    /// compute the least-cost edit from the source string to the destination string.
    /// </summary>
    public static class EditDistance
    {
        public class Costs
        {
            public float CaseChange;
            public float Add;
            public float Delete;
            public float Transpose;
            public float Substitute;

            public Costs(float caseChange = 0.5f,
                         float add = 1,
                         float delete = 1,
                         float transpose = 1,
                         float substitute = 1)
            {
                CaseChange = caseChange;
                Transpose = transpose;
                Add = add;
                Delete = delete;
                Substitute = substitute;
            }
        }

        /// <summary>
        /// Calculate the cost to edit a source string into a destination string, where
        /// the set of possible operations (add char, delete char, substitute one char for another,
        /// transpose adjacent characters, or change the case of a character) can be set by the
        /// caller.
        /// </summary>
        /// <param name="sourceString">The string to be changed (edited).</param>
        /// <param name="destinationString">The string that the source string should be turned into through edits.</param>
        /// <param name="costs">The cost of each of the types of changes that can be made.</param>
        /// <returns>The lowest cost with which the edits can be made to change the <param name="sourceString"/>
        /// into the <paramref name="destinationString"/>.</returns>
        public static float Calculate(string sourceString, string destinationString, Costs costs = null)
        {
            return Calculate(sourceString.ToCharArray(), destinationString.ToCharArray(), costs);
        }

        public static float Calculate(char[] xstr,
            char[] ystr,
            Costs costs = null)
        {
            if (costs == null)
                costs = new Costs();

            int xSize = xstr.Length;
            int ySize = ystr.Length;

            float[,] costMatrix = new float[xSize + 1, ySize + 1];

            costMatrix[0, 0] = 0;
            for (int x = 1; x <= xSize; x++) {
                costMatrix[x, 0] = costs.Delete * x;
            }
            for (int y = 1; y <= ySize; y++)
            {
                costMatrix[0, y] = costs.Add * y;
            }


            for (int x = 1; x <= xSize; x++)
            {
                for (int y = 1; y <= ySize; y++)
                {
                    char xChar = xstr[x - 1];
                    char yChar = ystr[y - 1];

                    // The default cost is to substitute the character that's supposed to be here
                    // with another character
                    float cost = costMatrix[x - 1, y - 1] + costs.Substitute;

                    if (xChar == yChar)
                    {
                        // The two current characters match
                        cost = Math.Min(cost, costMatrix[x - 1, y - 1]);
                    } else if (char.ToLower(xChar) == char.ToLower(yChar))
                    {
                        // The characters match if case insensitive
                        cost = Math.Min(cost, costMatrix[x - 1, y - 1] + costs.CaseChange);
                    }

                    // Cost of deletion
                    cost = Math.Min(cost, costMatrix[x - 1, y] + costs.Delete);
                    
                    // Cost of addition
                    cost = Math.Min(cost, costMatrix[x, y - 1] + costs.Add);

                    if (x >= 2 && y >= 2 && xstr[x - 2] == yChar && ystr[y - 2] == xChar)
                    {
                        // Transposition
                        cost = Math.Min(cost, costMatrix[x - 2, y - 2] + costs.Transpose);
                    }

                    costMatrix[x,y] = cost;
                }
            }

            return costMatrix[xstr.Length, ystr.Length];
        }


    }
}
