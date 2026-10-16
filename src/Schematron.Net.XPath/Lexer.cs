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
using System.Collections.Generic;
using System.Text;

namespace Schematron.Net.XPath
{
    internal enum TokenKind
    {
        /// <summary>An NCName, a QName (prefix:local), a wildcard (*, prefix:*, *:local) or an EQName (Q{uri}local).</summary>
        Name,
        String,
        Integer,
        Decimal,
        Double,
        Symbol,
        End
    }


    internal sealed class Token
    {
        public TokenKind Kind;

        /// <summary>The text of symbols and literals; the local part (or "*") of names.</summary>
        public string Value;

        /// <summary>For names: the prefix (null if none, "*" for *:local).</summary>
        public string Prefix;

        /// <summary>For EQNames (Q{uri}local): the namespace URI.</summary>
        public string Uri;
        public int Position;

        /// <summary>True if whitespace (or a comment) precedes the token.</summary>
        public bool PrecededBySpace;


        public bool IsSymbol(string symbol)
        {
            return Kind == TokenKind.Symbol && Value == symbol;
        }


        /// <summary>
        /// True for an unprefixed NCName with the given value (used for keywords and operators).
        /// </summary>
        public bool IsName(string name)
        {
            return Kind == TokenKind.Name && Prefix == null && Uri == null && Value == name;
        }


        public override string ToString()
        {
            switch (Kind)
            {
                case TokenKind.Name:
                    if (Uri != null)
                    {
                        return "Q{" + Uri + "}" + Value;
                    }
                    return Prefix != null ? Prefix + ":" + Value : Value;
                case TokenKind.End:
                    return "end of expression";
                case TokenKind.String:
                    return "\"" + Value + "\"";
                default:
                    return Value;
            }
        }
    }


    /// <summary>
    /// Splits an XPath expression into tokens.
    /// </summary>
    internal static class Lexer
    {
        private static readonly string[] _Symbols =
        {
            "!=", "<=", ">=", "<<", ">>", "//", "..", "::", ":=", "||", "=>",
            "(", ")", "[", "]", ",", "/", "@", ".", "=", "<", ">", "+", "-", "*", "|", "?", "$", "!", "{", "}", ":", "#", ";"
        };


        public static List<Token> Tokenize(string expression)
        {
            List<Token> tokens = new List<Token>();
            int i = 0;
            int length = expression.Length;
            while (true)
            {
                int beforeSpace = i;
                i = _SkipWhitespaceAndComments(expression, i);
                bool space = i > beforeSpace;
                if (i >= length)
                {
                    tokens.Add(new Token { Kind = TokenKind.End, Value = "", Position = i, PrecededBySpace = space });
                    return tokens;
                }
                int start = i;
                char ch = expression[i];
                Token token = new Token { Position = start, PrecededBySpace = space };

                if (ch == '"' || ch == '\'')
                {
                    StringBuilder sb = new StringBuilder();
                    i++;
                    while (true)
                    {
                        if (i >= length)
                        {
                            throw new XPathException("XPST0003", "Unterminated string literal in \"" + expression + "\"");
                        }
                        if (expression[i] == ch)
                        {
                            if (i + 1 < length && expression[i + 1] == ch)
                            {
                                sb.Append(ch);
                                i += 2;
                                continue;
                            }
                            i++;
                            break;
                        }
                        sb.Append(expression[i]);
                        i++;
                    }
                    token.Kind = TokenKind.String;
                    token.Value = sb.ToString();
                }
                else if (Char.IsDigit(ch) || (ch == '.' && i + 1 < length && Char.IsDigit(expression[i + 1])))
                {
                    i = _ReadNumber(expression, i, token);
                }
                else if (ch == 'Q' && i + 1 < length && expression[i + 1] == '{')
                {
                    int close = expression.IndexOf('}', i + 2);
                    if (close < 0)
                    {
                        throw new XPathException("XPST0003", "Unterminated EQName in \"" + expression + "\"");
                    }
                    token.Kind = TokenKind.Name;
                    token.Uri = expression.Substring(i + 2, close - i - 2).Trim();
                    i = close + 1;
                    if (i < length && expression[i] == '*')
                    {
                        token.Value = "*";
                        i++;
                    }
                    else
                    {
                        int nameStart = i;
                        i = _ReadNcName(expression, i);
                        if (i == nameStart)
                        {
                            throw new XPathException("XPST0003", "Invalid EQName in \"" + expression + "\"");
                        }
                        token.Value = expression.Substring(nameStart, i - nameStart);
                    }
                }
                else if (IsNameStartChar(ch))
                {
                    i = _ReadNcName(expression, i);
                    string ncName = expression.Substring(start, i - start);
                    token.Kind = TokenKind.Name;
                    token.Value = ncName;
                    if (i + 1 < length && expression[i] == ':' && expression[i + 1] != ':')
                    {
                        if (expression[i + 1] == '*')
                        {
                            token.Prefix = ncName;
                            token.Value = "*";
                            i += 2;
                        }
                        else if (IsNameStartChar(expression[i + 1]))
                        {
                            int localStart = i + 1;
                            i = _ReadNcName(expression, localStart);
                            token.Prefix = ncName;
                            token.Value = expression.Substring(localStart, i - localStart);
                        }
                    }
                }
                else if (ch == '*' && i + 2 < length && expression[i + 1] == ':' && IsNameStartChar(expression[i + 2]))
                {
                    int localStart = i + 2;
                    i = _ReadNcName(expression, localStart);
                    token.Kind = TokenKind.Name;
                    token.Prefix = "*";
                    token.Value = expression.Substring(localStart, i - localStart);
                }
                else
                {
                    string symbol = null;
                    foreach (string s in _Symbols)
                    {
                        if (String.CompareOrdinal(expression, i, s, 0, s.Length) == 0)
                        {
                            symbol = s;
                            break;
                        }
                    }
                    if (symbol == null)
                    {
                        throw new XPathException("XPST0003", "Unexpected character '" + ch + "' at position " + i + " in \"" + expression + "\"");
                    }
                    token.Kind = TokenKind.Symbol;
                    token.Value = symbol;
                    i += symbol.Length;
                }
                tokens.Add(token);
            }
        }


