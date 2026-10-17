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
using System.Xml.Linq;
using Schematron.Net.XPath;

namespace Schematron.Net
{
    internal static class SchematronNamespaces
    {
        public const string Schematron = "http://purl.oclc.org/dsdl/schematron";
        public const string Xsl = "http://www.w3.org/1999/XSL/Transform";
        public const string Xml = "http://www.w3.org/XML/1998/namespace";
    }


    /// <summary>
    /// URIs of the schema files and the resources they include.
    /// </summary>
    internal static class Resources
    {
        public static Stream Open(IResourceResolver resolver, string uri)
        {
            Uri parsed;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out parsed))
            {
                // a relative reference without base URI: a path relative to the working directory
                parsed = new Uri(Path.GetFullPath(uri));
            }
            return (resolver ?? FileResourceResolver.Instance).Open(parsed);
        }


        /// <summary>
        /// Resolves a reference (e.g. sch:include/@href) against the URI of the referring file.
        /// </summary>
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


        public static string ToLocalPath(string uri)
        {
            Uri parsed;
            if (Uri.TryCreate(uri, UriKind.Absolute, out parsed))
            {
                return parsed.IsFile ? parsed.LocalPath : null;
            }
            return uri;
        }


        public static string FileUri(string path)
        {
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }
    }


    /// <summary>
    /// Copies of trees: the element content of sch:let as a document node, and nodes selected by xsl:copy-of for
    /// the SVRL output.
    /// </summary>
    internal static class NodeCopy
    {
        /// <summary>
        /// The content of an element as a new document node, like the value of an xsl:variable with content: the
        /// children are copied with the namespaces in scope; whitespace-only text between elements is dropped.
        /// </summary>
        public static XdmNode ContentAsDocument(XdmNode element, string baseUri)
        {
            StringBuilder xml = new StringBuilder();
            XmlWriterSettings writerSettings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment, OmitXmlDeclaration = true };
            using (XmlWriter writer = XmlWriter.Create(xml, writerSettings))
            {
                Dictionary<string, string> namespaces = element.GetInScopeNamespaces();
                foreach (XdmNode child in element.Children)
                {
                    _Write(writer, child, namespaces);
                }
            }
            XmlReaderSettings readerSettings = new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(new StringReader(xml.ToString()), readerSettings, baseUri))
            {
                return XdmDocument.Load(reader).Root;
            }
        }


        /// <summary>
        /// Writes a node of the schema (namespaces: those in scope of the copied content, declared on its elements).
        /// </summary>
        private static void _Write(XmlWriter writer, XdmNode node, Dictionary<string, string> namespaces)
        {
            switch (node.Kind)
            {
                case XdmNodeKind.Element:
                    writer.WriteStartElement(node.Prefix, node.LocalName, node.NamespaceUri);
                    if (namespaces != null)
                    {
                        foreach (KeyValuePair<string, string> ns in namespaces)
                        {
                            if (ns.Key.Length > 0 && ns.Key != "xml" && ns.Key != node.Prefix)
                            {
                                writer.WriteAttributeString("xmlns", ns.Key, "http://www.w3.org/2000/xmlns/", ns.Value);
                            }
                        }
                    }
                    foreach (XdmNode attribute in node.Attributes)
                    {
                        writer.WriteAttributeString(attribute.Prefix, attribute.LocalName, attribute.NamespaceUri, attribute.StringValue);
                    }
                    foreach (XdmNode child in node.Children)
                    {
                        _Write(writer, child, null);
                    }
                    writer.WriteEndElement();
                    break;
                case XdmNodeKind.Text:
                    writer.WriteString(node.StringValue);
                    break;
                case XdmNodeKind.Comment:
                    writer.WriteComment(node.StringValue);
                    break;
                case XdmNodeKind.ProcessingInstruction:
                    writer.WriteProcessingInstruction(node.LocalName, node.StringValue);
                    break;
            }
        }


        /// <summary>
        /// Converts a node to LINQ to XML nodes (a document node to its children; attributes are not converted).
        /// Iterative, so deep trees cannot exhaust the stack.
        /// </summary>
        public static IEnumerable<XNode> ToXNodes(XdmNode node)
        {
            List<XNode> result = new List<XNode>();
            IEnumerable<XdmNode> roots = node.Kind == XdmNodeKind.Document ? node.Children : new[] { node };
            foreach (XdmNode root in roots)
            {
                if (root.Kind != XdmNodeKind.Element)
                {
                    XNode leaf = _Leaf(root);
                    if (leaf != null)
                    {
                        result.Add(leaf);
                    }
                    continue;
                }
                XElement copy = _ShallowCopy(root);
                result.Add(copy);
                Stack<KeyValuePair<XdmNode, XElement>> pending = new Stack<KeyValuePair<XdmNode, XElement>>();
                pending.Push(new KeyValuePair<XdmNode, XElement>(root, copy));
                while (pending.Count > 0)
                {
                    KeyValuePair<XdmNode, XElement> current = pending.Pop();
                    foreach (XdmNode child in current.Key.Children)
                    {
                        if (child.Kind == XdmNodeKind.Element)
                        {
                            XElement element = _ShallowCopy(child);
                            current.Value.Add(element);
                            pending.Push(new KeyValuePair<XdmNode, XElement>(child, element));
                        }
                        else
                        {
                            XNode leaf = _Leaf(child);
                            if (leaf != null)
                            {
                                current.Value.Add(leaf);
                            }
                        }
                    }
                }
            }
            return result;
        }


        private static XElement _ShallowCopy(XdmNode element)
        {
            XElement copy = new XElement(XName.Get(element.LocalName, element.NamespaceUri));
            if (element.Prefix.Length > 0 && element.NamespaceUri.Length > 0)
            {
                copy.SetAttributeValue(XNamespace.Xmlns + element.Prefix, element.NamespaceUri);
            }
            foreach (XdmNode attribute in element.Attributes)
            {
                copy.SetAttributeValue(XName.Get(attribute.LocalName, attribute.NamespaceUri), attribute.StringValue);
            }
            return copy;
        }


        private static XNode _Leaf(XdmNode node)
        {
            switch (node.Kind)
            {
                case XdmNodeKind.Text:
                    return new XText(node.StringValue);
                case XdmNodeKind.Comment:
                    return new XComment(node.StringValue);
                case XdmNodeKind.ProcessingInstruction:
                    return new XProcessingInstruction(node.LocalName, node.StringValue);
                default:
                    return null;
            }
        }
    }


    internal static class Text
    {
        /// <summary>
        /// Collapses whitespace like normalize-space(): trims and replaces each run of whitespace with one space.
        /// </summary>
        public static string Collapse(string s)
        {
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
    }


    /// <summary>
    /// The location of a node in the SVRL output (svrl:failed-assert/@location), in the notations of the two common
    /// Schematron implementations.
    /// </summary>
    internal static class NodeLocations
    {
        /// <summary>
        /// The notation of SchXslt (and of the KoSIT validator), e.g. /Q{urn:...:Invoice-2}Invoice[1]/Q{urn:...}AccountingSupplierParty[1].
        /// </summary>
        public static string EQName(XdmNode node)
        {
            List<string> segments = new List<string>();
            for (XdmNode n = node; n != null; n = n.Parent)
            {
                switch (n.Kind)
                {
                    case XdmNodeKind.Element:
                        segments.Add("Q{" + n.NamespaceUri + "}" + n.LocalName + "[" + _Position(n) + "]");
                        break;
                    case XdmNodeKind.Attribute:
                        segments.Add("@Q{" + n.NamespaceUri + "}" + n.LocalName);
                        break;
                    case XdmNodeKind.Text:
                        segments.Add("text()[" + _Position(n) + "]");
                        break;
                    case XdmNodeKind.Comment:
                        segments.Add("comment()[" + _Position(n) + "]");
                        break;
                    case XdmNodeKind.ProcessingInstruction:
                        segments.Add("processing-instruction(\"" + n.LocalName + "\")[" + _Position(n) + "]");
                        break;
                }
            }
            segments.Reverse();
            return "/" + String.Join("/", segments);
        }


        /// <summary>
        /// The notation of the XSLT compiled with the ISO Schematron skeleton (e.g. the compiled XSLT published with the
        /// CEN EN 16931 and the Factur-X rules): /*:Invoice[namespace-uri()='urn:...:Invoice-2'][1]. Only elements have
        /// a path; as in the skeleton, the location of other nodes is their string value.
        /// </summary>
        public static string IsoSkeleton(XdmNode node)
        {
            switch (node.Kind)
            {
                case XdmNodeKind.Element:
                    {
                        StringBuilder sb = new StringBuilder();
                        _AppendIsoSkeletonPath(node, sb);
                        return sb.ToString();
                    }
                case XdmNodeKind.Document:
                    {
                        foreach (XdmNode child in node.Children)
                        {
                            if (child.Kind == XdmNodeKind.Element)
                            {
                                return IsoSkeleton(child);
                            }
                        }
                        return "";
                    }
                default:
                    return node.StringValue;
            }
        }


        private static void _AppendIsoSkeletonPath(XdmNode element, StringBuilder sb)
        {
            List<XdmNode> elements = new List<XdmNode>();
            for (XdmNode n = element; n != null && n.Kind == XdmNodeKind.Element; n = n.Parent)
            {
                elements.Add(n);
            }
            for (int i = elements.Count - 1; i >= 0; i--)
            {
                XdmNode e = elements[i];
                sb.Append('/');
                if (e.NamespaceUri.Length == 0)
                {
                    sb.Append(e.Prefix.Length == 0 ? e.LocalName : e.Prefix + ":" + e.LocalName);
                }
                else
                {
                    sb.Append("*:").Append(e.LocalName).Append("[namespace-uri()='").Append(e.NamespaceUri).Append("']");
                }
                sb.Append('[').Append(_Position(e)).Append(']');
            }
        }


        /// <summary>
        /// The position of a node among the preceding siblings of the same kind (and, for elements and processing
        /// instructions, the same name), starting at 1.
        /// </summary>
        private static int _Position(XdmNode node)
        {
            if (node.Parent == null)
            {
                return 1;
            }
            int position = 1;
            foreach (XdmNode sibling in node.Parent.Children)
            {
                if (ReferenceEquals(sibling, node))
                {
                    break;
                }
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
    }
}
