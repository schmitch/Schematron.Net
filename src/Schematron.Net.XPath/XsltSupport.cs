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
using System.Text;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// An XSLT instruction of the body of an xsl:function (the subset that is used in Schematron files:
    /// xsl:variable, xsl:sequence, xsl:value-of, xsl:choose, xsl:if and literal text).
    /// </summary>
    internal abstract class XslInstruction
    {
        public abstract XdmValue Evaluate(EvalContext context);
    }


    internal sealed class XslSequence : XslInstruction
    {
        private readonly Expr _Select;


        public XslSequence(Expr select)
        {
            _Select = select;
        }


        public override XdmValue Evaluate(EvalContext context)
        {
            return _Select.Evaluate(context);
        }
    }


    internal sealed class XslValueOf : XslInstruction
    {
        private readonly Expr _Select;
        private readonly XslInstruction _Content;
        private readonly string _Separator;


        public XslValueOf(Expr select, XslInstruction content, string separator)
        {
            _Select = select;
            _Content = content;
            _Separator = separator;
        }


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmValue value = _Select != null ? _Select.Evaluate(context) : _Content.Evaluate(context);
            string separator = _Separator ?? (_Select != null ? " " : "");
            string text = String.Join(separator, Operations.Atomize(value).Select(i => ((XdmAtomicValue)i).StringValue));
            return XdmValue.Of(XdmDocumentBuilder.CreateTextNode(text));
        }
    }


    internal sealed class XslText : XslInstruction
    {
        private readonly string _Text;


        public XslText(string text)
        {
            _Text = text;
        }


        public override XdmValue Evaluate(EvalContext context)
        {
            return XdmValue.Of(XdmDocumentBuilder.CreateTextNode(_Text));
        }
    }


    internal sealed class XslVariable : XslInstruction
    {
        private readonly int _Slot;
        private readonly Expr _Select;
        private readonly XslInstruction _Content;
        private readonly XPathSequenceType _As;
        private readonly string _Name;


        public XslVariable(string name, int slot, Expr select, XslInstruction content, XPathSequenceType asType)
        {
            _Name = name;
            _Slot = slot;
            _Select = select;
            _Content = content;
            _As = asType;
        }


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmValue value;
            if (_Select != null)
            {
                value = _Select.Evaluate(context);
            }
            else if (_Content == null)
            {
                value = _As != null ? XdmValue.Empty : XdmValue.EmptyString;
            }
            else if (_As != null)
            {
                value = _Content.Evaluate(context);
            }
            else
            {
                // a variable with content and without "as" is a temporary tree
                StringBuilder sb = new StringBuilder();
                foreach (XdmItem item in _Content.Evaluate(context))
                {
                    sb.Append(item is XdmNode node ? node.StringValue : ((XdmAtomicValue)item).StringValue);
                }
                value = XdmValue.Of(XdmDocumentBuilder.CreateDocumentWithText(sb.ToString()));
            }
            if (_As != null)
            {
                value = _As.Convert(value, "the value of variable $" + _Name);
            }
            context.Locals[_Slot] = value;
            return XdmValue.Empty;
        }
    }


    internal sealed class XslChoose : XslInstruction
    {
        private readonly List<KeyValuePair<Expr, XslInstruction>> _Whens;
        private readonly XslInstruction _Otherwise;


        public XslChoose(List<KeyValuePair<Expr, XslInstruction>> whens, XslInstruction otherwise)
        {
            _Whens = whens;
            _Otherwise = otherwise;
        }


        public override XdmValue Evaluate(EvalContext context)
        {
            foreach (KeyValuePair<Expr, XslInstruction> when in _Whens)
            {
                if (when.Key.EffectiveBooleanValue(context))
                {
                    return when.Value.Evaluate(context);
                }
            }
            return _Otherwise != null ? _Otherwise.Evaluate(context) : XdmValue.Empty;
        }
    }


    internal sealed class XslIf : XslInstruction
    {
        private readonly Expr _Test;
        private readonly XslInstruction _Body;


        public XslIf(Expr test, XslInstruction body)
        {
            _Test = test;
            _Body = body;
        }


        public override XdmValue Evaluate(EvalContext context)
        {
            return _Test.EffectiveBooleanValue(context) ? _Body.Evaluate(context) : XdmValue.Empty;
        }
    }


    /// <summary>
    /// A sequence constructor: the concatenation of the results of its instructions.
    /// </summary>
    internal sealed class XslBlock : XslInstruction
    {
        private readonly XslInstruction[] _Instructions;


        public XslBlock(XslInstruction[] instructions)
        {
            _Instructions = instructions;
        }


        public override XdmValue Evaluate(EvalContext context)
        {
            if (_Instructions.Length == 1)
            {
                return _Instructions[0].Evaluate(context);
            }
            List<XdmItem> result = new List<XdmItem>();
            foreach (XslInstruction instruction in _Instructions)
            {
                result.AddRange(instruction.Evaluate(context));
            }
            return XdmValue.FromList(result);
        }
    }


    /// <summary>
    /// The key() function: builds an index of the nodes matched by an xsl:key per document on first use.
    /// </summary>
    internal static class KeyLookup
    {
        public static XdmValue Evaluate(EvalContext context, string name, XdmValue values, XdmNode top, FunctionCallExpr call)
        {
            string expanded = name;
            int colon = name.IndexOf(':');
            if (colon >= 0)
            {
                string ns;
                if (!call.StaticContext.Namespaces.TryGetValue(name.Substring(0, colon), out ns))
                {
                    throw new XPathException("XTDE1260", "Undeclared prefix in key name " + name);
                }
                expanded = "{" + ns + "}" + name.Substring(colon + 1);
            }
            XslKey key;
            if (context.Environment.Keys == null || !context.Environment.Keys.TryGetValue(expanded, out key))
            {
                throw new XPathException("XTDE1260", "No key named " + name + " has been declared");
            }
            XdmNode origin = top ?? context.ContextNode;
            XdmNode root = origin.Root;
            string indexKey = root.Document.Id.ToString(CultureInfo.InvariantCulture) + "|" + expanded;
            Dictionary<string, List<XdmNode>> index;
            if (!context.Environment.KeyIndexes.TryGetValue(indexKey, out index))
            {
                index = _Build(context, key, root);
                context.Environment.KeyIndexes[indexKey] = index;
            }
            List<XdmNode> result = new List<XdmNode>();
            foreach (XdmItem value in values)
            {
                List<XdmNode> nodes;
                if (index.TryGetValue(((XdmAtomicValue)value).StringValue, out nodes))
                {
                    result.AddRange(nodes);
                }
            }
            if (top != null)
            {
                result = result.Where(n => n == top || top.IsAncestorOf(n)).ToList();
            }
            NodeSorting.SortAndDeduplicate(result);
            return XdmValue.FromNodes(result);
        }


        private static Dictionary<string, List<XdmNode>> _Build(EvalContext context, XslKey key, XdmNode root)
        {
            Dictionary<string, List<XdmNode>> index = new Dictionary<string, List<XdmNode>>();
            EvalContext keyContext = new EvalContext(context.Environment) { Variables = context.Environment.GlobalVariables };
            foreach (XdmItem item in key.Match.Evaluate(keyContext, root))
            {
                XdmNode node = (XdmNode)item;
                foreach (XdmItem value in Operations.Atomize(key.Use.Evaluate(keyContext, node)))
                {
                    string s = ((XdmAtomicValue)value).StringValue;
                    List<XdmNode> list;
                    if (!index.TryGetValue(s, out list))
                    {
                        list = new List<XdmNode>();
                        index[s] = list;
                    }
                    list.Add(node);
                }
            }
            return index;
        }
    }


    /// <summary>
    /// format-number() with the default decimal format.
    /// </summary>
    internal static class NumberPicture
    {
        public static string Format(NumericValue value, string picture)
        {
            if (value.IsNaN)
            {
                return "NaN";
            }
            if (value is DoubleValue || value is FloatValue)
            {
                return Format(value.ToDouble(), picture);
            }
            return _Format(Operations.ToDecimal(value), picture);
        }


        public static string Format(double value, string picture)
        {
            if (double.IsNaN(value))
            {
                return "NaN";
            }
            string[] parts = picture.Split(';');
            if (double.IsInfinity(value))
            {
                string sub = value < 0 && parts.Length > 1 ? parts[1] : parts[0];
                _Split(sub, out string prefix, out string mantissa, out string suffix);
                return (value < 0 && parts.Length == 1 ? "-" : "") + prefix + "Infinity" + suffix;
            }
            return _Format(NumberFormatting.DoubleToDecimal(value), picture);
        }


        private static void _Split(string picture, out string prefix, out string mantissa, out string suffix)
        {
            int first = -1;
            int last = -1;
            for (int i = 0; i < picture.Length; i++)
            {
                char ch = picture[i];
                if (Char.IsDigit(ch) || ch == '#' || ch == '.' || ch == ',')
                {
                    if (first < 0)
                    {
                        first = i;
                    }
                    last = i;
                }
            }
            if (first < 0)
            {
                prefix = picture;
                mantissa = "";
                suffix = "";
                return;
            }
            prefix = picture.Substring(0, first);
            mantissa = picture.Substring(first, last - first + 1);
            suffix = picture.Substring(last + 1);
        }


        private static string _Format(decimal value, string picture)
        {
            string[] parts = picture.Split(';');
            bool negative = value < 0;
            string sub = negative && parts.Length > 1 ? parts[1] : parts[0];
            _Split(sub, out string prefix, out string mantissa, out string suffix);
            decimal abs = Math.Abs(value);
            if ((prefix + suffix).Contains("%"))
            {
                abs *= 100;
            }
            else if ((prefix + suffix).Contains("‰"))
            {
                abs *= 1000;
            }
            int point = mantissa.IndexOf('.');
            string integerPart = point >= 0 ? mantissa.Substring(0, point) : mantissa;
            string fractionPart = point >= 0 ? mantissa.Substring(point + 1) : "";
            int minInteger = integerPart.Count(Char.IsDigit);
            int lastComma = integerPart.LastIndexOf(',');
            int grouping = lastComma >= 0 ? integerPart.Length - lastComma - 1 : 0;
            int minFraction = fractionPart.Count(Char.IsDigit);
            int maxFraction = minFraction + fractionPart.Count(ch => ch == '#');

            decimal rounded = Math.Round(abs, Math.Min(maxFraction, 28), MidpointRounding.ToEven);
            string digits = rounded.ToString("F" + maxFraction, CultureInfo.InvariantCulture);
            string intDigits = maxFraction > 0 ? digits.Substring(0, digits.IndexOf('.')) : digits;
            string fracDigits = maxFraction > 0 ? digits.Substring(digits.IndexOf('.') + 1) : "";
            while (fracDigits.Length > minFraction && fracDigits.EndsWith("0", StringComparison.Ordinal))
            {
                fracDigits = fracDigits.Substring(0, fracDigits.Length - 1);
            }
            intDigits = intDigits.TrimStart('0');
            if (intDigits.Length < minInteger)
            {
                intDigits = new string('0', minInteger - intDigits.Length) + intDigits;
            }
            if (intDigits.Length == 0 && fracDigits.Length == 0)
            {
                intDigits = "0";
            }
            if (grouping > 0 && intDigits.Length > grouping)
            {
                StringBuilder grouped = new StringBuilder();
                for (int i = 0; i < intDigits.Length; i++)
                {
                    if (i > 0 && (intDigits.Length - i) % grouping == 0)
                    {
                        grouped.Append(',');
                    }
                    grouped.Append(intDigits[i]);
                }
                intDigits = grouped.ToString();
            }
            string result = prefix + intDigits + (fracDigits.Length > 0 ? "." + fracDigits : "") + suffix;
            if (negative && parts.Length == 1 && rounded != 0)
            {
                result = "-" + result;
            }
            return result;
        }
    }

    /// <summary>
    /// Compiles xsl:function declarations: first all signatures (so the functions can call each other), then the
    /// bodies.
    /// </summary>
    internal static class XslFunctionCompiler
    {
        public static void Declare(XPathFunctionLibrary library, List<XdmNode> functions, Func<XdmNode, XPathStaticContext> staticContextFor)
        {
            List<KeyValuePair<UserFunction, KeyValuePair<XdmNode, CompileScope>>> pending = new List<KeyValuePair<UserFunction, KeyValuePair<XdmNode, CompileScope>>>();
            foreach (XdmNode node in functions)
            {
                if (!_IsXsl(node, "function"))
                {
                    throw new XPathException("XTSE0010", "Expected xsl:function, found " + node.Name);
                }
                XPathStaticContext context = staticContextFor(node);
                string name = node.GetAttribute("name");
                int colon = name == null ? -1 : name.IndexOf(':');
                if (colon < 0)
                {
                    throw new XPathException("XTSE0740", "xsl:function requires a prefixed name, found '" + name + "'");
                }
                CompileScope scope = new CompileScope();
                List<XPathSequenceType> types = new List<XPathSequenceType>();
                List<int> slots = new List<int>();
                foreach (XdmNode param in node.ChildNodes.Where(e => _IsXsl(e, "param")))
                {
                    string asType = param.GetAttribute("as");
                    types.Add(asType != null ? XPathParser.ParseSequenceType(asType, context) : null);
                    slots.Add(scope.Declare(context.ExpandName(param.GetAttribute("name"))));
                }
                string resultType = node.GetAttribute("as");
                UserFunction function = new UserFunction
                {
                    NamespaceUri = context.ResolvePrefix(name.Substring(0, colon)),
                    LocalName = name.Substring(colon + 1),
                    ParameterTypes = types.ToArray(),
                    ParameterSlots = slots.ToArray(),
                    ResultType = resultType != null ? XPathParser.ParseSequenceType(resultType, context) : null
                };
                library.Add(function);
                pending.Add(new KeyValuePair<UserFunction, KeyValuePair<XdmNode, CompileScope>>(function, new KeyValuePair<XdmNode, CompileScope>(node, scope)));
            }
            foreach (KeyValuePair<UserFunction, KeyValuePair<XdmNode, CompileScope>> entry in pending)
            {
                XdmNode node = entry.Value.Key;
                CompileScope scope = entry.Value.Value;
                try
                {
                    entry.Key.Body = _CompileSequenceConstructor(node.ChildNodes.Where(c => !_IsXsl(c, "param")), staticContextFor(node), scope);
                }
                catch (XPathException e)
                {
                    throw new XPathException(e.Code, "Invalid expression in xsl:function " + node.GetAttribute("name") + ": " + e.Message, e);
                }
                entry.Key.SlotCount = scope.SlotCount;
            }
        }


        private static bool _IsXsl(XdmNode node, string localName)
        {
            return node.Kind == XdmNodeKind.Element && node.LocalName == localName && node.NamespaceUri == XmlNamespaces.Xsl;
        }


        private static XslInstruction _CompileSequenceConstructor(IEnumerable<XdmNode> nodes, XPathStaticContext context, CompileScope scope)
        {
            List<XslInstruction> instructions = new List<XslInstruction>();
            int declared = 0;
            foreach (XdmNode node in nodes)
            {
                if (node.Kind == XdmNodeKind.Text)
                {
                    if (!String.IsNullOrWhiteSpace(node.Value))
                    {
                        instructions.Add(new XslText(node.Value));
                    }
                    continue;
                }
                if (node.Kind != XdmNodeKind.Element)
                {
                    continue;
                }
                if (node.NamespaceUri != XmlNamespaces.Xsl)
                {
                    throw new XPathException("XTSE0010", "Literal result elements are not supported in xsl:function (" + node.Name + ")");
                }
                switch (node.LocalName)
                {
                    case "variable":
                        {
                            string select = node.GetAttribute("select");
                            string asType = node.GetAttribute("as");
                            Expr selectExpr = select != null ? XPathParser.Parse(select, context, scope) : null;
                            XslInstruction content = select == null && node.ChildNodes.Length > 0 ? _CompileSequenceConstructor(node.ChildNodes, context, scope) : null;
                            string name = context.ExpandName(node.GetAttribute("name"));
                            int slot = scope.Declare(name);
                            declared++;
                            instructions.Add(new XslVariable(name, slot, selectExpr, content, asType != null ? XPathParser.ParseSequenceType(asType, context) : null));
                            break;
                        }
                    case "sequence":
                        instructions.Add(new XslSequence(XPathParser.Parse(node.GetAttribute("select") ?? "()", context, scope)));
                        break;
                    case "value-of":
                        {
                            string select = node.GetAttribute("select");
                            instructions.Add(new XslValueOf(select != null ? XPathParser.Parse(select, context, scope) : null,
                                select == null ? _CompileSequenceConstructor(node.ChildNodes, context, scope) : null,
                                node.GetAttribute("separator")));
                            break;
                        }
                    case "text":
                        instructions.Add(new XslText(node.StringValue));
                        break;
                    case "if":
                        instructions.Add(new XslIf(XPathParser.Parse(node.GetAttribute("test"), context, scope), _CompileSequenceConstructor(node.ChildNodes, context, scope)));
                        break;
                    case "choose":
                        {
                            List<KeyValuePair<Expr, XslInstruction>> whens = new List<KeyValuePair<Expr, XslInstruction>>();
                            XslInstruction otherwise = null;
                            foreach (XdmNode branch in node.ChildNodes.Where(c => c.Kind == XdmNodeKind.Element))
                            {
                                if (_IsXsl(branch, "when"))
                                {
                                    whens.Add(new KeyValuePair<Expr, XslInstruction>(XPathParser.Parse(branch.GetAttribute("test"), context, scope), _CompileSequenceConstructor(branch.ChildNodes, context, scope)));
                                }
                                else if (_IsXsl(branch, "otherwise"))
                                {
                                    otherwise = _CompileSequenceConstructor(branch.ChildNodes, context, scope);
                                }
                            }
                            instructions.Add(new XslChoose(whens, otherwise));
                            break;
                        }
                    default:
                        throw new XPathException("XTSE0010", "The XSLT instruction xsl:" + node.LocalName + " is not supported in xsl:function");
                }
            }
            scope.Undeclare(declared);
            return new XslBlock(instructions.ToArray());
        }
    }
}
