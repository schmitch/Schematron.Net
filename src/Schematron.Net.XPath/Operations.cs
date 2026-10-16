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
using System.Numerics;

namespace Schematron.Net.XPath
{
    internal enum ComparisonOperator
    {
        Equal,
        NotEqual,
        LessThan,
        LessOrEqual,
        GreaterThan,
        GreaterOrEqual
    }


    internal enum ArithmeticOperator
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        IntegerDivide,
        Modulo
    }


    /// <summary>
    /// Atomization, effective boolean value, comparisons and arithmetic.
    /// </summary>
    internal static class Operations
    {
        /// <summary>
        /// The implicit timezone (minutes) used to compare date/time values without timezone.
        /// </summary>
        public const int ImplicitTimezoneMinutes = 0;

        /// <summary>
        /// Minimum number of fractional digits of a decimal division (as in Saxon).
        /// </summary>
        private const int _DividePrecision = 18;


        public static XdmAtomicValue Atomize(XdmItem item)
        {
            if (item is XdmAtomicValue atomic)
            {
                return atomic;
            }
            XdmNode node = (XdmNode)item;
            switch (node.Kind)
            {
                case XdmNodeKind.Comment:
                case XdmNodeKind.ProcessingInstruction:
                    return new StringAtomic(node.StringValue);
                case XdmNodeKind.Namespace:
                    return new StringAtomic(node.Value);
                default:
                    return StringAtomic.Untyped(node.StringValue);
            }
        }


        public static XdmValue Atomize(XdmValue sequence)
        {
            if (sequence.Count == 0)
            {
                return sequence;
            }
            if (sequence.Count == 1)
            {
                XdmItem item = sequence[0];
                return item is XdmAtomicValue ? sequence : XdmValue.Of(Atomize(item));
            }
            XdmItem[] items = new XdmItem[sequence.Count];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = Atomize(sequence[i]);
            }
            return XdmValue.FromArray(items);
        }


        /// <summary>
        /// Atomizes a sequence that must contain at most one item (XPTY0004 otherwise); returns null for the empty sequence.
        /// </summary>
        public static XdmAtomicValue AtomizeOptional(XdmValue sequence, string what)
        {
            if (sequence.Count == 0)
            {
                return null;
            }
            if (sequence.Count > 1)
            {
                throw new XPathException("XPTY0004", "A sequence of more than one item is not allowed as " + what + " " + _DescribeSequence(sequence));
            }
            return Atomize(sequence[0]);
        }


        internal static string _DescribeSequence(XdmValue sequence)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < Math.Min(3, sequence.Count); i++)
            {
                parts.Add(sequence[i] is XdmNode n ? n.ToString() : ((XdmAtomicValue)sequence[i]).StringValue);
            }
            return "(" + String.Join(", ", parts) + (sequence.Count > 3 ? ", ..." : "") + ")";
        }


        public static bool EffectiveBooleanValue(XdmValue sequence)
        {
            if (sequence.Count == 0)
            {
                return false;
            }
            XdmItem first = sequence[0];
            if (first is XdmNode)
            {
                return true;
            }
            if (sequence.Count > 1)
            {
                throw new XPathException("FORG0006", "Effective boolean value is not defined for a sequence of two or more items starting with an atomic value");
            }
            return EffectiveBooleanValue((XdmAtomicValue)first);
        }


        public static bool EffectiveBooleanValue(XdmAtomicValue value)
        {
            switch (value)
            {
                case BooleanValue b:
                    return b.Value;
                case StringAtomic s:
                    return s.Value.Length > 0;
                case NumericValue n:
                    return !(n.IsZero || n.IsNaN);
            }
            throw new XPathException("FORG0006", "Effective boolean value is not defined for a value of type " + value.Type.QualifiedName);
        }


        // ------------------------------------------------------------------------------------------------------------
        // comparisons
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// General comparison (=, !=, &lt;, ...): existentially quantified over the atomized operands.
        /// </summary>
        public static bool GeneralCompare(XdmValue left, ComparisonOperator op, XdmValue right)
        {
            if (left.Count == 0 || right.Count == 0)
            {
                return false;
            }
            if (left.Count == 1 && right.Count == 1)
            {
                return _GeneralComparePair(Atomize(left[0]), op, Atomize(right[0]));
            }
            XdmValue l = Atomize(left);
            XdmValue r = Atomize(right);
            foreach (XdmItem a in l)
            {
                foreach (XdmItem b in r)
                {
                    if (_GeneralComparePair((XdmAtomicValue)a, op, (XdmAtomicValue)b))
                    {
                        return true;
                    }
                }
            }
            return false;
        }


        private static bool _GeneralComparePair(XdmAtomicValue a, ComparisonOperator op, XdmAtomicValue b)
        {
            bool aUntyped = a.Type == XsType.UntypedAtomic;
            bool bUntyped = b.Type == XsType.UntypedAtomic;
            if (aUntyped || bUntyped)
            {
                if (aUntyped && bUntyped)
                {
                    return _CompareStrings(((StringAtomic)a).Value, op, ((StringAtomic)b).Value);
                }
                if (aUntyped && b is NumericValue bn)
                {
                    return _CompareUntypedWithNumber(_UntypedToDouble((StringAtomic)a), op, bn.ToDouble());
                }
                if (bUntyped && a is NumericValue an)
                {
                    return _CompareUntypedWithNumber(an.ToDouble(), op, _UntypedToDouble((StringAtomic)b));
                }
                if (aUntyped)
                {
                    a = _ConvertUntypedForComparison((StringAtomic)a, b);
                }
                else
                {
                    b = _ConvertUntypedForComparison((StringAtomic)b, a);
                }
            }
            return ValueCompare(a, op, b);
        }


        /// <summary>
        /// Converts an untyped value to xs:double for a general comparison with a number as Saxon does: integers
        /// (without fraction or exponent) are converted exactly, so "-0" becomes positive zero.
        /// </summary>
        private static double _UntypedToDouble(StringAtomic value)
        {
            string s = Casting.Collapse(value.Value);
            bool integer = s.Length > 0;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (!(ch >= '0' && ch <= '9') && !(i == 0 && (ch == '+' || ch == '-') && s.Length > 1))
                {
                    integer = false;
                    break;
                }
            }
            if (integer)
            {
                return (double)BigInteger.Parse(s.TrimStart('+'), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture);
            }
            return ((DoubleValue)Casting.Cast(value, XsType.Double)).Value;
        }


        /// <summary>
        /// Compares an untyped value (converted to double) with a number like Saxon: the order of Java's
        /// Double.compare (negative zero is less than positive zero, NaN is greater than all values) and NaN
        /// never equal.
        /// </summary>
        private static bool _CompareUntypedWithNumber(double a, ComparisonOperator op, double b)
        {
            if (op == ComparisonOperator.Equal || op == ComparisonOperator.NotEqual)
            {
                bool equal = !double.IsNaN(a) && !double.IsNaN(b) && _JavaCompare(a, b) == 0;
                return op == ComparisonOperator.Equal ? equal : !equal;
            }
            return _Apply(op, _JavaCompare(a, b));
        }


        private static int _JavaCompare(double a, double b)
        {
            if (a < b)
            {
                return -1;
            }
            if (a > b)
            {
                return 1;
            }
            long bitsA = double.IsNaN(a) ? 0x7ff8000000000000L : BitConverter.DoubleToInt64Bits(a);
            long bitsB = double.IsNaN(b) ? 0x7ff8000000000000L : BitConverter.DoubleToInt64Bits(b);
            return bitsA == bitsB ? 0 : (bitsA < bitsB ? -1 : 1);
        }


        private static XdmAtomicValue _ConvertUntypedForComparison(StringAtomic untyped, XdmAtomicValue other)
        {
            if (other is NumericValue)
            {
                return Casting.Cast(untyped, XsType.Double);
            }
            if (other.Type.Primitive == PrimitiveType.String || other.Type.Primitive == PrimitiveType.AnyUri)
            {
                return new StringAtomic(untyped.Value);
            }
            return Casting.Cast(untyped, _PrimitiveTypeOf(other.Type));
        }


        private static XsType _PrimitiveTypeOf(XsType type)
        {
            XsType t = type;
            while (t.Parent != null && t.Parent != XsType.AnyAtomicType)
            {
                t = t.Parent;
            }
            return t;
        }


        private static bool _CompareStrings(string a, ComparisonOperator op, string b)
        {
            int c = String.CompareOrdinal(a, b);
            return _Apply(op, c);
        }


        private static bool _Apply(ComparisonOperator op, int c)
        {
            switch (op)
            {
                case ComparisonOperator.Equal:
                    return c == 0;
                case ComparisonOperator.NotEqual:
                    return c != 0;
                case ComparisonOperator.LessThan:
                    return c < 0;
                case ComparisonOperator.LessOrEqual:
                    return c <= 0;
                case ComparisonOperator.GreaterThan:
                    return c > 0;
                default:
                    return c >= 0;
            }
        }


        /// <summary>
        /// Value comparison of two atomic values (untypedAtomic is compared as xs:string).
        /// </summary>
        public static bool ValueCompare(XdmAtomicValue a, ComparisonOperator op, XdmAtomicValue b)
        {
            if (a is NumericValue na && b is NumericValue nb)
            {
                if (na.IsNaN || nb.IsNaN)
                {
                    return op == ComparisonOperator.NotEqual;
                }
                return _Apply(op, CompareNumeric(na, nb));
            }
            int c;
            if (TryCompare(a, b, op == ComparisonOperator.Equal || op == ComparisonOperator.NotEqual, out c))
            {
                return _Apply(op, c);
            }
            throw new XPathException("XPTY0004", "Cannot compare " + a.Type.QualifiedName + " with " + b.Type.QualifiedName);
        }


        /// <summary>
        /// Compares two (non NaN) numeric values after type promotion.
        /// </summary>
        public static int CompareNumeric(NumericValue a, NumericValue b)
        {
            int rank = Math.Max(a.Rank, b.Rank);
            switch (rank)
            {
                case 0:
                    return ((IntegerValue)a).Value.CompareTo(((IntegerValue)b).Value);
                case 1:
                    if (a is IntegerValue ia && b is IntegerValue ib)
                    {
                        return ia.Value.CompareTo(ib.Value);
                    }
                    return ToDecimal(a).CompareTo(ToDecimal(b));
                case 2:
                    return ((float)a.ToDouble()).CompareTo((float)b.ToDouble());
                default:
                    return a.ToDouble().CompareTo(b.ToDouble());
            }
        }


        /// <summary>
        /// Compares two atomic values of compatible types; returns false if they are not comparable.
        /// </summary>
        public static bool TryCompare(XdmAtomicValue a, XdmAtomicValue b, bool equalityOnly, out int result)
        {
            result = 0;
            PrimitiveType pa = a.Type.Primitive;
            PrimitiveType pb = b.Type.Primitive;
            if (a.Type.IsStringLike && b.Type.IsStringLike)
            {
                result = String.CompareOrdinal(a.StringValue, b.StringValue);
                return true;
            }
            if (a is NumericValue na && b is NumericValue nb)
            {
                if (na.IsNaN || nb.IsNaN)
                {
                    result = na.IsNaN && nb.IsNaN ? 0 : (na.IsNaN ? -1 : 1);
                    return true;
                }
                result = CompareNumeric(na, nb);
                return true;
            }
            if (pa != pb)
            {
                if (pa == PrimitiveType.Duration && pb == PrimitiveType.Duration)
                {
                    // handled below
                }
                else
                {
                    return false;
                }
            }
            switch (pa)
            {
                case PrimitiveType.Boolean:
                    result = ((BooleanValue)a).Value.CompareTo(((BooleanValue)b).Value);
                    return true;
                case PrimitiveType.DateTime:
                case PrimitiveType.Date:
                case PrimitiveType.Time:
                    result = ((CalendarValue)a).ToInstant(ImplicitTimezoneMinutes).CompareTo(((CalendarValue)b).ToInstant(ImplicitTimezoneMinutes));
                    return true;
                case PrimitiveType.GYear:
                case PrimitiveType.GYearMonth:
                case PrimitiveType.GMonth:
                case PrimitiveType.GMonthDay:
                case PrimitiveType.GDay:
                    if (!equalityOnly)
                    {
                        return false;
                    }
                    result = ((CalendarValue)a).ToInstant(ImplicitTimezoneMinutes).CompareTo(((CalendarValue)b).ToInstant(ImplicitTimezoneMinutes));
                    return true;
                case PrimitiveType.Duration:
                    {
                        DurationValue da = (DurationValue)a;
                        DurationValue db = (DurationValue)b;
                        if (da.Type == XsType.DayTimeDuration && db.Type == XsType.DayTimeDuration)
                        {
                            result = da.Seconds.CompareTo(db.Seconds);
                            return true;
                        }
                        if (da.Type == XsType.YearMonthDuration && db.Type == XsType.YearMonthDuration)
                        {
                            result = da.Months.CompareTo(db.Months);
                            return true;
                        }
                        if (!equalityOnly)
                        {
                            return false;
                        }
                        result = da.Months == db.Months && da.Seconds == db.Seconds ? 0 : 1;
                        return true;
                    }
                case PrimitiveType.QName:
                    if (!equalityOnly)
                    {
                        return false;
                    }
                    QNameValue qa = (QNameValue)a;
                    QNameValue qb = (QNameValue)b;
                    result = qa.LocalName == qb.LocalName && qa.NamespaceUri == qb.NamespaceUri ? 0 : 1;
                    return true;
                case PrimitiveType.HexBinary:
                case PrimitiveType.Base64Binary:
                    if (!equalityOnly)
                    {
                        return false;
                    }
                    result = a.StringValue == b.StringValue ? 0 : 1;
                    return true;
            }
            return false;
        }


        /// <summary>
        /// Equality as used by fn:distinct-values and fn:index-of: NaN equals NaN, incomparable values are not equal.
        /// </summary>
        public static bool DeepEqualAtomic(XdmAtomicValue a, XdmAtomicValue b)
        {
            if (a.Type.Primitive == PrimitiveType.UntypedAtomic)
            {
                a = new StringAtomic(((StringAtomic)a).Value);
            }
            if (b.Type.Primitive == PrimitiveType.UntypedAtomic)
            {
                b = new StringAtomic(((StringAtomic)b).Value);
            }
            int c;
            return TryCompare(a, b, true, out c) && c == 0;
        }


        public static decimal ToDecimal(NumericValue value)
        {
            switch (value)
            {
                case IntegerValue i:
                    return i.ToDecimal();
                case DecimalValue d:
                    return d.Value;
                default:
                    return ((DecimalValue)Casting.Cast(value, XsType.Decimal)).Value;
            }
        }


        // ------------------------------------------------------------------------------------------------------------
        // arithmetic
        // ------------------------------------------------------------------------------------------------------------

        public static XdmAtomicValue Arithmetic(XdmAtomicValue a, ArithmeticOperator op, XdmAtomicValue b)
        {
            if (a.Type == XsType.UntypedAtomic)
            {
                a = Casting.Cast(a, XsType.Double);
            }
            if (b.Type == XsType.UntypedAtomic)
            {
                b = Casting.Cast(b, XsType.Double);
            }
            if (a is NumericValue na && b is NumericValue nb)
            {
                return NumericArithmetic(na, op, nb);
            }
            return _CalendarArithmetic(a, op, b);
        }


        public static NumericValue NumericArithmetic(NumericValue a, ArithmeticOperator op, NumericValue b)
        {
            int rank = Math.Max(a.Rank, b.Rank);
            if (op == ArithmeticOperator.IntegerDivide)
            {
                return _IntegerDivide(a, b, rank);
            }
            switch (rank)
            {
                case 0:
                    {
                        BigInteger x = ((IntegerValue)a).Value;
                        BigInteger y = ((IntegerValue)b).Value;
                        switch (op)
                        {
                            case ArithmeticOperator.Add:
                                return IntegerValue.Get(x + y);
                            case ArithmeticOperator.Subtract:
                                return IntegerValue.Get(x - y);
                            case ArithmeticOperator.Multiply:
                                return IntegerValue.Get(x * y);
                            case ArithmeticOperator.Modulo:
                                if (y.IsZero)
                                {
                                    throw new XPathException("FOAR0001", "Integer modulo by zero");
                                }
                                return IntegerValue.Get(BigInteger.Remainder(x, y));
                            default:
                                return DecimalDivide(((IntegerValue)a).ToDecimal(), ((IntegerValue)b).ToDecimal(), 0, 0);
                        }
                    }
                case 1:
                    {
                        decimal x = ToDecimal(a);
                        decimal y = ToDecimal(b);
                        try
                        {
                            switch (op)
                            {
                                case ArithmeticOperator.Add:
                                    return new DecimalValue(x + y);
                                case ArithmeticOperator.Subtract:
                                    return new DecimalValue(x - y);
                                case ArithmeticOperator.Multiply:
                                    return new DecimalValue(x * y);
                                case ArithmeticOperator.Modulo:
                                    if (y == 0)
                                    {
                                        throw new XPathException("FOAR0001", "Decimal modulo by zero");
                                    }
                                    return new DecimalValue(x % y);
                                default:
                                    return DecimalDivide(x, y, _Scale(x), _Scale(y));
                            }
                        }
                        catch (OverflowException e)
                        {
                            throw new XPathException("FOAR0002", "Decimal overflow", e);
                        }
                    }
                case 2:
                    {
                        float x = (float)a.ToDouble();
                        float y = (float)b.ToDouble();
                        return new FloatValue((float)_DoubleOp(x, op, y));
                    }
                default:
                    return new DoubleValue(_DoubleOp(a.ToDouble(), op, b.ToDouble()));
            }
        }


        private static double _DoubleOp(double x, ArithmeticOperator op, double y)
        {
            switch (op)
            {
                case ArithmeticOperator.Add:
                    return x + y;
                case ArithmeticOperator.Subtract:
                    return x - y;
                case ArithmeticOperator.Multiply:
                    return x * y;
                case ArithmeticOperator.Modulo:
                    return x % y;
                default:
                    return x / y;
            }
        }


        private static int _Scale(decimal d)
        {
            return (decimal.GetBits(d)[3] >> 16) & 0xFF;
        }


        /// <summary>
        /// Decimal division as Saxon does it: max(18, scale(a), scale(b)) fractional digits, rounded half up.
        /// </summary>
        public static DecimalValue DecimalDivide(decimal x, decimal y, int scaleX, int scaleY)
        {
            if (y == 0)
            {
                throw new XPathException("FOAR0001", "Decimal divide by zero");
            }
            decimal quotient;
            try
            {
                quotient = x / y;
            }
            catch (OverflowException e)
            {
                throw new XPathException("FOAR0002", "Decimal overflow", e);
            }
            int scale = Math.Max(_DividePrecision, Math.Max(scaleX, scaleY));
            if (scale < 28 && _Scale(quotient) > scale)
            {
                quotient = Math.Round(quotient, scale, MidpointRounding.AwayFromZero);
            }
            return new DecimalValue(quotient);
        }


        private static NumericValue _IntegerDivide(NumericValue a, NumericValue b, int rank)
        {
            if (rank == 0)
            {
                BigInteger y = ((IntegerValue)b).Value;
                if (y.IsZero)
                {
                    throw new XPathException("FOAR0001", "Integer division by zero");
                }
                return IntegerValue.Get(BigInteger.Divide(((IntegerValue)a).Value, y));
            }
            if (rank == 1)
            {
                decimal y = ToDecimal(b);
                if (y == 0)
                {
                    throw new XPathException("FOAR0001", "Integer division by zero");
                }
                return IntegerValue.Get(new BigInteger(Math.Truncate(ToDecimal(a) / y)));
            }
            double x = a.ToDouble();
            double d = b.ToDouble();
            if (d == 0)
            {
                throw new XPathException("FOAR0001", "Integer division by zero");
            }
            if (double.IsNaN(x) || double.IsNaN(d) || double.IsInfinity(x))
            {
                throw new XPathException("FOAR0002", "Invalid operand of idiv");
            }
            return IntegerValue.Get(new BigInteger(Math.Truncate(x / d)));
        }


        public static NumericValue Negate(NumericValue value)
        {
            switch (value)
            {
                case IntegerValue i:
                    return IntegerValue.Get(-i.Value);
                case DecimalValue d:
                    return new DecimalValue(-d.Value);
                case FloatValue f:
                    return new FloatValue(-f.Value);
                default:
                    return new DoubleValue(-((DoubleValue)value).Value);
            }
        }


        private static XdmAtomicValue _CalendarArithmetic(XdmAtomicValue a, ArithmeticOperator op, XdmAtomicValue b)
        {
            if (a is CalendarValue ca && b is CalendarValue cb && op == ArithmeticOperator.Subtract && ca.Type.Primitive == cb.Type.Primitive)
            {
                decimal seconds = ca.ToInstant(ImplicitTimezoneMinutes) - cb.ToInstant(ImplicitTimezoneMinutes);
                return new DurationValue(XsType.DayTimeDuration, 0, seconds);
            }
            if (a is CalendarValue c && b is DurationValue d && (op == ArithmeticOperator.Add || op == ArithmeticOperator.Subtract))
            {
                return _AddDuration(c, d, op == ArithmeticOperator.Subtract ? -1 : 1);
            }
            if (a is DurationValue d2 && b is CalendarValue c2 && op == ArithmeticOperator.Add)
            {
                return _AddDuration(c2, d2, 1);
            }
            if (a is DurationValue x && b is DurationValue y && x.Type == y.Type && x.Type != XsType.Duration)
            {
                switch (op)
                {
                    case ArithmeticOperator.Add:
                        return new DurationValue(x.Type, x.Months + y.Months, x.Seconds + y.Seconds);
                    case ArithmeticOperator.Subtract:
                        return new DurationValue(x.Type, x.Months - y.Months, x.Seconds - y.Seconds);
                    case ArithmeticOperator.Divide:
                        if (x.Type == XsType.DayTimeDuration)
                        {
                            return DecimalDivide(x.Seconds, y.Seconds, _Scale(x.Seconds), _Scale(y.Seconds));
                        }
                        return DecimalDivide(x.Months, y.Months, 0, 0);
                }
            }
            if (a is DurationValue dur && b is NumericValue factor && (op == ArithmeticOperator.Multiply || op == ArithmeticOperator.Divide))
            {
                double f = factor.ToDouble();
                if (op == ArithmeticOperator.Divide)
                {
                    f = 1 / f;
                }
                return new DurationValue(dur.Type, (int)Math.Round(dur.Months * f, MidpointRounding.AwayFromZero), (decimal)((double)dur.Seconds * f));
            }
            if (a is NumericValue factor2 && b is DurationValue dur2 && op == ArithmeticOperator.Multiply)
            {
                return _CalendarArithmetic(dur2, op, factor2);
            }
            throw new XPathException("XPTY0004", "Arithmetic operator is not defined for " + a.Type.QualifiedName + " and " + b.Type.QualifiedName);
        }


        private static CalendarValue _AddDuration(CalendarValue c, DurationValue d, int sign)
        {
            int year = c.Year;
            int month = c.Month;
            int day = c.Day;
            if (d.Months != 0 && c.Type.Primitive != PrimitiveType.Time)
            {
                int totalMonths = year * 12 + (month - 1) + sign * d.Months;
                year = (int)Math.Floor(totalMonths / 12.0);
                month = totalMonths - year * 12 + 1;
                day = Math.Min(day, CalendarMath.DaysInMonth(year, month));
            }
            decimal seconds = c.Hour * 3600 + c.Minute * 60 + c.Second + sign * d.Seconds;
            long dayShift = (long)Math.Floor(seconds / 86400);
            seconds -= dayShift * 86400m;
            if (c.Type.Primitive != PrimitiveType.Time)
            {
                long days = CalendarMath.DaysFromCivil(year, month, day) + dayShift;
                CalendarMath.CivilFromDays(days, out year, out month, out day);
            }
            int hour = (int)(seconds / 3600);
            seconds -= hour * 3600;
            int minute = (int)(seconds / 60);
            seconds -= minute * 60;
            if (c.Type.Primitive == PrimitiveType.Date)
            {
                return new CalendarValue(c.Type, year, month, day, 0, 0, 0, c.TimezoneMinutes);
            }
            return new CalendarValue(c.Type, year, month, day, hour, minute, seconds, c.TimezoneMinutes);
        }
    }
}
