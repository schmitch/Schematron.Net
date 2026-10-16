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
using System.Reflection;
using System.Text;
using System.Xml;

namespace Schematron.Net.XPath.Tests
{
    /// <summary>
    /// The public API: what a host language such as Schematron (or a validator selecting rules for a document) builds on.
    /// </summary>
    [TestClass]
    public class XPathApiTests
    {
        private static readonly Dictionary<string, string> _Namespaces = new Dictionary<string, string>
        {
            ["invoice"] = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2",
            ["cbc"] = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
        };

        private const string _Invoice = "<Invoice xmlns='urn:oasis:names:specification:ubl:schema:xsd:Invoice-2' xmlns:cbc='urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2'>\n"
            + "<cbc:CustomizationID>urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0</cbc:CustomizationID>"
            + "<cbc:ID>R-1</cbc:ID><cbc:Note>a</cbc:Note><cbc:Note>b</cbc:Note><cbc:Amount currencyID='EUR'>1.50</cbc:Amount></Invoice>";


        private static XdmDocument _Load(string xml, string? uri = null)
        {
            return XdmDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)), uri);
        }


        private static string[] _Strings(XdmValue value)
        {
            return value.Select(i => i.StringValue).ToArray();
        }


        private sealed class Variables : IXPathVariableResolver
        {
            public readonly Dictionary<string, XdmValue> Values = new Dictionary<string, XdmValue>();


            public bool TryResolve(string expandedName, out XdmValue value)
            {
                return Values.TryGetValue(expandedName, out value!);
            }
        }


        [TestMethod]
        public void MatchExpression()
        {
            XdmDocument document = _Load(_Invoice);
            XPathStaticContext context = new XPathStaticContext(_Namespaces);
            Assert.IsTrue(XPathExpression.Compile("exists(/invoice:Invoice/cbc:CustomizationID[. = 'urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0'])", context).EvaluateBoolean(document.Root));
            Assert.IsFalse(XPathExpression.Compile("/invoice:Invoice/cbc:CustomizationID = 'other'", context).EvaluateBoolean(document.Root));
        }


        [TestMethod]
        public void ResultItems()
        {
            XdmDocument document = _Load(_Invoice);
            XPathStaticContext context = new XPathStaticContext(_Namespaces);
            XdmValue notes = XPathExpression.Compile("//cbc:Note", context).Evaluate(document.Root);
            CollectionAssert.AreEqual(new[] { "a", "b" }, _Strings(notes));
            XdmNode note = (XdmNode)notes[0];
            Assert.AreEqual(XdmNodeKind.Element, note.Kind);
            Assert.AreEqual("Note", note.LocalName);
            Assert.AreEqual("cbc", note.Prefix);
            Assert.AreEqual("urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2", note.NamespaceUri);
            Assert.AreEqual("Invoice", note.Parent!.LocalName);
            Assert.AreSame(document, note.Document);
            Assert.AreEqual(2, note.LineNumber);

            XdmValue atomics = XPathExpression.Compile("count(//cbc:Note), //cbc:ID || '!', xs:decimal(//cbc:Amount) * 2, //cbc:Amount/@currencyID", context).Evaluate(document.Root);
            CollectionAssert.AreEqual(new[] { "2", "R-1!", "3", "EUR" }, _Strings(atomics));
            CollectionAssert.AreEqual(new[] { "xs:integer", "xs:string", "xs:decimal" }, atomics.Take(3).Cast<XdmAtomicValue>().Select(a => a.TypeName).ToArray());
            Assert.AreEqual(XdmNodeKind.Attribute, ((XdmNode)atomics[3]).Kind);
            Assert.IsTrue(XPathExpression.Compile("//cbc:Missing", context).Evaluate(document.Root).IsEmpty);
        }


        [TestMethod]
        public void NodeNavigation()
        {
            XdmDocument document = _Load(_Invoice);
            XdmNode invoice = document.Root.Children.Single();
            Assert.AreEqual(XdmNodeKind.Document, document.Root.Kind);
            Assert.IsNull(document.Root.Parent);
            Assert.AreEqual(5, invoice.Children.Count(c => c.Kind == XdmNodeKind.Element));
            XdmNode amount = invoice.Descendants().Single(n => n.LocalName == "Amount");
            Assert.AreEqual("EUR", amount.GetAttribute("currencyID"));
            Assert.AreEqual("currencyID", amount.Attributes.Single().LocalName);
            Assert.AreEqual("cbc:Amount", amount.Name);
            Assert.IsTrue(invoice.IsAncestorOf(amount));
            Assert.IsLessThan(0, XdmNode.CompareOrder(invoice, amount));
            Assert.AreEqual("urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2", amount.GetInScopeNamespaces()["cbc"]);
        }


        [TestMethod]
        public void PredeclaredPrefixes()
        {
            XdmDocument document = _Load("<a>1.50</a>");
            CollectionAssert.AreEqual(new[] { "3" }, _Strings(XPathExpression.Compile("xs:decimal(/a) * 2").Evaluate(document.Root)));
            CollectionAssert.AreEqual(new[] { "true" }, _Strings(XPathExpression.Compile("fn:true()").Evaluate(null)));
        }


        [TestMethod]
        public void StaticErrors()
        {
            Assert.AreEqual("XPST0081", Assert.ThrowsExactly<XPathException>(() => XPathExpression.Compile("/undeclared:a")).Code);
            Assert.AreEqual("XPST0003", Assert.ThrowsExactly<XPathException>(() => XPathExpression.Compile("/a[")).Code);
            Assert.AreEqual("XPST0017", Assert.ThrowsExactly<XPathException>(() => XPathExpression.Compile("no-such-function()")).Code);
        }


        [TestMethod]
        public void DynamicErrors()
        {
            XdmDocument document = _Load("<a>x</a>");
            Assert.AreEqual("FORG0001", Assert.ThrowsExactly<XPathException>(() => XPathExpression.Compile("xs:decimal(/a)").EvaluateBoolean(document.Root)).Code);
            Assert.AreEqual("XPDY0002", Assert.ThrowsExactly<XPathException>(() => XPathExpression.Compile("/a").Evaluate(null)).Code);
            Assert.AreEqual("XPST0008", Assert.ThrowsExactly<XPathException>(() => XPathExpression.Compile("$undefined").Evaluate(null)).Code);
        }


        [TestMethod]
        public void ExternalVariables()
        {
            XPathStaticContext context = new XPathStaticContext(new Dictionary<string, string> { ["v"] = "urn:example:variables" });
            Variables variables = new Variables();
            variables.Values["limit"] = XdmValue.Of("3");
            variables.Values["{urn:example:variables}flag"] = XdmValue.Of(true);
            Assert.AreEqual("{urn:example:variables}flag", context.ExpandName("v:flag"));
            XdmValue result = XPathExpression.Compile("xs:integer($limit) + 1, $v:flag", context).Evaluate(null, null, variables);
            CollectionAssert.AreEqual(new[] { "4", "true" }, _Strings(result));

            // without variables, the global variables of the dynamic context are used
            XPathDynamicContext dynamicContext = new XPathDynamicContext { GlobalVariables = variables };
            Assert.IsTrue(XPathExpression.Compile("$limit = '3'").EvaluateBoolean(null, dynamicContext));
        }


        [TestMethod]
        public void Patterns()
        {
            XdmDocument document = _Load("<order><line id='1'><product/></line><line id='2'/><note/></order>");
            XdmValue lines = XPathExpression.CompilePattern("line[@id] | note").Evaluate(document.Root);
            CollectionAssert.AreEqual(new[] { "line", "line", "note" }, lines.Cast<XdmNode>().Select(n => n.LocalName).ToArray());
            Assert.HasCount(1, XPathExpression.CompilePattern("line/product").Evaluate(document.Root));
            Assert.HasCount(1, XPathExpression.CompilePattern("/").Evaluate(document.Root));
        }


        [TestMethod]
        public void XslFunctionsAndKeys()
        {
            XdmDocument stylesheet = _Load("""
                <functions xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:f="urn:example:functions" xmlns:xs="http://www.w3.org/2001/XMLSchema">
                  <xsl:function name="f:factorial" as="xs:integer">
                    <xsl:param name="n" as="xs:integer"/>
                    <xsl:choose>
                      <xsl:when test="$n le 1"><xsl:sequence select="1"/></xsl:when>
                      <xsl:otherwise><xsl:sequence select="$n * f:factorial($n - 1)"/></xsl:otherwise>
                    </xsl:choose>
                  </xsl:function>
                  <xsl:function name="f:label" as="xs:string">
                    <xsl:param name="value"/>
                    <xsl:variable name="text" select="string($value)"/>
                    <xsl:value-of select="'[', $text, ']'" separator=""/>
                  </xsl:function>
                </functions>
                """);
            XPathFunctionLibrary functions = new XPathFunctionLibrary();
            XdmNode root = stylesheet.Root.Children.Single();
            functions.DeclareXslFunctions(root.Children.Where(c => c.Kind == XdmNodeKind.Element),
                node => new XPathStaticContext(node.GetInScopeNamespaces(), null, functions));
            XPathStaticContext context = new XPathStaticContext(new Dictionary<string, string> { ["f"] = "urn:example:functions" }, null, functions);
            CollectionAssert.AreEqual(new[] { "120", "[x]" }, _Strings(XPathExpression.Compile("f:factorial(5), f:label('x')", context).Evaluate(null)));

            // xsl:key and key()
            XdmDocument document = _Load("<order><line product='A'/><line product='B'/><line product='A'/></order>");
            XPathDynamicContext dynamicContext = new XPathDynamicContext
            {
                Keys = new Dictionary<string, XslKey> { ["product"] = new XslKey("product", XPathExpression.CompilePattern("line"), XPathExpression.Compile("@product")) }
            };
            CollectionAssert.AreEqual(new[] { "2" }, _Strings(XPathExpression.Compile("count(key('product', 'A'))").Evaluate(document.Root, dynamicContext)));
            Assert.AreEqual("XTDE1260", Assert.ThrowsExactly<XPathException>(() => XPathExpression.Compile("key('other', 'A')").Evaluate(document.Root)).Code);
        }


        [TestMethod]
        public void InvalidXslFunctions()
        {
            XPathFunctionLibrary functions = new XPathFunctionLibrary();
            XdmNode Function(string xml)
            {
                return _Load("<xsl:function xmlns:xsl='http://www.w3.org/1999/XSL/Transform' xmlns:f='urn:f' " + xml).Root.Children.Single();
            }
            Func<XdmNode, XPathStaticContext> contextFor = node => new XPathStaticContext(node.GetInScopeNamespaces(), null, functions);
            Assert.AreEqual("XTSE0740", Assert.ThrowsExactly<XPathException>(() => functions.DeclareXslFunctions(new[] { Function("name='plain'/>") }, contextFor)).Code);
            Assert.AreEqual("XTSE0010", Assert.ThrowsExactly<XPathException>(() => functions.DeclareXslFunctions(new[] { Function("name='f:a'><xsl:for-each select='1'/></xsl:function>") }, contextFor)).Code);
            functions.DeclareXslFunctions(new[] { Function("name='f:b'><xsl:sequence select='1'/></xsl:function>") }, contextFor);
            Assert.AreEqual("XTSE0770", Assert.ThrowsExactly<XPathException>(() => functions.DeclareXslFunctions(new[] { Function("name='f:b'><xsl:sequence select='2'/></xsl:function>") }, contextFor)).Code);
        }


        [TestMethod]
        public void SequenceTypes()
        {
            XPathSequenceType type = XPathSequenceType.Parse("xs:decimal?");
            XdmDocument document = _Load("<a>1.5</a>");
            XdmValue converted = type.Convert(XPathExpression.Compile("/a").Evaluate(document.Root), "the test value");
            Assert.AreEqual("xs:decimal", ((XdmAtomicValue)converted.Single()).TypeName);
            Assert.IsTrue(type.Matches(converted));
            Assert.IsTrue(type.Matches(XdmValue.Empty));
            Assert.AreEqual("XPTY0004", Assert.ThrowsExactly<XPathException>(() => type.Convert(XdmValue.Of(new XdmItem[] { converted.Single(), converted.Single() }), "the test value")).Code);
        }


        [TestMethod]
        public void DocumentsAreLoadedThroughTheResolver()
        {
            Dictionary<string, string> files = new Dictionary<string, string> { ["embedded:/codes/currencies.xml"] = "<codes><code>EUR</code><code>USD</code></codes>" };
            DocumentCache documents = new DocumentCache(new InMemoryResolver(files));
            XPathStaticContext context = new XPathStaticContext(null, "embedded:/rules/main.sch");
            XPathExpression expression = XPathExpression.Compile("count(doc('../codes/currencies.xml')//code), doc-available('missing.xml')", context);
            CollectionAssert.AreEqual(new[] { "2", "false" }, _Strings(expression.Evaluate(null, new XPathDynamicContext(documents))));
            Assert.AreEqual("FODC0002", Assert.ThrowsExactly<XPathException>(() => expression.Evaluate(null)).Code);
        }


        private sealed class InMemoryResolver : IResourceResolver
        {
            private readonly Dictionary<string, string> _Files;


            public InMemoryResolver(Dictionary<string, string> files)
            {
                _Files = files;
            }


            public Stream Open(Uri uri)
            {
                return _Files.TryGetValue(uri.AbsoluteUri, out string? content) ? new MemoryStream(Encoding.UTF8.GetBytes(content)) : throw new FileNotFoundException(uri.AbsoluteUri);
            }
        }


        [TestMethod]
        public void TheDefaultResolverOnlyOpensLocalFiles()
        {
            Assert.ThrowsExactly<NotSupportedException>(() => FileResourceResolver.Instance.Open(new Uri("https://example.org/codes.xml")));
        }


        [TestMethod]
        public void DocumentsWithDoctypeAreRejected()
        {
            Assert.Throws<XmlException>(() => _Load("<!DOCTYPE a [<!ENTITY e 'x'>]><a>&e;</a>"));
        }


        [TestMethod]
        public void DocumentUri()
        {
            Assert.IsNull(_Load("<a/>").DocumentUri);
            Assert.AreEqual("urn:example:doc", _Load("<a/>", "urn:example:doc").DocumentUri);
            string path = Path.Combine(AppContext.BaseDirectory, "TestData", "xpath-context.xml");
            XdmDocument file = XdmDocument.Load(path);
            Assert.AreEqual(new Uri(path).AbsoluteUri, file.DocumentUri);
            CollectionAssert.AreEqual(new[] { file.DocumentUri }, _Strings(XPathExpression.Compile("document-uri(/)").Evaluate(file.Root)));
        }


        [TestMethod]
        public void LoadFromXmlReader()
        {
            using XmlReader reader = XmlReader.Create(new StringReader("<a> <b>1</b> </a>"));
            XdmDocument document = XdmDocument.Load(reader);
            // whitespace-only text nodes are kept, as in XSLT without xsl:strip-space
            Assert.HasCount(3, document.Root.Children.Single().Children);
        }


        /// <summary>
        /// The public surface of the package: a change here is a change of the API.
        /// </summary>
        [TestMethod]
        public void PublicTypes()
        {
            string[] types = typeof(XPathExpression).Assembly.GetExportedTypes().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "DocumentCache", "FileResourceResolver", "IDocumentResolver", "IResourceResolver", "IXPathVariableResolver",
                "XPathDynamicContext", "XPathException", "XPathExpression", "XPathFunctionLibrary", "XPathSequenceType", "XPathStaticContext",
                "XdmAtomicValue", "XdmDocument", "XdmItem", "XdmNode", "XdmNodeKind", "XdmValue", "XslKey"
            }, types, string.Join(", ", types));
        }
    }
}
