using System;
using System.Collections.Generic;
using System.Linq;
using StopGuessing.AccountStorage.Sql;
using Xunit;

namespace StopGuessingTests
{
    public class TableKeyEncodingTests
    {
        const char Unicode0X1A = (char) 0x1a;


        public void RoundTripTest(string unencoded, string encoded)
        {
            Assert.Equal(encoded, TableKeyEncoding.Encode(unencoded));
            Assert.Equal(unencoded, TableKeyEncoding.Decode(encoded));
        }

        [Fact]
        public void RoundTrips()
        {
            RoundTripTest("!\n", "!!!n");
            RoundTripTest("left" + Unicode0X1A + "right", "left!x1aright");
        }


        // The following characters are not allowed in values for the PartitionKey and RowKey properties:
        // The forward slash(/) character
        // The backslash(\) character
        // The number sign(#) character
        // The question mark (?) character
        // Control characters from U+0000 to U+001F, including:
        // The horizontal tab(\t) character
        // The linefeed(\n) character
        // The carriage return (\r) character
        // Control characters from U+007F to U+009F
        [Fact]
        void EncodesAllForbiddenCharacters()
        {
            List<char> forbiddenCharacters = "\\/#?\t\n\r".ToCharArray().ToList();
            forbiddenCharacters.AddRange(Enumerable.Range(0x00, 1+(0x1f-0x00)).Select(i => (char)i));
            forbiddenCharacters.AddRange(Enumerable.Range(0x7f, 1+(0x9f-0x7f)).Select(i => (char)i));
            string allForbiddenCharacters = String.Join("", forbiddenCharacters);
            string allForbiddenCharactersEncoded = TableKeyEncoding.Encode(allForbiddenCharacters);

            // Make sure decoding is same as encoding
            Assert.Equal(allForbiddenCharacters, TableKeyEncoding.Decode(allForbiddenCharactersEncoded));

            // Ensure encoding does not contain any forbidden characters
            Assert.Equal(0, allForbiddenCharacters.Count( c => allForbiddenCharactersEncoded.Contains(c) ));
        }

    }
}
