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
using System.Text.RegularExpressions;
using System.Xml;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// fn:sort (without a key function) and fn:analyze-string.
    /// </summary>
    internal static class SequenceFunctions
    {
        public const string CodepointCollation = "http://www.w3.org/2005/xpath-functions/collation/codepoint";


        /// <summary>
        /// fn:sort#1/#2: a stable sort by the atomized values, compared with lt (untyped values as strings, NaN first).
        /// </summary>
        public static XdmValue Sort(XdmValue input, string collation)
        {
            if (collation != null && collation != CodepointCollation)
            {
                throw new XPathException("FOCH0002", "The collation '" + collation + "' is not supported");
            }
            if (input.Count < 2)
            {
                return input;
            }
            XdmItem[] items = new XdmItem[input.Count];
            XdmValue[] keys = new XdmValue[input.Count];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = input[i];
                keys[i] = Operations.Atomize(XdmValue.Of(input[i]));
            }
            int[] order = new int[items.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }
            _MergeSort(order, new int[order.Length], 0, order.Length, (a, b) => _CompareKeys(keys[a], keys[b]));
            XdmItem[] sorted = new XdmItem[items.Length];
            for (int i = 0; i < order.Length; i++)
            {
                sorted[i] = items[order[i]];
            }
            return XdmValue.Of(sorted);
        }


        /// <summary>A stable merge sort (the comparer may throw XPathException, which is passed on unchanged).</summary>
        private static void _MergeSort(int[] values, int[] buffer, int from, int to, Comparison<int> compare)
        {
            if (to - from < 2)
            {
                return;
            }
            int middle = (from + to) / 2;
            _MergeSort(values, buffer, from, middle, compare);
            _MergeSort(values, buffer, middle, to, compare);
            int left = from;
            int right = middle;
            int target = from;
            while (left < middle && right < to)
            {
                buffer[target++] = compare(values[right], values[left]) < 0 ? values[right++] : values[left++];
            }
            while (left < middle)
            {
                buffer[target++] = values[left++];
            }
            while (right < to)
            {
                buffer[target++] = values[right++];
            }
            Array.Copy(buffer, from, values, from, to - from);
        }


        private static int _CompareKeys(XdmValue a, XdmValue b)
        {
            int count = Math.Min(a.Count, b.Count);
            for (int i = 0; i < count; i++)
            {
                int c = _CompareKey((XdmAtomicValue)a[i], (XdmAtomicValue)b[i]);
                if (c != 0)
                {
                    return c;
                }
            }
            return a.Count.CompareTo(b.Count);
        }


        private static int _CompareKey(XdmAtomicValue x, XdmAtomicValue y)
        {
            if (x.Type == XsType.UntypedAtomic)
            {
                x = new StringAtomic(x.StringValue);
            }
            if (y.Type == XsType.UntypedAtomic)
            {
                y = new StringAtomic(y.StringValue);
            }
            bool xNaN = x is NumericValue nx && nx.IsNaN;
            bool yNaN = y is NumericValue ny && ny.IsNaN;
            if (xNaN || yNaN)
            {
                return xNaN == yNaN ? 0 : (xNaN ? -1 : 1);
            }
            int result;
            if (Operations.TryCompare(x, y, false, out result))
            {
                return result;
            }
            throw new XPathException("XPTY0004", "sort() cannot compare " + x.Type.QualifiedName + " with " + y.Type.QualifiedName);
        }


        /// <summary>
        /// fn:analyze-string: an fn:analyze-string-result element with fn:match (and nested fn:group) and
        /// fn:non-match children.
        /// </summary>
        public static XdmNode AnalyzeString(string input, string pattern, string flags, Regex regex)
        {
            int[] parents = (flags ?? "").IndexOf('q') >= 0 ? new int[1] : _GroupParents(pattern);
            StringBuilder xml = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(xml, new XmlWriterSettings { OmitXmlDeclaration = true }))
            {
                writer.WriteStartElement("analyze-string-result", XmlNamespaces.Fn);
                int position = 0;
                foreach (Match match in regex.Matches(input))
                {
                    if (match.Index > position)
                    {
                        writer.WriteElementString("non-match", XmlNamespaces.Fn, input.Substring(position, match.Index - position));
                    }
                    writer.WriteStartElement("match", XmlNamespaces.Fn);
                    _WriteGroups(writer, input, match, parents, 0, match.Index, match.Index + match.Length);
                    writer.WriteEndElement();
                    position = match.Index + match.Length;
                }
                if (position < input.Length)
                {
                    writer.WriteElementString("non-match", XmlNamespaces.Fn, input.Substring(position));
                }
                writer.WriteEndElement();
            }
            using (XmlReader reader = XmlReader.Create(new StringReader(xml.ToString()), XdmDocumentBuilder.CreateSecureSettings()))
            {
                XdmDocument document = XdmDocumentBuilder.Load(reader, null);
                return document.Root.ChildNodes[0];
            }
        }


        /// <summary>
        /// Writes the text from..to of a match with the groups whose parent group is the given one (0: the match).
        /// </summary>
        private static void _WriteGroups(XmlWriter writer, string input, Match match, int[] parents, int parent, int from, int to)
        {
            int position = from;
            for (int nr = 1; nr < match.Groups.Count && nr < parents.Length; nr++)
            {
                Group group = match.Groups[nr];
                if (parents[nr] != parent || !group.Success || group.Index < position || group.Index + group.Length > to)
                {
                    continue;
                }
                if (group.Index > position)
                {
                    writer.WriteString(input.Substring(position, group.Index - position));
                }
                writer.WriteStartElement("group", XmlNamespaces.Fn);
                writer.WriteAttributeString("nr", nr.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _WriteGroups(writer, input, match, parents, nr, group.Index, group.Index + group.Length);
                writer.WriteEndElement();
                position = group.Index + group.Length;
            }
            if (to > position)
            {
                writer.WriteString(input.Substring(position, to - position));
            }
        }


        /// <summary>
        /// The parent capturing group of each capturing group of an XPath regular expression (index 0 unused, 0 for
        /// a top-level group).
        /// </summary>
        private static int[] _GroupParents(string pattern)
        {
            List<int> parents = new List<int> { 0 };
            Stack<int> open = new Stack<int>();
            int classDepth = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                char ch = pattern[i];
                if (ch == '\\')
                {
                    i++;
                    continue;
                }
                if (classDepth > 0)
                {
                    if (ch == '[')
                    {
                        classDepth++;
                    }
                    else if (ch == ']')
                    {
                        classDepth--;
                    }
                    continue;
                }
                switch (ch)
                {
                    case '[':
                        classDepth = 1;
                        break;
                    case '(':
                        if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                        {
                            // (?:...) does not capture
                            open.Push(open.Count > 0 ? open.Peek() : 0);
                        }
                        else
                        {
                            parents.Add(open.Count > 0 ? open.Peek() : 0);
                            open.Push(parents.Count - 1);
                        }
                        break;
                    case ')':
                        if (open.Count > 0)
                        {
                            open.Pop();
                        }
                        break;
                }
            }
            return parents.ToArray();
        }
    }
}
