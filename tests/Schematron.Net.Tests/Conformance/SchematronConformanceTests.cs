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
using System.Xml;
using System.Xml.Linq;
using Schematron.Net.XPath;

namespace Schematron.Net.Tests.Conformance
{
    /// <summary>
    /// Runs the ISO Schematron conformance tests of SchXslt (https://github.com/Schematron/schematron-conformance):
    /// each test case has a schema, the documents to validate and the expected outcome (valid, invalid or a static
    /// error) and XPath expectations about the SVRL report. The documents are served by an
    /// <see cref="IResourceResolver"/>, the expectations are evaluated with <see cref="XPathExpression"/>.
    ///
    /// TestData/schematron-conformance-known-failures.txt lists the test cases that fail; the test fails if another
    /// one fails or a listed one passes.
    /// </summary>
    [TestClass]
    public class SchematronConformanceTests
    {
        private static readonly XNamespace _T = "tag:dmaus@dmaus.name,2019:Schematron:Testsuite";


        private sealed class TestCaseResolver : IResourceResolver
        {
            public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();


            public Stream Open(Uri uri)
            {
                if (Files.TryGetValue(uri.AbsoluteUri, out byte[]? content))
                {
                    return new MemoryStream(content);
                }
                throw new FileNotFoundException("not part of the test case: " + uri);
            }
        }


        public TestContext TestContext { get; set; } = null!;


        [TestMethod]
        public void ConformanceTests()
        {
            string root = Path.Combine(TestData.Upstream("schematron-conformance"), "src", "main", "resources", "tests");
            HashSet<string> known = File.ReadAllLines(TestData.Local("schematron-conformance-known-failures.txt"))
                .Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal);
            List<string> regressions = new List<string>();
            List<string> fixedCases = new List<string>();
            int passed = 0;
            List<string> files = Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList();
            foreach (string file in files)
            {
                string id = Path.GetRelativePath(root, file).Replace('\\', '/');
                string? failure = _Run(XDocument.Load(file, LoadOptions.PreserveWhitespace).Root!, id);
                TestContext.WriteLine(id + ": " + (failure ?? "passed"));
                if (failure == null)
                {
                    passed++;
                    if (known.Contains(id))
                    {
                        fixedCases.Add(id);
                    }
                }
                else if (!known.Contains(id))
                {
                    regressions.Add(id + ": " + failure);
                }
            }
            TestContext.WriteLine(passed + " of " + files.Count + " test cases passed");
            Assert.IsGreaterThan(40, files.Count, "too few test cases");
            Assert.IsEmpty(regressions, regressions.Count + " test cases fail that are not listed as known failures:\n" + string.Join("\n", regressions));
            Assert.IsEmpty(fixedCases, "these known failures pass now, remove them from the list:\n" + string.Join("\n", fixedCases));
        }


        /// <summary>Runs a test case; returns why it failed, or null.</summary>
        private static string? _Run(XElement testCase, string id)
        {
            string expect = (string?)testCase.Attribute("expect") ?? "valid";
            string baseUri = "test:/" + id.Replace(".xml", "", StringComparison.Ordinal) + "/";
            TestCaseResolver resolver = new TestCaseResolver();
            XElement documents = testCase.Element(_T + "documents")!;
            foreach (XElement document in documents.Elements())
            {
                resolver.Files[baseUri + (string)document.Attribute("filename")!] = Encoding.UTF8.GetBytes(document.Elements().Single().ToString(SaveOptions.DisableFormatting));
            }
            string primary = baseUri + (string)documents.Element(_T + "primary")!.Attribute("filename")!;
            XElement schemas = testCase.Element(_T + "schemas")!;
            string? phase = (string?)schemas.Attribute("phase");

            foreach (XElement schemaElement in schemas.Elements())
            {
                SchematronResult result;
                try
                {
                    byte[] schemaContent = Encoding.UTF8.GetBytes(schemaElement.ToString(SaveOptions.DisableFormatting));
                    SchematronSchema schema = SchematronSchema.Load(new MemoryStream(schemaContent), baseUri + "schema.sch",
                        new SchematronOptions { Phase = phase, ResourceResolver = resolver });
                    result = schema.Validate(XdmDocument.Load(resolver.Open(new Uri(primary)), primary));
                }
                catch (Exception e) when (e is SchematronException or XPathException or XmlException)
                {
                    if (expect == "error")
                    {
                        continue;
                    }
                    return "unexpected error: " + e.Message;
                }
                if (expect == "error")
                {
                    return "expected an error, but the document was validated";
                }
                bool valid = !result.Messages.Any();
                if (valid != (expect == "valid"))
                {
                    return "expected " + expect + ", found " + result.Messages.Count + " messages";
                }

                XdmDocument svrl = XdmDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes(result.ToSvrl().ToString(SaveOptions.DisableFormatting))));
                foreach (XElement expectation in testCase.Elements(_T + "expectations").Elements(_T + "expectation"))
                {
                    string test = (string)expectation.Attribute("test")!;
                    Dictionary<string, string> namespaces = expectation.AncestorsAndSelf().Attributes()
                        .Where(a => a.IsNamespaceDeclaration && a.Name.Namespace == XNamespace.Xmlns)
                        .GroupBy(a => a.Name.LocalName).ToDictionary(g => g.Key, g => g.First().Value);
                    if (!XPathExpression.Compile(test, new XPathStaticContext(namespaces)).EvaluateBoolean(svrl.Root))
                    {
                        return "expectation does not hold: " + test + "\n      SVRL: " + result.ToSvrl().ToString(SaveOptions.DisableFormatting);
                    }
                }
            }
            return null;
        }
    }
}
