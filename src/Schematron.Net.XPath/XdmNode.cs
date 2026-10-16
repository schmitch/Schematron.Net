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
using System.IO;
using System.Text;
using System.Xml;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// Base class of all items of the XPath data model (nodes and atomic values).
    /// </summary>
    public abstract class XdmItem
    {
        private protected XdmItem()
        {
        }


        /// <summary>
        /// The string value: dm:string-value of a node, the canonical lexical representation of an atomic value.
        /// </summary>
        public abstract string StringValue { get; }
    }


    /// <summary>The kind of a node.</summary>
    public enum XdmNodeKind : byte
    {
        /// <summary>A document node, the root of a parsed document.</summary>
        Document,
        /// <summary>An element.</summary>
        Element,
        /// <summary>An attribute.</summary>
        Attribute,
        /// <summary>A text node (adjacent text and CDATA sections are one node).</summary>
        Text,
        /// <summary>A comment.</summary>
        Comment,
        /// <summary>A processing instruction.</summary>
        ProcessingInstruction,
        /// <summary>A namespace node (namespace axis).</summary>
        Namespace
    }


    /// <summary>
    /// A node of an immutable XML tree as defined by the XPath 2.0 data model.
    ///
    /// Every node knows its position in document order (<see cref="Order"/>) and the order of the last
    /// node in its subtree (<see cref="EndOrder"/>), so document order comparisons and descendant tests
    /// are integer comparisons.
    /// </summary>
    public sealed class XdmNode : XdmItem
    {
        internal static readonly XdmNode[] NoNodes = new XdmNode[0];

        /// <summary>Content of text, comment, processing instruction, attribute and namespace nodes.</summary>
        internal string Value;
        internal XdmNode[] ChildNodes = NoNodes;
        internal XdmNode[] AttributeNodes = NoNodes;

        /// <summary>Namespace declarations (prefix, uri) of an element, empty prefix for the default namespace.</summary>
        internal KeyValuePair<string, string>[] NamespaceDeclarations;
        internal int Order;
        internal int EndOrder;

        /// <summary>Index of the node in the children (or attributes) of its parent.</summary>
        internal int Index;
        private string _StringValue;


        internal XdmNode(XdmNodeKind kind, XdmDocument document)
        {
            Kind = kind;
            Document = document;
        }


        /// <summary>The kind of the node.</summary>
        public XdmNodeKind Kind { get; }

        /// <summary>The tree the node belongs to.</summary>
        public XdmDocument Document { get; }

        /// <summary>The parent (null for the root); the parent of an attribute is its element.</summary>
        public XdmNode Parent { get; internal set; }

        /// <summary>The local name of an element or attribute, the target of a processing instruction, the prefix of a namespace node, "" otherwise.</summary>
        public string LocalName { get; internal set; } = "";

        /// <summary>The namespace URI of an element or attribute, "" if it has none.</summary>
        public string NamespaceUri { get; internal set; } = "";

        /// <summary>The prefix of an element or attribute in the source document, "" if it has none.</summary>
        public string Prefix { get; internal set; } = "";

        /// <summary>The children in document order: elements, text, comments and processing instructions.</summary>
        public IReadOnlyList<XdmNode> Children => ChildNodes;

        /// <summary>The attributes of an element.</summary>
        public IReadOnlyList<XdmNode> Attributes => AttributeNodes;

        /// <summary>The line in the source document, 0 if unknown.</summary>
        public int LineNumber { get; internal set; }

        /// <summary>The position in the line of the source document, 0 if unknown.</summary>
        public int LinePosition { get; internal set; }


        internal bool IsElement => Kind == XdmNodeKind.Element;


        /// <summary>
        /// The descendants of the node in document order, without attributes.
        /// </summary>
        public IEnumerable<XdmNode> Descendants()
        {
            foreach (XdmNode child in ChildNodes)
            {
                yield return child;
                foreach (XdmNode descendant in child.Descendants())
                {
                    yield return descendant;
                }
            }
        }


        /// <summary>
        /// The string value of the node (dm:string-value).
        /// </summary>
        public override string StringValue
        {
            get
            {
                switch (Kind)
                {
                    case XdmNodeKind.Element:
                    case XdmNodeKind.Document:
                        if (_StringValue == null)
                        {
                            _StringValue = _ComputeStringValue();
                        }
                        return _StringValue;
                    default:
                        return Value ?? "";
                }
            }
        }


        private string _ComputeStringValue()
        {
            if (ChildNodes.Length == 0)
            {
                return "";
            }
            if (ChildNodes.Length == 1 && ChildNodes[0].Kind == XdmNodeKind.Text)
            {
                return ChildNodes[0].Value;
            }
            StringBuilder sb = new StringBuilder();
            _AppendText(this, sb);
            return sb.ToString();
        }


        private static void _AppendText(XdmNode node, StringBuilder sb)
        {
            foreach (XdmNode child in node.ChildNodes)
            {
                if (child.Kind == XdmNodeKind.Text)
                {
                    sb.Append(child.Value);
                }
                else if (child.Kind == XdmNodeKind.Element)
                {
                    _AppendText(child, sb);
                }
            }
        }


        /// <summary>
        /// The lexical QName of the node, as returned by fn:name().
        /// </summary>
        public string Name
        {
            get
            {
                switch (Kind)
                {
                    case XdmNodeKind.Element:
                    case XdmNodeKind.Attribute:
                        return String.IsNullOrEmpty(Prefix) ? LocalName : Prefix + ":" + LocalName;
                    case XdmNodeKind.ProcessingInstruction:
                    case XdmNodeKind.Namespace:
                        return LocalName;
                    default:
                        return "";
                }
            }
        }


        /// <summary>
        /// The root of the tree that contains this node.
        /// </summary>
        public XdmNode Root
        {
            get
            {
                XdmNode node = this;
                while (node.Parent != null)
                {
                    node = node.Parent;
                }
                return node;
            }
        }


        /// <summary>
        /// True if the other node is a descendant (or an attribute of a descendant or of this element).
        /// </summary>
        public bool IsAncestorOf(XdmNode other)
        {
            return other.Document == Document && other.Order > Order && other.Order <= EndOrder;
        }


        /// <summary>
        /// Compares two nodes in document order. Nodes of different trees are ordered by the creation of their trees.
        /// </summary>
        public static int CompareOrder(XdmNode a, XdmNode b)
        {
            if (a.Document != b.Document)
            {
                return a.Document.Id.CompareTo(b.Document.Id);
            }
            return a.Order.CompareTo(b.Order);
        }


        /// <summary>
        /// Returns the in-scope namespaces of an element (prefix → uri), including the xml prefix.
        /// </summary>
        public Dictionary<string, string> GetInScopeNamespaces()
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            List<XdmNode> chain = new List<XdmNode>();
            for (XdmNode node = this; node != null; node = node.Parent)
            {
                if (node.Kind == XdmNodeKind.Element)
                {
                    chain.Add(node);
                }
            }
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                KeyValuePair<string, string>[] declarations = chain[i].NamespaceDeclarations;
                if (declarations == null)
                {
                    continue;
                }
                foreach (KeyValuePair<string, string> declaration in declarations)
                {
                    if (String.IsNullOrEmpty(declaration.Value))
                    {
                        result.Remove(declaration.Key);
                    }
                    else
                    {
                        result[declaration.Key] = declaration.Value;
                    }
                }
            }
            result["xml"] = XmlNamespaces.Xml;
            return result;
        }


        /// <summary>
        /// Returns the value of the attribute with the given name or null.
        /// </summary>
        public string GetAttribute(string localName, string namespaceUri = "")
        {
            foreach (XdmNode attribute in AttributeNodes)
            {
                if (attribute.LocalName == localName && attribute.NamespaceUri == namespaceUri)
                {
                    return attribute.Value;
                }
            }
            return null;
        }


        /// <inheritdoc />
        public override string ToString()
        {
            switch (Kind)
            {
                case XdmNodeKind.Document:
                    return "document-node()";
                case XdmNodeKind.Element:
                    return "<" + Name + ">";
                case XdmNodeKind.Attribute:
                    return "@" + Name + "=\"" + Value + "\"";
                case XdmNodeKind.Text:
                    return "text(\"" + Value + "\")";
                default:
                    return Kind.ToString();
            }
        }
    }


    /// <summary>
    /// A parsed XML document, ready to be queried with <see cref="XPathExpression"/>s (or validated with Schematron schemas).
    /// Parse a document once to run several schemas or queries on it. Instances are immutable and thread-safe.
    /// </summary>
    /// <remarks>
    /// Internally a tree of nodes with its document node, document URI and an index of element and attribute names
    /// that speeds up the descendant axis.
    /// </remarks>
    public sealed class XdmDocument
    {
        private static int _NextId;
        private Dictionary<NameKey, List<XdmNode>> _ElementIndex;
        private Dictionary<NameKey, List<XdmNode>> _AttributeIndex;
        private readonly object _IndexLock = new object();

        internal readonly int Id;

        /// <summary>
        /// The document node (or, for parentless constructed nodes, the root node) of the tree.
        /// </summary>
        public XdmNode Root { get; internal set; }

        /// <summary>
        /// All nodes of the tree, indexed by <see cref="XdmNode.Order"/>.
        /// </summary>
        internal XdmNode[] AllNodes = XdmNode.NoNodes;


        internal XdmDocument(string documentUri)
        {
            Id = System.Threading.Interlocked.Increment(ref _NextId);
            DocumentUri = documentUri;
        }


        /// <summary>
        /// The URI the document was loaded from (the result of fn:document-uri()), null if unknown.
        /// </summary>
        public string DocumentUri { get; }


        /// <summary>
        /// Parses an XML file.
        /// </summary>
        /// <exception cref="XmlException">The document is not well-formed or has a document type declaration</exception>
        public static XdmDocument Load(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }
            using (FileStream stream = File.OpenRead(path))
            {
                return XdmDocumentBuilder.Load(stream, ResourceLoader.FileUri(path));
            }
        }


        /// <summary>
        /// Parses an XML document from a stream. Document type declarations are rejected and external resources are
        /// never loaded, so untrusted input can be parsed safely.
        /// </summary>
        /// <param name="stream">The XML document</param>
        /// <param name="documentUri">The URI the document was read from, null if unknown</param>
        /// <exception cref="XmlException">The document is not well-formed or has a document type declaration</exception>
        public static XdmDocument Load(Stream stream, string documentUri = null)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            return XdmDocumentBuilder.Load(stream, documentUri);
        }


        /// <summary>
        /// Builds the document from an XML reader. The reader's settings decide how DTDs and external resources are
        /// handled; whitespace-only text nodes must not be ignored by the reader if the rules depend on them.
        /// </summary>
        /// <param name="reader">The reader, positioned at the start of the document</param>
        /// <exception cref="XmlException">The document is not well-formed</exception>
        public static XdmDocument Load(XmlReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }
            return XdmDocumentBuilder.Load(reader, String.IsNullOrEmpty(reader.BaseURI) ? null : reader.BaseURI);
        }


        /// <summary>
        /// Returns all elements (or attributes) with the given expanded name in document order.
        /// </summary>
        internal List<XdmNode> GetNodesByName(string namespaceUri, string localName, bool attributes)
        {
            _EnsureIndex();
            Dictionary<NameKey, List<XdmNode>> index = attributes ? _AttributeIndex : _ElementIndex;
            List<XdmNode> list;
            return index.TryGetValue(new NameKey(namespaceUri, localName), out list) ? list : null;
        }


        private void _EnsureIndex()
        {
            if (_ElementIndex != null)
            {
                return;
            }
            lock (_IndexLock)
            {
                if (_ElementIndex != null)
                {
                    return;
                }
                Dictionary<NameKey, List<XdmNode>> elements = new Dictionary<NameKey, List<XdmNode>>();
                Dictionary<NameKey, List<XdmNode>> attributes = new Dictionary<NameKey, List<XdmNode>>();
                foreach (XdmNode node in AllNodes)
                {
                    Dictionary<NameKey, List<XdmNode>> target;
                    if (node.Kind == XdmNodeKind.Element)
                    {
                        target = elements;
                    }
                    else if (node.Kind == XdmNodeKind.Attribute)
                    {
                        target = attributes;
                    }
                    else
                    {
                        continue;
                    }
                    NameKey key = new NameKey(node.NamespaceUri, node.LocalName);
                    List<XdmNode> list;
                    if (!target.TryGetValue(key, out list))
                    {
                        list = new List<XdmNode>();
                        target[key] = list;
                    }
                    list.Add(node);
                }
                _AttributeIndex = attributes;
                _ElementIndex = elements;
            }
        }


        private readonly struct NameKey : IEquatable<NameKey>
        {
            private readonly string _Namespace;
            private readonly string _Local;


            public NameKey(string ns, string local)
            {
                _Namespace = ns ?? "";
                _Local = local;
            }


            public bool Equals(NameKey other)
            {
                return String.Equals(_Local, other._Local, StringComparison.Ordinal) && String.Equals(_Namespace, other._Namespace, StringComparison.Ordinal);
            }


            public override bool Equals(object obj)
            {
                return obj is NameKey other && Equals(other);
            }


            public override int GetHashCode()
            {
                return (_Local.GetHashCode() * 397) ^ _Namespace.GetHashCode();
            }
        }
    }


    internal static class XmlNamespaces
    {
        public const string Xml = "http://www.w3.org/XML/1998/namespace";
        public const string Xmlns = "http://www.w3.org/2000/xmlns/";
        public const string Xs = "http://www.w3.org/2001/XMLSchema";
        public const string Xsi = "http://www.w3.org/2001/XMLSchema-instance";
        public const string Fn = "http://www.w3.org/2005/xpath-functions";
        public const string Math = "http://www.w3.org/2005/xpath-functions/math";
        public const string Err = "http://www.w3.org/2005/xqt-errors";
        public const string Xsl = "http://www.w3.org/1999/XSL/Transform";
        public const string Schematron = "http://purl.oclc.org/dsdl/schematron";
        public const string Svrl = "http://purl.oclc.org/dsdl/svrl";
    }
}
