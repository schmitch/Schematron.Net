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
using System.Numerics;

namespace Schematron.Net.XPath
{
    internal enum PrimitiveType
    {
        AnyAtomic,
        UntypedAtomic,
        String,
        Boolean,
        Decimal,
        Double,
        Float,
        Duration,
        DateTime,
        Date,
        Time,
        GYearMonth,
        GYear,
        GMonthDay,
        GDay,
        GMonth,
        HexBinary,
        Base64Binary,
        AnyUri,
        QName,
        Notation
    }


    /// <summary>
    /// A built-in atomic type of XML Schema / XPath 2.0.
    /// </summary>
    internal sealed class XsType
    {
        private static readonly Dictionary<string, XsType> _ByName = new Dictionary<string, XsType>();

        public readonly string Name;
        public readonly XsType Parent;
        public readonly PrimitiveType Primitive;

        /// <summary>Inclusive range of the integer types derived from xs:integer.</summary>
        public readonly BigInteger? MinInclusive;
        public readonly BigInteger? MaxInclusive;

        public static readonly XsType AnyAtomicType = new XsType("anyAtomicType", null, PrimitiveType.AnyAtomic);
        public static readonly XsType UntypedAtomic = new XsType("untypedAtomic", AnyAtomicType, PrimitiveType.UntypedAtomic);
        public static readonly XsType String = new XsType("string", AnyAtomicType, PrimitiveType.String);
        public static readonly XsType NormalizedString = new XsType("normalizedString", String, PrimitiveType.String);
        public static readonly XsType Token = new XsType("token", NormalizedString, PrimitiveType.String);
        public static readonly XsType Language = new XsType("language", Token, PrimitiveType.String);
        public static readonly XsType NmToken = new XsType("NMTOKEN", Token, PrimitiveType.String);
        public static readonly XsType Name_ = new XsType("Name", Token, PrimitiveType.String);
        public static readonly XsType NcName = new XsType("NCName", Name_, PrimitiveType.String);
        public static readonly XsType Id = new XsType("ID", NcName, PrimitiveType.String);
        public static readonly XsType IdRef = new XsType("IDREF", NcName, PrimitiveType.String);
        public static readonly XsType Entity = new XsType("ENTITY", NcName, PrimitiveType.String);
        public static readonly XsType Boolean = new XsType("boolean", AnyAtomicType, PrimitiveType.Boolean);
        public static readonly XsType Decimal = new XsType("decimal", AnyAtomicType, PrimitiveType.Decimal);
        public static readonly XsType Integer = new XsType("integer", Decimal, PrimitiveType.Decimal);
        public static readonly XsType NonPositiveInteger = new XsType("nonPositiveInteger", Integer, PrimitiveType.Decimal, null, 0);
        public static readonly XsType NegativeInteger = new XsType("negativeInteger", NonPositiveInteger, PrimitiveType.Decimal, null, -1);
        public static readonly XsType Long = new XsType("long", Integer, PrimitiveType.Decimal, long.MinValue, long.MaxValue);
        public static readonly XsType Int = new XsType("int", Long, PrimitiveType.Decimal, int.MinValue, int.MaxValue);
        public static readonly XsType Short = new XsType("short", Int, PrimitiveType.Decimal, short.MinValue, short.MaxValue);
        public static readonly XsType Byte = new XsType("byte", Short, PrimitiveType.Decimal, sbyte.MinValue, sbyte.MaxValue);
        public static readonly XsType NonNegativeInteger = new XsType("nonNegativeInteger", Integer, PrimitiveType.Decimal, 0, null);
        public static readonly XsType UnsignedLong = new XsType("unsignedLong", NonNegativeInteger, PrimitiveType.Decimal, 0, ulong.MaxValue);
        public static readonly XsType UnsignedInt = new XsType("unsignedInt", UnsignedLong, PrimitiveType.Decimal, 0, uint.MaxValue);
        public static readonly XsType UnsignedShort = new XsType("unsignedShort", UnsignedInt, PrimitiveType.Decimal, 0, ushort.MaxValue);
        public static readonly XsType UnsignedByte = new XsType("unsignedByte", UnsignedShort, PrimitiveType.Decimal, 0, byte.MaxValue);
        public static readonly XsType PositiveInteger = new XsType("positiveInteger", NonNegativeInteger, PrimitiveType.Decimal, 1, null);
        public static readonly XsType Double = new XsType("double", AnyAtomicType, PrimitiveType.Double);
        public static readonly XsType Float = new XsType("float", AnyAtomicType, PrimitiveType.Float);
        public static readonly XsType Duration = new XsType("duration", AnyAtomicType, PrimitiveType.Duration);
        public static readonly XsType DayTimeDuration = new XsType("dayTimeDuration", Duration, PrimitiveType.Duration);
        public static readonly XsType YearMonthDuration = new XsType("yearMonthDuration", Duration, PrimitiveType.Duration);
        public static readonly XsType DateTime = new XsType("dateTime", AnyAtomicType, PrimitiveType.DateTime);
        public static readonly XsType DateTimeStamp = new XsType("dateTimeStamp", DateTime, PrimitiveType.DateTime);
        public static readonly XsType Date = new XsType("date", AnyAtomicType, PrimitiveType.Date);
        public static readonly XsType Time = new XsType("time", AnyAtomicType, PrimitiveType.Time);
        public static readonly XsType GYearMonth = new XsType("gYearMonth", AnyAtomicType, PrimitiveType.GYearMonth);
        public static readonly XsType GYear = new XsType("gYear", AnyAtomicType, PrimitiveType.GYear);
        public static readonly XsType GMonthDay = new XsType("gMonthDay", AnyAtomicType, PrimitiveType.GMonthDay);
        public static readonly XsType GDay = new XsType("gDay", AnyAtomicType, PrimitiveType.GDay);
        public static readonly XsType GMonth = new XsType("gMonth", AnyAtomicType, PrimitiveType.GMonth);
        public static readonly XsType HexBinary = new XsType("hexBinary", AnyAtomicType, PrimitiveType.HexBinary);
        public static readonly XsType Base64Binary = new XsType("base64Binary", AnyAtomicType, PrimitiveType.Base64Binary);
        public static readonly XsType AnyUri = new XsType("anyURI", AnyAtomicType, PrimitiveType.AnyUri);
        public static readonly XsType QName = new XsType("QName", AnyAtomicType, PrimitiveType.QName);
        public static readonly XsType Notation = new XsType("NOTATION", AnyAtomicType, PrimitiveType.Notation);


