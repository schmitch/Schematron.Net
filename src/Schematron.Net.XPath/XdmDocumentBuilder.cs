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
    /// Builds <see cref="XdmDocument"/>s from XML. Whitespace-only text nodes are preserved (as XSLT does without
    /// xsl:strip-space), adjacent text and CDATA sections are merged into one text node.
    /// </summary>
    internal static class XdmDocumentBuilder
    {
        /// <summary>
        /// Settings for untrusted input documents: DTDs are rejected, external resources are never resolved.
        /// </summary>
        public static XmlReaderSettings CreateSecureSettings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = false,
                IgnoreWhitespace = false,
                IgnoreProcessingInstructions = false,
                CloseInput = false
            };
        }


        public static XdmDocument Load(Stream stream, string documentUri, int maxDepth = XdmLoadOptions.DefaultMaxDepth)
        {
            using (XmlReader reader = XmlReader.Create(stream, CreateSecureSettings(), documentUri))
            {
                return Load(reader, documentUri, maxDepth);
            }
        }


        /// <summary>
        /// Builds the tree. Elements nested more deeply than maxDepth (0: no limit) are rejected, so that the
        /// functions that walk a tree cannot exhaust the stack.
        /// </summary>
        public static XdmDocument Load(XmlReader reader, string documentUri, int maxDepth = XdmLoadOptions.DefaultMaxDepth)
        {
            XdmDocument document = new XdmDocument(documentUri);
            List<XdmNode> allNodes = new List<XdmNode>();
            IXmlLineInfo lineInfo = reader as IXmlLineInfo;
            bool hasLineInfo = lineInfo != null && lineInfo.HasLineInfo();

            XdmNode root = new XdmNode(XdmNodeKind.Document, document);
            root.Order = 0;
            allNodes.Add(root);
            document.Root = root;

            // stack of open containers and their children
            Stack<XdmNode> openNodes = new Stack<XdmNode>();
            Stack<List<XdmNode>> openChildren = new Stack<List<XdmNode>>();
            openNodes.Push(root);
            openChildren.Push(new List<XdmNode>());

            StringBuilder pendingText = null;
            int textLine = 0;
            int textColumn = 0;

            void FlushText()
            {
                if (pendingText == null)
                {
                    return;
                }
                XdmNode text = new XdmNode(XdmNodeKind.Text, document)
                {
                    Value = pendingText.ToString(),
                    Parent = openNodes.Peek(),
                    Order = allNodes.Count,
                    LineNumber = textLine,
                    LinePosition = textColumn
                };
                text.EndOrder = text.Order;
                allNodes.Add(text);
                List<XdmNode> siblings = openChildren.Peek();
                text.Index = siblings.Count;
                siblings.Add(text);
                pendingText = null;
            }

            void CloseContainer()
            {
                XdmNode node = openNodes.Pop();
                List<XdmNode> children = openChildren.Pop();
                node.ChildNodes = children.Count == 0 ? XdmNode.NoNodes : children.ToArray();
                node.EndOrder = allNodes.Count - 1;
            }

            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        if (openNodes.Count == 1 && (reader.NodeType == XmlNodeType.Whitespace || reader.NodeType == XmlNodeType.SignificantWhitespace))
                        {
                            // whitespace outside of the document element is not part of the data model (other text
                            // only occurs there when a fragment is read)
                            break;
                        }
                        if (pendingText == null)
                        {
                            pendingText = new StringBuilder();
                            textLine = hasLineInfo ? lineInfo.LineNumber : 0;
                            textColumn = hasLineInfo ? lineInfo.LinePosition : 0;
                        }
                        pendingText.Append(reader.Value);
                        break;

                    case XmlNodeType.Element:
                        {
                            FlushText();
                            // the open containers are the document node and the ancestors of this element
                            if (maxDepth > 0 && openNodes.Count > maxDepth)
                            {
                                throw new XmlException("The elements are nested more than " + maxDepth + " levels deep",
                                                       null, hasLineInfo ? lineInfo.LineNumber : 0, hasLineInfo ? lineInfo.LinePosition : 0);
                            }
                            XdmNode element = new XdmNode(XdmNodeKind.Element, document)
                            {
                                LocalName = reader.LocalName,
                                NamespaceUri = reader.NamespaceURI,
                                Prefix = reader.Prefix,
                                Parent = openNodes.Peek(),
                                Order = allNodes.Count,
                                LineNumber = hasLineInfo ? lineInfo.LineNumber : 0,
                                LinePosition = hasLineInfo ? lineInfo.LinePosition : 0
                            };
                            allNodes.Add(element);
                            List<XdmNode> siblings = openChildren.Peek();
                            element.Index = siblings.Count;
                            siblings.Add(element);

                            bool isEmpty = reader.IsEmptyElement;
                            if (reader.HasAttributes)
                            {
                                List<XdmNode> attributes = null;
                                List<KeyValuePair<string, string>> declarations = null;
                                while (reader.MoveToNextAttribute())
                                {
                                    if (reader.NamespaceURI == XmlNamespaces.Xmlns)
                                    {
                                        if (declarations == null)
                                        {
                                            declarations = new List<KeyValuePair<string, string>>();
                                        }
                                        string prefix = reader.Prefix == "xmlns" ? reader.LocalName : "";
                                        declarations.Add(new KeyValuePair<string, string>(prefix, reader.Value));
                                        continue;
                                    }
                                    if (attributes == null)
                                    {
                                        attributes = new List<XdmNode>();
                                    }
                                    XdmNode attribute = new XdmNode(XdmNodeKind.Attribute, document)
                                    {
                                        LocalName = reader.LocalName,
                                        NamespaceUri = reader.NamespaceURI,
                                        Prefix = reader.Prefix,
                                        Value = reader.Value,
                                        Parent = element,
                                        Order = allNodes.Count,
                                        LineNumber = hasLineInfo ? lineInfo.LineNumber : 0,
                                        LinePosition = hasLineInfo ? lineInfo.LinePosition : 0,
                                        Index = attributes.Count
                                    };
                                    attribute.EndOrder = attribute.Order;
                                    allNodes.Add(attribute);
                                    attributes.Add(attribute);
                                }
                                reader.MoveToElement();
                                if (attributes != null)
                                {
                                    element.AttributeNodes = attributes.ToArray();
                                }
                                if (declarations != null)
                                {
                                    element.NamespaceDeclarations = declarations.ToArray();
                                }
                            }

                            if (isEmpty)
                            {
                                element.EndOrder = allNodes.Count - 1;
                            }
                            else
                            {
                                openNodes.Push(element);
                                openChildren.Push(new List<XdmNode>());
                            }
                            break;
                        }

                    case XmlNodeType.EndElement:
                        FlushText();
                        CloseContainer();
                        break;

                    case XmlNodeType.Comment:
                    case XmlNodeType.ProcessingInstruction:
                        {
                            FlushText();
                            XdmNode node = new XdmNode(reader.NodeType == XmlNodeType.Comment ? XdmNodeKind.Comment : XdmNodeKind.ProcessingInstruction, document)
                            {
                                LocalName = reader.NodeType == XmlNodeType.Comment ? "" : reader.Name,
                                Value = reader.Value,
                                Parent = openNodes.Peek(),
                                Order = allNodes.Count,
                                LineNumber = hasLineInfo ? lineInfo.LineNumber : 0,
                                LinePosition = hasLineInfo ? lineInfo.LinePosition : 0
                            };
                            node.EndOrder = node.Order;
                            allNodes.Add(node);
                            List<XdmNode> siblings = openChildren.Peek();
                            node.Index = siblings.Count;
                            siblings.Add(node);
                            break;
                        }

                    default:
                        // XML declaration, document type (rejected by the reader settings for input documents), ...
                        break;
                }
            }

            FlushText();
            while (openNodes.Count > 0)
            {
                CloseContainer();
            }
            document.AllNodes = allNodes.ToArray();
            return document;
        }


        /// <summary>
        /// Creates a parentless text node (the result of xsl:value-of in a function body).
        /// </summary>
        public static XdmNode CreateTextNode(string value)
        {
            XdmDocument document = new XdmDocument(null);
            XdmNode text = new XdmNode(XdmNodeKind.Text, document) { Value = value };
            document.Root = text;
            document.AllNodes = new[] { text };
            return text;
        }


        /// <summary>
        /// Creates a document node with a single text child (the value of an xsl:variable with content).
        /// </summary>
        public static XdmNode CreateDocumentWithText(string value)
        {
            XdmDocument document = new XdmDocument(null);
            XdmNode root = new XdmNode(XdmNodeKind.Document, document);
            document.Root = root;
            if (String.IsNullOrEmpty(value))
            {
                document.AllNodes = new[] { root };
                return root;
            }
            XdmNode text = new XdmNode(XdmNodeKind.Text, document) { Value = value, Parent = root, Order = 1, EndOrder = 1 };
            root.ChildNodes = new[] { text };
            root.EndOrder = 1;
            document.AllNodes = new[] { root, text };
            return root;
        }
    }
}
