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

namespace Schematron.Net.XPath
{
    /// <summary>
    /// An atomic value of the XPath data model.
    /// </summary>
    public abstract class XdmAtomicValue : XdmItem
    {
        internal readonly XsType Type;


        private protected XdmAtomicValue(XsType type)
        {
            Type = type;
        }


        /// <summary>
        /// The name of the type of the value, e.g. xs:decimal or xs:untypedAtomic.
        /// </summary>
        public string TypeName => Type.QualifiedName;


        /// <inheritdoc />
        public override string ToString()
        {
            return Type.QualifiedName + "(\"" + StringValue + "\")";
        }
    }


    /// <summary>
    /// xs:string and its subtypes, xs:untypedAtomic and xs:anyURI.
    /// </summary>
    internal sealed class StringAtomic : XdmAtomicValue
    {
        public static readonly StringAtomic Empty = new StringAtomic("");
        public readonly string Value;


        public StringAtomic(string value) : this(value, XsType.String)
        {
        }


        public StringAtomic(string value, XsType type) : base(type)
        {
            Value = value ?? "";
        }


        public static StringAtomic Untyped(string value)
        {
            return new StringAtomic(value, XsType.UntypedAtomic);
        }


        public override string StringValue => Value;
    }


    internal sealed class BooleanValue : XdmAtomicValue
    {
        public static readonly BooleanValue True = new BooleanValue(true);
        public static readonly BooleanValue False = new BooleanValue(false);
        public readonly bool Value;


        private BooleanValue(bool value) : base(XsType.Boolean)
        {
            Value = value;
        }


        public static BooleanValue Get(bool value)
        {
            return value ? True : False;
        }


        public override string StringValue => Value ? "true" : "false";
    }


    /// <summary>
    /// Base class of the numeric values.
    /// </summary>
    internal abstract class NumericValue : XdmAtomicValue
    {
        protected NumericValue(XsType type) : base(type)
        {
        }


        /// <summary>0 = integer, 1 = decimal, 2 = float, 3 = double (the order of numeric type promotion).</summary>
        public abstract int Rank { get; }
        public abstract double ToDouble();
        public abstract bool IsNaN { get; }
        public abstract bool IsZero { get; }
        public abstract int Sign { get; }
    }


    internal sealed class IntegerValue : NumericValue
    {
        public static readonly IntegerValue Zero = new IntegerValue(BigInteger.Zero);
        public static readonly IntegerValue One = new IntegerValue(BigInteger.One);
        private static readonly IntegerValue[] _Small = _CreateSmall();
        public readonly BigInteger Value;


        public IntegerValue(BigInteger value) : this(value, XsType.Integer)
        {
        }


        public IntegerValue(BigInteger value, XsType type) : base(type)
        {
            Value = value;
        }


        private static IntegerValue[] _CreateSmall()
        {
            IntegerValue[] values = new IntegerValue[257];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = new IntegerValue(new BigInteger(i - 1));
            }
            return values;
        }


        public static IntegerValue Get(long value)
        {
            if (value >= -1 && value < 256)
            {
                return _Small[value + 1];
            }
            return new IntegerValue(new BigInteger(value));
        }


        public static IntegerValue Get(BigInteger value)
        {
            if (value >= BigInteger.MinusOne && value < 256)
            {
                return _Small[(int)value + 1];
            }
            return new IntegerValue(value);
        }


        public override int Rank => 0;
        public override bool IsNaN => false;
        public override bool IsZero => Value.IsZero;
        public override int Sign => Value.Sign;
        public override double ToDouble() => (double)Value;


        public decimal ToDecimal()
        {
            try
            {
                return (decimal)Value;
            }
            catch (OverflowException)
            {
                throw new XPathException("FOAR0002", "Integer value " + Value + " is too large to be converted to xs:decimal");
            }
        }