        private XsType(string name, XsType parent, PrimitiveType primitive, BigInteger? min = null, BigInteger? max = null)
        {
            Name = name;
            Parent = parent;
            Primitive = primitive;
            MinInclusive = min;
            MaxInclusive = max;
            _ByName[name] = this;
        }


        public static XsType FromLocalName(string localName)
        {
            XsType type;
            return _ByName.TryGetValue(localName, out type) ? type : null;
        }


        public string QualifiedName => "xs:" + Name;


        public bool IsSubtypeOf(XsType other)
        {
            for (XsType t = this; t != null; t = t.Parent)
            {
                if (t == other)
                {
                    return true;
                }
            }
            return false;
        }


        public bool IsInteger => IsSubtypeOf(Integer);


        public bool IsNumeric => Primitive == PrimitiveType.Decimal || Primitive == PrimitiveType.Double || Primitive == PrimitiveType.Float;


        public bool IsStringLike => Primitive == PrimitiveType.String || Primitive == PrimitiveType.UntypedAtomic || Primitive == PrimitiveType.AnyUri;


        /// <summary>
        /// Abstract types (xs:anyAtomicType, xs:NOTATION) cannot be the target of a cast.
        /// </summary>
        public bool IsAbstract => this == AnyAtomicType || this == Notation;


        public override string ToString()
        {
            return QualifiedName;
        }
    }
}
