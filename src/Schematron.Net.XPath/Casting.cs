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
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// Casting between atomic types (XPath 2.0 Functions and Operators, chapter 17).
    /// </summary>
    internal static class Casting
    {
        private static readonly Regex _DecimalLexical = new Regex(@"^[+-]?(\d+(\.\d*)?|\.\d+)$", RegexOptions.CultureInvariant);
        private static readonly Regex _IntegerLexical = new Regex(@"^[+-]?\d+$", RegexOptions.CultureInvariant);
        private static readonly Regex _DoubleLexical = new Regex(@"^([+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?|[+-]?INF|NaN)$", RegexOptions.CultureInvariant);
        private static readonly Regex _DateTimeLexical = new Regex(@"^(-?\d{4,})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2}(\.\d+)?)(Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant);
        private static readonly Regex _DateLexical = new Regex(@"^(-?\d{4,})-(\d{2})-(\d{2})(Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant);
        private static readonly Regex _TimeLexical = new Regex(@"^(\d{2}):(\d{2}):(\d{2}(\.\d+)?)(Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant);
        private static readonly Regex _GYearLexical = new Regex(@"^(-?\d{4,})(Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant);
        private static readonly Regex _GYearMonthLexical = new Regex(@"^(-?\d{4,})-(\d{2})(Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant);
        private static readonly Regex _GMonthLexical = new Regex(@"^--(\d{2})(Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant);
        private static readonly Regex _GMonthDayLexical = new Regex(@"^--(\d{2})-(\d{2})(Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant);
        private static readonly Regex _GDayLexical = new Regex(@"^---(\d{2})(Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant);
        private static readonly Regex _DurationLexical = new Regex(@"^(-)?P(?:(\d+)Y)?(?:(\d+)M)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?)?$", RegexOptions.CultureInvariant);
        private static readonly Regex _NcNameLexical = new Regex(@"^[\p{L}_][\p{L}\p{Nd}\p{Mn}\p{Mc}\p{Pc}\p{Lm}.\-·]*$", RegexOptions.CultureInvariant);
        private static readonly Regex _NameLexical = new Regex(@"^[\p{L}_:][\p{L}\p{Nd}\p{Mn}\p{Mc}\p{Pc}\p{Lm}.\-·:]*$", RegexOptions.CultureInvariant);
        private static readonly Regex _NmTokenLexical = new Regex(@"^[\p{L}\p{Nd}\p{Mn}\p{Mc}\p{Pc}\p{Lm}.\-·:_]+$", RegexOptions.CultureInvariant);
        private static readonly Regex _LanguageLexical = new Regex(@"^[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*$", RegexOptions.CultureInvariant);


        /// <summary>
        /// Casts the value to the target type, raising FORG0001 for invalid lexical forms and XPTY0004 for casts
        /// that are not allowed.
        /// </summary>
        public static XdmAtomicValue Cast(XdmAtomicValue value, XsType target)
        {
            XdmAtomicValue result;
            string error = TryCast(value, target, out result);
            if (error != null)
            {
                throw new XPathException(error, "Cannot convert " + _Describe(value) + " to " + target.QualifiedName);
            }
            return result;
        }


        public static bool IsCastable(XdmAtomicValue value, XsType target)
        {
            XdmAtomicValue result;
            return TryCast(value, target, out result) == null;
        }


        private static string _Describe(XdmAtomicValue value)
        {
            string s = value.StringValue;
            if (s.Length > 60)
            {
                s = s.Substring(0, 60) + "...";
            }
            return value.Type.QualifiedName + " \"" + s + "\"";
        }


        /// <summary>
        /// Returns null on success, else the error code.
        /// </summary>
        public static string TryCast(XdmAtomicValue value, XsType target, out XdmAtomicValue result)
        {
            result = null;
            if (target.IsAbstract)
            {
                return "XPST0080";
            }
            if (value.Type == target)
            {
                result = value;
                return null;
            }

            PrimitiveType source = value.Type.Primitive;

            // casts from xs:string / xs:untypedAtomic: parse the lexical form
            if (source == PrimitiveType.String || source == PrimitiveType.UntypedAtomic)
            {
                return FromString(value.StringValue, target, out result);
            }

            // casts to xs:string / xs:untypedAtomic
            if (target.Primitive == PrimitiveType.String || target == XsType.UntypedAtomic)
            {
                if (target == XsType.String || target == XsType.UntypedAtomic)
                {
                    result = new StringAtomic(value.StringValue, target);
                    return null;
                }
                return FromString(value.StringValue, target, out result);
            }

            switch (target.Primitive)
            {
                case PrimitiveType.Boolean:
                    if (value is NumericValue numeric)
                    {
                        result = BooleanValue.Get(!(numeric.IsZero || numeric.IsNaN));
                        return null;
                    }
                    return "XPTY0004";

                case PrimitiveType.Double:
                    if (value is NumericValue n1)
                    {
                        result = new DoubleValue(n1.ToDouble());
                        return null;
                    }
                    if (value is BooleanValue b1)
                    {
                        result = new DoubleValue(b1.Value ? 1d : 0d);
                        return null;
                    }
                    return "XPTY0004";

                case PrimitiveType.Float:
                    if (value is NumericValue n2)
                    {
                        result = new FloatValue((float)n2.ToDouble());
                        return null;
                    }
                    if (value is BooleanValue b2)
                    {
                        result = new FloatValue(b2.Value ? 1f : 0f);
                        return null;
                    }
                    return "XPTY0004";

                case PrimitiveType.Decimal:
                    return _ToDecimalFamily(value, target, out result);

                case PrimitiveType.AnyUri:
                    return "XPTY0004";

                case PrimitiveType.Duration:
                    if (value is DurationValue duration)
                    {
                        if (target == XsType.DayTimeDuration)
                        {
                            result = new DurationValue(target, 0, duration.Seconds);
                        }
                        else if (target == XsType.YearMonthDuration)
                        {
                            result = new DurationValue(target, duration.Months, 0);
                        }
                        else
                        {
                            result = new DurationValue(target, duration.Months, duration.Seconds);
                        }
                        return null;
                    }
                    return "XPTY0004";

                case PrimitiveType.DateTime:
                case PrimitiveType.Date:
                case PrimitiveType.Time:
                case PrimitiveType.GYear:
                case PrimitiveType.GYearMonth:
                case PrimitiveType.GMonth:
                case PrimitiveType.GMonthDay:
                case PrimitiveType.GDay:
                    return _CalendarCast(value, target, out result);

                case PrimitiveType.HexBinary:
                case PrimitiveType.Base64Binary:
                    if (value is BinaryValue binary)
                    {
                        result = new BinaryValue(target, binary.Value);
                        return null;
                    }
                    return "XPTY0004";

                case PrimitiveType.QName:
                    return "XPTY0004";
            }
            return "XPTY0004";
        }


        private static string _ToDecimalFamily(XdmAtomicValue value, XsType target, out XdmAtomicValue result)
        {
            result = null;
            if (target == XsType.Decimal)
            {
                switch (value)
                {
                    case IntegerValue i:
                        result = new DecimalValue(i.ToDecimal());
                        return null;
                    case DecimalValue d:
                        result = d;
                        return null;
                    case DoubleValue dbl:
                        if (double.IsNaN(dbl.Value) || double.IsInfinity(dbl.Value))
                        {
                            return "FOCA0002";
                        }
                        result = new DecimalValue(NumberFormatting.DoubleToDecimal(dbl.Value));
                        return null;
                    case FloatValue flt:
                        if (float.IsNaN(flt.Value) || float.IsInfinity(flt.Value))
                        {
                            return "FOCA0002";
                        }
                        result = new DecimalValue(NumberFormatting.DoubleToDecimal(flt.Value));
                        return null;
                    case BooleanValue b:
                        result = new DecimalValue(b.Value ? 1m : 0m);
                        return null;
                }
                return "XPTY0004";
            }

            // integer types
            BigInteger integer;
            switch (value)
            {
                case IntegerValue i:
                    integer = i.Value;
                    break;
                case DecimalValue d:
                    integer = new BigInteger(Math.Truncate(d.Value));
                    break;
                case DoubleValue dbl:
                    if (double.IsNaN(dbl.Value) || double.IsInfinity(dbl.Value))
                    {
                        return "FOCA0002";
                    }
                    integer = new BigInteger(Math.Truncate(dbl.Value));
                    break;
                case FloatValue flt:
                    if (float.IsNaN(flt.Value) || float.IsInfinity(flt.Value))
                    {
                        return "FOCA0002";
                    }
                    integer = new BigInteger(Math.Truncate((double)flt.Value));
                    break;
                case BooleanValue b:
                    integer = b.Value ? BigInteger.One : BigInteger.Zero;
                    break;
                default:
                    return "XPTY0004";
            }
            if ((target.MinInclusive.HasValue && integer < target.MinInclusive.Value) || (target.MaxInclusive.HasValue && integer > target.MaxInclusive.Value))
            {
                return "FORG0001";
            }
            result = target == XsType.Integer ? IntegerValue.Get(integer) : new IntegerValue(integer, target);
            return null;
        }


        private static string _CalendarCast(XdmAtomicValue value, XsType target, out XdmAtomicValue result)
        {
            result = null;
            CalendarValue c = value as CalendarValue;
            if (c == null)
            {
                return "XPTY0004";
            }
            PrimitiveType source = c.Type.Primitive;
            switch (target.Primitive)
            {
                case PrimitiveType.DateTime:
                    if (source == PrimitiveType.Date)
                    {
                        result = new CalendarValue(target, c.Year, c.Month, c.Day, 0, 0, 0, c.TimezoneMinutes);
                        return null;
                    }
                    if (source == PrimitiveType.DateTime)
                    {
                        result = new CalendarValue(target, c.Year, c.Month, c.Day, c.Hour, c.Minute, c.Second, c.TimezoneMinutes);
                        return null;
                    }
                    return "XPTY0004";
                case PrimitiveType.Time:
                    if (source == PrimitiveType.DateTime)
                    {
                        result = new CalendarValue(target, 0, 0, 0, c.Hour, c.Minute, c.Second, c.TimezoneMinutes);
                        return null;
                    }
                    return "XPTY0004";
                default:
                    if (source != PrimitiveType.DateTime && source != PrimitiveType.Date)
                    {
                        return "XPTY0004";
                    }
                    result = new CalendarValue(target, c.Year, c.Month, c.Day, 0, 0, 0, c.TimezoneMinutes);
                    return null;
            }
        }


        /// <summary>
        /// Whitespace processing of XML Schema: collapse.
        /// </summary>
        public static string Collapse(string s)
        {
            if (s.Length == 0)
            {
                return s;
            }
            bool needsWork = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\t' || ch == '\n' || ch == '\r' || (ch == ' ' && (i == 0 || i == s.Length - 1 || s[i + 1] == ' ')))
                {
                    needsWork = true;
                    break;
                }
            }
            if (!needsWork)
            {
                return s;
            }
            StringBuilder sb = new StringBuilder(s.Length);
            bool pendingSpace = false;
            foreach (char ch in s)
            {
                if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')
                {
                    pendingSpace = sb.Length > 0;
                }
                else
                {
                    if (pendingSpace)
                    {
                        sb.Append(' ');
                        pendingSpace = false;
                    }
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }


        /// <summary>
        /// Casts a string to the target type. Returns null on success, else the error code.
        /// </summary>
        public static string FromString(string input, XsType target, out XdmAtomicValue result)
        {
            result = null;
            if (target == XsType.String || target == XsType.UntypedAtomic)
            {
                result = new StringAtomic(input, target);
                return null;
            }
            if (target.Primitive == PrimitiveType.String)
            {
                string s = target == XsType.NormalizedString ? input.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ') : Collapse(input);
                bool valid = true;
                if (target == XsType.Language)
                {
                    valid = _LanguageLexical.IsMatch(s);
                }
                else if (target == XsType.NmToken)
                {
                    valid = _NmTokenLexical.IsMatch(s);
                }
                else if (target == XsType.Name_)
                {
                    valid = _NameLexical.IsMatch(s);
                }
                else if (target.IsSubtypeOf(XsType.NcName))
                {
                    valid = _NcNameLexical.IsMatch(s);
                }
                if (!valid)
                {
                    return "FORG0001";
                }
                result = new StringAtomic(s, target);
                return null;
            }

            string v = Collapse(input);
            switch (target.Primitive)
            {
                case PrimitiveType.AnyUri:
                    result = new StringAtomic(v, XsType.AnyUri);
                    return null;

                case PrimitiveType.Boolean:
                    switch (v)
                    {
                        case "true":
                        case "1":
                            result = BooleanValue.True;
                            return null;
                        case "false":
                        case "0":
                            result = BooleanValue.False;
                            return null;
                    }
                    return "FORG0001";

                case PrimitiveType.Decimal:
                    if (target == XsType.Decimal)
                    {
                        if (!_DecimalLexical.IsMatch(v))
                        {
                            return "FORG0001";
                        }
                        decimal d;
                        if (!decimal.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out d))
                        {
                            return _ParseLongDecimal(v, out result);
                        }
                        result = new DecimalValue(d);
                        return null;
                    }
                    if (!_IntegerLexical.IsMatch(v))
                    {
                        return "FORG0001";
                    }
                    BigInteger integer = BigInteger.Parse(v.TrimStart('+'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                    if ((target.MinInclusive.HasValue && integer < target.MinInclusive.Value) || (target.MaxInclusive.HasValue && integer > target.MaxInclusive.Value))
                    {
                        return "FORG0001";
                    }
                    result = target == XsType.Integer ? IntegerValue.Get(integer) : new IntegerValue(integer, target);
                    return null;

                case PrimitiveType.Double:
                case PrimitiveType.Float:
                    {
                        if (!_DoubleLexical.IsMatch(v))
                        {
                            return "FORG0001";
                        }
                        double d = _ParseDouble(v);
                        result = target.Primitive == PrimitiveType.Double ? (XdmAtomicValue)new DoubleValue(d) : new FloatValue((float)d);
                        return null;
                    }

                case PrimitiveType.DateTime:
                    return _ParseDateTime(v, target, out result);
                case PrimitiveType.Date:
                    return _ParseDate(v, out result);
                case PrimitiveType.Time:
                    return _ParseTime(v, out result);
                case PrimitiveType.GYear:
                case PrimitiveType.GYearMonth:
                case PrimitiveType.GMonth:
                case PrimitiveType.GMonthDay:
                case PrimitiveType.GDay:
                    return _ParseGregorian(v, target, out result);
                case PrimitiveType.Duration:
                    return _ParseDuration(v, target, out result);

                case PrimitiveType.HexBinary:
                    {
                        if (v.Length % 2 != 0)
                        {
                            return "FORG0001";
                        }
                        byte[] bytes = new byte[v.Length / 2];
                        for (int i = 0; i < bytes.Length; i++)
                        {
                            if (!byte.TryParse(v.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                            {
                                return "FORG0001";
                            }
                        }
                        result = new BinaryValue(XsType.HexBinary, bytes);
                        return null;
                    }

                case PrimitiveType.Base64Binary:
                    try
                    {
                        result = new BinaryValue(XsType.Base64Binary, Convert.FromBase64String(v.Replace(" ", "")));
                        return null;
                    }
                    catch (FormatException)
                    {
                        return "FORG0001";
                    }

                case PrimitiveType.QName:
                case PrimitiveType.Notation:
                    // requires a static namespace context, only supported for literals in xs:QName()
                    return "XPTY0004";
            }
            return "XPTY0004";
        }


        private static string _ParseLongDecimal(string v, out XdmAtomicValue result)
        {
            // more significant digits than System.Decimal supports: round to the supported precision
            result = null;
            double d = double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (Math.Abs(d) >= 7.9e28)
            {
                return "FOAR0002";
            }
            result = new DecimalValue(NumberFormatting.DoubleToDecimal(d));
            return null;
        }


        private static double _ParseDouble(string v)
        {
            switch (v)
            {
                case "INF":
                case "+INF":
                    return double.PositiveInfinity;
                case "-INF":
                    return double.NegativeInfinity;
                case "NaN":
                    return double.NaN;
            }
            return double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);
        }


        private static string _ParseTimezone(string tz, out int? minutes)
        {
            minutes = null;
            if (String.IsNullOrEmpty(tz))
            {
                return null;
            }
            if (tz == "Z")
            {
                minutes = 0;
                return null;
            }
            int hours = int.Parse(tz.Substring(1, 2), CultureInfo.InvariantCulture);
            int mins = int.Parse(tz.Substring(4, 2), CultureInfo.InvariantCulture);
            if (hours > 14 || mins > 59 || (hours == 14 && mins != 0))
            {
                return "FORG0001";
            }
            minutes = (tz[0] == '-' ? -1 : 1) * (hours * 60 + mins);
            return null;
        }


        private static bool _ParseYear(string s, out int year)
        {
            // years with leading zeros beyond four digits are not allowed
            string digits = s.TrimStart('-');
            if (digits.Length > 4 && digits[0] == '0')
            {
                year = 0;
                return false;
            }
            return int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out year);
        }


        private static string _ParseDate(string v, out XdmAtomicValue result)
        {
            result = null;
            Match m = _DateLexical.Match(v);
            if (!m.Success)
            {
                return "FORG0001";
            }
            int year;
            if (!_ParseYear(m.Groups[1].Value, out year))
            {
                return "FORG0001";
            }
            int month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int day = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (month < 1 || month > 12 || day < 1 || day > CalendarMath.DaysInMonth(year, month))
            {
                return "FORG0001";
            }
            int? tz;
            string error = _ParseTimezone(m.Groups[4].Value, out tz);
            if (error != null)
            {
                return error;
            }
            result = new CalendarValue(XsType.Date, year, month, day, 0, 0, 0, tz);
            return null;
        }


        private static string _ParseDateTime(string v, XsType target, out XdmAtomicValue result)
        {
            result = null;
            Match m = _DateTimeLexical.Match(v);
            if (!m.Success)
            {
                return "FORG0001";
            }
            int year;
            if (!_ParseYear(m.Groups[1].Value, out year))
            {
                return "FORG0001";
            }
            int month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int day = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            int hour = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            int minute = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            decimal second = decimal.Parse(m.Groups[6].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            if (month < 1 || month > 12 || day < 1 || day > CalendarMath.DaysInMonth(year, month) || minute > 59 || second >= 60)
            {
                return "FORG0001";
            }
            if (hour == 24)
            {
                if (minute != 0 || second != 0)
                {
                    return "FORG0001";
                }
                // 24:00:00 is the first instant of the following day
                long days = CalendarMath.DaysFromCivil(year, month, day) + 1;
                CalendarMath.CivilFromDays(days, out year, out month, out day);
                hour = 0;
            }
            else if (hour > 23)
            {
                return "FORG0001";
            }
            int? tz;
            string error = _ParseTimezone(m.Groups[8].Value, out tz);
            if (error != null)
            {
                return error;
            }
            if (target == XsType.DateTimeStamp && !tz.HasValue)
            {
                return "FORG0001";
            }
            result = new CalendarValue(target, year, month, day, hour, minute, second, tz);
            return null;
        }


        private static string _ParseTime(string v, out XdmAtomicValue result)
        {
            result = null;
            Match m = _TimeLexical.Match(v);
            if (!m.Success)
            {
                return "FORG0001";
            }
            int hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int minute = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            decimal second = decimal.Parse(m.Groups[3].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            if (minute > 59 || second >= 60)
            {
                return "FORG0001";
            }
            if (hour == 24)
            {
                if (minute != 0 || second != 0)
                {
                    return "FORG0001";
                }
                hour = 0;
            }
            else if (hour > 23)
            {
                return "FORG0001";
            }
            int? tz;
            string error = _ParseTimezone(m.Groups[5].Value, out tz);
            if (error != null)
            {
                return error;
            }
            result = new CalendarValue(XsType.Time, 0, 0, 0, hour, minute, second, tz);
            return null;
        }


        private static string _ParseGregorian(string v, XsType target, out XdmAtomicValue result)
        {
            result = null;
            int year = 1972;
            int month = 1;
            int day = 1;
            string tz;
            Match m;
            switch (target.Primitive)
            {
                case PrimitiveType.GYear:
                    m = _GYearLexical.Match(v);
                    if (!m.Success || !_ParseYear(m.Groups[1].Value, out year))
                    {
                        return "FORG0001";
                    }
                    tz = m.Groups[2].Value;
                    break;
                case PrimitiveType.GYearMonth:
                    m = _GYearMonthLexical.Match(v);
                    if (!m.Success || !_ParseYear(m.Groups[1].Value, out year))
                    {
                        return "FORG0001";
                    }
                    month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    tz = m.Groups[3].Value;
                    break;
                case PrimitiveType.GMonth:
                    m = _GMonthLexical.Match(v);
                    if (!m.Success)
                    {
                        return "FORG0001";
                    }
                    month = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    tz = m.Groups[2].Value;
                    break;
                case PrimitiveType.GMonthDay:
                    m = _GMonthDayLexical.Match(v);
                    if (!m.Success)
                    {
                        return "FORG0001";
                    }
                    month = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    day = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    tz = m.Groups[3].Value;
                    break;
                default:
                    m = _GDayLexical.Match(v);
                    if (!m.Success)
                    {
                        return "FORG0001";
                    }
                    day = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    tz = m.Groups[2].Value;
                    break;
            }
            if (month < 1 || month > 12 || day < 1 || day > CalendarMath.DaysInMonth(target.Primitive == PrimitiveType.GMonthDay ? 2000 : year, month))
            {
                return "FORG0001";
            }
            int? timezone;
            string error = _ParseTimezone(tz, out timezone);
            if (error != null)
            {
                return error;
            }
            result = new CalendarValue(target, year, month, day, 0, 0, 0, timezone);
            return null;
        }


        private static string _ParseDuration(string v, XsType target, out XdmAtomicValue result)
        {
            result = null;
            Match m = _DurationLexical.Match(v);
            if (!m.Success || v.EndsWith("T", StringComparison.Ordinal) || v == "P" || v == "-P")
            {
                return "FORG0001";
            }
            bool hasYearMonth = m.Groups[2].Success || m.Groups[3].Success;
            bool hasDayTime = m.Groups[4].Success || m.Groups[5].Success || m.Groups[6].Success || m.Groups[7].Success;
            if ((target == XsType.DayTimeDuration && hasYearMonth) || (target == XsType.YearMonthDuration && hasDayTime))
            {
                return "FORG0001";
            }
            try
            {
                int months = (m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 12 : 0) + (m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0);
                decimal seconds = (m.Groups[4].Success ? decimal.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) * 86400 : 0)
                    + (m.Groups[5].Success ? decimal.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture) * 3600 : 0)
                    + (m.Groups[6].Success ? decimal.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture) * 60 : 0)
                    + (m.Groups[7].Success ? decimal.Parse(m.Groups[7].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) : 0);
                if (m.Groups[1].Success)
                {
                    months = -months;
                    seconds = -seconds;
                }
                result = new DurationValue(target, months, seconds);
                return null;
            }
            catch (OverflowException)
            {
                return "FODT0002";
            }
        }
    }
}