        public override string StringValue => Value.ToString(CultureInfo.InvariantCulture);
    }


    internal sealed class DecimalValue : NumericValue
    {
        public static readonly DecimalValue Zero = new DecimalValue(0m);
        public readonly decimal Value;


        public DecimalValue(decimal value) : base(XsType.Decimal)
        {
            Value = value;
        }


        public override int Rank => 1;
        public override bool IsNaN => false;
        public override bool IsZero => Value == 0m;
        public override int Sign => Math.Sign(Value);
        public override double ToDouble() => NumberFormatting.DecimalToDouble(Value);
        public override string StringValue => NumberFormatting.FormatDecimal(Value);
    }


    internal sealed class FloatValue : NumericValue
    {
        public readonly float Value;


        public FloatValue(float value) : base(XsType.Float)
        {
            Value = value;
        }


        public override int Rank => 2;
        public override bool IsNaN => float.IsNaN(Value);
        public override bool IsZero => Value == 0f;
        public override int Sign => float.IsNaN(Value) ? 0 : Math.Sign(Value);
        public override double ToDouble() => Value;
        public override string StringValue => NumberFormatting.FormatFloat(Value);
    }


    internal sealed class DoubleValue : NumericValue
    {
        public static readonly DoubleValue NaN = new DoubleValue(double.NaN);
        public static readonly DoubleValue Zero = new DoubleValue(0d);
        public readonly double Value;


        public DoubleValue(double value) : base(XsType.Double)
        {
            Value = value;
        }


        public override int Rank => 3;
        public override bool IsNaN => double.IsNaN(Value);
        public override bool IsZero => Value == 0d;
        public override int Sign => double.IsNaN(Value) ? 0 : Math.Sign(Value);
        public override double ToDouble() => Value;
        public override string StringValue => NumberFormatting.FormatDouble(Value);
    }


    /// <summary>
    /// xs:dateTime, xs:date, xs:time and the Gregorian types (xs:gYear, ...).
    /// </summary>
    internal sealed class CalendarValue : XdmAtomicValue
    {
        public readonly int Year;
        public readonly int Month;
        public readonly int Day;
        public readonly int Hour;
        public readonly int Minute;
        public readonly decimal Second;

        /// <summary>Timezone offset in minutes or null if the value has no timezone.</summary>
        public readonly int? TimezoneMinutes;


        public CalendarValue(XsType type, int year, int month, int day, int hour, int minute, decimal second, int? timezoneMinutes) : base(type)
        {
            Year = year;
            Month = month;
            Day = day;
            Hour = hour;
            Minute = minute;
            Second = second;
            TimezoneMinutes = timezoneMinutes;
        }


        /// <summary>
        /// Seconds since 0001-01-01T00:00:00Z (proleptic Gregorian calendar), using the given timezone if the value has none.
        /// </summary>
        public decimal ToInstant(int implicitTimezoneMinutes)
        {
            int year = Year;
            int month = Month;
            int day = Day;
            switch (Type.Primitive)
            {
                case PrimitiveType.Time:
                    year = 1972;
                    month = 12;
                    day = 31;
                    break;
                case PrimitiveType.GYear:
                    month = 1;
                    day = 1;
                    break;
                case PrimitiveType.GYearMonth:
                    day = 1;
                    break;
                case PrimitiveType.GMonth:
                    year = 1972;
                    day = 1;
                    break;
                case PrimitiveType.GMonthDay:
                case PrimitiveType.GDay:
                    year = 1972;
                    if (Type.Primitive == PrimitiveType.GDay)
                    {
                        month = 12;
                    }
                    break;
            }
            long days = CalendarMath.DaysFromCivil(year, month, day);
            decimal seconds = days * 86400m + Hour * 3600 + Minute * 60 + Second;
            return seconds - (TimezoneMinutes ?? implicitTimezoneMinutes) * 60;
        }


        public override string StringValue
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                switch (Type.Primitive)
                {
                    case PrimitiveType.DateTime:
                        _AppendDate(sb);
                        sb.Append('T');
                        _AppendTime(sb);
                        break;
                    case PrimitiveType.Date:
                        _AppendDate(sb);
                        break;
                    case PrimitiveType.Time:
                        _AppendTime(sb);
                        break;
                    case PrimitiveType.GYear:
                        _AppendYear(sb);
                        break;
                    case PrimitiveType.GYearMonth:
                        _AppendYear(sb);
                        sb.Append('-').Append(Month.ToString("00", CultureInfo.InvariantCulture));
                        break;
                    case PrimitiveType.GMonth:
                        sb.Append("--").Append(Month.ToString("00", CultureInfo.InvariantCulture));
                        break;
                    case PrimitiveType.GMonthDay:
                        sb.Append("--").Append(Month.ToString("00", CultureInfo.InvariantCulture)).Append('-').Append(Day.ToString("00", CultureInfo.InvariantCulture));
                        break;
                    case PrimitiveType.GDay:
                        sb.Append("---").Append(Day.ToString("00", CultureInfo.InvariantCulture));
                        break;
                }
                if (TimezoneMinutes.HasValue)
                {
                    sb.Append(CalendarMath.FormatTimezone(TimezoneMinutes.Value));
                }
                return sb.ToString();
            }
        }


        private void _AppendYear(StringBuilder sb)
        {
            if (Year < 0)
            {
                sb.Append('-');
            }
            sb.Append(Math.Abs(Year).ToString("0000", CultureInfo.InvariantCulture));
        }


        private void _AppendDate(StringBuilder sb)
        {
            _AppendYear(sb);
            sb.Append('-').Append(Month.ToString("00", CultureInfo.InvariantCulture));
            sb.Append('-').Append(Day.ToString("00", CultureInfo.InvariantCulture));
        }


        private void _AppendTime(StringBuilder sb)
        {
            sb.Append(Hour.ToString("00", CultureInfo.InvariantCulture)).Append(':');
            sb.Append(Minute.ToString("00", CultureInfo.InvariantCulture)).Append(':');
            int whole = (int)Math.Truncate(Second);
            sb.Append(whole.ToString("00", CultureInfo.InvariantCulture));
            decimal fraction = Second - whole;
            if (fraction != 0)
            {
                string f = NumberFormatting.FormatDecimal(fraction);
                sb.Append(f.Substring(f.IndexOf('.')));
            }
        }
    }


    /// <summary>
    /// xs:duration, xs:dayTimeDuration and xs:yearMonthDuration. The value is kept as months and seconds,
    /// both carrying the sign of the duration.
    /// </summary>
    internal sealed class DurationValue : XdmAtomicValue
    {
        public readonly int Months;
        public readonly decimal Seconds;


        public DurationValue(XsType type, int months, decimal seconds) : base(type)
        {
            Months = months;
            Seconds = seconds;
        }


        public override string StringValue
        {
            get
            {
                if (Months == 0 && Seconds == 0)
                {
                    return Type == XsType.YearMonthDuration ? "P0M" : "PT0S";
                }
                StringBuilder sb = new StringBuilder();
                if (Months < 0 || Seconds < 0)
                {
                    sb.Append('-');
                }
                sb.Append('P');
                int months = Math.Abs(Months);
                if (months / 12 != 0)
                {
                    sb.Append(months / 12).Append('Y');
                }
                if (months % 12 != 0)
                {
                    sb.Append(months % 12).Append('M');
                }
                decimal seconds = Math.Abs(Seconds);
                long days = (long)Math.Truncate(seconds / 86400);
                seconds -= days * 86400m;
                if (days != 0)
                {
                    sb.Append(days).Append('D');
                }
                if (seconds != 0)
                {
                    sb.Append('T');
                    long hours = (long)Math.Truncate(seconds / 3600);
                    seconds -= hours * 3600m;
                    long minutes = (long)Math.Truncate(seconds / 60);
                    seconds -= minutes * 60m;
                    if (hours != 0)
                    {
                        sb.Append(hours).Append('H');
                    }
                    if (minutes != 0)
                    {
                        sb.Append(minutes).Append('M');
                    }
                    if (seconds != 0)
                    {
                        sb.Append(NumberFormatting.FormatDecimal(seconds)).Append('S');
                    }
                }
                return sb.ToString();
            }
        }
    }


    internal sealed class QNameValue : XdmAtomicValue
    {
        public readonly string Prefix;
        public readonly string NamespaceUri;
        public readonly string LocalName;


        public QNameValue(string prefix, string namespaceUri, string localName) : base(XsType.QName)
        {
            Prefix = prefix ?? "";
            NamespaceUri = namespaceUri ?? "";
            LocalName = localName;
        }


        public override string StringValue => Prefix.Length == 0 ? LocalName : Prefix + ":" + LocalName;
    }


    internal sealed class BinaryValue : XdmAtomicValue
    {
        public readonly byte[] Value;


        public BinaryValue(XsType type, byte[] value) : base(type)
        {
            Value = value;
        }


        public override string StringValue
        {
            get
            {
                if (Type == XsType.Base64Binary)
                {
                    return Convert.ToBase64String(Value);
                }
                StringBuilder sb = new StringBuilder(Value.Length * 2);
                foreach (byte b in Value)
                {
                    sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }
    }


    internal static class CalendarMath
    {
        /// <summary>
        /// Days since 0001-01-01 in the proleptic Gregorian calendar (algorithm by Howard Hinnant), valid for any year.
        /// </summary>
        public static long DaysFromCivil(long year, int month, int day)
        {
            year -= month <= 2 ? 1 : 0;
            long era = (year >= 0 ? year : year - 399) / 400;
            long yoe = year - era * 400;
            long doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1;
            long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            return era * 146097 + doe - 719468 + 719162;
        }


        public static void CivilFromDays(long days, out int year, out int month, out int day)
        {
            long z = days - 719162 + 719468;
            long era = (z >= 0 ? z : z - 146096) / 146097;
            long doe = z - era * 146097;
            long yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
            long y = yoe + era * 400;
            long doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
            long mp = (5 * doy + 2) / 153;
            day = (int)(doy - (153 * mp + 2) / 5 + 1);
            month = (int)(mp < 10 ? mp + 3 : mp - 9);
            year = (int)(y + (month <= 2 ? 1 : 0));
        }


        public static bool IsLeapYear(int year)
        {
            return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
        }


        public static int DaysInMonth(int year, int month)
        {
            switch (month)
            {
                case 2:
                    return IsLeapYear(year) ? 29 : 28;
                case 4:
                case 6:
                case 9:
                case 11:
                    return 30;
                default:
                    return 31;
            }
        }


        public static string FormatTimezone(int minutes)
        {
            if (minutes == 0)
            {
                return "Z";
            }
            int abs = Math.Abs(minutes);
            return (minutes < 0 ? "-" : "+") + (abs / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (abs % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }


    internal static class NumberFormatting
    {
        public static string FormatDecimal(decimal value)
        {
            string s = value.ToString(CultureInfo.InvariantCulture);
            if (s.IndexOf('.') >= 0)
            {
                s = s.TrimEnd('0');
                if (s.EndsWith(".", StringComparison.Ordinal))
                {
                    s = s.Substring(0, s.Length - 1);
                }
            }
            if (s == "-0")
            {
                s = "0";
            }
            return s;
        }


        public static double DecimalToDouble(decimal value)
        {
            return double.Parse(value.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        }


        private static readonly BigInteger _MaxMantissa = BigInteger.Pow(2, 96) - 1;


        /// <summary>
        /// Converts a double to xs:decimal. As in Saxon the exact binary value is converted (0.1e0 becomes
        /// 0.1000000000000000055511151231...), limited to the 28 significant digits of System.Decimal.
        /// </summary>
        public static decimal DoubleToDecimal(double value)
        {
            if (value == 0)
            {
                return 0m;
            }
            long bits = BitConverter.DoubleToInt64Bits(value);
            bool negative = bits < 0;
            int exponent = (int)((bits >> 52) & 0x7FF);
            long fraction = bits & 0xFFFFFFFFFFFFFL;
            if (exponent == 0)
            {
                exponent = 1;
            }
            else
            {
                fraction |= 1L << 52;
            }
            exponent -= 1075;

            BigInteger mantissa;
            int scale;
            if (exponent >= 0)
            {
                mantissa = new BigInteger(fraction) << exponent;
                scale = 0;
            }
            else
            {
                // m * 2^-k = m * 5^k / 10^k
                mantissa = new BigInteger(fraction) * BigInteger.Pow(5, -exponent);
                scale = -exponent;
            }
            while (scale > 0 && (mantissa > _MaxMantissa || scale > 28))
            {
                mantissa = _DivideRoundHalfEven(mantissa, 10);
                scale--;
            }
            while (scale > 0 && mantissa % 10 == 0)
            {
                mantissa /= 10;
                scale--;
            }
            if (mantissa > _MaxMantissa)
            {
                throw new XPathException("FOCA0001", "Value " + value.ToString("R", CultureInfo.InvariantCulture) + " is too large for xs:decimal");
            }
            byte[] bytes = mantissa.ToByteArray();
            int[] parts = new int[3];
            for (int i = 0; i < bytes.Length && i < 12; i++)
            {
                parts[i / 4] |= bytes[i] << (8 * (i % 4));
            }
            return new decimal(parts[0], parts[1], parts[2], negative, (byte)scale);
        }


        private static BigInteger _DivideRoundHalfEven(BigInteger value, int divisor)
        {
            BigInteger remainder;
            BigInteger quotient = BigInteger.DivRem(value, divisor, out remainder);
            int twice = (int)remainder * 2;
            if (twice > divisor || (twice == divisor && !quotient.IsEven))
            {
                quotient += 1;
            }
            return quotient;
        }


        public static string FormatDouble(double value)
        {
            if (double.IsNaN(value))
            {
                return "NaN";
            }
            if (double.IsPositiveInfinity(value))
            {
                return "INF";
            }
            if (double.IsNegativeInfinity(value))
            {
                return "-INF";
            }
            if (value == 0d)
            {
                return 1d / value < 0 ? "-0" : "0";
            }
            return _Format(value.ToString("R", CultureInfo.InvariantCulture), Math.Abs(value));
        }


        public static string FormatFloat(float value)
        {
            if (float.IsNaN(value))
            {
                return "NaN";
            }
            if (float.IsPositiveInfinity(value))
            {
                return "INF";
            }
            if (float.IsNegativeInfinity(value))
            {
                return "-INF";
            }
            if (value == 0f)
            {
                return 1f / value < 0 ? "-0" : "0";
            }
            return _Format(value.ToString("R", CultureInfo.InvariantCulture), Math.Abs(value));
        }


        /// <summary>
        /// Formats the shortest round-trip representation of a float/double according to the XPath 2.0 casting
        /// rules: plain decimal notation in [1e-6, 1e6), scientific notation (one digit before the point) otherwise.
        /// </summary>
        private static string _Format(string roundTrip, double abs)
        {
            bool negative = roundTrip.StartsWith("-", StringComparison.Ordinal);
            if (negative)
            {
                roundTrip = roundTrip.Substring(1);
            }
            int exponent = 0;
            int e = roundTrip.IndexOfAny(new[] { 'E', 'e' });
            if (e >= 0)
            {
                exponent = int.Parse(roundTrip.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                roundTrip = roundTrip.Substring(0, e);
            }
            int point = roundTrip.IndexOf('.');
            string digits;
            int pointPosition;
            if (point >= 0)
            {
                digits = roundTrip.Substring(0, point) + roundTrip.Substring(point + 1);
                pointPosition = point + exponent;
            }
            else
            {
                digits = roundTrip;
                pointPosition = roundTrip.Length + exponent;
            }
            // strip leading zeros
            int leading = 0;
            while (leading < digits.Length - 1 && digits[leading] == '0')
            {
                leading++;
            }
            digits = digits.Substring(leading);
            pointPosition -= leading;
            digits = digits.TrimEnd('0');
            if (digits.Length == 0)
            {
                digits = "0";
            }

            StringBuilder sb = new StringBuilder();
            if (negative)
            {
                sb.Append('-');
            }
            if (abs >= 1e-6 && abs < 1e6)
            {
                if (pointPosition <= 0)
                {
                    sb.Append("0.").Append('0', -pointPosition).Append(digits);
                }
                else if (pointPosition >= digits.Length)
                {
                    sb.Append(digits).Append('0', pointPosition - digits.Length);
                }
                else
                {
                    sb.Append(digits, 0, pointPosition).Append('.').Append(digits, pointPosition, digits.Length - pointPosition);
                }
            }
            else
            {
                sb.Append(digits[0]).Append('.');
                sb.Append(digits.Length > 1 ? digits.Substring(1) : "0");
                sb.Append('E').Append((pointPosition - 1).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
