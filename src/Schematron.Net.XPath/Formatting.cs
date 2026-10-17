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
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// fn:format-integer (F&amp;O 3.1, 4.6.1) and fn:format-date / format-dateTime / format-time (9.8), with English
    /// and German names and words.
    /// </summary>
    internal static class Formatting
    {
        // ------------------------------------------------------------------------------------------------------------
        // languages
        // ------------------------------------------------------------------------------------------------------------

        private sealed class Language
        {
            public string Code;
            public string[] Months;

            /// <summary>Monday first.</summary>
            public string[] Days;
            public Func<BigInteger, string> Cardinal;

            /// <summary>Ordinal words (number, variant of the format modifier o(...)).</summary>
            public Func<BigInteger, string, string> OrdinalWords;

            /// <summary>The suffix of an ordinal number in digits.</summary>
            public Func<BigInteger, string> OrdinalSuffix;
            public string Am;
            public string Pm;
        }


        private static readonly Language _English = new Language
        {
            Code = "en",
            Months = new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" },
            Days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" },
            Cardinal = _EnglishWords,
            OrdinalWords = (n, variant) => _EnglishOrdinalWords(n),
            OrdinalSuffix = _EnglishOrdinalSuffix,
            Am = "am",
            Pm = "pm"
        };


        private static readonly Language _German = new Language
        {
            Code = "de",
            Months = new[] { "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember" },
            Days = new[] { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" },
            Cardinal = _GermanWords,
            OrdinalWords = _GermanOrdinalWords,
            OrdinalSuffix = n => ".",
            Am = "vorm.",
            Pm = "nachm."
        };


        /// <summary>
        /// The language to use; unsupported languages fall back to English.
        /// </summary>
        private static Language _Language(string requested, out bool supported)
        {
            supported = true;
            if (String.IsNullOrEmpty(requested))
            {
                return _English;
            }
            string primary = requested.Split('-')[0];
            if (String.Equals(primary, "en", StringComparison.OrdinalIgnoreCase))
            {
                return _English;
            }
            if (String.Equals(primary, "de", StringComparison.OrdinalIgnoreCase))
            {
                return _German;
            }
            supported = false;
            return _English;
        }


        // ------------------------------------------------------------------------------------------------------------
        // fn:format-integer
        // ------------------------------------------------------------------------------------------------------------

        public static string FormatInteger(BigInteger value, string picture, string language)
        {
            int semicolon = picture.LastIndexOf(';');
            string token = semicolon >= 0 ? picture.Substring(0, semicolon) : picture;
            string modifier = semicolon >= 0 ? picture.Substring(semicolon + 1) : "";
            if (token.Length == 0)
            {
                throw new XPathException("FODF1310", "The primary format token of format-integer() is empty");
            }
            string variant;
            bool ordinal = _ParseFormatModifier(modifier, out variant);
            bool supported;
            Language lang = _Language(language, out supported);
            NumberFormat format = NumberFormat.Parse(token, "FODF1310", false);
            return format.Format(value, ordinal, variant, lang, 1, int.MaxValue);
        }


        /// <summary>
        /// The format modifier of format-integer: ^([co](\(.+\))?)?[at]?$. Returns true for an ordinal.
        /// </summary>
        private static bool _ParseFormatModifier(string modifier, out string variant)
        {
            variant = null;
            int i = 0;
            bool ordinal = false;
            if (i < modifier.Length && (modifier[i] == 'c' || modifier[i] == 'o'))
            {
                ordinal = modifier[i] == 'o';
                i++;
                if (i < modifier.Length && modifier[i] == '(')
                {
                    int close = modifier.IndexOf(')', i);
                    if (close < 0 || close == i + 1)
                    {
                        throw new XPathException("FODF1310", "Invalid format modifier '" + modifier + "'");
                    }
                    variant = modifier.Substring(i + 1, close - i - 1);
                    i = close + 1;
                }
            }
            if (i < modifier.Length && (modifier[i] == 'a' || modifier[i] == 't'))
            {
                i++;
            }
            if (i != modifier.Length)
            {
                throw new XPathException("FODF1310", "Invalid format modifier '" + modifier + "'");
            }
            return ordinal;
        }


        // ------------------------------------------------------------------------------------------------------------
        // number formats
        // ------------------------------------------------------------------------------------------------------------

        private enum NumberKind
        {
            Decimal,
            AlphaLower,
            AlphaUpper,
            RomanLower,
            RomanUpper,
            WordsLower,
            WordsUpper,
            WordsTitle
        }


        /// <summary>
        /// A primary format token: a decimal digit pattern (digits of one family, # and grouping separators), a, A,
        /// i, I, w, W or Ww. Other tokens fall back to the pattern 1.
        /// </summary>
        private sealed class NumberFormat
        {
            public NumberKind Kind;

            /// <summary>The code point of the digit zero of the digit family.</summary>
            public int Zero = '0';
            public int Mandatory = 1;
            public int Optional;

            /// <summary>Grouping separators: (number of digit signs to the right, or to the left for fractions; separator).</summary>
            public List<KeyValuePair<int, string>> Separators = new List<KeyValuePair<int, string>>();

            /// <summary>The grouping size if the grouping is regular (repeated beyond the pattern), else 0.</summary>
            public int GroupSize;
            public string GroupSeparator;


            public int Digits => Mandatory + Optional;


            public static NumberFormat Parse(string token, string errorCode, bool fraction)
            {
                switch (token)
                {
                    case "a":
                        return new NumberFormat { Kind = NumberKind.AlphaLower };
                    case "A":
                        return new NumberFormat { Kind = NumberKind.AlphaUpper };
                    case "i":
                        return new NumberFormat { Kind = NumberKind.RomanLower };
                    case "I":
                        return new NumberFormat { Kind = NumberKind.RomanUpper };
                    case "w":
                        return new NumberFormat { Kind = NumberKind.WordsLower };
                    case "W":
                        return new NumberFormat { Kind = NumberKind.WordsUpper };
                    case "Ww":
                        return new NumberFormat { Kind = NumberKind.WordsTitle };
                }
                List<int> codePoints = _CodePoints(token);
                if (!codePoints.Exists(cp => _DigitValue(cp) >= 0))
                {
                    // not a decimal digit pattern, and a numbering sequence this library does not know: the fallback is 1
                    return new NumberFormat();
                }
                NumberFormat format = new NumberFormat { Mandatory = 0 };
                int zero = -1;
                // the digit signs and separators, read from the right (from the left for fractional seconds)
                List<int> ordered = new List<int>(codePoints);
                if (!fraction)
                {
                    ordered.Reverse();
                }
                int digitSigns = 0;
                bool previousWasSeparator = true;
                bool seenOptional = false;
                foreach (int cp in ordered)
                {
                    int digit = _DigitValue(cp);
                    if (digit >= 0)
                    {
                        if (zero >= 0 && cp - digit != zero)
                        {
                            throw new XPathException(errorCode, "The digits of the picture '" + token + "' belong to different digit families");
                        }
                        zero = cp - digit;
                        if (seenOptional && !fraction)
                        {
                            // read from the right, a mandatory digit after an optional one: # follows a digit
                            throw new XPathException(errorCode, "In the picture '" + token + "', an optional digit sign (#) follows a mandatory digit");
                        }
                        if (seenOptional && fraction)
                        {
                            throw new XPathException(errorCode, "In the picture '" + token + "', a mandatory digit follows an optional digit sign (#)");
                        }
                        format.Mandatory++;
                        digitSigns++;
                        previousWasSeparator = false;
                    }
                    else if (cp == '#')
                    {
                        if (fraction && format.Mandatory == 0)
                        {
                            throw new XPathException(errorCode, "In the picture '" + token + "', an optional digit sign (#) precedes the mandatory digits");
                        }
                        seenOptional = true;
                        format.Optional++;
                        digitSigns++;
                        previousWasSeparator = false;
                    }
                    else if (_IsAlphanumeric(cp))
                    {
                        throw new XPathException(errorCode, "Invalid character in the decimal digit pattern '" + token + "'");
                    }
                    else
                    {
                        if (previousWasSeparator)
                        {
                            throw new XPathException(errorCode, "A grouping separator in the picture '" + token + "' is at the start or end or next to another one");
                        }
                        format.Separators.Add(new KeyValuePair<int, string>(digitSigns, Char.ConvertFromUtf32(cp)));
                        previousWasSeparator = true;
                    }
                }
                if (previousWasSeparator && format.Separators.Count > 0)
                {
                    throw new XPathException(errorCode, "A grouping separator in the picture '" + token + "' is at the start or end");
                }
                if (format.Mandatory == 0)
                {
                    throw new XPathException(errorCode, "The decimal digit pattern '" + token + "' has no mandatory digit");
                }
                format.Zero = zero;
                format._DetectRegularGrouping();
                return format;
            }


            /// <summary>
            /// Regular grouping: one separator character at every multiple of a group size within the pattern; it is
            /// then repeated for numbers with more digits than the pattern.
            /// </summary>
            private void _DetectRegularGrouping()
            {
                if (Separators.Count == 0)
                {
                    return;
                }
                int size = Separators[0].Key;
                string separator = Separators[0].Value;
                for (int i = 0; i < Separators.Count; i++)
                {
                    if (Separators[i].Value != separator || Separators[i].Key != size * (i + 1))
                    {
                        return;
                    }
                }
                if (size * (Separators.Count + 1) < Digits)
                {
                    return;
                }
                GroupSize = size;
                GroupSeparator = separator;
            }


            /// <summary>
            /// Formats an integer with at least minWidth and (for decimal digits) at most maxWidth digits.
            /// </summary>
            public string Format(BigInteger value, bool ordinal, string variant, Language language, int minWidth, int maxWidth)
            {
                string sign = value.Sign < 0 ? "-" : "";
                BigInteger abs = BigInteger.Abs(value);
                switch (Kind)
                {
                    case NumberKind.AlphaLower:
                    case NumberKind.AlphaUpper:
                        if (abs.IsZero)
                        {
                            break;
                        }
                        return sign + _Alphabetic(abs, Kind == NumberKind.AlphaUpper);
                    case NumberKind.RomanLower:
                    case NumberKind.RomanUpper:
                        if (abs.IsZero || abs > 4999)
                        {
                            break;
                        }
                        string roman = _Roman((int)abs);
                        return sign + (Kind == NumberKind.RomanLower ? roman.ToLowerInvariant() : roman);
                    case NumberKind.WordsLower:
                    case NumberKind.WordsUpper:
                    case NumberKind.WordsTitle:
                        string words = ordinal ? language.OrdinalWords(abs, variant) : language.Cardinal(abs);
                        return sign + _Case(words, Kind == NumberKind.WordsUpper ? "N" : Kind == NumberKind.WordsTitle ? "Nn" : "n");
                }
                return sign + FormatDigits(abs, minWidth, maxWidth) + (ordinal ? language.OrdinalSuffix(abs) : "");
            }


            /// <summary>
            /// The decimal digits of a non-negative integer, padded to the minimum number of digits (the larger of
            /// the pattern's and minWidth), cut to the rightmost maxWidth digits, grouped and in the digit family.
            /// </summary>
            public string FormatDigits(BigInteger abs, int minWidth, int maxWidth)
            {
                string digits = abs.ToString(CultureInfo.InvariantCulture);
                if (digits.Length > maxWidth)
                {
                    digits = digits.Substring(digits.Length - maxWidth);
                }
                int min = Math.Max(Kind == NumberKind.Decimal ? Mandatory : 1, minWidth);
                if (digits.Length < min)
                {
                    digits = new string('0', min - digits.Length) + digits;
                }
                return Group(digits, false);
            }


            /// <summary>
            /// Inserts the grouping separators and converts the ASCII digits to the digit family.
            /// </summary>
            public string Group(string digits, bool fromLeft)
            {
                StringBuilder sb = new StringBuilder();
                int count = digits.Length;
                for (int i = 0; i < count; i++)
                {
                    // the number of digits before (fromLeft) or after (from the right) this position
                    int position = fromLeft ? i : count - i;
                    if (i > 0)
                    {
                        string separator = _SeparatorAt(position);
                        if (separator != null)
                        {
                            sb.Append(separator);
                        }
                    }
                    sb.Append(Char.ConvertFromUtf32(Zero + (digits[i] - '0')));
                }
                return sb.ToString();
            }


            private string _SeparatorAt(int position)
            {
                if (GroupSize > 0)
                {
                    return position % GroupSize == 0 ? GroupSeparator : null;
                }
                foreach (KeyValuePair<int, string> separator in Separators)
                {
                    if (separator.Key == position)
                    {
                        return separator.Value;
                    }
                }
                return null;
            }
        }


        private static List<int> _CodePoints(string s)
        {
            List<int> result = new List<int>();
            for (int i = 0; i < s.Length; i++)
            {
                if (Char.IsHighSurrogate(s[i]) && i + 1 < s.Length && Char.IsLowSurrogate(s[i + 1]))
                {
                    result.Add(Char.ConvertToUtf32(s[i], s[i + 1]));
                    i++;
                }
                else
                {
                    result.Add(s[i]);
                }
            }
            return result;
        }


        /// <summary>The value of a decimal digit (Unicode category Nd), -1 for other characters.</summary>
        private static int _DigitValue(int codePoint)
        {
            string s = Char.ConvertFromUtf32(codePoint);
            return CharUnicodeInfo.GetUnicodeCategory(s, 0) == UnicodeCategory.DecimalDigitNumber ? CharUnicodeInfo.GetDecimalDigitValue(s, 0) : -1;
        }


        private static bool _IsAlphanumeric(int codePoint)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(Char.ConvertFromUtf32(codePoint), 0))
            {
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.LetterNumber:
                case UnicodeCategory.OtherNumber:
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                    return true;
                default:
                    return false;
            }
        }


        private static string _Alphabetic(BigInteger n, bool upper)
        {
            StringBuilder sb = new StringBuilder();
            while (n > 0)
            {
                n -= 1;
                sb.Insert(0, (char)((upper ? 'A' : 'a') + (int)(n % 26)));
                n /= 26;
            }
            return sb.ToString();
        }


        private static string _Roman(int n)
        {
            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                while (n >= values[i])
                {
                    sb.Append(symbols[i]);
                    n -= values[i];
                }
            }
            return sb.ToString();
        }


        /// <summary>
        /// Applies the case of a name or words presentation: N upper case, n lower case, Nn title case.
        /// </summary>
        private static string _Case(string text, string presentation)
        {
            switch (presentation)
            {
                case "N":
                    return text.ToUpperInvariant();
                case "n":
                    return text.ToLowerInvariant();
                default:
                    StringBuilder sb = new StringBuilder(text.Length);
                    bool start = true;
                    foreach (char ch in text)
                    {
                        sb.Append(start ? Char.ToUpperInvariant(ch) : Char.ToLowerInvariant(ch));
                        start = ch == ' ' || ch == '-';
                    }
                    return sb.ToString();
            }
        }


        // ------------------------------------------------------------------------------------------------------------
        // words
        // ------------------------------------------------------------------------------------------------------------

        private static readonly string[] _EnglishSmall =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen",
            "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"
        };

        private static readonly string[] _EnglishTens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

        private static readonly string[] _EnglishScales = { "", "thousand", "million", "billion", "trillion", "quadrillion", "quintillion", "sextillion" };


        private static string _EnglishWords(BigInteger n)
        {
            if (n.IsZero)
            {
                return "zero";
            }
            List<string> parts = new List<string>();
            int scale = 0;
            while (n > 0)
            {
                int group = (int)(n % 1000);
                n /= 1000;
                if (group != 0)
                {
                    if (scale >= _EnglishScales.Length)
                    {
                        return n.ToString(CultureInfo.InvariantCulture);
                    }
                    parts.Insert(0, _EnglishBelowThousand(group) + (scale > 0 ? " " + _EnglishScales[scale] : ""));
                }
                scale++;
            }
            return String.Join(" ", parts);
        }


        private static string _EnglishBelowThousand(int n)
        {
            List<string> words = new List<string>();
            if (n >= 100)
            {
                words.Add(_EnglishSmall[n / 100] + " hundred");
                n %= 100;
            }
            if (n >= 20)
            {
                words.Add(_EnglishTens[n / 10] + (n % 10 != 0 ? " " + _EnglishSmall[n % 10] : ""));
            }
            else if (n > 0)
            {
                words.Add(_EnglishSmall[n]);
            }
            return String.Join(" ", words);
        }


        private static string _EnglishOrdinalWords(BigInteger n)
        {
            string words = _EnglishWords(n);
            int last = words.LastIndexOf(' ');
            string head = last >= 0 ? words.Substring(0, last + 1) : "";
            string tail = last >= 0 ? words.Substring(last + 1) : words;
            switch (tail)
            {
                case "one":
                    tail = "first";
                    break;
                case "two":
                    tail = "second";
                    break;
                case "three":
                    tail = "third";
                    break;
                case "five":
                    tail = "fifth";
                    break;
                case "eight":
                    tail = "eighth";
                    break;
                case "nine":
                    tail = "ninth";
                    break;
                case "twelve":
                    tail = "twelfth";
                    break;
                default:
                    tail = tail.EndsWith("y", StringComparison.Ordinal) ? tail.Substring(0, tail.Length - 1) + "ieth" : tail + "th";
                    break;
            }
            return head + tail;
        }


        private static string _EnglishOrdinalSuffix(BigInteger n)
        {
            int lastTwo = (int)(n % 100);
            if (lastTwo >= 11 && lastTwo <= 13)
            {
                return "th";
            }
            switch ((int)(n % 10))
            {
                case 1:
                    return "st";
                case 2:
                    return "nd";
                case 3:
                    return "rd";
                default:
                    return "th";
            }
        }


        private static readonly string[] _GermanSmall =
        {
            "null", "ein", "zwei", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun", "zehn", "elf", "zwölf", "dreizehn",
            "vierzehn", "fünfzehn", "sechzehn", "siebzehn", "achtzehn", "neunzehn"
        };

        private static readonly string[] _GermanTens = { "", "", "zwanzig", "dreißig", "vierzig", "fünfzig", "sechzig", "siebzig", "achtzig", "neunzig" };


        private static string _GermanWords(BigInteger n)
        {
            if (n.IsZero)
            {
                return "null";
            }
            string words = _GermanCompound(n);
            // a final "ein" is "eins" (einundzwanzig, but hunderteins)
            return words.EndsWith("ein", StringComparison.Ordinal) ? words + "s" : words;
        }


        private static string _GermanCompound(BigInteger n)
        {
            if (n >= 1000000)
            {
                BigInteger millions = n / 1000000;
                BigInteger rest = n % 1000000;
                string head = millions == 1 ? "eine Million" : _GermanCompound(millions) + " Millionen";
                return rest.IsZero ? head : head + " " + _GermanCompound(rest);
            }
            int value = (int)n;
            StringBuilder sb = new StringBuilder();
            if (value >= 1000)
            {
                sb.Append(_GermanBelowThousand(value / 1000)).Append("tausend");
                value %= 1000;
            }
            sb.Append(_GermanBelowThousand(value));
            return sb.ToString();
        }


        private static string _GermanBelowThousand(int n)
        {
            StringBuilder sb = new StringBuilder();
            if (n >= 100)
            {
                sb.Append(_GermanSmall[n / 100]).Append("hundert");
                n %= 100;
            }
            if (n >= 20)
            {
                if (n % 10 != 0)
                {
                    sb.Append(_GermanSmall[n % 10]).Append("und");
                }
                sb.Append(_GermanTens[n / 10]);
            }
            else if (n > 0)
            {
                sb.Append(_GermanSmall[n]);
            }
            return sb.ToString();
        }


        /// <summary>
        /// German ordinal words: the stem (erst, zweit, dritt, ..., zwanzigst, hundertst) and the ending of the
        /// variant (o(-er) → erster), "e" by default.
        /// </summary>
        private static string _GermanOrdinalWords(BigInteger n, string variant)
        {
            string ending = variant != null && variant.StartsWith("-", StringComparison.Ordinal) ? variant.Substring(1) : "e";
            int lastTwo = (int)(n % 100);
            string stem;
            if (lastTwo >= 1 && lastTwo < 20)
            {
                string head = n - lastTwo == 0 ? "" : _GermanCompound(n - lastTwo);
                switch (lastTwo)
                {
                    case 1:
                        stem = head + "erst";
                        break;
                    case 3:
                        stem = head + "dritt";
                        break;
                    case 7:
                        stem = head + "siebt";
                        break;
                    case 8:
                        stem = head + "acht";
                        break;
                    default:
                        stem = head + _GermanSmall[lastTwo] + "t";
                        break;
                }
            }
            else
            {
                stem = (n.IsZero ? "null" : _GermanCompound(n)) + "st";
            }
            return stem + ending;
        }


        // ------------------------------------------------------------------------------------------------------------
        // fn:format-date, format-dateTime, format-time
        // ------------------------------------------------------------------------------------------------------------

        private static readonly HashSet<string> _Calendars = new HashSet<string>(StringComparer.Ordinal)
        {
            "AD", "AH", "AME", "AM", "AP", "AS", "BE", "CB", "CE", "CL", "CS", "EE", "FE", "ISO", "JE", "KE", "KY", "ME", "MS", "NS",
            "OS", "RS", "SE", "SH", "SS", "TE", "VE", "VS"
        };


        /// <summary>
        /// Formats a date, time or dateTime. kind: 'D' (date), 'T' (time) or 'B' (both, dateTime).
        /// </summary>
        public static string FormatDateTime(CalendarValue value, string picture, string language, string calendar, string place, char kind)
        {
            StringBuilder prefix = new StringBuilder();
            bool supported;
            Language lang = _Language(language, out supported);
            if (!supported)
            {
                prefix.Append("[Language: en]");
            }
            string calendarName = "ISO";
            if (!String.IsNullOrEmpty(calendar))
            {
                string name = _CalendarName(calendar.Trim());
                if (name == null)
                {
                    // a calendar in a namespace: implementation-defined, not supported
                    prefix.Insert(0, "[Calendar: AD]");
                }
                else if (!_Calendars.Contains(name))
                {
                    throw new XPathException("FOFD1340", "Unknown calendar '" + calendar + "'");
                }
                else if (name == "AD" || name == "ISO" || name == "CE")
                {
                    calendarName = name;
                }
                else
                {
                    prefix.Insert(0, "[Calendar: AD]");
                }
            }
            value = _AdjustToPlace(value, place);

            StringBuilder result = new StringBuilder(prefix.ToString());
            int i = 0;
            while (i < picture.Length)
            {
                char ch = picture[i];
                if (ch == '[')
                {
                    if (i + 1 < picture.Length && picture[i + 1] == '[')
                    {
                        result.Append('[');
                        i += 2;
                        continue;
                    }
                    int close = picture.IndexOf(']', i + 1);
                    int nested = picture.IndexOf('[', i + 1);
                    if (close < 0 || (nested >= 0 && nested < close))
                    {
                        throw new XPathException("FOFD1340", "Unmatched '[' in the picture '" + picture + "'");
                    }
                    result.Append(_FormatMarker(value, picture.Substring(i + 1, close - i - 1), lang, calendarName, kind));
                    i = close + 1;
                }
                else if (ch == ']')
                {
                    if (i + 1 < picture.Length && picture[i + 1] == ']')
                    {
                        result.Append(']');
                        i += 2;
                        continue;
                    }
                    throw new XPathException("FOFD1340", "Unmatched ']' in the picture '" + picture + "'");
                }
                else
                {
                    result.Append(ch);
                    i++;
                }
            }
            return result.ToString();
        }


        /// <summary>
        /// The name of a calendar in no namespace (NCName or Q{}NCName), null for an EQName in a namespace.
        /// </summary>
        /// <exception cref="XPathException">FOFD1340: the calendar is not an EQName</exception>
        private static string _CalendarName(string calendar)
        {
            string local = calendar;
            if (calendar.StartsWith("Q{", StringComparison.Ordinal))
            {
                int close = calendar.IndexOf('}');
                if (close < 0)
                {
                    throw new XPathException("FOFD1340", "Invalid calendar name '" + calendar + "'");
                }
                local = calendar.Substring(close + 1);
                if (close > 2)
                {
                    return _IsNcName(local) ? null : throw new XPathException("FOFD1340", "Invalid calendar name '" + calendar + "'");
                }
            }
            else if (calendar.Contains(":"))
            {
                string[] parts = calendar.Split(':');
                if (parts.Length == 2 && _IsNcName(parts[0]) && _IsNcName(parts[1]))
                {
                    return null;
                }
                throw new XPathException("FOFD1340", "Invalid calendar name '" + calendar + "'");
            }
            if (!_IsNcName(local))
            {
                throw new XPathException("FOFD1340", "Invalid calendar name '" + calendar + "'");
            }
            return local;
        }


        private static bool _IsNcName(string s)
        {
            if (s.Length == 0 || !(Char.IsLetter(s[0]) || s[0] == '_'))
            {
                return false;
            }
            foreach (char ch in s)
            {
                if (!(Char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.'))
                {
                    return false;
                }
            }
            return true;
        }


        /// <summary>
        /// The place argument: a value with a timezone is shown in the timezone of an IANA (Olson) timezone name.
        /// </summary>
        private static CalendarValue _AdjustToPlace(CalendarValue value, string place)
        {
            if (String.IsNullOrWhiteSpace(place) || !value.TimezoneMinutes.HasValue || value.Type.Primitive == PrimitiveType.Time)
            {
                return value;
            }
            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(place.Trim());
            }
            catch (Exception e) when (e is TimeZoneNotFoundException || e is InvalidTimeZoneException)
            {
                return value;
            }
            if (value.Year < 1 || value.Year > 9998)
            {
                return value;
            }
            DateTime local = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Unspecified);
            DateTime utc = local.AddMinutes(-value.TimezoneMinutes.Value);
            int offset = (int)zone.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).TotalMinutes;
            DateTime target = utc.AddMinutes(offset);
            return new CalendarValue(value.Type, target.Year, target.Month, target.Day, target.Hour, target.Minute, value.Second, offset);
        }


        private static string _FormatMarker(CalendarValue value, string marker, Language lang, string calendarName, char kind)
        {
            // whitespace in a variable marker is ignored
            StringBuilder compact = new StringBuilder();
            foreach (char ch in marker)
            {
                if (!Char.IsWhiteSpace(ch))
                {
                    compact.Append(ch);
                }
            }
            marker = compact.ToString();
            if (marker.Length == 0)
            {
                throw new XPathException("FOFD1340", "Empty variable marker in the picture");
            }
            char component = marker[0];
            string rest = marker.Substring(1);

            // the width modifier follows the last comma
            int minWidth = 0;
            int maxWidth = 0;
            bool hasWidth = false;
            int comma = rest.LastIndexOf(',');
            if (comma >= 0)
            {
                _ParseWidth(rest.Substring(comma + 1), out minWidth, out maxWidth);
                hasWidth = true;
                rest = rest.Substring(0, comma);
            }

            // first and second presentation modifiers
            string second = null;
            if (rest.Length >= 2 && (rest[rest.Length - 1] == 'o' || rest[rest.Length - 1] == 't' || rest[rest.Length - 1] == 'c'))
            {
                second = rest.Substring(rest.Length - 1);
                rest = rest.Substring(0, rest.Length - 1);
            }
            else if (rest == "o" || rest == "t" || rest == "c")
            {
                second = rest;
                rest = "";
            }
            string first = rest.Length > 0 ? rest : null;

            const string dateComponents = "YMDdFWwCEZz";
            const string timeComponents = "HhPmsfCZz";
            if ("YMDdFWwHhPmsfZzCE".IndexOf(component) < 0)
            {
                throw new XPathException("FOFD1340", "Unknown component '" + component + "' in the picture");
            }
            if ((kind == 'D' && dateComponents.IndexOf(component) < 0) || (kind == 'T' && timeComponents.IndexOf(component) < 0))
            {
                throw new XPathException("FOFD1350", "The component '" + component + "' is not available in a value of type " + value.Type.QualifiedName);
            }

            switch (component)
            {
                case 'Z':
                case 'z':
                    return _FormatTimezone(value.TimezoneMinutes, first, second == "t", component == 'z');
                case 'P':
                    return _FormatName(value.Hour < 12 ? lang.Am : lang.Pm, first ?? "n", hasWidth, minWidth, maxWidth);
                case 'C':
                    return _FormatName(calendarName, first ?? "n", hasWidth, minWidth, maxWidth);
                case 'E':
                    return _FormatName(value.Year > 0 ? "AD" : "BC", first ?? "n", hasWidth, minWidth, maxWidth);
                case 'f':
                    return _FormatFraction(value.Second, first, hasWidth, minWidth, maxWidth);
            }

            long days = CalendarMath.DaysFromCivil(value.Year, value.Month, value.Day);
            int dayOfWeek = (int)(((days % 7) + 7) % 7) + 1;
            BigInteger number;
            string defaultFormat = "1";
            string[] names = null;
            switch (component)
            {
                case 'Y':
                    number = Math.Abs(value.Year);
                    break;
                case 'M':
                    number = value.Month;
                    names = lang.Months;
                    break;
                case 'D':
                    number = value.Day;
                    break;
                case 'd':
                    number = days - CalendarMath.DaysFromCivil(value.Year, 1, 1) + 1;
                    break;
                case 'F':
                    number = dayOfWeek;
                    names = lang.Days;
                    defaultFormat = "n";
                    break;
                case 'W':
                    number = _WeekOfYear(days, dayOfWeek);
                    break;
                case 'w':
                    number = _WeekOfMonth(days, dayOfWeek);
                    break;
                case 'H':
                    number = value.Hour;
                    break;
                case 'h':
                    number = value.Hour % 12 == 0 ? 12 : value.Hour % 12;
                    break;
                case 'm':
                    number = value.Minute;
                    defaultFormat = "01";
                    break;
                default:
                    number = (int)Math.Truncate(value.Second);
                    defaultFormat = "01";
                    break;
            }
            string format = first ?? defaultFormat;
            if (format == "N" || format == "n" || format == "Nn")
            {
                if (names != null)
                {
                    return _FormatName(names[(int)number - 1], format, hasWidth, minWidth, maxWidth);
                }
                format = "1";
            }
            NumberFormat numberFormat = NumberFormat.Parse(format, "FOFD1340", false);
            int max = hasWidth ? maxWidth : int.MaxValue;
            if (!hasWidth && component == 'Y' && numberFormat.Kind == NumberKind.Decimal && numberFormat.Digits >= 2)
            {
                // [Y01]: a two-digit year
                max = numberFormat.Digits;
            }
            if (component == 'Y' && max < 10)
            {
                // the year is cut to its last digits, whatever the presentation ([Yi,3-3] for 1004: iv)
                number %= BigInteger.Pow(10, max);
            }
            string text = numberFormat.Format(number, second == "o", null, lang, hasWidth ? minWidth : 1, max);
            if (hasWidth && numberFormat.Kind != NumberKind.Decimal && text.Length < minWidth)
            {
                text = text.PadRight(minWidth);
            }
            return text;
        }


        /// <summary>
        /// A width modifier: min[-max], each a positive integer or *.
        /// </summary>
        private static void _ParseWidth(string width, out int min, out int max)
        {
            string[] parts = width.Split('-');
            if (parts.Length > 2)
            {
                throw new XPathException("FOFD1340", "Invalid width modifier '," + width + "'");
            }
            min = _WidthValue(parts[0], 1, width);
            max = parts.Length == 2 ? _WidthValue(parts[1], int.MaxValue, width) : int.MaxValue;
            if (max < min)
            {
                throw new XPathException("FOFD1340", "The maximum width is less than the minimum width in '," + width + "'");
            }
        }


        private static int _WidthValue(string part, int star, string width)
        {
            if (part == "*")
            {
                return star;
            }
            int value;
            if (part.Length == 0 || !Int32.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 1)
            {
                throw new XPathException("FOFD1340", "Invalid width modifier '," + width + "'");
            }
            return value;
        }


        private static string _FormatName(string name, string presentation, bool hasWidth, int minWidth, int maxWidth)
        {
            if (presentation != "N" && presentation != "n" && presentation != "Nn")
            {
                presentation = "n";
            }
            string text = _Case(name, presentation);
            if (hasWidth && text.Length > maxWidth)
            {
                // the conventional abbreviation is the first three letters
                text = text.Substring(0, maxWidth >= 3 ? 3 : maxWidth);
            }
            if (hasWidth && text.Length < minWidth)
            {
                text = text.PadRight(minWidth);
            }
            return text;
        }


        /// <summary>
        /// The ISO week of the year: the week (Monday to Sunday) belongs to the year of its Thursday.
        /// </summary>
        private static int _WeekOfYear(long days, int dayOfWeek)
        {
            long thursday = days - dayOfWeek + 4;
            int year;
            int month;
            int day;
            CalendarMath.CivilFromDays(thursday, out year, out month, out day);
            return (int)((thursday - CalendarMath.DaysFromCivil(year, 1, 1)) / 7) + 1;
        }


        /// <summary>
        /// The week of the month: week 1 is the first week (Monday to Sunday) with at least four days in the month;
        /// the days before it belong to the last week of the previous month, the days after the last full week to a
        /// last week of their month.
        /// </summary>
        private static int _WeekOfMonth(long days, int dayOfWeek)
        {
            int year;
            int month;
            int day;
            CalendarMath.CivilFromDays(days, out year, out month, out day);
            long first = days - day + 1;
            int firstDayOfWeek = (int)(((first % 7) + 7) % 7) + 1;
            long week1 = firstDayOfWeek <= 4 ? first - (firstDayOfWeek - 1) : first + (8 - firstDayOfWeek);
            if (days >= week1)
            {
                return (int)((days - week1) / 7) + 1;
            }
            // the Monday of the week is in the previous month
            long monday = days - dayOfWeek + 1;
            return _WeekOfMonth(monday, 1);
        }


        /// <summary>
        /// The fractional seconds: the leading digits of the fraction, at least the mandatory digits of the pattern
        /// (or the minimum width), at most the digits of the pattern (or the maximum width; unlimited for a single
        /// digit), truncated, trailing zeros removed beyond the minimum.
        /// </summary>
        private static string _FormatFraction(decimal seconds, string first, bool hasWidth, int minWidth, int maxWidth)
        {
            NumberFormat format = NumberFormat.Parse(first ?? "1", "FOFD1340", true);
            if (format.Kind != NumberKind.Decimal)
            {
                format = NumberFormat.Parse("1", "FOFD1340", true);
            }
            int min = format.Mandatory;
            int max = format.Digits > 1 ? format.Digits : int.MaxValue;
            if (hasWidth)
            {
                min = Math.Max(min, minWidth);
                max = Math.Max(maxWidth, min);
            }
            string fraction = NumberFormatting.FormatDecimal(seconds - Math.Truncate(seconds));
            int point = fraction.IndexOf('.');
            string digits = point >= 0 ? fraction.Substring(point + 1) : "";
            if (digits.Length > max)
            {
                digits = digits.Substring(0, max);
            }
            digits = digits.TrimEnd('0');
            if (digits.Length < min)
            {
                digits = digits.PadRight(min, '0');
            }
            return format.Group(digits, true);
        }


        /// <summary>
        /// The timezone: as an offset (+05:30) with the digits and separator of the presentation modifier, as a
        /// military timezone letter (Z), with GMT for z; Z for UTC with the modifier t.
        /// </summary>
        private static string _FormatTimezone(int? timezone, string first, bool traditional, bool gmt)
        {
            if (!timezone.HasValue)
            {
                return first == "Z" ? "J" : "";
            }
            int offset = timezone.Value;
            if (first == "Z")
            {
                if (offset % 60 == 0 && Math.Abs(offset) <= 720)
                {
                    int hours = offset / 60;
                    return hours == 0 ? "Z" : hours > 0 ? ((char)(hours <= 9 ? 'A' + hours - 1 : 'K' + hours - 10)).ToString() : ((char)('N' - hours - 1)).ToString();
                }
                first = null;
            }
            if (traditional && offset == 0)
            {
                return "Z";
            }
            if (first == null || first == "N" || first == "n" || first == "Nn")
            {
                first = "01:01";
            }
            List<int> codePoints = _CodePoints(first);
            int zero = '0';
            int hourDigits = 0;
            int minuteDigits = 0;
            string separator = null;
            foreach (int cp in codePoints)
            {
                int digit = _DigitValue(cp);
                if (digit >= 0 || cp == '#')
                {
                    if (digit >= 0)
                    {
                        zero = cp - digit;
                    }
                    if (separator == null)
                    {
                        hourDigits++;
                    }
                    else
                    {
                        minuteDigits++;
                    }
                }
                else if (separator == null)
                {
                    separator = Char.ConvertFromUtf32(cp);
                }
            }
            if (hourDigits == 0)
            {
                hourDigits = 2;
                separator = ":";
            }
            int abs = Math.Abs(offset);
            string sign = offset < 0 ? "-" : "+";
            string text;
            if (separator != null)
            {
                text = (abs / 60).ToString(CultureInfo.InvariantCulture).PadLeft(hourDigits, '0') + separator
                       + (abs % 60).ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(2, minuteDigits), '0');
            }
            else if (hourDigits <= 2)
            {
                text = (abs / 60).ToString(CultureInfo.InvariantCulture).PadLeft(hourDigits, '0');
                if (abs % 60 != 0)
                {
                    text += ":" + (abs % 60).ToString("00", CultureInfo.InvariantCulture);
                }
            }
            else
            {
                text = (abs / 60 * 100 + abs % 60).ToString(CultureInfo.InvariantCulture).PadLeft(hourDigits, '0');
            }
            StringBuilder sb = new StringBuilder(gmt ? "GMT" : "");
            sb.Append(sign);
            foreach (char ch in text)
            {
                sb.Append(ch >= '0' && ch <= '9' ? Char.ConvertFromUtf32(zero + (ch - '0')) : ch.ToString());
            }
            return sb.ToString();
        }
    }
}
