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

namespace Schematron.Net.XPath
{
    /// <summary>
    /// The rules of XPath 1.0 compatibility mode (XPath 2.0, 3.1.5, 3.4 and 3.5.2): how function arguments,
    /// arithmetic and general comparisons treat values when expressions written for XPath 1.0 (e.g. a Schematron
    /// schema with the query binding xslt) are evaluated by this XPath 2.0 engine.
    /// </summary>
    internal static class XPath1Compatibility
    {
        /// <summary>
        /// Functions whose first parameter is numeric in XPath 1.0 (declared as xs:anyAtomicType? by this library, as
        /// numeric? in XPath 2.0): in compatibility mode the argument is converted with fn:number() like an
        /// xs:double parameter.
        /// </summary>
        private static readonly HashSet<string> _NumericFirstParameter = new HashSet<string>(StringComparer.Ordinal)
        {
            "abs", "ceiling", "floor", "round", "round-half-to-even"
        };


        /// <summary>
        /// fn:number() of an atomic value; NaN for null (the empty sequence) and values that are not numbers.
        /// </summary>
        public static double Number(XdmAtomicValue value)
        {
            if (value == null)
            {
                return double.NaN;
            }
            switch (value)
            {
                case NumericValue n:
                    return n.ToDouble();
                case BooleanValue b:
                    return b.Value ? 1 : 0;
            }
            XdmAtomicValue result;
            if (Casting.TryCast(value, XsType.Double, out result) == null)
            {
                return ((DoubleValue)result).Value;
            }
            return double.NaN;
        }


        /// <summary>
        /// XPath 2.0, 3.1.5: the conversions of an argument before the function conversion rules. A parameter that
        /// takes one item gets the first item; an xs:string parameter gets fn:string() of it, an xs:double (or numeric)
        /// parameter fn:number() of it.
        /// </summary>
        public static XdmValue ConvertArgument(XdmValue value, XPathSequenceType type, FunctionDefinition function, int index)
        {
            if (type == null || type.IsEmptySequence || type.AllowsMany)
            {
                return value;
            }
            if (value.Count > 1)
            {
                value = XdmValue.Of(value[0]);
            }
            if (type.ItemType is AtomicItemType atomic)
            {
                if (atomic.Type == XsType.String)
                {
                    return XdmValue.Of(new StringAtomic(value.Count == 0 ? "" : value[0].StringValue));
                }
                bool numeric = atomic.Type == XsType.Double
                               || (index == 0 && function.NamespaceUri == XmlNamespaces.Fn && _NumericFirstParameter.Contains(function.LocalName));
                if (numeric)
                {
                    return XdmValue.Of(new DoubleValue(Number(value.Count == 0 ? null : Operations.Atomize(value[0]))));
                }
            }
            return value;
        }


        /// <summary>
        /// XPath 2.0, 3.4: an operand of an arithmetic operator. Null if it is empty (the result is then NaN);
        /// otherwise its first atomized item, converted with fn:number() unless it is a date, time or duration.
        /// </summary>
        public static XdmAtomicValue ArithmeticOperand(XdmValue operand)
        {
            XdmValue atomized = Operations.Atomize(operand);
            if (atomized.Count == 0)
            {
                return null;
            }
            XdmAtomicValue first = (XdmAtomicValue)atomized[0];
            if (first is NumericValue || first is BooleanValue || _IsStringLike(first))
            {
                return new DoubleValue(Number(first));
            }
            return first;
        }


        public static XdmValue Arithmetic(XdmValue left, ArithmeticOperator op, XdmValue right)
        {
            XdmAtomicValue a = ArithmeticOperand(left);
            if (a == null)
            {
                return XdmValue.Of(new DoubleValue(double.NaN));
            }
            XdmAtomicValue b = ArithmeticOperand(right);
            if (b == null)
            {
                return XdmValue.Of(new DoubleValue(double.NaN));
            }
            return XdmValue.Of(Operations.Arithmetic(a, op, b));
        }


        /// <summary>
        /// XPath 2.0, 3.5.2: a general comparison. A single boolean operand converts the other operand to its
        /// effective boolean value; &lt;, &lt;=, &gt; and &gt;= compare numbers; = and != compare numbers if one value
        /// is numeric, strings if one is a string or both are untyped.
        /// </summary>
        public static bool GeneralCompare(XdmValue left, ComparisonOperator op, XdmValue right)
        {
            if (left.Count == 1 && left[0] is BooleanValue)
            {
                right = XdmValue.Of(Operations.EffectiveBooleanValue(right));
            }
            else if (right.Count == 1 && right[0] is BooleanValue)
            {
                left = XdmValue.Of(Operations.EffectiveBooleanValue(left));
            }
            XdmValue l = Operations.Atomize(left);
            XdmValue r = Operations.Atomize(right);
            bool relational = op != ComparisonOperator.Equal && op != ComparisonOperator.NotEqual;
            foreach (XdmItem x in l)
            {
                foreach (XdmItem y in r)
                {
                    if (_Compare((XdmAtomicValue)x, op, (XdmAtomicValue)y, relational))
                    {
                        return true;
                    }
                }
            }
            return false;
        }


        private static bool _Compare(XdmAtomicValue a, ComparisonOperator op, XdmAtomicValue b, bool relational)
        {
            if (relational || a is NumericValue || b is NumericValue)
            {
                double x = Number(a);
                double y = Number(b);
                switch (op)
                {
                    case ComparisonOperator.Equal:
                        return x == y;
                    case ComparisonOperator.NotEqual:
                        return x != y;
                    case ComparisonOperator.LessThan:
                        return x < y;
                    case ComparisonOperator.LessOrEqual:
                        return x <= y;
                    case ComparisonOperator.GreaterThan:
                        return x > y;
                    default:
                        return x >= y;
                }
            }
            bool aUntyped = a.Type == XsType.UntypedAtomic;
            bool bUntyped = b.Type == XsType.UntypedAtomic;
            if (_IsString(a) || _IsString(b) || (aUntyped && bUntyped))
            {
                bool equal = String.Equals(a.StringValue, b.StringValue, StringComparison.Ordinal);
                return op == ComparisonOperator.Equal ? equal : !equal;
            }
            if (aUntyped)
            {
                a = Casting.Cast(a, b.Type);
            }
            else if (bUntyped)
            {
                b = Casting.Cast(b, a.Type);
            }
            return Operations.ValueCompare(a, op, b);
        }


        private static bool _IsString(XdmAtomicValue value)
        {
            return value.Type.IsSubtypeOf(XsType.String);
        }


        private static bool _IsStringLike(XdmAtomicValue value)
        {
            return value.Type == XsType.UntypedAtomic || _IsString(value);
        }
    }
}