        private static int _SkipWhitespaceAndComments(string s, int i)
        {
            while (i < s.Length)
            {
                char ch = s[i];
                if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')
                {
                    i++;
                    continue;
                }
                if (ch == '(' && i + 1 < s.Length && s[i + 1] == ':')
                {
                    int depth = 1;
                    i += 2;
                    while (i < s.Length && depth > 0)
                    {
                        if (s[i] == '(' && i + 1 < s.Length && s[i + 1] == ':')
                        {
                            depth++;
                            i += 2;
                        }
                        else if (s[i] == ':' && i + 1 < s.Length && s[i + 1] == ')')
                        {
                            depth--;
                            i += 2;
                        }
                        else
                        {
                            i++;
                        }
                    }
                    if (depth > 0)
                    {
                        throw new XPathException("XPST0003", "Unterminated comment in \"" + s + "\"");
                    }
                    continue;
                }
                break;
            }
            return i;
        }


        private static int _ReadNumber(string s, int i, Token token)
        {
            int start = i;
            while (i < s.Length && Char.IsDigit(s[i]))
            {
                i++;
            }
            TokenKind kind = TokenKind.Integer;
            if (i < s.Length && s[i] == '.' && !(i + 1 < s.Length && s[i + 1] == '.'))
            {
                kind = TokenKind.Decimal;
                i++;
                while (i < s.Length && Char.IsDigit(s[i]))
                {
                    i++;
                }
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                int j = i + 1;
                if (j < s.Length && (s[j] == '+' || s[j] == '-'))
                {
                    j++;
                }
                if (j < s.Length && Char.IsDigit(s[j]))
                {
                    kind = TokenKind.Double;
                    i = j;
                    while (i < s.Length && Char.IsDigit(s[i]))
                    {
                        i++;
                    }
                }
            }
            token.Kind = kind;
            token.Value = s.Substring(start, i - start);
            return i;
        }


        private static int _ReadNcName(string s, int i)
        {
            if (i >= s.Length || !IsNameStartChar(s[i]))
            {
                return i;
            }
            i++;
            while (i < s.Length && IsNameChar(s[i]))
            {
                i++;
            }
            return i;
        }


        public static bool IsNameStartChar(char ch)
        {
            return Char.IsLetter(ch) || ch == '_' || (ch >= 0xC0 && ch <= 0x2FF && ch != 0xD7 && ch != 0xF7) || (ch >= 0x370 && ch <= 0x1FFF && ch != 0x37E);
        }


        public static bool IsNameChar(char ch)
        {
            return IsNameStartChar(ch) || Char.IsDigit(ch) || ch == '-' || ch == '.' || ch == 0xB7 || (ch >= 0x300 && ch <= 0x36F) || ch == 0x203F || ch == 0x2040
                   || Char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark
                   || Char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.SpacingCombiningMark;
        }
    }
}
