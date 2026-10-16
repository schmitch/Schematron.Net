/*
 * Licensed to the Apache Software Foundation (ASF) under one
 * or more contributor license agreements.  See the NOTICE file
 * distributed with this work for additional information
 * regarding copyright ownership.  The ASF licenses this file
 * to you under the Apache License, Version 2.0 (the
 * "License"); you may not use this file except in compliance
 * with the License.  You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// Translates XPath (XML Schema based) regular expressions to .NET regular expressions.
    /// </summary>
    internal static class XPathRegex
    {
        private static readonly ConcurrentDictionary<string, Regex> _Cache = new ConcurrentDictionary<string, Regex>();
        private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(10);


        public static Regex Get(string pattern, string flags)
        {
            string key = flags + "\u0001" + pattern;
            Regex regex;
            if (_Cache.TryGetValue(key, out regex))
            {
                return regex;
            }
            regex = _Compile(pattern, flags);
            if (_Cache.Count < 2000)
            {
                _Cache[key] = regex;
            }
            return regex;
        }


        private static Regex _Compile(string pattern, string flags)
        {
            RegexOptions options = RegexOptions.CultureInvariant;
            bool dotAll = false;
            bool multiLine = false;
            bool ignoreWhitespace = false;
            bool literal = false;
            foreach (char flag in flags ?? "")
            {
                switch (flag)
                {
                    case 's':
                        dotAll = true;
                        break;
                    case 'm':
                        multiLine = true;
                        options |= RegexOptions.Multiline;
                        break;
                    case 'i':
                        options |= RegexOptions.IgnoreCase;
                        break;
                    case 'x':
                        ignoreWhitespace = true;
                        break;
                    case 'q':
                        literal = true;
                        break;
                    default:
                        throw new XPathException("FORX0001", "Invalid regular expression flag '" + flag + "'");
                }
            }
            string translated = literal ? Regex.Escape(pattern) : Translate(pattern, dotAll, multiLine, ignoreWhitespace);
            try
            {
                return new Regex(translated, options, _Timeout);
            }
            catch (ArgumentException e)
            {
                throw new XPathException("FORX0002", "Invalid regular expression '" + pattern + "': " + e.Message, e);
            }
        }


        public static string Translate(string pattern, bool dotAll, bool multiLine, bool ignoreWhitespace)
        {
            StringBuilder sb = new StringBuilder(pattern.Length + 16);
            int classDepth = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                char ch = pattern[i];
                if (ignoreWhitespace && classDepth == 0 && (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r'))
                {
                    continue;
                }
                if (ch == '\\')
                {
                    if (i + 1 >= pattern.Length)
                    {
                        throw new XPathException("FORX0002", "Invalid regular expression '" + pattern + "': trailing backslash");
                    }
                    char next = pattern[++i];
                    bool inClass = classDepth > 0;
                    switch (next)
                    {
                        case 'd':
                            sb.Append(@"\p{Nd}");
                            break;
                        case 'D':
                            sb.Append(@"\P{Nd}");
                            break;
                        case 's':
                            sb.Append(inClass ? @"\x20\t\n\r" : @"[\x20\t\n\r]");
                            break;
                        case 'S':
                            sb.Append(inClass ? @"\S" : @"[^\x20\t\n\r]");
                            break;
                        case 'w':
                            sb.Append(inClass ? @"\p{L}\p{M}\p{N}\p{S}" : @"[\p{L}\p{M}\p{N}\p{S}]");
                            break;
                        case 'W':
                            sb.Append(inClass ? @"\p{P}\p{Z}\p{C}" : @"[\p{P}\p{Z}\p{C}]");
                            break;
                        case 'i':
                            sb.Append(inClass ? @"\p{L}_:" : @"[\p{L}_:]");
                            break;
                        case 'I':
                            sb.Append(inClass ? @"\P{L}" : @"[^\p{L}_:]");
                            break;
                        case 'c':
                            sb.Append(inClass ? @"\p{L}\p{Nd}\p{Mn}\p{Mc}._:\-·" : @"[\p{L}\p{Nd}\p{Mn}\p{Mc}._:\-·]");
                            break;
                        case 'C':
                            sb.Append(inClass ? @"\W" : @"[^\p{L}\p{Nd}\p{Mn}\p{Mc}._:\-·]");
                            break;
                        case 'p':
                        case 'P':
                            {
                                int close = pattern.IndexOf('}', i);
                                if (i + 1 >= pattern.Length || pattern[i + 1] != '{' || close < 0)
                                {
                                    throw new XPathException("FORX0002", "Invalid regular expression '" + pattern + "': invalid category escape");
                                }
                                string name = pattern.Substring(i + 2, close - i - 2);
                                sb.Append('\\').Append(next).Append('{').Append(_TranslateCategory(name)).Append('}');
                                i = close;
                                break;
                            }
                        case 'n':
                        case 'r':
                        case 't':
                        case '\\':
                        case '|':
                        case '.':
                        case '-':
                        case '^':
                        case '?':
                        case '*':
                        case '+':
                        case '{':
                        case '}':
                        case '(':
                        case ')':
                        case '[':
                        case ']':
                        case '$':
                            sb.Append('\\').Append(next);
                            break;
                        default:
                            if (Char.IsDigit(next) && !inClass)
                            {
                                // back reference
                                sb.Append('\\').Append(next);
                                break;
                            }
                            throw new XPathException("FORX0002", "Invalid regular expression '" + pattern + "': invalid escape \\" + next);
                    }
                    continue;
                }
                if (classDepth > 0)
                {
                    if (ch == '[')
                    {
                        // character class subtraction: -[...]
                        classDepth++;
                    }
                    else if (ch == ']')
                    {
                        classDepth--;
                    }
                    sb.Append(ch);
                    continue;
                }
                switch (ch)
                {
                    case '[':
                        classDepth++;
                        sb.Append(ch);
                        break;
                    case '.':
                        sb.Append(dotAll ? @"[\s\S]" : @"[^\n\r]");
                        break;
                    case '$':
                        sb.Append(multiLine ? "(?=\\n|\\z)" : "\\z");
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }


        private static string _TranslateCategory(string name)
        {
            // XML Schema block names (IsBasicLatin) are supported by .NET with the same spelling, except a few.
            switch (name)
            {
                case "IsLatin-1Supplement":
                    return "IsLatin-1Supplement";
                default:
                    return name;
            }
        }


        /// <summary>
        /// Builds the replacement of fn:replace for one match ($N group references, \$ and \\ escapes).
        /// </summary>
        public static string Replace(Regex regex, string input, string replacement)
        {
            List<object> parts = _ParseReplacement(replacement, regex.GetGroupNumbers().Length - 1);
            return regex.Replace(input, match =>
            {
                if (match.Length == 0)
                {
                    throw new XPathException("FORX0003", "The regular expression in replace() matches a zero-length string");
                }
                StringBuilder sb = new StringBuilder();
                foreach (object part in parts)
                {
                    if (part is string s)
                    {
                        sb.Append(s);
                    }
                    else
                    {
                        Group group = match.Groups[(int)part];
                        if (group.Success)
                        {
                            sb.Append(group.Value);
                        }
                    }
                }
                return sb.ToString();
            });
        }


        private static List<object> _ParseReplacement(string replacement, int groupCount)
        {
            List<object> parts = new List<object>();
            StringBuilder literal = new StringBuilder();
            for (int i = 0; i < replacement.Length; i++)
            {
                char ch = replacement[i];
                if (ch == '\\')
                {
                    if (i + 1 < replacement.Length && (replacement[i + 1] == '\\' || replacement[i + 1] == '$'))
                    {
                        literal.Append(replacement[++i]);
                        continue;
                    }
                    throw new XPathException("FORX0004", "Invalid replacement string '" + replacement + "'");
                }
                if (ch == '$')
                {
                    if (i + 1 >= replacement.Length || !Char.IsDigit(replacement[i + 1]))
                    {
                        throw new XPathException("FORX0004", "Invalid replacement string '" + replacement + "'");
                    }
                    int group = replacement[++i] - '0';
                    while (i + 1 < replacement.Length && Char.IsDigit(replacement[i + 1]) && group * 10 + (replacement[i + 1] - '0') <= groupCount)
                    {
                        group = group * 10 + (replacement[++i] - '0');
                    }
                    if (literal.Length > 0)
                    {
                        parts.Add(literal.ToString());
                        literal.Clear();
                    }
                    if (group <= groupCount)
                    {
                        parts.Add(group);
                    }
                    continue;
                }
                literal.Append(ch);
            }
            if (literal.Length > 0)
            {
                parts.Add(literal.ToString());
            }
            return parts;
        }


        /// <summary>
        /// fn:tokenize: the parts of the input between the matches of the regular expression.
        /// </summary>
        public static List<string> Tokenize(Regex regex, string input)
        {
            List<string> result = new List<string>();
            int start = 0;
            Match match = regex.Match(input);
            while (match.Success)
            {
                if (match.Length == 0)
                {
                    throw new XPathException("FORX0003", "The regular expression in tokenize() matches a zero-length string");
                }
                result.Add(input.Substring(start, match.Index - start));
                start = match.Index + match.Length;
                match = match.NextMatch();
            }
            result.Add(input.Substring(start));
            return result;
        }
    }
}
