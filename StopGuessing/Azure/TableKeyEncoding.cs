

using System.ComponentModel;
using System.Globalization;
using System.Text;
using Microsoft.AspNet.Mvc.ViewFeatures;

namespace StopGuessing.Azure
{
    public static class TableKeyEncoding
    {
        // https://msdn.microsoft.com/library/azure/dd179338.aspx
        // 
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
