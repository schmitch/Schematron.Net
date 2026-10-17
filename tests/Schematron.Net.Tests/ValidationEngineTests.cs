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
using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Schematron.Net.XPath;

namespace Schematron.Net.Tests
{
    /// <summary>
    /// What a validation engine for arbitrary schemas and untrusted documents needs: variable scopes, query bindings,
    /// schema errors found at load time, subordinate documents, external rules, properties, parameters, phases per
    /// validation, reported dynamic errors, cancellation and limits.
    /// </summary>
    [TestClass]
    public class ValidationEngineTests
    {
        private static readonly XNamespace _Svrl = "http://purl.oclc.org/dsdl/svrl";


        private sealed class InMemoryResolver : IResourceResolver
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();


            public Stream Open(Uri uri)
            {
                if (!Files.TryGetValue(uri.AbsoluteUri, out string? content))
                {
                    throw new FileNotFoundException("not found: " + uri);
                }
                return new MemoryStream(Encoding.UTF8.GetBytes(content));
            }
        }


        private static SchematronSchema _Schema(string content, string? queryBinding = "xslt2", SchematronOptions? options = null)
        {
            string binding = queryBinding == null ? "" : " queryBinding=\"" + queryBinding + "\"";
            string xml = "<schema xmlns=\"http://purl.oclc.org/dsdl/schematron\"" + binding + ">" + content + "</schema>";
            return SchematronSchema.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "embedded:/test/schema.sch", options ?? new SchematronOptions());
        }


        private static XdmDocument _Document(string xml, string? uri = null)
        {
            return XdmDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), uri);
        }


        private static string[] _Ids(SchematronResult result)
        {
            return result.Messages.Select(m => m.Id ?? "").ToArray();
        }


        private static string[] _Texts(SchematronResult result)
        {
            return result.Messages.Select(m => m.Text.Trim()).ToArray();
        }


        private static string _Nested(int depth)
        {
            return string.Concat(Enumerable.Repeat("<n>", depth)) + "<leaf/>" + string.Concat(Enumerable.Repeat("</n>", depth));
        }


        // ----------------------------------------------------------------------------------------------------------
        // variables
        // ----------------------------------------------------------------------------------------------------------

        [TestMethod]
        public void AbstractPatternInstancesHaveTheirOwnVariables()
        {
            SchematronSchema schema = _Schema("""
                <pattern id="limit" abstract="true">
                  <let name="max" value="$limit"/>
                  <rule context="$element">
                    <assert test="number(.) le $max"><name/> exceeds <value-of select="$max"/></assert>
                  </rule>
                </pattern>
                <pattern id="a-limit" is-a="limit"><param name="element" value="a"/><param name="limit" value="5"/></pattern>
                <pattern id="b-limit" is-a="limit"><param name="element" value="b"/><param name="limit" value="100"/></pattern>
                """);
            Assert.IsEmpty(schema.Validate(_Document("<r><a>3</a><b>50</b></r>")).Messages);
            CollectionAssert.AreEqual(new[] { "a exceeds 5", "b exceeds 100" }, _Texts(schema.Validate(_Document("<r><a>6</a><b>101</b></r>"))));
        }


        [TestMethod]
        public void PatternVariablesAreScopedToTheirPattern()
        {
            // the same name in two patterns, a pattern variable shadowing a schema variable, and a variable that only
            // one pattern declares used by another pattern
            SchematronSchema schema = _Schema("""
                <let name="limit" value="1000"/>
                <pattern id="pa"><let name="limit" value="5"/><rule context="a"><assert test=". &lt;= $limit">a &gt; <value-of select="$limit"/></assert></rule></pattern>
                <pattern id="pb"><let name="limit" value="100"/><let name="unit" value="'kg'"/><rule context="b"><assert test=". &lt;= $limit">b &gt; <value-of select="$limit"/></assert></rule></pattern>
                <pattern id="pc"><rule context="c"><assert test=". &lt;= $limit">c &gt; <value-of select="$limit"/> <value-of select="$unit"/></assert></rule></pattern>
                """);
            CollectionAssert.AreEqual(new[] { "a > 5", "b > 100", "c > 1000 kg" }, _Texts(schema.Validate(_Document("<r><a>6</a><b>101</b><c>1001</c></r>"))));
            Assert.IsEmpty(schema.Validate(_Document("<r><a>5</a><b>100</b><c>1000</c></r>")).Messages);
        }


        [TestMethod]
        public void PhaseVariablesShadowSchemaVariables()
        {
            SchematronSchema schema = _Schema("""
                <let name="max" value="10"/>
                <phase id="strict"><let name="max" value="1"/><active pattern="p"/></phase>
                <pattern id="p"><rule context="v"><assert test=". &lt;= $max">max <value-of select="$max"/></assert></rule></pattern>
                """);
            XdmDocument document = _Document("<v>5</v>");
            Assert.IsEmpty(schema.Validate(document).Messages);
            CollectionAssert.AreEqual(new[] { "max 1" }, _Texts(schema.Validate(document, new SchematronValidationOptions { Phase = "strict" })));
        }


        [TestMethod]
        public void VariableErrorsAreFoundWhenTheSchemaIsLoaded()
        {
            // declared twice in one scope
            SchematronException e = Assert.ThrowsExactly<SchematronException>(() => _Schema("<pattern><rule context='*'><let name='x' value='1'/><let name='x' value='2'/><assert test='$x'/></rule></pattern>"));
            Assert.AreEqual("XTSE0630", e.ErrorCode);

            // not declared
            e = Assert.ThrowsExactly<SchematronException>(() => _Schema("<pattern id='p'><rule context='*'><assert id='A1' test='$missing = 1'/></rule></pattern>"));
            Assert.AreEqual("XPST0008", e.ErrorCode);
            Assert.AreEqual("Variable $missing in the test of assert A1 of rule \"*\" in pattern p is not declared", e.Message);

            // declared by two patterns and used by a third
            e = Assert.ThrowsExactly<SchematronException>(() => _Schema("<pattern id='p1'><let name='x' value='1'/></pattern><pattern id='p2'><let name='x' value='2'/></pattern>"
                                                                       + "<pattern id='p3'><rule context='*'><assert test='$x'/></rule></pattern>"));
            Assert.AreEqual("XPST0008", e.ErrorCode);
            StringAssert.Contains(e.Message, "p1, p2");

            // a rule variable is visible only after its declaration
            e = Assert.ThrowsExactly<SchematronException>(() => _Schema("<pattern><rule context='*'><let name='a' value='$b'/><let name='b' value='1'/><assert test='$a'/></rule></pattern>"));
            Assert.AreEqual("XPST0008", e.ErrorCode);
        }


        [TestMethod]
        public void VariablesCanHaveElementContent()
        {
            SchematronSchema schema = _Schema("""
                <ns prefix="h" uri="http://www.w3.org/1999/xhtml"/>
                <let name="note"><h:p xmlns:h="http://www.w3.org/1999/xhtml">A <h:b>bold</h:b> paragraph</h:p></let>
                <pattern><rule context="/">
                  <assert id="string" test="$note = 'A bold paragraph'"/>
                  <assert id="path" test="$note/h:p/h:b = 'bold'"/>
                  <assert id="document" test="$note instance of document-node()"/>
                </rule></pattern>
                """);
            Assert.IsEmpty(schema.Validate(_Document("<r/>")).Messages);
        }


        [TestMethod]
        public void ParametersReplaceSchemaVariables()
        {
            SchematronSchema schema = _Schema("""
                <ns prefix="c" uri="urn:example:config"/>
                <let name="currency" value="'EUR'"/>
                <let name="c:max" value="100" as="xs:integer"/>
                <pattern><rule context="amount">
                  <assert test="@currency = $currency">currency <value-of select="$currency"/></assert>
                  <assert test=". &lt;= $c:max">max <value-of select="$c:max"/></assert>
                </rule></pattern>
                """);
            XdmDocument document = _Document("<amount currency='USD'>150</amount>");
            CollectionAssert.AreEqual(new[] { "currency EUR", "max 100" }, _Texts(schema.Validate(document)));

            // by name as written in the schema or by expanded name
            SchematronValidationOptions options = new SchematronValidationOptions();
            options.Parameters["currency"] = XdmValue.Of("USD");
            options.Parameters["{urn:example:config}max"] = XdmValue.Of(200L);
            Assert.IsEmpty(schema.Validate(document, options).Messages);
            options.Parameters.Remove("{urn:example:config}max");
            options.Parameters["c:max"] = XdmValue.Of(120L);
            CollectionAssert.AreEqual(new[] { "max 120" }, _Texts(schema.Validate(document, options)));

            // the declared type applies, unknown names are rejected
            options.Parameters["c:max"] = XdmValue.Of("many");
            Assert.ThrowsExactly<ArgumentException>(() => schema.Validate(document, options));
            SchematronValidationOptions unknown = new SchematronValidationOptions();
            unknown.Parameters["curency"] = XdmValue.Of("USD");
            Assert.ThrowsExactly<ArgumentException>(() => schema.Validate(document, unknown));
        }


        // ----------------------------------------------------------------------------------------------------------
        // query bindings
        // ----------------------------------------------------------------------------------------------------------

        [TestMethod]
        public void XPath1QueryBindingsUseCompatibilityMode()
        {
            const string rules = """
                <pattern><rule context="r">
                  <assert id="compare" test="count(item) = '2'"/>
                  <assert id="first-item" test="contains(item, 'first')"/>
                  <assert id="arithmetic" test="item[1]/@n + '1' = 3"/>
                  <assert id="round" test="round('2.5') = 3"/>
                  <assert id="boolean" test="item = true()"/>
                </rule></pattern>
                """;
            XdmDocument document = _Document("<r><item n='2'>first</item><item n='3'>second</item></r>");
            foreach (string? binding in new[] { null, "xslt", "xpath", "exslt" })
            {
                SchematronResult result = _Schema(rules, binding).Validate(document);
                Assert.IsEmpty(result.Messages, (binding ?? "no query binding") + ": " + string.Join(", ", _Ids(result)));
            }
            // with an XPath 2.0 binding the same comparison is a type error
            SchematronException e = Assert.ThrowsExactly<SchematronException>(() => _Schema(rules, "xslt2").Validate(document));
            Assert.AreEqual("XPTY0004", e.ErrorCode);
        }


        [TestMethod]
        public void UnsupportedQueryBindingsAreRejected()
        {
            const string rules = "<pattern><rule context='*'><assert test='true()'/></rule></pattern>";
            SchematronException e = Assert.ThrowsExactly<SchematronException>(() => _Schema(rules, "xquery"));
            StringAssert.Contains(e.Message, "xquery");
            Assert.IsNotNull(_Schema(rules, "XSLT3"));
            Assert.IsNotNull(_Schema(rules, "xpath31"));
        }


        [TestMethod]
        public void Schematron15SchemasUseCompatibilityMode()
        {
            string xml = "<schema xmlns='http://www.ascc.net/xml/schematron'><pattern name='p'><rule context='r'><assert test=\"count(item) = '1'\">one item</assert></rule></pattern></schema>";
            SchematronSchema schema = SchematronSchema.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "embedded:/test/old.sch", new SchematronOptions());
            Assert.IsEmpty(schema.Validate(_Document("<r><item/></r>")).Messages);
            CollectionAssert.AreEqual(new[] { "one item" }, _Texts(schema.Validate(_Document("<r/>"))));
        }


        // ----------------------------------------------------------------------------------------------------------
        // schema structure
        // ----------------------------------------------------------------------------------------------------------

        [TestMethod]
        public void PhaseWithUnknownPatternIsRejected()
        {
            SchematronException e = Assert.ThrowsExactly<SchematronException>(() => _Schema("<phase id='ph'><active pattern='typo'/></phase><pattern id='real'><rule context='*'><assert test='false()'/></rule></pattern>"));
            StringAssert.Contains(e.Message, "typo");
        }


        [TestMethod]
        public void AbstractPatternParametersAreReplacedAsWholeNames()
        {
            SchematronSchema schema = _Schema("""
                <pattern id="p" abstract="true">
                  <rule context="$x"><let name="xy" value="count(*)"/><assert test="$xy = 0">children of <name/>: <value-of select="$xy"/></assert></rule>
                </pattern>
                <pattern id="item" is-a="p"><param name="x" value="item"/></pattern>
                """);
            CollectionAssert.AreEqual(new[] { "children of item: 1" }, _Texts(schema.Validate(_Document("<r><item><c/></item></r>"))));
        }


        [TestMethod]
        public void PatternsCanValidateSubordinateDocuments()
        {
            InMemoryResolver resolver = new InMemoryResolver();
            resolver.Files["embedded:/data/attachment.xml"] = "<attachment><size>12</size></attachment>";
            SchematronSchema schema = _Schema("""
                <pattern id="attachments" documents="//attachment/@href">
                  <rule context="attachment"><assert id="A1" test="size &lt; 10">too large: <value-of select="size"/></assert></rule>
                </pattern>
                <pattern id="main"><rule context="order"><assert id="M1" test="false()">main</assert></rule></pattern>
                """, options: new SchematronOptions { ResourceResolver = resolver });

            SchematronResult result = schema.Validate(_Document("<order><attachment href='attachment.xml'/></order>", "embedded:/data/order.xml"));
            CollectionAssert.AreEqual(new[] { "A1", "M1" }, _Ids(result));
            SchematronMessage attachment = result.Messages[0];
            Assert.AreEqual("too large: 12", attachment.Text);
            Assert.AreEqual("/Q{}attachment[1]", attachment.Location);
            Assert.AreEqual("embedded:/data/attachment.xml", attachment.DocumentUri);
            Assert.AreEqual("embedded:/data/order.xml", result.Messages[1].DocumentUri);
            CollectionAssert.AreEqual(new[] { "embedded:/data/attachment.xml" }, result.ActivePatterns[0].Documents.ToArray());
            Assert.IsEmpty(result.ActivePatterns[1].Documents);
            Assert.AreEqual("embedded:/data/attachment.xml", (string?)result.ToSvrl().Root!.Element(_Svrl + "active-pattern")!.Attribute("documents"));

            // a document that cannot be loaded is a dynamic error
            SchematronException e = Assert.ThrowsExactly<SchematronException>(() => schema.Validate(_Document("<order><attachment href='missing.xml'/></order>", "embedded:/data/order.xml")));
            Assert.AreEqual("FODC0002", e.ErrorCode);
        }


        [TestMethod]
        public void ExternalRulesAreIncludedWithExtends()
        {
            InMemoryResolver resolver = new InMemoryResolver();
            // the href of a nested external rule is relative to the file that contains it
            resolver.Files["embedded:/test/rules/common.sch"] = "<rule xmlns='http://purl.oclc.org/dsdl/schematron'><extends href='id.sch'/><assert id='NAME' test='@name'>name</assert></rule>";
            resolver.Files["embedded:/test/rules/id.sch"] = "<rule xmlns='http://purl.oclc.org/dsdl/schematron'><assert id='ID' test='@id'>id</assert></rule>";
            SchematronSchema schema = _Schema("<pattern><rule context='item'><extends href='rules/common.sch'/></rule></pattern>", options: new SchematronOptions { ResourceResolver = resolver });
            CollectionAssert.AreEqual(new[] { "ID", "NAME" }, _Ids(schema.Validate(_Document("<item/>"))));
            Assert.IsEmpty(schema.Validate(_Document("<item id='1' name='a'/>")).Messages);

            resolver.Files["embedded:/test/rules/common.sch"] = "<pattern xmlns='http://purl.oclc.org/dsdl/schematron'/>";
            Assert.ThrowsExactly<SchematronException>(() => _Schema("<pattern><rule context='item'><extends href='rules/common.sch'/></rule></pattern>", options: new SchematronOptions { ResourceResolver = resolver }));
        }


        [TestMethod]
        public void RuleFlagAndRoleAreWrittenToSvrl()
        {
            XDocument svrl = _Schema("<pattern><rule context='r' flag='checked' role='header'><assert test='true()'/></rule></pattern>").Validate(_Document("<r/>")).ToSvrl();
            XElement fired = svrl.Root!.Element(_Svrl + "fired-rule")!;
            Assert.AreEqual("checked", (string?)fired.Attribute("flag"));
            Assert.AreEqual("header", (string?)fired.Attribute("role"));
        }


        [TestMethod]
        public void PropertiesAndLanguagesAreWrittenToSvrl()
        {
            SchematronSchema schema = _Schema("""
                <pattern><rule context="item">
                  <assert id="A" test="false()" properties="price copy" diagnostics="d" xml:lang="en">failed</assert>
                </rule></pattern>
                <diagnostics><diagnostic id="d" xml:lang="de">Artikel <value-of select="@id"/></diagnostic></diagnostics>
                <properties>
                  <property id="price" role="amount" scheme="urn:example">Price <value-of select="price"/></property>
                  <property id="copy"><xsl:copy-of xmlns:xsl="http://www.w3.org/1999/XSL/Transform" select="price"/></property>
                </properties>
                """);
            SchematronResult result = schema.Validate(_Document("<item id='7'><price cur='EUR'>9.50</price></item>"));
            SchematronMessage message = result.Messages.Single();
            Assert.AreEqual("en", message.Language);
            Assert.AreEqual("de", message.Diagnostics.Single().Language);
            Assert.AreEqual("Artikel 7", message.Diagnostics.Single().Text);
            SchematronPropertyReference price = message.Properties[0];
            Assert.AreEqual("price", price.Id);
            Assert.AreEqual("amount", price.Role);
            Assert.AreEqual("urn:example", price.Scheme);
            Assert.AreEqual("Price 9.50", price.Text);
            Assert.AreEqual("price", message.Properties[1].Nodes.Single().LocalName);

            XElement failed = result.ToSvrl().Root!.Element(_Svrl + "failed-assert")!;
            CollectionAssert.AreEqual(new[] { "diagnostic-reference", "property-reference", "property-reference", "text" }, failed.Elements().Select(e => e.Name.LocalName).ToArray());
            Assert.AreEqual("de", (string?)failed.Element(_Svrl + "diagnostic-reference")!.Element(_Svrl + "text")!.Attribute(XNamespace.Xml + "lang"));
            Assert.AreEqual("en", (string?)failed.Element(_Svrl + "text")!.Attribute(XNamespace.Xml + "lang"));
            XElement first = failed.Elements(_Svrl + "property-reference").First();
            Assert.AreEqual("amount", (string?)first.Attribute("role"));
            XElement copied = failed.Elements(_Svrl + "property-reference").Last().Element(_Svrl + "text")!.Element("price")!;
            Assert.AreEqual("9.50", copied.Value);
            Assert.AreEqual("EUR", (string?)copied.Attribute("cur"));
        }


        // ----------------------------------------------------------------------------------------------------------
        // validation options and results
        // ----------------------------------------------------------------------------------------------------------

        [TestMethod]
        public void ThePhaseCanBeSelectedForEachValidation()
        {
            SchematronSchema schema = SchematronSchema.Load(TestData.Local("custom", "rules.sch"));
            XdmDocument order = XdmDocument.Load(TestData.Local("custom", "order.xml"));
            Assert.AreEqual("all", schema.Phase);
            CollectionAssert.AreEqual(new[] { "all", "header-only" }, schema.Phases.ToArray());

            SchematronResult header = schema.Validate(order, new SchematronValidationOptions { Phase = "header-only" });
            Assert.AreEqual("header-only", header.Phase);
            CollectionAssert.AreEqual(new[] { "H-01", "H-02", "H-03" }, _Ids(header));
            Assert.AreEqual("header-only", (string?)header.ToSvrl().Root!.Attribute("phase"));

            Assert.HasCount(9, schema.Validate(order, new SchematronValidationOptions { Phase = "#DEFAULT" }).Messages);
            SchematronResult all = schema.Validate(order, new SchematronValidationOptions { Phase = "#ALL" });
            CollectionAssert.AreEqual(new[] { "header", "lines-instance", "pattern-1", "included" }, all.ActivePatterns.Select(p => p.Id).ToArray());
            Assert.ThrowsExactly<SchematronException>(() => schema.Validate(order, new SchematronValidationOptions { Phase = "missing" }));
        }


        [TestMethod]
        public void DynamicErrorsCanBeReported()
        {
            SchematronSchema schema = _Schema("""
                <pattern id="p">
                  <rule context="v">
                    <assert id="decimal" test="xs:decimal(.) gt 0">positive</assert>
                    <assert id="short" test="string-length(.) lt 5">short</assert>
                  </rule>
                </pattern>
                """);
            XdmDocument document = _Document("<r><v>abcdef</v><v>2</v></r>");
            SchematronException thrown = Assert.ThrowsExactly<SchematronException>(() => schema.Validate(document));
            Assert.AreEqual("FORG0001", thrown.ErrorCode);

            SchematronResult result = schema.Validate(document, new SchematronValidationOptions { DynamicErrors = SchematronDynamicErrorHandling.Report });
            SchematronError error = result.Errors.Single();
            Assert.AreEqual("FORG0001", error.Code);
            Assert.AreEqual("p", error.PatternId);
            Assert.AreEqual("v", error.RuleContext);
            Assert.AreEqual("decimal", error.AssertionId);
            Assert.AreEqual("/Q{}r[1]/Q{}v[1]", error.Location);
            // the validation continued with the next assertion and the next node
            CollectionAssert.AreEqual(new[] { "short" }, _Ids(result));
            Assert.IsFalse(result.IsValid);
        }


        [TestMethod]
        public void ReportsDoNotMakeADocumentInvalid()
        {
            SchematronSchema schema = _Schema("<pattern><rule context='r'><report test='true()'>note</report><assert test='@id'>id</assert></rule></pattern>");
            Assert.IsTrue(schema.Validate(_Document("<r id='1'/>")).IsValid);
            Assert.IsFalse(schema.Validate(_Document("<r/>")).IsValid);
        }


        [TestMethod]
        public void ValidationsCanBeCancelled()
        {
            // about 4 * 10^8 evaluations without cancellation
            SchematronSchema schema = _Schema("<pattern><rule context='r'><assert test='some $i in 1 to 20000, $j in 1 to 20000 satisfies $i + $j lt 0'/></rule></pattern>");
            XdmDocument document = _Document("<r/>");
            using (CancellationTokenSource cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                Assert.ThrowsExactly<OperationCanceledException>(() => schema.Validate(document, new SchematronValidationOptions { CancellationToken = cancelled.Token }));
            }
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            {
                Stopwatch watch = Stopwatch.StartNew();
                Assert.ThrowsExactly<OperationCanceledException>(() => schema.Validate(document, new SchematronValidationOptions { CancellationToken = timeout.Token }));
                Assert.IsLessThan(5000, watch.ElapsedMilliseconds);
            }
        }


        [TestMethod]
        public void SlowRegularExpressionsAreStopped()
        {
            SchematronSchema schema = _Schema("<pattern><rule context='v'><assert id='R' test=\"matches(., '^(a+)+$')\">no match</assert></rule></pattern>");
            XdmDocument document = _Document("<v>" + new string('a', 40) + "!</v>");
            SchematronValidationOptions options = new SchematronValidationOptions { RegexTimeout = TimeSpan.FromMilliseconds(100) };
            SchematronException e = Assert.ThrowsExactly<SchematronException>(() => schema.Validate(document, options));
            Assert.AreEqual("XPDY0130", e.ErrorCode);

            options.DynamicErrors = SchematronDynamicErrorHandling.Report;
            Assert.AreEqual("XPDY0130", schema.Validate(document, options).Errors.Single().Code);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SchematronValidationOptions { RegexTimeout = TimeSpan.Zero });
        }


        [TestMethod]
        public void DeeplyNestedDocuments()
        {
            SchematronSchema schema = _Schema("<pattern><rule context='leaf'><assert test='false()'/></rule></pattern>",
                                              options: new SchematronOptions { LocationFormat = SchematronLocationFormat.IsoSkeleton });
            // rejected by default
            Assert.Throws<XmlException>(() => schema.Validate(new MemoryStream(Encoding.UTF8.GetBytes(_Nested(5000)))));

            // without the limit, the location of a deep node is computed without recursion: a stack overflow would end the process
            XdmDocument document = XdmDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes(_Nested(100000))), null, new XdmLoadOptions { MaxDepth = 0 });
            string? location = null;
            Exception? error = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    location = schema.Validate(document).Messages.Single().Location;
                }
                catch (Exception e)
                {
                    error = e;
                }
            }, 1024 * 1024);
            thread.Start();
            thread.Join();
            Assert.IsNull(error, error?.ToString());
            Assert.AreEqual(100000 * "/n[1]".Length + "/leaf[1]".Length, location!.Length);
        }
    }
}
