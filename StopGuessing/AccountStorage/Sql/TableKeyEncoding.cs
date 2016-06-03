using System.Globalization;
using System.Text;

namespace StopGuessing.AccountStorage.Sql
{
    /// <summary>
    /// Azure tables forbid the folowing characters in PartitionKey and RowKey properties:
    ///   --The forward slash(/) character
    ///   --The backslash(\) character
    ///   --The number sign(#) character
    ///   --The question mark (?) character
    ///   --Control characters from U+0000 to U+001F, including:
    ///   --The horizontal tab(\t) character
    ///   --The linefeed(\n) character
    ///   --The carriage return (\r) character
    ///   --Control characters from U+007F to U+009F
    ///   (see https://msdn.microsoft.com/library/azure/dd179338.aspx)
    /// 
    /// This class provides a method to encode any string to remove the forbidden characters and allow
    /// its use as a PartitionKey or RowKey, as well as a method to decode the keys to recover the original
    /// string with the forbidden characters.
    /// 
    /// It encodes forbidden chracters using the '!' (exclamation point) character as an escape character.
    ///   !! = !  ('!' is a legal character but '!!' is needed to indicate '!' is not escaping anything)
    ///   !f = /
    ///   !b = \
    ///   !p = #
    ///   !q = ?
    ///   !t = \t  (tab)
    ///   !n = \n  (line feed)
    ///   !r = \r  (carriage return)
    ///   !xXY = character with unicode value 0xXY
    /// </summary>
    public static class TableKeyEncoding
    {
        /// <summary>
        /// Encode a string to ensure that it can safely be used as a PartitionKey or RowKey in an AzureTable
        /// </summary>
        /// <param name="unsafeForUseAsAKey">A string that may contain characters that are forbidden for use in an AzureTable PartitionKey or RowKey.</param>
        /// <returns>A string in which forbidden characters been replaced with safe characters.</returns>
        public static string Encode(string unsafeForUseAsAKey)
        {
            StringBuilder safe = new StringBuilder();
            foreach (char c in unsafeForUseAsAKey)
            {
                switch (c)
                {
                    case '/':
                        safe.Append("!f");
                        break;
                    case '\\':
                        safe.Append("!b");
                        break;
                    case '#':
                        safe.Append("!p");
                        break;
                    case '?':
                        safe.Append("!q");
                        break;
                    case '\t':
                        safe.Append("!t");
                        break;
                    case '\n':
                        safe.Append("!n");
                        break;
                    case '\r':
                        safe.Append("!r");
                        break;
                    case '!':
                        safe.Append("!!");
                        break;
                    default:
                        if (c <= 0x1f || (c >= 0x7f && c <= 0x9f))
                        {
                            int charCode = c;
                            safe.Append("!x" + charCode.ToString("x2"));
                        }
                        else
                        {
                            safe.Append(c);
                        }
                        break;
                }
            }
            return safe.ToString();
        }

        /// <summary>
        /// Decode a string that was encoded with the Encode method
        /// </summary>
        /// <param name="key">A string that had been encoded with the Encode() method to remove forbidden characters.</param>
        /// <returns>The string that was the input to Encode such that Decode(Encode(x))==x for any string x.</returns>
        public static string Decode(string key)
        {
            StringBuilder decoded = new StringBuilder();
            int i = 0;
            while (i < key.Length)
            {
                char c = key[i++];
                if (c != '!' || i == key.Length)
                {
                    // There's no escape character ('!'), or the escape should be ignored because it's the end of the array
                    decoded.Append(c);
                }
                else
                {
                    char escapeCode = key[i++];
                    switch (escapeCode)
                    {
                        case 'f':
                            decoded.Append('/');
                            break;
                        case 'b':
                            decoded.Append('\\');
                            break;
                        case 'p':
                            decoded.Append('#');
                            break;
                        case 'q':
                            decoded.Append('?');
                            break;
                        case 't':
                            decoded.Append('\t');
                            break;
                        case 'n':
                            decoded.Append("\n");
                            break;
                        case 'r':
                            decoded.Append("\r");
                            break;
                        case '!':
                            decoded.Append('!');
                            break;
                        case 'x':
                            if (i + 2 <= key.Length)
                            {
                                string charCodeString = key.Substring(i, 2);
                                int charCode;
                                if (int.TryParse(charCodeString, NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out charCode))
                                {
                                    decoded.Append((char)charCode);
                                }
                                i += 2;
                            }
                            break;
                        default:
                            decoded.Append('!');
                            break;
                    }
                }
            }
            return decoded.ToString();
        }
    }
}
