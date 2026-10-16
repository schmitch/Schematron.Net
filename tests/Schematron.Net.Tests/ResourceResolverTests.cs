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
using Schematron.Net.XPath;

namespace Schematron.Net.Tests
{
    /// <summary>
    /// Loading schemas through an <see cref="IResourceResolver"/> (e.g. rules embedded into an assembly) and
    /// validating parsed documents.
    /// </summary>
    [TestClass]
    public class ResourceResolverTests
    {
        private sealed class InMemoryResolver : IResourceResolver
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
            public readonly List<string> Opened = new List<string>();


            public Stream Open(Uri uri)
            {
                Opened.Add(uri.AbsoluteUri);
                if (!Files.TryGetValue(uri.AbsoluteUri, out string? content))
                {
                    throw new FileNotFoundException("not found: " + uri);
                }
                return new MemoryStream(Encoding.UTF8.GetBytes(content));
            }
        }


        private static InMemoryResolver _CreateRules()
        {
            InMemoryResolver resolver = new InMemoryResolver();
            resolver.Files["embedded:/rules/main.sch"] = """
                <schema xmlns="http://purl.oclc.org/dsdl/schematron" queryBinding="xslt2">
                  <ns prefix="o" uri="urn:example:order"/>
                  <let name="codes" value="document('../codes/currencies.xml')//code"/>
                  <include href="parts/currency.sch"/>
                </schema>
                """;
            resolver.Files["embedded:/rules/parts/currency.sch"] = """
                <pattern xmlns="http://purl.oclc.org/dsdl/schematron" id="currency">
                  <rule context="o:Order">
                    <assert id="C-01" flag="fatal" test="o:Currency = $codes">Unknown currency <value-of select="o:Currency"/>.</assert>
                  </rule>
                </pattern>
                """;
            resolver.Files["embedded:/codes/currencies.xml"] = "<codes><code>EUR</code><code>USD</code></codes>";
            return resolver;
        }


        [TestMethod]
        public void IncludesAndDocumentsAreLoadedThroughTheResolver()
        {
            InMemoryResolver resolver = _CreateRules();
            SchematronSchema schema = SchematronSchema.Load(new Uri("embedded:/rules/main.sch"), new SchematronOptions { ResourceResolver = resolver });
            CollectionAssert.AreEqual(new[] { "currency" }, schema.PatternIds.ToArray());

            XdmDocument valid = XdmDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes("<Order xmlns='urn:example:order'><Currency>EUR</Currency></Order>")));
            Assert.IsEmpty(schema.Validate(valid).Messages);

            XdmDocument invalid = XdmDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes("<Order xmlns='urn:example:order'><Currency>XXX</Currency></Order>")));
            SchematronMessage message = schema.Validate(invalid).Messages.Single();
            Assert.AreEqual("C-01", message.Id);
            Assert.AreEqual("Unknown currency XXX.", message.Text);

            CollectionAssert.AreEquivalent(new[] { "embedded:/rules/main.sch", "embedded:/rules/parts/currency.sch", "embedded:/codes/currencies.xml" }, resolver.Opened.Distinct().ToArray());
            // documents loaded with document() are cached per schema
            Assert.AreEqual(1, resolver.Opened.Count(u => u.EndsWith("currencies.xml", StringComparison.Ordinal)));
        }


        [TestMethod]
        public void MissingIncludeIsReported()
        {
            InMemoryResolver resolver = _CreateRules();
            resolver.Files.Remove("embedded:/rules/parts/currency.sch");
            SchematronException e = Assert.ThrowsExactly<SchematronException>(() => SchematronSchema.Load(new Uri("embedded:/rules/main.sch"), new SchematronOptions { ResourceResolver = resolver }));
            StringAssert.Contains(e.Message, "embedded:/rules/parts/currency.sch");
        }


        [TestMethod]
        public void TheDefaultResolverOnlyOpensLocalFiles()
        {
            Assert.ThrowsExactly<NotSupportedException>(() => FileResourceResolver.Instance.Open(new Uri("https://example.org/rules.sch")));
            Assert.ThrowsExactly<NotSupportedException>(() => SchematronSchema.Load(new Uri("https://example.org/rules.sch")));
        }


        [TestMethod]
        public void RelativeSchemaUriIsRejected()
        {
            Assert.ThrowsExactly<ArgumentException>(() => SchematronSchema.Load(new Uri("rules.sch", UriKind.Relative)));
        }


        [TestMethod]
        public void OneParsedDocumentForSeveralSchemas()
        {
            XdmDocument order = XdmDocument.Load(TestData.Local("custom", "order.xml"));
            SchematronSchema all = SchematronSchema.Load(TestData.Local("custom", "rules.sch"));
            SchematronSchema header = SchematronSchema.Load(TestData.Local("custom", "rules.sch"), "header-only");
            Assert.HasCount(9, all.Validate(order).Messages);
            Assert.HasCount(3, header.Validate(order).Messages);
            // the result is the same as validating the file
            CollectionAssert.AreEqual(all.Validate(TestData.Local("custom", "order.xml")).Messages.Select(m => m.Location).ToArray(),
                                      all.Validate(order).Messages.Select(m => m.Location).ToArray());
        }
    }
}
