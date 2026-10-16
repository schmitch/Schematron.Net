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
using System.Text;
using System.Xml.Linq;

namespace Schematron.Net.Tests
{
    /// <summary>
    /// Tests the Schematron features used by the official rules with TestData/custom/rules.sch: phases, global,
    /// pattern and rule variables, abstract patterns and rules, includes, xsl:function, xsl:key, diagnostics,
    /// value-of, name, subject and the SVRL output.
    /// </summary>
    [TestClass]
    public class SchematronEngineTests
    {
        private const string _Order = "Q{urn:example:order}";
        private static readonly XNamespace _Svrl = "http://purl.oclc.org/dsdl/svrl";


        private static string _RulesPath => TestData.Local("custom", "rules.sch");
        private static string _OrderPath => TestData.Local("custom", "order.xml");


        private static SchematronResult _ValidateString(SchematronSchema schema, string xml)
        {
            return schema.Validate(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        }


        [TestMethod]
        public void DefaultPhaseReportsAllFindings()
        {
            SchematronSchema schema = SchematronSchema.Load(_RulesPath);
            Assert.AreEqual("Test rules", schema.Title);
            Assert.AreEqual(SchematronLocationFormat.EQName, schema.LocationFormat);

            SchematronResult result = schema.Validate(_OrderPath);
            CollectionAssert.AreEqual(new[] { "header", "lines-instance", "included" }, result.ActivePatterns.Select(p => p.Id).ToArray());
            // per pattern in document order; the first rule of a pattern that matches a node wins
            CollectionAssert.AreEqual(new[] { "H-01", "H-02", "H-03", "L-99", "L-03", "L-01", "L-02", "L-03", "P-01" }, result.Messages.Select(m => m.Id).ToArray());
            Assert.AreEqual(8, result.FailedAssertions.Count());
            Assert.AreEqual(1, result.SuccessfulReports.Count());

            SchematronMessage h01 = result.Messages[0];
            Assert.AreEqual(SchematronMessageKind.FailedAssert, h01.Kind);
            Assert.AreEqual("fatal", h01.Flag);
            Assert.AreEqual("o:Id", h01.Test);
            Assert.AreEqual("An order must have an id.", h01.Text);
            Assert.AreEqual("/" + _Order + "Order[1]", h01.Location);
            Assert.AreEqual("header", h01.PatternId);
            Assert.AreEqual("/o:Order", h01.RuleContext);

            // rule variable, value-of and diagnostics
            SchematronMessage h02 = result.Messages[1];
            Assert.AreEqual("warning", h02.Flag);
            Assert.AreEqual("An order should have at most 3 lines, found 4.", h02.Text);
            Assert.HasCount(1, h02.Diagnostics);
            Assert.AreEqual("d-lines", h02.Diagnostics[0].Id);
            Assert.AreEqual("There are 4 lines.", h02.Diagnostics[0].Text);

            // successful report with name
            SchematronMessage h03 = result.Messages[2];
            Assert.AreEqual(SchematronMessageKind.SuccessfulReport, h03.Kind);
            Assert.AreEqual("information", h03.Flag);
            Assert.AreEqual("The order Order has a note: Please deliver in the morning", h03.Text);

            // the second rule of the abstract pattern only fires for the note, the lines are handled by the first rule
            SchematronMessage l99 = result.Messages[3];
            Assert.AreEqual("/" + _Order + "Order[1]/" + _Order + "Note[1]", l99.Location);
            Assert.AreEqual("o:Line | o:Note", l99.RuleContext);
            Assert.AreEqual("lines-instance", l99.PatternId);

            // xsl:key, subject
            SchematronMessage firstDuplicate = result.Messages[4];
            Assert.AreEqual("Product ABC-1 is ordered more than once.", firstDuplicate.Text);
            Assert.AreEqual("/" + _Order + "Order[1]/" + _Order + "Line[1]/" + _Order + "Product[1]", firstDuplicate.Location);
            Assert.AreEqual("/" + _Order + "Order[1]/" + _Order + "Line[4]/" + _Order + "Product[1]", result.Messages[7].Location);

            // abstract rule (extends) with a global variable
            SchematronMessage l01 = result.Messages[5];
            Assert.AreEqual("error", l01.Flag);
            Assert.AreEqual("error", l01.Role);
            Assert.AreEqual("Quantity must not exceed 100.", l01.Text);
            Assert.AreEqual("xs:decimal(o:Quantity) <= $max-quantity", l01.Test);
            Assert.AreEqual("/" + _Order + "Order[1]/" + _Order + "Line[2]", l01.Location);

            // xsl:function
            Assert.AreEqual("Line abc: total must be price times quantity (2).", result.Messages[6].Text);

            // included pattern with a regular expression
            SchematronMessage p01 = result.Messages[8];
            Assert.AreEqual("included", p01.PatternId);
            Assert.AreEqual("Product codes have the form ABC-123, found \"abc\".", p01.Text);
            Assert.AreEqual("/" + _Order + "Order[1]/" + _Order + "Line[3]/" + _Order + "Product[1]", p01.Location);
            Assert.IsNotNull(p01.LineNumber);
        }


        [TestMethod]
        public void PhaseSelectsPatterns()
        {
            SchematronSchema schema = SchematronSchema.Load(_RulesPath, "header-only");
            SchematronResult result = schema.Validate(_OrderPath);
            CollectionAssert.AreEqual(new[] { "header" }, result.ActivePatterns.Select(p => p.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "H-01", "H-02", "H-03" }, result.Messages.Select(m => m.Id).ToArray());
        }


        [TestMethod]
        public void UnknownPhaseIsRejected()
        {
            Assert.ThrowsExactly<SchematronException>(() => SchematronSchema.Load(_RulesPath, "does-not-exist"));
        }


        [TestMethod]
        public void ValidDocumentHasNoFindings()
        {
            SchematronSchema schema = SchematronSchema.Load(_RulesPath);
            SchematronResult result = _ValidateString(schema, "<Order xmlns='urn:example:order'><Id>1</Id>"
                + "<Line><Product>ABC-1</Product><Quantity>2</Quantity><Price>1.25</Price><Total>2.50</Total></Line>"
                + "<Line><Product>XYZ-22</Product><Quantity>100</Quantity><Price>0.1</Price><Total>10</Total></Line></Order>");
            Assert.IsEmpty(result.Messages, string.Join("\n", result.Messages));
            Assert.AreEqual(5, result.FiredRuleCount);
        }


        [TestMethod]
        public void IsoSkeletonLocations()
        {
            SchematronSchema schema = SchematronSchema.Load(_RulesPath, new SchematronOptions { LocationFormat = SchematronLocationFormat.IsoSkeleton });
            SchematronResult result = schema.Validate(_OrderPath);
            Assert.AreEqual("/*:Order[namespace-uri()='urn:example:order'][1]/*:Line[namespace-uri()='urn:example:order'][3]/*:Product[namespace-uri()='urn:example:order'][1]",
                            result.Messages.Single(m => m.Id == "P-01").Location);
        }


        [TestMethod]
        public void LoadFromStreamResolvesIncludesAgainstBaseUri()
        {
            SchematronSchema schema;
            using (FileStream stream = File.OpenRead(_RulesPath))
            {
                schema = SchematronSchema.Load(stream, new Uri(_RulesPath).AbsoluteUri, (string?)null);
            }
            CollectionAssert.Contains(schema.PatternIds.ToList(), "included");
            Assert.HasCount(9, schema.Validate(_OrderPath).Messages);
        }


        [TestMethod]
        public void SvrlOutput()
        {
            SchematronResult result = SchematronSchema.Load(_RulesPath).Validate(_OrderPath);
            XDocument svrl = result.ToSvrl();
            Assert.AreEqual(_Svrl + "schematron-output", svrl.Root!.Name);
            Assert.AreEqual("Test rules", (string?)svrl.Root.Attribute("title"));
            Assert.AreEqual(3, svrl.Root.Elements(_Svrl + "active-pattern").Count());
            Assert.AreEqual(result.FiredRuleCount, svrl.Root.Elements(_Svrl + "fired-rule").Count());
            Assert.AreEqual(8, svrl.Root.Elements(_Svrl + "failed-assert").Count());

            // each fired rule is followed by the messages of its firing
            List<XElement> header = svrl.Root.Elements().SkipWhile(e => e.Name != _Svrl + "active-pattern").Take(5).ToList();
            CollectionAssert.AreEqual(new[] { "active-pattern", "fired-rule", "failed-assert", "failed-assert", "successful-report" }, header.Select(e => e.Name.LocalName).ToArray());
            Assert.AreEqual("/o:Order", (string?)header[1].Attribute("context"));

            XElement h02 = svrl.Root.Elements(_Svrl + "failed-assert").Single(e => (string?)e.Attribute("id") == "H-02");
            Assert.AreEqual("warning", (string?)h02.Attribute("flag"));
            Assert.AreEqual("/" + _Order + "Order[1]", (string?)h02.Attribute("location"));
            Assert.AreEqual("An order should have at most 3 lines, found 4.", h02.Element(_Svrl + "text")!.Value);
            XElement diagnostic = h02.Element(_Svrl + "diagnostic-reference")!;
            Assert.AreEqual("d-lines", (string?)diagnostic.Attribute("diagnostic"));
            Assert.AreEqual("There are 4 lines.", diagnostic.Value.Trim());

            XElement report = svrl.Root.Elements(_Svrl + "successful-report").Single();
            Assert.AreEqual("H-03", (string?)report.Attribute("id"));
        }


        [TestMethod]
        public void DynamicErrorsAreReported()
        {
            SchematronSchema schema = SchematronSchema.Load(_RulesPath);
            SchematronException e = Assert.ThrowsExactly<SchematronException>(() => _ValidateString(schema,
                "<Order xmlns='urn:example:order'><Id>1</Id><Line><Product>ABC-1</Product><Quantity>many</Quantity><Price>1</Price><Total>1</Total></Line></Order>"));
            Assert.AreEqual("FORG0001", e.ErrorCode);
            Assert.AreEqual("lines-instance", e.PatternId);
        }


        [TestMethod]
        public void DocumentsWithDoctypeAreRejected()
        {
            SchematronSchema schema = SchematronSchema.Load(_RulesPath);
            Assert.Throws<System.Xml.XmlException>(() => _ValidateString(schema,
                "<!DOCTYPE Order [<!ENTITY e 'x'>]><Order xmlns='urn:example:order'><Id>&e;</Id></Order>"));
        }
    }
}
