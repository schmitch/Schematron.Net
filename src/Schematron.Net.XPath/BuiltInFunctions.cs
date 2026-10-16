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
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// The XPath 3.1 core function library (as far as it is relevant for validation rules), the XSLT functions
    /// (current(), document(), key(), ...) and the constructor functions of the built-in atomic types.
    /// </summary>
    internal static class BuiltInFunctions
    {
        private static Dictionary<string, List<FunctionDefinition>> _Functions;


        public static Dictionary<string, List<FunctionDefinition>> Create()
        {
            _Functions = new Dictionary<string, List<FunctionDefinition>>();
            _RegisterAccessors();
            _RegisterNumeric();
            _RegisterStrings();
            _RegisterBoolean();
            _RegisterSequences();
            _RegisterNodes();
            _RegisterDates();
            _RegisterXslt();
            _RegisterMath();
            _RegisterConstructors();
            Dictionary<string, List<FunctionDefinition>> result = _Functions;
            _Functions = null;
            return result;
        }


        // --------------------------------------------------------------------------------------------------------
        // registration helpers
        // --------------------------------------------------------------------------------------------------------

        private static XPathSequenceType _T(string spec)
        {
            if (spec == null)
            {
                return null;
            }
            Occurrence occurrence = Occurrence.ExactlyOne;
            char last = spec[spec.Length - 1];
            if (last == '?' || last == '*' || last == '+')
            {
                occurrence = last == '?' ? Occurrence.ZeroOrOne : last == '*' ? Occurrence.ZeroOrMore : Occurrence.OneOrMore;
                spec = spec.Substring(0, spec.Length - 1);
            }
            ItemType itemType;
            switch (spec)
            {
                case "item()":
                    if (occurrence == Occurrence.ZeroOrMore)
                    {
                        return null;
                    }
                    itemType = AnyItemType.Instance;
                    break;
                case "node()":
                    itemType = new NodeItemType(KindTest.AnyNode);
                    break;
                case "element()":
                    itemType = new NodeItemType(new KindTest(XdmNodeKind.Element));
                    break;
                default:
                    itemType = new AtomicItemType(XsType.FromLocalName(spec.Substring(3)));
                    break;
            }
            return new XPathSequenceType(itemType, occurrence);
        }


        private static void _Add(string ns, string name, int min, int max, ResultKind kind, FunctionImplementation implementation, params string[] types)
        {
            FunctionDefinition definition = new FunctionDefinition
            {
                NamespaceUri = ns,
                LocalName = name,
                MinArity = min,
                MaxArity = max,
                ParameterTypes = types.Select(_T).ToArray(),
                Implementation = implementation,
                ResultKind = kind
            };
            string key = "{" + ns + "}" + name;
            List<FunctionDefinition> list;
            if (!_Functions.TryGetValue(key, out list))
            {
                list = new List<FunctionDefinition>();
                _Functions[key] = list;
            }
            list.Add(definition);
        }


        private static void _Fn(string name, int min, int max, ResultKind kind, FunctionImplementation implementation, params string[] types)
        {
            _Add(XmlNamespaces.Fn, name, min, max, kind, implementation, types);
        }


        // --------------------------------------------------------------------------------------------------------
        // argument helpers
        // --------------------------------------------------------------------------------------------------------

        private static string _String(XdmValue s)
        {
            return s.Count == 0 ? "" : ((XdmAtomicValue)s[0]).StringValue;
        }


        private static double _Double(XdmValue s)
        {
            return ((NumericValue)s[0]).ToDouble();
        }


        private static XdmNode _NodeOrContext(EvalContext context, XdmValue[] args, int index)
        {
            if (args.Length > index)
            {
                return (XdmNode)args[index].First;
            }
            return context.ContextNode;
        }


        private static XdmItem _ContextItem(EvalContext context)
        {
            if (context.ContextItem == null)
            {
                throw new XPathException("XPDY0002", "The context item is absent");
            }
            return context.ContextItem;
        }


        private static string _StringOfContextOrArg(EvalContext context, XdmValue[] args)
        {
            if (args.Length > 0)
            {
                return _String(args[0]);
            }
            XdmItem item = _ContextItem(context);
            return item is XdmNode node ? node.StringValue : ((XdmAtomicValue)item).StringValue;
        }


        private static NumericValue _ToNumeric(XdmAtomicValue value, string function)
        {
            if (value.Type == XsType.UntypedAtomic)
            {
                return (NumericValue)Casting.Cast(value, XsType.Double);
            }
            NumericValue numeric = value as NumericValue;
            if (numeric == null)
            {
                throw new XPathException("XPTY0004", "The argument of " + function + "() must be numeric, supplied value is of type " + value.Type.QualifiedName);
            }
            return numeric;
        }


        private static bool _HasSurrogates(string s)
        {
            foreach (char ch in s)
            {
                if (Char.IsSurrogate(ch))
                {
                    return true;
                }
            }
            return false;
        }


        private static int[] _Codepoints(string s)
        {
            List<int> result = new List<int>(s.Length);
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
            return result.ToArray();
        }


        private static string _FromCodepoints(IEnumerable<int> codepoints)
        {
            StringBuilder sb = new StringBuilder();
            foreach (int cp in codepoints)
            {
                sb.Append(Char.ConvertFromUtf32(cp));
            }
            return sb.ToString();
        }


        private static int _CodepointLength(string s)
        {
            if (!_HasSurrogates(s))
            {
                return s.Length;
            }
            return _Codepoints(s).Length;
        }


        /// <summary>
        /// XPath rounding (round half towards positive infinity) of a double.
        /// </summary>
        private static double _RoundDouble(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || x == 0)
            {
                return x;
            }
            if (x >= -0.5 && x < 0)
            {
                return -0.0;
            }
            double floor = Math.Floor(x);
            return x - floor >= 0.5 ? floor + 1 : floor;
        }


        // --------------------------------------------------------------------------------------------------------
        // accessors, errors and diagnostics
        // --------------------------------------------------------------------------------------------------------

        private static void _RegisterAccessors()
        {
            _Fn("string", 0, 1, ResultKind.String, (c, a, f) =>
            {
                if (a.Length == 0)
                {
                    XdmItem item = _ContextItem(c);
                    return XdmValue.Of(item is XdmNode n ? n.StringValue : ((XdmAtomicValue)item).StringValue);
                }
                XdmItem arg = a[0].First;
                if (arg == null)
                {
                    return XdmValue.EmptyString;
                }
                return XdmValue.Of(arg is XdmNode node ? node.StringValue : ((XdmAtomicValue)arg).StringValue);
            }, "item()?");

            _Fn("data", 0, 1, ResultKind.Unknown, (c, a, f) => Operations.Atomize(a.Length == 0 ? XdmValue.Of(_ContextItem(c)) : a[0]), "item()*");

            _Fn("node-name", 0, 1, ResultKind.Unknown, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 0);
                if (node == null || (node.Kind != XdmNodeKind.Element && node.Kind != XdmNodeKind.Attribute && node.Kind != XdmNodeKind.ProcessingInstruction))
                {
                    return XdmValue.Empty;
                }
                return XdmValue.Of(new QNameValue(node.Prefix, node.NamespaceUri, node.LocalName));
            }, "node()?");

            _Fn("base-uri", 0, 1, ResultKind.String, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 0);
                if (node == null || node.Document.DocumentUri == null)
                {
                    return XdmValue.Empty;
                }
                return XdmValue.Of(new StringAtomic(node.Document.DocumentUri, XsType.AnyUri));
            }, "node()?");

            _Fn("document-uri", 0, 1, ResultKind.String, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 0);
                if (node == null || node.Kind != XdmNodeKind.Document || node.Document.DocumentUri == null)
                {
                    return XdmValue.Empty;
                }
                return XdmValue.Of(new StringAtomic(node.Document.DocumentUri, XsType.AnyUri));
            }, "node()?");

            _Fn("static-base-uri", 0, 0, ResultKind.String, (c, a, f) => f.StaticContext.BaseUri == null ? XdmValue.Empty : XdmValue.Of(new StringAtomic(f.StaticContext.BaseUri, XsType.AnyUri)));

            _Fn("error", 0, 3, ResultKind.Unknown, (c, a, f) =>
            {
                string code = "FOER0000";
                if (a.Length > 0 && a[0].Count > 0)
                {
                    code = ((QNameValue)a[0][0]).LocalName;
                }
                string description = a.Length > 1 ? _String(a[1]) : "error() called";
                throw new XPathException(code, description);
            }, "xs:QName?", "xs:string", null);

            _Fn("trace", 1, 2, ResultKind.Unknown, (c, a, f) => a[0], null, "xs:string");
        }


        // --------------------------------------------------------------------------------------------------------
        // numeric functions
        // --------------------------------------------------------------------------------------------------------

        private static void _RegisterNumeric()
        {
            _Fn("number", 0, 1, ResultKind.Numeric, (c, a, f) =>
            {
                XdmAtomicValue value;
                if (a.Length == 0)
                {
                    value = Operations.Atomize(_ContextItem(c));
                }
                else
                {
                    value = (XdmAtomicValue)a[0].First;
                }
                return XdmValue.Of(new DoubleValue(_NumberOf(value)));
            }, "xs:anyAtomicType?");

            _Fn("abs", 1, 1, ResultKind.Numeric, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return XdmValue.Empty;
                }
                NumericValue n = _ToNumeric((XdmAtomicValue)a[0][0], "abs");
                switch (n)
                {
                    case IntegerValue i:
                        return XdmValue.Of(IntegerValue.Get(BigInteger.Abs(i.Value)));
                    case DecimalValue d:
                        return XdmValue.Of(new DecimalValue(Math.Abs(d.Value)));
                    case FloatValue fl:
                        return XdmValue.Of(new FloatValue(Math.Abs(fl.Value)));
                    default:
                        return XdmValue.Of(new DoubleValue(Math.Abs(n.ToDouble())));
                }
            }, "xs:anyAtomicType?");

            _Fn("ceiling", 1, 1, ResultKind.Numeric, (c, a, f) => _RoundingFunction(a, "ceiling", Math.Ceiling, Math.Ceiling), "xs:anyAtomicType?");
            _Fn("floor", 1, 1, ResultKind.Numeric, (c, a, f) => _RoundingFunction(a, "floor", Math.Floor, Math.Floor), "xs:anyAtomicType?");

            _Fn("round", 1, 2, ResultKind.Numeric, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return XdmValue.Empty;
                }
                NumericValue n = _ToNumeric((XdmAtomicValue)a[0][0], "round");
                int precision = a.Length > 1 && a[1].Count > 0 ? (int)((IntegerValue)a[1][0]).Value : 0;
                return XdmValue.Of(_Round(n, precision, false));
            }, "xs:anyAtomicType?", "xs:integer?");

            _Fn("round-half-to-even", 1, 2, ResultKind.Numeric, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return XdmValue.Empty;
                }
                NumericValue n = _ToNumeric((XdmAtomicValue)a[0][0], "round-half-to-even");
                int precision = a.Length > 1 ? (int)((IntegerValue)a[1][0]).Value : 0;
                return XdmValue.Of(_Round(n, precision, true));
            }, "xs:anyAtomicType?", "xs:integer");
        }


        private static double _NumberOf(XdmAtomicValue value)
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


        private static XdmValue _RoundingFunction(XdmValue[] a, string name, Func<decimal, decimal> decimalFunction, Func<double, double> doubleFunction)
        {
            if (a[0].Count == 0)
            {
                return XdmValue.Empty;
            }
            NumericValue n = _ToNumeric((XdmAtomicValue)a[0][0], name);
            switch (n)
            {
                case IntegerValue i:
                    return XdmValue.Of(i);
                case DecimalValue d:
                    return XdmValue.Of(new DecimalValue(decimalFunction(d.Value)));
                case FloatValue fl:
                    return XdmValue.Of(new FloatValue((float)doubleFunction(fl.Value)));
                default:
                    return XdmValue.Of(new DoubleValue(doubleFunction(n.ToDouble())));
            }
        }


        private static NumericValue _Round(NumericValue n, int precision, bool halfToEven)
        {
            switch (n)
            {
                case IntegerValue i:
                    if (precision >= 0)
                    {
                        return i;
                    }
                    {
                        BigInteger factor = BigInteger.Pow(10, -precision);
                        decimal scaled = (decimal)i.Value / (decimal)factor;
                        decimal rounded = halfToEven ? Math.Round(scaled, MidpointRounding.ToEven) : Math.Floor(scaled + 0.5m);
                        return IntegerValue.Get(new BigInteger(rounded) * factor);
                    }
                case DecimalValue d:
                    return new DecimalValue(_RoundDecimal(d.Value, precision, halfToEven));
                default:
                    {
                        double x = n.ToDouble();
                        double result;
                        if (double.IsNaN(x) || double.IsInfinity(x) || x == 0)
                        {
                            result = x;
                        }
                        else if (precision == 0 && !halfToEven)
                        {
                            result = _RoundDouble(x);
                        }
                        else
                        {
                            try
                            {
                                decimal dx = NumberFormatting.DoubleToDecimal(x);
                                result = NumberFormatting.DecimalToDouble(_RoundDecimal(dx, precision, halfToEven));
                                if (result == 0 && x < 0)
                                {
                                    result = -0.0;
                                }
                            }
                            catch (XPathException)
                            {
                                // too large for a decimal: already integral
                                result = x;
                            }
                        }
                        if (n is FloatValue)
                        {
                            return new FloatValue((float)result);
                        }
                        return new DoubleValue(result);
                    }
            }
        }


        private static decimal _RoundDecimal(decimal value, int precision, bool halfToEven)
        {
            if (precision > 28)
            {
                return value;
            }
            if (halfToEven)
            {
                if (precision >= 0)
                {
                    return Math.Round(value, precision, MidpointRounding.ToEven);
                }
                decimal factor = _Pow10(-precision);
                return Math.Round(value / factor, MidpointRounding.ToEven) * factor;
            }
            if (precision >= 0)
            {
                decimal factor = _Pow10(precision);
                try
                {
                    return Math.Floor(value * factor + 0.5m) / factor;
                }
                catch (OverflowException)
                {
                    return value;
                }
            }
            decimal divisor = _Pow10(-precision);
            return Math.Floor(value / divisor + 0.5m) * divisor;
        }


        private static decimal _Pow10(int n)
        {
            decimal result = 1m;
            for (int i = 0; i < n; i++)
            {
                result *= 10;
            }
            return result;
        }


        // --------------------------------------------------------------------------------------------------------
        // string functions
        // --------------------------------------------------------------------------------------------------------

        private static void _RegisterStrings()
        {
            _Fn("concat", 2, int.MaxValue, ResultKind.String, (c, a, f) =>
            {
                StringBuilder sb = new StringBuilder();
                foreach (XdmValue arg in a)
                {
                    if (arg.Count > 0)
                    {
                        sb.Append(((XdmAtomicValue)arg[0]).StringValue);
                    }
                }
                return XdmValue.Of(sb.ToString());
            }, "xs:anyAtomicType?");

            _Fn("string-join", 1, 2, ResultKind.String, (c, a, f) =>
            {
                string separator = a.Length > 1 ? _String(a[1]) : "";
                return XdmValue.Of(String.Join(separator, a[0].Select(i => ((XdmAtomicValue)i).StringValue)));
            }, "xs:anyAtomicType*", "xs:string");

            _Fn("string-length", 0, 1, ResultKind.Numeric, (c, a, f) => XdmValue.Of(IntegerValue.Get(_CodepointLength(_StringOfContextOrArg(c, a)))), "xs:string?");

            _Fn("normalize-space", 0, 1, ResultKind.String, (c, a, f) => XdmValue.Of(Casting.Collapse(_StringOfContextOrArg(c, a))), "xs:string?");

            _Fn("normalize-unicode", 1, 2, ResultKind.String, (c, a, f) =>
            {
                string s = _String(a[0]);
                string form = a.Length > 1 ? _String(a[1]).Trim().ToUpperInvariant() : "NFC";
                switch (form)
                {
                    case "":
                        return XdmValue.Of(s);
                    case "NFC":
                        return XdmValue.Of(s.Normalize(NormalizationForm.FormC));
                    case "NFD":
                        return XdmValue.Of(s.Normalize(NormalizationForm.FormD));
                    case "NFKC":
                        return XdmValue.Of(s.Normalize(NormalizationForm.FormKC));
                    case "NFKD":
                        return XdmValue.Of(s.Normalize(NormalizationForm.FormKD));
                    default:
                        throw new XPathException("FOCH0003", "Unsupported normalization form " + form);
                }
            }, "xs:string?", "xs:string");

            _Fn("upper-case", 1, 1, ResultKind.String, (c, a, f) => XdmValue.Of(_UpperCase(_String(a[0]))), "xs:string?");
            _Fn("lower-case", 1, 1, ResultKind.String, (c, a, f) => XdmValue.Of(_String(a[0]).ToLowerInvariant()), "xs:string?");

            _Fn("substring", 2, 3, ResultKind.String, (c, a, f) =>
            {
                string s = _String(a[0]);
                double start = _RoundDouble(_Double(a[1]));
                double length = a.Length > 2 ? _RoundDouble(_Double(a[2])) : double.PositiveInfinity;
                double end = start + length;
                if (double.IsNaN(start) || double.IsNaN(end))
                {
                    return XdmValue.EmptyString;
                }
                if (!_HasSurrogates(s))
                {
                    double from = Math.Max(1, start);
                    double to = Math.Min(s.Length + 1, end);
                    if (!(to > from))
                    {
                        return XdmValue.EmptyString;
                    }
                    return XdmValue.Of(s.Substring((int)from - 1, (int)(to - from)));
                }
                int[] cps = _Codepoints(s);
                List<int> result = new List<int>();
                for (int p = 1; p <= cps.Length; p++)
                {
                    if (p >= start && p < end)
                    {
                        result.Add(cps[p - 1]);
                    }
                }
                return XdmValue.Of(_FromCodepoints(result));
            }, "xs:string?", "xs:double", "xs:double");

            _Fn("contains", 2, 3, ResultKind.Boolean, (c, a, f) => XdmValue.Of(_String(a[0]).IndexOf(_String(a[1]), StringComparison.Ordinal) >= 0), "xs:string?", "xs:string?", "xs:string");
            _Fn("starts-with", 2, 3, ResultKind.Boolean, (c, a, f) => XdmValue.Of(_String(a[0]).StartsWith(_String(a[1]), StringComparison.Ordinal)), "xs:string?", "xs:string?", "xs:string");
            _Fn("ends-with", 2, 3, ResultKind.Boolean, (c, a, f) => XdmValue.Of(_String(a[0]).EndsWith(_String(a[1]), StringComparison.Ordinal)), "xs:string?", "xs:string?", "xs:string");

            _Fn("substring-before", 2, 3, ResultKind.String, (c, a, f) =>
            {
                string s = _String(a[0]);
                string search = _String(a[1]);
                int index = s.IndexOf(search, StringComparison.Ordinal);
                return XdmValue.Of(index < 0 ? "" : s.Substring(0, index));
            }, "xs:string?", "xs:string?", "xs:string");

            _Fn("substring-after", 2, 3, ResultKind.String, (c, a, f) =>
            {
                string s = _String(a[0]);
                string search = _String(a[1]);
                int index = s.IndexOf(search, StringComparison.Ordinal);
                return XdmValue.Of(index < 0 ? "" : s.Substring(index + search.Length));
            }, "xs:string?", "xs:string?", "xs:string");

            _Fn("translate", 3, 3, ResultKind.String, (c, a, f) =>
            {
                int[] input = _Codepoints(_String(a[0]));
                int[] map = _Codepoints(_String(a[1]));
                int[] trans = _Codepoints(_String(a[2]));
                List<int> result = new List<int>(input.Length);
                foreach (int cp in input)
                {
                    int index = Array.IndexOf(map, cp);
                    if (index < 0)
                    {
                        result.Add(cp);
                    }
                    else if (index < trans.Length)
                    {
                        result.Add(trans[index]);
                    }
                }
                return XdmValue.Of(_FromCodepoints(result));
            }, "xs:string?", "xs:string", "xs:string");

            _Fn("string-to-codepoints", 1, 1, ResultKind.Unknown, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return XdmValue.Empty;
                }
                return XdmValue.FromArray(_Codepoints(_String(a[0])).Select(cp => (XdmItem)IntegerValue.Get(cp)).ToArray());
            }, "xs:string?");

            _Fn("codepoints-to-string", 1, 1, ResultKind.String, (c, a, f) =>
            {
                List<int> cps = new List<int>();
                foreach (XdmItem item in a[0])
                {
                    BigInteger cp = ((IntegerValue)item).Value;
                    if (cp < 1 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
                    {
                        throw new XPathException("FOCH0001", "Invalid XML character [x" + cp.ToString("X", CultureInfo.InvariantCulture) + "]");
                    }
                    cps.Add((int)cp);
                }
                return XdmValue.Of(_FromCodepoints(cps));
            }, "xs:integer*");

            _Fn("compare", 2, 3, ResultKind.Numeric, (c, a, f) =>
            {
                if (a[0].Count == 0 || a[1].Count == 0)
                {
                    return XdmValue.Empty;
                }
                return XdmValue.Of(IntegerValue.Get(Math.Sign(String.CompareOrdinal(_String(a[0]), _String(a[1])))));
            }, "xs:string?", "xs:string?", "xs:string");

            _Fn("codepoint-equal", 2, 2, ResultKind.Boolean, (c, a, f) =>
            {
                if (a[0].Count == 0 || a[1].Count == 0)
                {
                    return XdmValue.Empty;
                }
                return XdmValue.Of(String.Equals(_String(a[0]), _String(a[1]), StringComparison.Ordinal));
            }, "xs:string?", "xs:string?");

            _Fn("matches", 2, 3, ResultKind.Boolean, (c, a, f) =>
            {
                Regex regex = XPathRegex.Get(_String(a[1]), a.Length > 2 ? _String(a[2]) : "");
                return XdmValue.Of(regex.IsMatch(_String(a[0])));
            }, "xs:string?", "xs:string", "xs:string");

            _Fn("replace", 3, 4, ResultKind.String, (c, a, f) =>
            {
                Regex regex = XPathRegex.Get(_String(a[1]), a.Length > 3 ? _String(a[3]) : "");
                if (regex.IsMatch(""))
                {
                    throw new XPathException("FORX0003", "The regular expression in replace() matches a zero-length string");
                }
                return XdmValue.Of(XPathRegex.Replace(regex, _String(a[0]), _String(a[2])));
            }, "xs:string?", "xs:string", "xs:string", "xs:string");

            _Fn("tokenize", 1, 3, ResultKind.Unknown, (c, a, f) =>
            {
                string input = _String(a[0]);
                if (a.Length == 1)
                {
                    input = Casting.Collapse(input);
                    if (input.Length == 0)
                    {
                        return XdmValue.Empty;
                    }
                    return XdmValue.FromArray(input.Split(' ').Select(s => (XdmItem)new StringAtomic(s)).ToArray());
                }
                if (input.Length == 0)
                {
                    return XdmValue.Empty;
                }
                Regex regex = XPathRegex.Get(_String(a[1]), a.Length > 2 ? _String(a[2]) : "");
                if (regex.IsMatch(""))
                {
                    throw new XPathException("FORX0003", "The regular expression in tokenize() matches a zero-length string");
                }
                return XdmValue.FromArray(XPathRegex.Tokenize(regex, input).Select(s => (XdmItem)new StringAtomic(s)).ToArray());
            }, "xs:string?", "xs:string", "xs:string");

            _Fn("encode-for-uri", 1, 1, ResultKind.String, (c, a, f) => XdmValue.Of(_PercentEncode(_String(a[0]), ch => (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.' || ch == '~')), "xs:string?");
            _Fn("iri-to-uri", 1, 1, ResultKind.String, (c, a, f) => XdmValue.Of(_PercentEncode(_String(a[0]), ch => ch > 0x20 && ch < 0x7F && ch != '<' && ch != '>' && ch != '"' && ch != '{' && ch != '}' && ch != '|' && ch != '\\' && ch != '^' && ch != '`')), "xs:string?");
            _Fn("escape-html-uri", 1, 1, ResultKind.String, (c, a, f) => XdmValue.Of(_PercentEncode(_String(a[0]), ch => ch >= 0x20 && ch < 0x7F)), "xs:string?");

            _Fn("resolve-uri", 1, 2, ResultKind.String, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return XdmValue.Empty;
                }
                string baseUri = a.Length > 1 ? _String(a[1]) : f.StaticContext.BaseUri;
                return XdmValue.Of(new StringAtomic(UriHelper.Resolve(baseUri, _String(a[0])), XsType.AnyUri));
            }, "xs:string?", "xs:string");
        }


        /// <summary>
        /// Upper-casing with the special cases of Java's String.toUpperCase() that expand to two characters.
        /// </summary>
        private static string _UpperCase(string s)
        {
            string upper = s.ToUpperInvariant();
            if (upper.IndexOf('\u00DF') < 0 && upper.IndexOf('\uFB00') < 0 && upper.IndexOf('\uFB01') < 0 && upper.IndexOf('\uFB02') < 0)
            {
                return upper;
            }
            return upper.Replace("\u00DF", "SS").Replace("\uFB00", "FF").Replace("\uFB01", "FI").Replace("\uFB02", "FL");
        }


        private static string _PercentEncode(string s, Func<char, bool> keep)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                char ch = (char)b;
                if (b < 0x80 && keep(ch))
                {
                    sb.Append(ch);
                }
                else
                {
                    sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }


        // --------------------------------------------------------------------------------------------------------
        // boolean functions
        // --------------------------------------------------------------------------------------------------------

        private static void _RegisterBoolean()
        {
            _Fn("true", 0, 0, ResultKind.Boolean, (c, a, f) => XdmValue.True);
            _Fn("false", 0, 0, ResultKind.Boolean, (c, a, f) => XdmValue.False);
            _Fn("not", 1, 1, ResultKind.Boolean, (c, a, f) => XdmValue.Of(!Operations.EffectiveBooleanValue(a[0])), "item()*");
            _Fn("boolean", 1, 1, ResultKind.Boolean, (c, a, f) => XdmValue.Of(Operations.EffectiveBooleanValue(a[0])), "item()*");
        }


        // --------------------------------------------------------------------------------------------------------
        // sequence functions
        // --------------------------------------------------------------------------------------------------------

        private static void _RegisterSequences()
        {
            _Fn("empty", 1, 1, ResultKind.Boolean, (c, a, f) => XdmValue.Of(a[0].Count == 0), "item()*");
            _Fn("exists", 1, 1, ResultKind.Boolean, (c, a, f) => XdmValue.Of(a[0].Count > 0), "item()*");
            _Fn("count", 1, 1, ResultKind.Numeric, (c, a, f) => XdmValue.Of(IntegerValue.Get(a[0].Count)), "item()*");
            _Fn("reverse", 1, 1, ResultKind.Unknown, (c, a, f) => XdmValue.FromArray(a[0].Reverse().ToArray()), "item()*");
            _Fn("unordered", 1, 1, ResultKind.Unknown, (c, a, f) => a[0], "item()*");
            _Fn("head", 1, 1, ResultKind.Unknown, (c, a, f) => a[0].Count == 0 ? XdmValue.Empty : XdmValue.Of(a[0][0]), "item()*");
            _Fn("tail", 1, 1, ResultKind.Unknown, (c, a, f) => a[0].Slice(1, a[0].Count - 1), "item()*");

            _Fn("position", 0, 0, ResultKind.Numeric, (c, a, f) =>
            {
                _ContextItem(c);
                return XdmValue.Of(IntegerValue.Get(c.Position));
            });
            _Functions["{" + XmlNamespaces.Fn + "}position"][0].DependsOnPosition = true;
            _Fn("last", 0, 0, ResultKind.Numeric, (c, a, f) =>
            {
                _ContextItem(c);
                return XdmValue.Of(IntegerValue.Get(c.Size));
            });
            _Functions["{" + XmlNamespaces.Fn + "}last"][0].DependsOnPosition = true;

            _Fn("zero-or-one", 1, 1, ResultKind.Unknown, (c, a, f) =>
            {
                if (a[0].Count > 1)
                {
                    throw new XPathException("FORG0003", "fn:zero-or-one() called with a sequence containing more than one item");
                }
                return a[0];
            }, "item()*");
            _Fn("one-or-more", 1, 1, ResultKind.Unknown, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    throw new XPathException("FORG0004", "fn:one-or-more() called with an empty sequence");
                }
                return a[0];
            }, "item()*");
            _Fn("exactly-one", 1, 1, ResultKind.Unknown, (c, a, f) =>
            {
                if (a[0].Count != 1)
                {
                    throw new XPathException("FORG0005", "fn:exactly-one() called with a sequence containing zero or more than one item");
                }
                return a[0];
            }, "item()*");

            _Fn("subsequence", 2, 3, ResultKind.Unknown, (c, a, f) =>
            {
                double start = _RoundDouble(_Double(a[1]));
                double end = a.Length > 2 ? start + _RoundDouble(_Double(a[2])) : double.PositiveInfinity;
                List<XdmItem> result = new List<XdmItem>();
                for (int p = 1; p <= a[0].Count; p++)
                {
                    if (p >= start && p < end)
                    {
                        result.Add(a[0][p - 1]);
                    }
                }
                return XdmValue.FromList(result);
            }, "item()*", "xs:double", "xs:double");

            _Fn("insert-before", 3, 3, ResultKind.Unknown, (c, a, f) =>
            {
                List<XdmItem> result = new List<XdmItem>(a[0]);
                long position = (long)((IntegerValue)a[1][0]).Value;
                int index = (int)Math.Max(0, Math.Min(result.Count, position - 1));
                result.InsertRange(index, a[2]);
                return XdmValue.FromList(result);
            }, "item()*", "xs:integer", "item()*");

            _Fn("remove", 2, 2, ResultKind.Unknown, (c, a, f) =>
            {
                long position = (long)((IntegerValue)a[1][0]).Value;
                if (position < 1 || position > a[0].Count)
                {
                    return a[0];
                }
                List<XdmItem> result = new List<XdmItem>(a[0]);
                result.RemoveAt((int)position - 1);
                return XdmValue.FromList(result);
            }, "item()*", "xs:integer");

            _Fn("index-of", 2, 3, ResultKind.Unknown, (c, a, f) =>
            {
                XdmAtomicValue search = (XdmAtomicValue)a[1][0];
                List<XdmItem> result = new List<XdmItem>();
                for (int i = 0; i < a[0].Count; i++)
                {
                    if (Operations.DeepEqualAtomic((XdmAtomicValue)a[0][i], search) && !(search is NumericValue n && n.IsNaN))
                    {
                        result.Add(IntegerValue.Get(i + 1));
                    }
                }
                return XdmValue.FromList(result);
            }, "xs:anyAtomicType*", "xs:anyAtomicType", "xs:string");

            _Fn("distinct-values", 1, 2, ResultKind.Unknown, (c, a, f) =>
            {
                List<XdmItem> result = new List<XdmItem>();
                Dictionary<string, List<XdmAtomicValue>> buckets = new Dictionary<string, List<XdmAtomicValue>>();
                foreach (XdmItem item in a[0])
                {
                    XdmAtomicValue value = (XdmAtomicValue)item;
                    string key = _DistinctKey(value);
                    List<XdmAtomicValue> bucket;
                    if (!buckets.TryGetValue(key, out bucket))
                    {
                        bucket = new List<XdmAtomicValue>();
                        buckets[key] = bucket;
                    }
                    bool isNaN = value is NumericValue n && n.IsNaN;
                    if (bucket.Any(other => (isNaN && other is NumericValue o && o.IsNaN) || Operations.DeepEqualAtomic(other, value)))
                    {
                        continue;
                    }
                    bucket.Add(value);
                    result.Add(value);
                }
                return XdmValue.FromList(result);
            }, "xs:anyAtomicType*", "xs:string");

            _Fn("deep-equal", 2, 3, ResultKind.Boolean, (c, a, f) => XdmValue.Of(_DeepEqual(a[0], a[1])), "item()*", "item()*", "xs:string");

            _Fn("sum", 1, 2, ResultKind.Numeric, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return a.Length > 1 ? a[1] : XdmValue.Of(IntegerValue.Zero);
                }
                return XdmValue.Of(_Sum(a[0], "sum"));
            }, "xs:anyAtomicType*", "xs:anyAtomicType?");

            _Fn("avg", 1, 1, ResultKind.Numeric, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return XdmValue.Empty;
                }
                XdmAtomicValue total = _Sum(a[0], "avg");
                if (total is DurationValue duration)
                {
                    return XdmValue.Of(Operations.Arithmetic(duration, ArithmeticOperator.Divide, IntegerValue.Get(a[0].Count)));
                }
                return XdmValue.Of(Operations.NumericArithmetic((NumericValue)total, ArithmeticOperator.Divide, IntegerValue.Get(a[0].Count)));
            }, "xs:anyAtomicType*");

            _Fn("max", 1, 2, ResultKind.Unknown, (c, a, f) => _MinMax(a[0], true), "xs:anyAtomicType*", "xs:string");
            _Fn("min", 1, 2, ResultKind.Unknown, (c, a, f) => _MinMax(a[0], false), "xs:anyAtomicType*", "xs:string");
        }


        private static string _DistinctKey(XdmAtomicValue value)
        {
            if (value is NumericValue n)
            {
                return "n" + n.ToDouble().ToString("R", CultureInfo.InvariantCulture);
            }
            if (value.Type.IsStringLike)
            {
                return "s" + value.StringValue;
            }
            return "o" + value.Type.Primitive;
        }


        private static XdmAtomicValue _Sum(XdmValue values, string function)
        {
            XdmAtomicValue total = null;
            foreach (XdmItem item in values)
            {
                XdmAtomicValue value = (XdmAtomicValue)item;
                if (value.Type == XsType.UntypedAtomic)
                {
                    value = Casting.Cast(value, XsType.Double);
                }
                if (!(value is NumericValue) && !(value is DurationValue d && d.Type != XsType.Duration))
                {
                    throw new XPathException("FORG0006", "Input to " + function + "() contains a value of type " + value.Type.QualifiedName);
                }
                if (total == null)
                {
                    total = value;
                }
                else if (total is NumericValue tn && value is NumericValue vn)
                {
                    total = Operations.NumericArithmetic(tn, ArithmeticOperator.Add, vn);
                }
                else if (total is DurationValue && value is DurationValue && total.Type == value.Type)
                {
                    total = Operations.Arithmetic(total, ArithmeticOperator.Add, value);
                }
                else
                {
                    throw new XPathException("FORG0006", "Input to " + function + "() contains values of incompatible types");
                }
            }
            return total;
        }


        private static XdmValue _MinMax(XdmValue values, bool max)
        {
            if (values.Count == 0)
            {
                return XdmValue.Empty;
            }
            List<XdmAtomicValue> converted = new List<XdmAtomicValue>(values.Count);
            bool hasDouble = false;
            bool hasFloat = false;
            bool hasDecimal = false;
            foreach (XdmItem item in values)
            {
                XdmAtomicValue value = (XdmAtomicValue)item;
                if (value.Type == XsType.UntypedAtomic)
                {
                    value = Casting.Cast(value, XsType.Double);
                }
                else if (value.Type == XsType.AnyUri)
                {
                    value = new StringAtomic(value.StringValue);
                }
                if (value is DoubleValue)
                {
                    hasDouble = true;
                }
                else if (value is FloatValue)
                {
                    hasFloat = true;
                }
                else if (value is DecimalValue)
                {
                    hasDecimal = true;
                }
                converted.Add(value);
            }
            XdmAtomicValue best = null;
            foreach (XdmAtomicValue v in converted)
            {
                XdmAtomicValue value = v;
                if (value is NumericValue numeric)
                {
                    if (numeric.IsNaN)
                    {
                        return XdmValue.Of(hasDouble ? (XdmAtomicValue)DoubleValue.NaN : new FloatValue(float.NaN));
                    }
                    if (hasDouble && !(value is DoubleValue))
                    {
                        value = new DoubleValue(numeric.ToDouble());
                    }
                    else if (hasFloat && !(value is FloatValue) && !hasDouble)
                    {
                        value = new FloatValue((float)numeric.ToDouble());
                    }
                    else if (hasDecimal && value is IntegerValue integer && !hasDouble && !hasFloat)
                    {
                        value = new DecimalValue(integer.ToDecimal());
                    }
                }
                if (best == null)
                {
                    best = value;
                    continue;
                }
                int c;
                if (!Operations.TryCompare(value, best, false, out c))
                {
                    throw new XPathException("FORG0006", "Cannot compare " + value.Type.QualifiedName + " with " + best.Type.QualifiedName + " in " + (max ? "max" : "min") + "()");
                }
                if (max ? c > 0 : c < 0)
                {
                    best = value;
                }
            }
            return XdmValue.Of(best);
        }


        private static bool _DeepEqual(XdmValue a, XdmValue b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                XdmItem x = a[i];
                XdmItem y = b[i];
                if (x is XdmAtomicValue ax && y is XdmAtomicValue ay)
                {
                    bool bothNaN = ax is NumericValue nx && nx.IsNaN && ay is NumericValue ny && ny.IsNaN;
                    if (!bothNaN && !Operations.DeepEqualAtomic(ax, ay))
                    {
                        return false;
                    }
                }
                else if (x is XdmNode nodeX && y is XdmNode nodeY)
                {
                    if (!_DeepEqualNodes(nodeX, nodeY))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            return true;
        }


        private static bool _DeepEqualNodes(XdmNode a, XdmNode b)
        {
            if (a.Kind != b.Kind)
            {
                return false;
            }
            switch (a.Kind)
            {
                case XdmNodeKind.Element:
                    if (a.LocalName != b.LocalName || a.NamespaceUri != b.NamespaceUri || a.AttributeNodes.Length != b.AttributeNodes.Length)
                    {
                        return false;
                    }
                    foreach (XdmNode attribute in a.AttributeNodes)
                    {
                        if (b.GetAttribute(attribute.LocalName, attribute.NamespaceUri) != attribute.Value)
                        {
                            return false;
                        }
                    }
                    return _DeepEqualChildren(a, b);
                case XdmNodeKind.Document:
                    return _DeepEqualChildren(a, b);
                case XdmNodeKind.Attribute:
                case XdmNodeKind.ProcessingInstruction:
                case XdmNodeKind.Namespace:
                    return a.LocalName == b.LocalName && a.NamespaceUri == b.NamespaceUri && a.StringValue == b.StringValue;
                default:
                    return a.StringValue == b.StringValue;
            }
        }


        private static bool _DeepEqualChildren(XdmNode a, XdmNode b)
        {
            List<XdmNode> ca = a.ChildNodes.Where(n => n.Kind == XdmNodeKind.Element || n.Kind == XdmNodeKind.Text).ToList();
            List<XdmNode> cb = b.ChildNodes.Where(n => n.Kind == XdmNodeKind.Element || n.Kind == XdmNodeKind.Text).ToList();
            if (ca.Count != cb.Count)
            {
                return false;
            }
            for (int i = 0; i < ca.Count; i++)
            {
                if (!_DeepEqualNodes(ca[i], cb[i]))
                {
                    return false;
                }
            }
            return true;
        }


        // --------------------------------------------------------------------------------------------------------
        // node functions
        // --------------------------------------------------------------------------------------------------------

        private static void _RegisterNodes()
        {
            _Fn("name", 0, 1, ResultKind.String, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 0);
                return XdmValue.Of(node == null ? "" : node.Name);
            }, "node()?");

            _Fn("local-name", 0, 1, ResultKind.String, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 0);
                if (node == null)
                {
                    return XdmValue.EmptyString;
                }
                return XdmValue.Of(node.Kind == XdmNodeKind.Element || node.Kind == XdmNodeKind.Attribute || node.Kind == XdmNodeKind.ProcessingInstruction || node.Kind == XdmNodeKind.Namespace ? node.LocalName : "");
            }, "node()?");

            _Fn("namespace-uri", 0, 1, ResultKind.String, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 0);
                return XdmValue.Of(new StringAtomic(node == null ? "" : node.NamespaceUri ?? "", XsType.AnyUri));
            }, "node()?");

            _Fn("root", 0, 1, ResultKind.Nodes, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 0);
                return node == null ? XdmValue.Empty : XdmValue.Of(node.Root);
            }, "node()?");

            _Fn("has-children", 0, 1, ResultKind.Boolean, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 0);
                return XdmValue.Of(node != null && node.ChildNodes.Length > 0);
            }, "node()?");

            _Fn("lang", 1, 2, ResultKind.Boolean, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 1);
                string test = _String(a[0]).ToLowerInvariant();
                for (XdmNode n = node; n != null; n = n.Parent)
                {
                    string lang = n.Kind == XdmNodeKind.Element ? n.GetAttribute("lang", XmlNamespaces.Xml) : null;
                    if (lang != null)
                    {
                        lang = lang.ToLowerInvariant();
                        return XdmValue.Of(lang == test || lang.StartsWith(test + "-", StringComparison.Ordinal));
                    }
                }
                return XdmValue.False;
            }, "xs:string?", "node()");

            _Fn("path", 0, 1, ResultKind.String, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 0);
                if (node == null)
                {
                    return XdmValue.Empty;
                }
                return XdmValue.Of(NodePaths.XPathPath(node));
            }, "node()?");

            _Fn("in-scope-prefixes", 1, 1, ResultKind.Unknown, (c, a, f) =>
            {
                XdmNode element = (XdmNode)a[0][0];
                return XdmValue.FromArray(element.GetInScopeNamespaces().Keys.Select(k => (XdmItem)new StringAtomic(k)).ToArray());
            }, "element()");

            _Fn("namespace-uri-for-prefix", 2, 2, ResultKind.String, (c, a, f) =>
            {
                XdmNode element = (XdmNode)a[1][0];
                string uri;
                if (element.GetInScopeNamespaces().TryGetValue(_String(a[0]), out uri))
                {
                    return XdmValue.Of(new StringAtomic(uri, XsType.AnyUri));
                }
                return XdmValue.Empty;
            }, "xs:string?", "element()");

            _Fn("QName", 2, 2, ResultKind.Unknown, (c, a, f) =>
            {
                string qname = _String(a[1]);
                int colon = qname.IndexOf(':');
                return XdmValue.Of(new QNameValue(colon < 0 ? "" : qname.Substring(0, colon), _String(a[0]), colon < 0 ? qname : qname.Substring(colon + 1)));
            }, "xs:string?", "xs:string");

            _Fn("local-name-from-QName", 1, 1, ResultKind.String, (c, a, f) => a[0].Count == 0 ? XdmValue.Empty : XdmValue.Of(new StringAtomic(((QNameValue)a[0][0]).LocalName, XsType.NcName)), "xs:QName?");
            _Fn("namespace-uri-from-QName", 1, 1, ResultKind.String, (c, a, f) => a[0].Count == 0 ? XdmValue.Empty : XdmValue.Of(new StringAtomic(((QNameValue)a[0][0]).NamespaceUri, XsType.AnyUri)), "xs:QName?");
            _Fn("prefix-from-QName", 1, 1, ResultKind.String, (c, a, f) =>
            {
                if (a[0].Count == 0 || ((QNameValue)a[0][0]).Prefix.Length == 0)
                {
                    return XdmValue.Empty;
                }
                return XdmValue.Of(new StringAtomic(((QNameValue)a[0][0]).Prefix, XsType.NcName));
            }, "xs:QName?");

            _Fn("id", 1, 2, ResultKind.Nodes, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 1);
                HashSet<string> ids = new HashSet<string>(a[0].SelectMany(i => ((XdmAtomicValue)i).StringValue.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)));
                List<XdmNode> result = new List<XdmNode>();
                foreach (XdmNode n in node.Document.AllNodes)
                {
                    if (n.Kind == XdmNodeKind.Element && ids.Contains(n.GetAttribute("id", XmlNamespaces.Xml) ?? "\u0000"))
                    {
                        result.Add(n);
                    }
                }
                return XdmValue.FromNodes(result);
            }, "xs:string*", "node()");

            _Fn("doc", 1, 1, ResultKind.Nodes, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return XdmValue.Empty;
                }
                return XdmValue.Of(_LoadDocument(c, UriHelper.Resolve(f.StaticContext.BaseUri, _String(a[0]))).Root);
            }, "xs:string?");

            _Fn("doc-available", 1, 1, ResultKind.Boolean, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return XdmValue.False;
                }
                try
                {
                    _LoadDocument(c, UriHelper.Resolve(f.StaticContext.BaseUri, _String(a[0])));
                    return XdmValue.True;
                }
                catch (Exception)
                {
                    return XdmValue.False;
                }
            }, "xs:string?");
        }


        private static XdmDocument _LoadDocument(EvalContext context, string uri)
        {
            if (context.Environment.Documents == null)
            {
                throw new XPathException("FODC0002", "No document resolver available to load " + uri);
            }
            try
            {
                return context.Environment.Documents.Resolve(uri);
            }
            catch (XPathException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new XPathException("FODC0002", "Cannot load document " + uri + ": " + e.Message, e);
            }
        }


        // --------------------------------------------------------------------------------------------------------
        // date and time functions
        // --------------------------------------------------------------------------------------------------------

        private static void _RegisterDates()
        {
            _Fn("current-dateTime", 0, 0, ResultKind.Unknown, (c, a, f) => XdmValue.Of(_Now(c, XsType.DateTime)));
            _Fn("current-date", 0, 0, ResultKind.Unknown, (c, a, f) => XdmValue.Of(_Now(c, XsType.Date)));
            _Fn("current-time", 0, 0, ResultKind.Unknown, (c, a, f) => XdmValue.Of(_Now(c, XsType.Time)));
            _Fn("implicit-timezone", 0, 0, ResultKind.Unknown, (c, a, f) => XdmValue.Of(new DurationValue(XsType.DayTimeDuration, 0, Operations.ImplicitTimezoneMinutes * 60)));

            _Fn("dateTime", 2, 2, ResultKind.Unknown, (c, a, f) =>
            {
                if (a[0].Count == 0 || a[1].Count == 0)
                {
                    return XdmValue.Empty;
                }
                CalendarValue date = (CalendarValue)a[0][0];
                CalendarValue time = (CalendarValue)a[1][0];
                if (date.TimezoneMinutes.HasValue && time.TimezoneMinutes.HasValue && date.TimezoneMinutes != time.TimezoneMinutes)
                {
                    throw new XPathException("FORG0008", "The date and time arguments of dateTime() have different timezones");
                }
                return XdmValue.Of(new CalendarValue(XsType.DateTime, date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, date.TimezoneMinutes ?? time.TimezoneMinutes));
            }, "xs:date?", "xs:time?");

            _Component("year-from-dateTime", "xs:dateTime?", v => IntegerValue.Get(((CalendarValue)v).Year));
            _Component("month-from-dateTime", "xs:dateTime?", v => IntegerValue.Get(((CalendarValue)v).Month));
            _Component("day-from-dateTime", "xs:dateTime?", v => IntegerValue.Get(((CalendarValue)v).Day));
            _Component("hours-from-dateTime", "xs:dateTime?", v => IntegerValue.Get(((CalendarValue)v).Hour));
            _Component("minutes-from-dateTime", "xs:dateTime?", v => IntegerValue.Get(((CalendarValue)v).Minute));
            _Component("seconds-from-dateTime", "xs:dateTime?", v => new DecimalValue(((CalendarValue)v).Second));
            _Component("timezone-from-dateTime", "xs:dateTime?", _Timezone);
            _Component("year-from-date", "xs:date?", v => IntegerValue.Get(((CalendarValue)v).Year));
            _Component("month-from-date", "xs:date?", v => IntegerValue.Get(((CalendarValue)v).Month));
            _Component("day-from-date", "xs:date?", v => IntegerValue.Get(((CalendarValue)v).Day));
            _Component("timezone-from-date", "xs:date?", _Timezone);
            _Component("hours-from-time", "xs:time?", v => IntegerValue.Get(((CalendarValue)v).Hour));
            _Component("minutes-from-time", "xs:time?", v => IntegerValue.Get(((CalendarValue)v).Minute));
            _Component("seconds-from-time", "xs:time?", v => new DecimalValue(((CalendarValue)v).Second));
            _Component("timezone-from-time", "xs:time?", _Timezone);
            _Component("years-from-duration", "xs:duration?", v => IntegerValue.Get(((DurationValue)v).Months / 12));
            _Component("months-from-duration", "xs:duration?", v => IntegerValue.Get(((DurationValue)v).Months % 12));
            _Component("days-from-duration", "xs:duration?", v => IntegerValue.Get((long)Math.Truncate(((DurationValue)v).Seconds / 86400)));
            _Component("hours-from-duration", "xs:duration?", v => IntegerValue.Get((long)Math.Truncate(((DurationValue)v).Seconds % 86400 / 3600)));
            _Component("minutes-from-duration", "xs:duration?", v => IntegerValue.Get((long)Math.Truncate(((DurationValue)v).Seconds % 3600 / 60)));
            _Component("seconds-from-duration", "xs:duration?", v => new DecimalValue(((DurationValue)v).Seconds % 60));
        }


        private static XdmAtomicValue _Timezone(XdmAtomicValue v)
        {
            CalendarValue c = (CalendarValue)v;
            return c.TimezoneMinutes.HasValue ? new DurationValue(XsType.DayTimeDuration, 0, c.TimezoneMinutes.Value * 60) : null;
        }


        private static void _Component(string name, string type, Func<XdmAtomicValue, XdmAtomicValue> component)
        {
            _Fn(name, 1, 1, ResultKind.Numeric, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return XdmValue.Empty;
                }
                XdmAtomicValue result = component((XdmAtomicValue)a[0][0]);
                return result == null ? XdmValue.Empty : XdmValue.Of(result);
            }, type);
        }


        private static CalendarValue _Now(EvalContext context, XsType type)
        {
            DateTimeOffset now = context.Environment.Now;
            int tz = (int)now.Offset.TotalMinutes;
            decimal seconds = now.Second + now.Millisecond / 1000m;
            if (type == XsType.Date)
            {
                return new CalendarValue(type, now.Year, now.Month, now.Day, 0, 0, 0, tz);
            }
            if (type == XsType.Time)
            {
                return new CalendarValue(type, 0, 0, 0, now.Hour, now.Minute, seconds, tz);
            }
            return new CalendarValue(type, now.Year, now.Month, now.Day, now.Hour, now.Minute, seconds, tz);
        }


        // --------------------------------------------------------------------------------------------------------
        // XSLT functions
        // --------------------------------------------------------------------------------------------------------

        private static void _RegisterXslt()
        {
            _Fn("current", 0, 0, ResultKind.Unknown, (c, a, f) =>
            {
                if (c.CurrentItem == null)
                {
                    throw new XPathException("XTDE1360", "The current item is absent");
                }
                return XdmValue.Of(c.CurrentItem);
            });

            _Fn("document", 1, 2, ResultKind.Nodes, (c, a, f) =>
            {
                List<XdmNode> result = new List<XdmNode>();
                foreach (XdmItem item in a[0])
                {
                    string relative;
                    string baseUri;
                    if (item is XdmNode node)
                    {
                        relative = node.StringValue;
                        baseUri = a.Length > 1 && a[1].Count > 0 ? ((XdmNode)a[1][0]).Document.DocumentUri : node.Document.DocumentUri;
                    }
                    else
                    {
                        relative = ((XdmAtomicValue)item).StringValue;
                        baseUri = a.Length > 1 && a[1].Count > 0 ? ((XdmNode)a[1][0]).Document.DocumentUri : f.StaticContext.BaseUri;
                    }
                    int hash = relative.IndexOf('#');
                    if (hash >= 0)
                    {
                        relative = relative.Substring(0, hash);
                    }
                    if (relative.Length == 0 && item is XdmAtomicValue)
                    {
                        // document('') is the stylesheet itself, which is not available as a tree
                        throw new XPathException("FODC0002", "document('') is not supported");
                    }
                    result.Add(_LoadDocument(c, UriHelper.Resolve(baseUri, relative)).Root);
                }
                NodeSorting.SortAndDeduplicate(result);
                return XdmValue.FromNodes(result);
            }, null, "node()?");

            _Fn("key", 2, 3, ResultKind.Nodes, (c, a, f) => KeyLookup.Evaluate(c, _String(a[0]), a[1], a.Length > 2 ? (XdmNode)a[2].First : null, f), "xs:string", "xs:anyAtomicType*", "node()");

            _Fn("generate-id", 0, 1, ResultKind.String, (c, a, f) =>
            {
                XdmNode node = _NodeOrContext(c, a, 0);
                if (node == null)
                {
                    return XdmValue.EmptyString;
                }
                return XdmValue.Of("d" + node.Document.Id.ToString(CultureInfo.InvariantCulture) + "n" + node.Order.ToString(CultureInfo.InvariantCulture) + (node.Kind == XdmNodeKind.Namespace ? "ns" + node.LocalName : ""));
            }, "node()?");

            _Fn("system-property", 1, 1, ResultKind.String, (c, a, f) =>
            {
                string name = _String(a[0]);
                int colon = name.IndexOf(':');
                string local = colon >= 0 ? name.Substring(colon + 1) : name;
                switch (local)
                {
                    case "version":
                        return XdmValue.Of("2.0");
                    case "vendor":
                        return XdmValue.Of("envisia GmbH");
                    case "vendor-url":
                        return XdmValue.Of("https://github.com/schmitch/Schematron.Net");
                    case "product-name":
                        return XdmValue.Of("Schematron.Net.XPath");
                    case "product-version":
                        return XdmValue.Of(typeof(BuiltInFunctions).Assembly.GetName().Version.ToString());
                    case "is-schema-aware":
                    case "supports-serialization":
                    case "supports-backwards-compatibility":
                    case "supports-namespace-axis":
                    case "supports-streaming":
                    case "supports-dynamic-evaluation":
                        return XdmValue.Of("no");
                    case "xpath-version":
                        return XdmValue.Of("3.1");
                    case "xsd-version":
                        return XdmValue.Of("1.0");
                    default:
                        return XdmValue.EmptyString;
                }
            }, "xs:string");

            _Fn("function-available", 1, 2, ResultKind.Boolean, (c, a, f) =>
            {
                string name = _String(a[0]);
                int arity = a.Length > 1 ? (int)((IntegerValue)a[1][0]).Value : -1;
                string ns = XmlNamespaces.Fn;
                int colon = name.IndexOf(':');
                if (colon >= 0)
                {
                    if (!f.StaticContext.Namespaces.TryGetValue(name.Substring(0, colon), out ns))
                    {
                        throw new XPathException("XTDE1400", "Undeclared prefix in function name " + name);
                    }
                    name = name.Substring(colon + 1);
                }
                return XdmValue.Of(f.StaticContext.Functions.IsAvailable(ns, name, arity));
            }, "xs:string", "xs:integer");

            _Fn("element-available", 1, 1, ResultKind.Boolean, (c, a, f) => XdmValue.False, "xs:string");
            _Fn("type-available", 1, 1, ResultKind.Boolean, (c, a, f) =>
            {
                string name = _String(a[0]);
                int colon = name.IndexOf(':');
                return XdmValue.Of(colon >= 0 && XsType.FromLocalName(name.Substring(colon + 1)) != null);
            }, "xs:string");

            _Fn("format-number", 2, 3, ResultKind.String, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return XdmValue.Of(NumberPicture.Format(double.NaN, _String(a[1])));
                }
                NumericValue value = _ToNumeric((XdmAtomicValue)a[0][0], "format-number");
                return XdmValue.Of(NumberPicture.Format(value, _String(a[1])));
            }, "xs:anyAtomicType?", "xs:string", "xs:string?");
        }


        // --------------------------------------------------------------------------------------------------------
        // math
        // --------------------------------------------------------------------------------------------------------

        private static void _RegisterMath()
        {
            _Add(XmlNamespaces.Math, "pi", 0, 0, ResultKind.Numeric, (c, a, f) => XdmValue.Of(new DoubleValue(Math.PI)));
            _MathUnary("sqrt", Math.Sqrt);
            _MathUnary("exp", Math.Exp);
            _MathUnary("exp10", x => Math.Pow(10, x));
            _MathUnary("log", Math.Log);
            _MathUnary("log10", Math.Log10);
            _MathUnary("sin", Math.Sin);
            _MathUnary("cos", Math.Cos);
            _MathUnary("tan", Math.Tan);
            _MathUnary("asin", Math.Asin);
            _MathUnary("acos", Math.Acos);
            _MathUnary("atan", Math.Atan);
            _Add(XmlNamespaces.Math, "pow", 2, 2, ResultKind.Numeric, (c, a, f) => a[0].Count == 0 ? XdmValue.Empty : XdmValue.Of(new DoubleValue(Math.Pow(_Double(a[0]), _Double(a[1])))), "xs:double?", "xs:double");
            _Add(XmlNamespaces.Math, "atan2", 2, 2, ResultKind.Numeric, (c, a, f) => XdmValue.Of(new DoubleValue(Math.Atan2(_Double(a[0]), _Double(a[1])))), "xs:double", "xs:double");
        }


        private static void _MathUnary(string name, Func<double, double> function)
        {
            _Add(XmlNamespaces.Math, name, 1, 1, ResultKind.Numeric, (c, a, f) => a[0].Count == 0 ? XdmValue.Empty : XdmValue.Of(new DoubleValue(function(_Double(a[0])))), "xs:double?");
        }


        // --------------------------------------------------------------------------------------------------------
        // constructor functions xs:TYPE($arg)
        // --------------------------------------------------------------------------------------------------------

        private static void _RegisterConstructors()
        {
            string[] names =
            {
                "untypedAtomic", "string", "normalizedString", "token", "language", "NMTOKEN", "Name", "NCName", "ID", "IDREF", "ENTITY",
                "boolean", "decimal", "integer", "nonPositiveInteger", "negativeInteger", "long", "int", "short", "byte",
                "nonNegativeInteger", "unsignedLong", "unsignedInt", "unsignedShort", "unsignedByte", "positiveInteger",
                "double", "float", "duration", "dayTimeDuration", "yearMonthDuration", "dateTime", "dateTimeStamp", "date", "time",
                "gYearMonth", "gYear", "gMonthDay", "gDay", "gMonth", "hexBinary", "base64Binary", "anyURI"
            };
            foreach (string name in names)
            {
                XsType type = XsType.FromLocalName(name);
                ResultKind kind = type.IsNumeric ? ResultKind.Numeric : type == XsType.Boolean ? ResultKind.Boolean : type.Primitive == PrimitiveType.String ? ResultKind.String : ResultKind.Unknown;
                _Add(XmlNamespaces.Xs, name, 1, 1, kind, (c, a, f) =>
                {
                    if (a[0].Count == 0)
                    {
                        return XdmValue.Empty;
                    }
                    return XdmValue.Of(Casting.Cast((XdmAtomicValue)a[0][0], type));
                }, "xs:anyAtomicType?");
            }

            _Add(XmlNamespaces.Xs, "QName", 1, 1, ResultKind.Unknown, (c, a, f) =>
            {
                if (a[0].Count == 0)
                {
                    return XdmValue.Empty;
                }
                XdmAtomicValue value = (XdmAtomicValue)a[0][0];
                if (value is QNameValue)
                {
                    return XdmValue.Of(value);
                }
                string lexical = Casting.Collapse(value.StringValue);
                int colon = lexical.IndexOf(':');
                string prefix = colon < 0 ? "" : lexical.Substring(0, colon);
                string uri = "";
                if (prefix.Length > 0 && !f.StaticContext.Namespaces.TryGetValue(prefix, out uri))
                {
                    throw new XPathException("FONS0004", "No namespace binding for prefix " + prefix);
                }
                return XdmValue.Of(new QNameValue(prefix, uri, colon < 0 ? lexical : lexical.Substring(colon + 1)));
            }, "xs:anyAtomicType?");
        }
    }


    internal static class UriHelper
    {
        public static string Resolve(string baseUri, string relative)
        {
            if (String.IsNullOrEmpty(baseUri))
            {
                return relative;
            }
            Uri absolute;
            if (Uri.TryCreate(relative, UriKind.Absolute, out absolute) && !(absolute.IsFile && !relative.Contains(":")))
            {
                return absolute.OriginalString;
            }
            Uri baseResult;
            if (!Uri.TryCreate(baseUri, UriKind.Absolute, out baseResult))
            {
                return relative;
            }
            return new Uri(baseResult, relative).AbsoluteUri;
        }
    }


    /// <summary>
    /// The location paths of nodes, for fn:path().
    /// </summary>
    internal static class NodePaths
    {
        private static int _Position(XdmNode node)
        {
            if (node.Parent == null)
            {
                return 1;
            }
            int position = 1;
            XdmNode[] siblings = node.Parent.ChildNodes;
            for (int i = 0; i < node.Index; i++)
            {
                XdmNode sibling = siblings[i];
                if (sibling.Kind != node.Kind)
                {
                    continue;
                }
                if (node.Kind == XdmNodeKind.Element || node.Kind == XdmNodeKind.ProcessingInstruction)
                {
                    if (sibling.LocalName == node.LocalName && sibling.NamespaceUri == node.NamespaceUri)
                    {
                        position++;
                    }
                }
                else
                {
                    position++;
                }
            }
            return position;
        }


        /// <summary>
        /// The path as returned by fn:path().
        /// </summary>
        public static string XPathPath(XdmNode node)
        {
            List<string> segments = new List<string>();
            for (XdmNode n = node; n != null; n = n.Parent)
            {
                switch (n.Kind)
                {
                    case XdmNodeKind.Document:
                        break;
                    case XdmNodeKind.Element:
                        segments.Add("Q{" + n.NamespaceUri + "}" + n.LocalName + "[" + _Position(n) + "]");
                        break;
                    case XdmNodeKind.Attribute:
                        segments.Add(n.NamespaceUri.Length == 0 ? "@" + n.LocalName : "@Q{" + n.NamespaceUri + "}" + n.LocalName);
                        break;
                    case XdmNodeKind.Text:
                        segments.Add("text()[" + _Position(n) + "]");
                        break;
                    case XdmNodeKind.Comment:
                        segments.Add("comment()[" + _Position(n) + "]");
                        break;
                    case XdmNodeKind.ProcessingInstruction:
                        segments.Add("processing-instruction(" + n.LocalName + ")[" + _Position(n) + "]");
                        break;
                    case XdmNodeKind.Namespace:
                        segments.Add("namespace::" + (n.LocalName.Length == 0 ? "*[Q{" + XmlNamespaces.Fn + "}local-name()=\"\"]" : n.LocalName));
                        break;
                }
            }
            segments.Reverse();
            XdmNode root = node.Root;
            string prefix = root.Kind == XdmNodeKind.Document ? "/" : "Q{" + XmlNamespaces.Fn + "}root()/";
            return prefix + String.Join("/", segments);
        }
    }
}
