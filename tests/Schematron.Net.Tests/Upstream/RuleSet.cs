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

namespace Schematron.Net.Tests.Upstream
{
    /// <summary>The syntax of an e-invoice, from the namespace of its root element.</summary>
    internal enum InvoiceSyntax
    {
        Other,
        Ubl,
        Cii
    }


    /// <summary>
    /// An official rule set: its Schematron source (validated by this library) and the XSLT its publisher compiled
    /// from it (run by Saxon as the reference), both from the upstream test data.
    /// </summary>
    internal sealed class RuleSet
    {
        private readonly Lazy<SchematronSchema> _Schema;


        private RuleSet(string name, InvoiceSyntax syntax, string upstream, string schematron, string xslt, SchematronLocationFormat locationFormat)
        {
            Name = name;
            Syntax = syntax;
            Upstream = upstream;
            SchematronPath = schematron;
            XsltPath = xslt;
            LocationFormat = locationFormat;
            _Schema = new Lazy<SchematronSchema>(() => SchematronSchema.Load(Path.Combine(TestData.Upstream(Upstream), SchematronPath), new SchematronOptions { LocationFormat = LocationFormat }));
        }


        public string Name { get; }
        public InvoiceSyntax Syntax { get; }
        public string Upstream { get; }
        public string SchematronPath { get; }
        public string XsltPath { get; }
        public SchematronLocationFormat LocationFormat { get; }

        /// <summary>The compiled schema (compiled once per test run).</summary>
        public SchematronSchema Schema => _Schema.Value;

        public string Xslt => Path.Combine(TestData.Upstream(Upstream), XsltPath);


        public override string ToString()
        {
            return Name;
        }


        public static readonly RuleSet CenUbl = new RuleSet("cen-ubl", InvoiceSyntax.Ubl, "cen",
            "ubl/schematron/EN16931-UBL-validation.sch", "ubl/xslt/EN16931-UBL-validation.xslt", SchematronLocationFormat.IsoSkeleton);

        public static readonly RuleSet CenCii = new RuleSet("cen-cii", InvoiceSyntax.Cii, "cen",
            "cii/schematron/EN16931-CII-validation.sch", "cii/xslt/EN16931-CII-validation.xslt", SchematronLocationFormat.IsoSkeleton);

        // the XRechnung rules are compiled with SchXslt
        public static readonly RuleSet XRechnungUbl = new RuleSet("xrechnung-ubl", InvoiceSyntax.Ubl, "xrechnung-rules",
            "schematron/ubl/XRechnung-UBL-validation.sch", "schematron/ubl/XRechnung-UBL-validation.xsl", SchematronLocationFormat.EQName);

        public static readonly RuleSet XRechnungCii = new RuleSet("xrechnung-cii", InvoiceSyntax.Cii, "xrechnung-rules",
            "schematron/cii/XRechnung-CII-validation.sch", "schematron/cii/XRechnung-CII-validation.xsl", SchematronLocationFormat.EQName);

        public static readonly RuleSet[] FacturX = new[] { "MINIMUM", "BASIC-WL", "BASIC", "EN16931", "EXTENDED" }
            .Select(profile => new RuleSet("facturx-" + profile.ToLowerInvariant(), InvoiceSyntax.Cii, "facturx",
                "documentation/zugferd252/Schematron/FACTUR-X_" + profile + ".sch", "documentation/zugferd252/XSLT/FACTUR-X_" + profile + ".xslt", SchematronLocationFormat.IsoSkeleton))
            .ToArray();

        public static IEnumerable<RuleSet> All => new[] { CenUbl, CenCii, XRechnungUbl, XRechnungCii }.Concat(FacturX);


        public static RuleSet Get(string name)
        {
            return All.Single(r => r.Name == name);
        }
    }
}
