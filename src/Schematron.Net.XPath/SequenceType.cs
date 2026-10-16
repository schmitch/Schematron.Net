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
    /// A node test of an axis step or a kind test of a sequence type.
    /// </summary>
    internal abstract class NodeTest
    {
        public abstract bool Matches(XdmNode node);


        /// <summary>
        /// The element / attribute name the test is restricted to, if any (used for index lookups).
        /// </summary>
        public virtual bool TryGetExactName(out string namespaceUri, out string localName)
        {
            namespaceUri = null;
            localName = null;
            return false;
        }
    }


    /// <summary>
    /// A name test (QName or wildcard) that matches nodes of the principal node kind of the axis.
    /// </summary>
    internal sealed class NameTest : NodeTest
    {
        /// <summary>Null matches any namespace.</summary>
        public readonly string NamespaceUri;

        /// <summary>Null matches any local name.</summary>
        public readonly string LocalName;
        public readonly XdmNodeKind PrincipalKind;


        public NameTest(string namespaceUri, string localName, XdmNodeKind principalKind)
        {
            NamespaceUri = namespaceUri;
            LocalName = localName;
            PrincipalKind = principalKind;
        }


        public override bool Matches(XdmNode node)
        {
            return node.Kind == PrincipalKind
                   && (LocalName == null || String.Equals(node.LocalName, LocalName, StringComparison.Ordinal))
                   && (NamespaceUri == null || String.Equals(node.NamespaceUri, NamespaceUri, StringComparison.Ordinal));
        }


        public override bool TryGetExactName(out string namespaceUri, out string localName)
        {
            namespaceUri = NamespaceUri;
            localName = LocalName;
            return NamespaceUri != null && LocalName != null;
        }


        /// <inheritdoc />
        public override string ToString()
        {
            return (NamespaceUri == null ? "*" : "Q{" + NamespaceUri + "}") + (LocalName ?? "*");
        }
    }


    /// <summary>
    /// node(), text(), comment(), processing-instruction(), element(), attribute(), document-node().
    /// </summary>
    internal sealed class KindTest : NodeTest
    {
        /// <summary>Null for node().</summary>
        public readonly XdmNodeKind? Kind;

        /// <summary>Optional name restriction of element(), attribute() and processing-instruction().</summary>
        public readonly NameTest Name;

        /// <summary>For document-node(element(...)).</summary>
        public readonly KindTest DocumentElement;


        public KindTest(XdmNodeKind? kind, NameTest name = null, KindTest documentElement = null)
        {
            Kind = kind;
            Name = name;
            DocumentElement = documentElement;
        }


        public static readonly KindTest AnyNode = new KindTest(null);


        public override bool Matches(XdmNode node)
        {
            if (Kind.HasValue && node.Kind != Kind.Value)
            {
                return false;
            }
            if (Name != null && !Name.Matches(node))
            {
                return false;
            }
            if (DocumentElement != null)
            {
                XdmNode element = null;
                foreach (XdmNode child in node.ChildNodes)
                {
                    if (child.Kind == XdmNodeKind.Element)
                    {
                        if (element != null)
                        {
                            return false;
                        }
                        element = child;
                    }
                    else if (child.Kind == XdmNodeKind.Text)
                    {
                        return false;
                    }
                }
                return element != null && DocumentElement.Matches(element);
            }
            return true;
        }


        public override bool TryGetExactName(out string namespaceUri, out string localName)
        {
            if (Name != null)
            {
                return Name.TryGetExactName(out namespaceUri, out localName);
            }
            return base.TryGetExactName(out namespaceUri, out localName);
        }
    }


    internal enum Occurrence
    {
        ExactlyOne,
        ZeroOrOne,
        ZeroOrMore,
        OneOrMore
    }


    /// <summary>
    /// The item type of a sequence type.
    /// </summary>
    internal abstract class ItemType
    {
        public abstract bool Matches(XdmItem item);
    }


    internal sealed class AnyItemType : ItemType
    {
        public static readonly AnyItemType Instance = new AnyItemType();


        public override bool Matches(XdmItem item)
        {
            return true;
        }


        /// <inheritdoc />
        public override string ToString()
        {
            return "item()";
        }
    }


    internal sealed class AtomicItemType : ItemType
    {
        public readonly XsType Type;


        public AtomicItemType(XsType type)
        {
            Type = type;
        }


        public override bool Matches(XdmItem item)
        {
            return item is XdmAtomicValue atomic && atomic.Type.IsSubtypeOf(Type);
        }


        /// <inheritdoc />
        public override string ToString()
        {
            return Type.QualifiedName;
        }
    }


    internal sealed class NodeItemType : ItemType
    {
        public readonly KindTest Test;


        public NodeItemType(KindTest test)
        {
            Test = test;
        }


        public override bool Matches(XdmItem item)
        {
            return item is XdmNode node && Test.Matches(node);
        }


        /// <inheritdoc />
        public override string ToString()
        {
            return Test.Kind.HasValue ? Test.Kind.Value.ToString().ToLowerInvariant() + "()" : "node()";
        }
    }


    /// <summary>
    /// A sequence type (item type with occurrence indicator, or empty-sequence()), e.g. the type in the as
    /// attribute of a variable.
    /// </summary>
    public sealed class XPathSequenceType
    {
        /// <summary>
        /// Parses a sequence type, e.g. xs:decimal? or element()*.
        /// </summary>
        /// <exception cref="XPathException">The type is invalid or unknown</exception>
        public static XPathSequenceType Parse(string text, XPathStaticContext staticContext = null)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            return XPathParser.ParseSequenceType(text, staticContext ?? new XPathStaticContext());
        }



        internal readonly ItemType ItemType;
        internal readonly Occurrence Occurrence;

        /// <summary>True for empty-sequence().</summary>
        internal readonly bool IsEmptySequence;

        internal static readonly XPathSequenceType AnySequence = new XPathSequenceType(AnyItemType.Instance, Occurrence.ZeroOrMore);
        internal static readonly XPathSequenceType Empty = new XPathSequenceType(null, Occurrence.ZeroOrOne, true);


        internal XPathSequenceType(ItemType itemType, Occurrence occurrence, bool isEmptySequence = false)
        {
            ItemType = itemType;
            Occurrence = occurrence;
            IsEmptySequence = isEmptySequence;
        }


        internal bool AllowsEmpty => IsEmptySequence || Occurrence == Occurrence.ZeroOrOne || Occurrence == Occurrence.ZeroOrMore;
        internal bool AllowsMany => Occurrence == Occurrence.ZeroOrMore || Occurrence == Occurrence.OneOrMore;


        /// <summary>
        /// Tests whether a value matches the type (without conversion).
        /// </summary>
        public bool Matches(XdmValue sequence)
        {
            if (IsEmptySequence)
            {
                return sequence.Count == 0;
            }
            if (sequence.Count == 0)
            {
                return AllowsEmpty;
            }
            if (sequence.Count > 1 && !AllowsMany)
            {
                return false;
            }
            foreach (XdmItem item in sequence)
            {
                if (!ItemType.Matches(item))
                {
                    return false;
                }
            }
            return true;
        }


        /// <summary>
        /// Applies the function conversion rules (atomization, casting of untyped values, numeric promotion) and
        /// checks the result against this type.
        /// </summary>
        /// <summary>
        /// Converts a value to the type with the function conversion rules (atomization, casting of untyped values,
        /// numeric promotion).
        /// </summary>
        /// <param name="value">The value</param>
        /// <param name="role">What the value is, for the error message (e.g. "the value of variable $x")</param>
        /// <exception cref="XPathException">XPTY0004: the value cannot be converted</exception>
        public XdmValue Convert(XdmValue value, string role)
        {
            if (!IsEmptySequence && ItemType is AtomicItemType atomicType)
            {
                XdmValue atomized = Operations.Atomize(value);
                List<XdmItem> converted = null;
                for (int i = 0; i < atomized.Count; i++)
                {
                    XdmAtomicValue a = (XdmAtomicValue)atomized[i];
                    XdmAtomicValue c = _ConvertAtomic(a, atomicType.Type);
                    if (c != a)
                    {
                        if (converted == null)
                        {
                            converted = new List<XdmItem>(atomized);
                        }
                        converted[i] = c;
                    }
                }
                value = converted != null ? XdmValue.FromList(converted) : atomized;
            }
            if (!Matches(value))
            {
                if (!AllowsMany && value.Count > 1)
                {
                    throw new XPathException("XPTY0004", "A sequence of more than one item is not allowed as " + role + " " + Operations._DescribeSequence(value));
                }
                if (!AllowsEmpty && value.Count == 0)
                {
                    throw new XPathException("XPTY0004", "An empty sequence is not allowed as " + role);
                }
                throw new XPathException("XPTY0004", "Required item type of " + role + " is " + this + ", supplied value " + Operations._DescribeSequence(value) + " does not match");
            }
            return value;
        }


        private static XdmAtomicValue _ConvertAtomic(XdmAtomicValue value, XsType expected)
        {
            if (value.Type.IsSubtypeOf(expected))
            {
                return value;
            }
            if (value.Type == XsType.UntypedAtomic)
            {
                return Casting.Cast(value, expected == XsType.AnyAtomicType ? XsType.UntypedAtomic : expected);
            }
            // numeric promotion
            if (value is NumericValue numeric)
            {
                if (expected == XsType.Double)
                {
                    return new DoubleValue(numeric.ToDouble());
                }
                if (expected == XsType.Float && numeric.Rank <= 1)
                {
                    return new FloatValue((float)numeric.ToDouble());
                }
            }
            // URI promotion
            if (value.Type == XsType.AnyUri && expected == XsType.String)
            {
                return new StringAtomic(value.StringValue);
            }
            return value;
        }


        /// <inheritdoc />
        public override string ToString()
        {
            if (IsEmptySequence)
            {
                return "empty-sequence()";
            }
            string suffix = Occurrence == Occurrence.ExactlyOne ? "" : Occurrence == Occurrence.ZeroOrOne ? "?" : Occurrence == Occurrence.ZeroOrMore ? "*" : "+";
            return ItemType + suffix;
        }
    }
}
