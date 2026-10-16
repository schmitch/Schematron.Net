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
            if (element.Parent != null && element.Parent.Kind == XdmNodeKind.Element)
            {
                _AppendIsoSkeletonPath(element.Parent, sb);
            }
            sb.Append('/');
            if (element.NamespaceUri.Length == 0)
            {
                sb.Append(element.Prefix.Length == 0 ? element.LocalName : element.Prefix + ":" + element.LocalName);
            }
            else
            {
                sb.Append("*:").Append(element.LocalName).Append("[namespace-uri()='").Append(element.NamespaceUri).Append("']");
            }
            sb.Append('[').Append(_Position(element)).Append(']');
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
