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
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// A value of the XPath data model: an immutable sequence of items (nodes and atomic values).
    /// </summary>
    public sealed class XdmValue : IReadOnlyList<XdmItem>
    {
        private static readonly XdmItem[] _NoItems = new XdmItem[0];

        /// <summary>The empty sequence.</summary>
        public static readonly XdmValue Empty = new XdmValue(_NoItems, 0);
        internal static readonly XdmValue True = new XdmValue(new XdmItem[] { BooleanValue.True }, 1);
        internal static readonly XdmValue False = new XdmValue(new XdmItem[] { BooleanValue.False }, 1);
        internal static readonly XdmValue EmptyString = new XdmValue(new XdmItem[] { StringAtomic.Empty }, 1);

        private readonly XdmItem[] _Items;
        private readonly int _Count;


        private XdmValue(XdmItem[] items, int count)
        {
            _Items = items;
            _Count = count;
        }


        /// <summary>A sequence of one item, or the empty sequence for null.</summary>
        public static XdmValue Of(XdmItem item)
        {
            if (item == null)
            {
                return Empty;
            }
            return new XdmValue(new[] { item }, 1);
        }


        /// <summary>A sequence of the given items.</summary>
        public static XdmValue Of(IEnumerable<XdmItem> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }
            XdmItem[] array = items.ToArray();
            return array.Length == 0 ? Empty : new XdmValue(array, array.Length);
        }


        /// <summary>An xs:boolean.</summary>
        public static XdmValue Of(bool value)
        {
            return value ? True : False;
        }


        /// <summary>An xs:integer.</summary>
        public static XdmValue Of(long value)
        {
            return new XdmValue(new XdmItem[] { IntegerValue.Get(value) }, 1);
        }


        /// <summary>An xs:decimal.</summary>
        public static XdmValue Of(decimal value)
        {
            return new XdmValue(new XdmItem[] { new DecimalValue(value) }, 1);
        }


        /// <summary>An xs:double.</summary>
        public static XdmValue Of(double value)
        {
            return new XdmValue(new XdmItem[] { new DoubleValue(value) }, 1);
        }


        /// <summary>An xs:string.</summary>
        public static XdmValue Of(string value)
        {
            return value.Length == 0 ? EmptyString : new XdmValue(new XdmItem[] { new StringAtomic(value) }, 1);
        }


        /// <summary>
        /// Wraps a list without copying it; the caller must not modify the list afterwards.
        /// </summary>
        internal static XdmValue FromList(List<XdmItem> items)
        {
            if (items.Count == 0)
            {
                return Empty;
            }
            return new XdmValue(items.ToArray(), items.Count);
        }


        internal static XdmValue FromArray(XdmItem[] items)
        {
            return items.Length == 0 ? Empty : new XdmValue(items, items.Length);
        }


        internal static XdmValue FromNodes(List<XdmNode> nodes)
        {
            if (nodes.Count == 0)
            {
                return Empty;
            }
            XdmItem[] items = new XdmItem[nodes.Count];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = nodes[i];
            }
            return new XdmValue(items, items.Length);
        }


        /// <inheritdoc />
        public int Count => _Count;


        /// <summary>True for the empty sequence.</summary>
        public bool IsEmpty => _Count == 0;


        /// <inheritdoc />
        public XdmItem this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
                return _Items[index];
            }
        }


        /// <summary>The first item, null for the empty sequence.</summary>
        public XdmItem First => _Count > 0 ? _Items[0] : null;


        /// <inheritdoc />
        public IEnumerator<XdmItem> GetEnumerator()
        {
            for (int i = 0; i < _Count; i++)
            {
                yield return _Items[i];
            }
        }


        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        /// <summary>
        /// Returns a sequence with the items in the range [start, start + length).
        /// </summary>
        internal XdmValue Slice(int start, int length)
        {
            if (length <= 0 || start >= _Count)
            {
                return Empty;
            }
            if (start == 0 && length >= _Count)
            {
                return this;
            }
            length = Math.Min(length, _Count - start);
            XdmItem[] items = new XdmItem[length];
            Array.Copy(_Items, start, items, 0, length);
            return new XdmValue(items, length);
        }


        /// <inheritdoc />
        public override string ToString()
        {
            return "(" + String.Join(", ", this) + ")";
        }
    }


    /// <summary>
    /// A dynamic or static error of the XPath / XSLT engine, identified by its W3C error code.
    /// </summary>
    public sealed class XPathException : Exception
    {
        /// <summary>The W3C error code, e.g. XPST0003 (syntax error) or FORG0001 (invalid cast).</summary>
        public string Code { get; }


        /// <summary>Creates an error with a W3C (or host language) error code.</summary>
        public XPathException(string code, string message) : base(code + ": " + message)
        {
            Code = code;
        }


        /// <summary>Creates an error with a W3C (or host language) error code and its cause.</summary>
        public XPathException(string code, string message, Exception inner) : base(code + ": " + message, inner)
        {
            Code = code;
        }
    }
}
