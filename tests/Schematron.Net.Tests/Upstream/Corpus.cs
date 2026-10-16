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

namespace Schematron.Net.Tests.Upstream
{
    /// <summary>A document of the test corpus.</summary>
    internal sealed class CorpusDocument
    {
        public CorpusDocument(string id, byte[] content)
        {
            Id = id;
            Content = content;
            Syntax = DetectSyntax(content);
        }


        public string Id { get; }
        public byte[] Content { get; }
        public InvoiceSyntax Syntax { get; }


        public static InvoiceSyntax DetectSyntax(byte[] content)
        {
            try
            {
                using XmlReader reader = XmlReader.Create(new MemoryStream(content), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                reader.MoveToContent();
                return reader.NamespaceURI switch
                {
                    "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" or "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2" => InvoiceSyntax.Ubl,
                    "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100" => InvoiceSyntax.Cii,
                    _ => InvoiceSyntax.Other
                };
            }
            catch (XmlException)
            {
                return InvoiceSyntax.Other;
            }
        }


        public override string ToString()
        {
            return Id;
        }
    }


    /// <summary>
    /// The sample and test documents of the upstream suites: example invoices, the documents of the CEN unit tests,
    /// the XRechnung test suite and the XML-Mutate instances of the XRechnung Schematron.
    /// </summary>
    internal static class Corpus
    {
        private static readonly XNamespace _Vefa = "http://difi.no/xsd/vefa/validator/1.0";


        /// <summary>The XML files below a directory of an upstream suite.</summary>
        public static IEnumerable<CorpusDocument> Files(string upstream, string directory)
        {
            string root = TestData.Upstream(upstream);
            string path = Path.Combine(root, directory);
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => new CorpusDocument(upstream + "/" + Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllBytes(f)));
        }


        /// <summary>The documents embedded into the CEN unit tests (vefa test sets).</summary>
        public static IEnumerable<CorpusDocument> UnitTestDocuments(string directory)
        {
            foreach (VefaTest test in VefaTest.Load(directory))
            {
                yield return new CorpusDocument(test.Id, test.Document);
            }
        }


        /// <summary>The XML-Mutate instances of the XRechnung Schematron with their mutations applied.</summary>
        public static IEnumerable<CorpusDocument> Mutants()
        {
            return XmlMutate.LoadAll().Select(m => new CorpusDocument(m.Id, m.Document));
        }


        /// <summary>Every document of the corpus.</summary>
        public static IEnumerable<CorpusDocument> All(bool withMutants)
        {
            IEnumerable<CorpusDocument> documents = Files("cen", "ubl/examples")
                .Concat(Files("cen", "cii/examples"))
                .Concat(Files("cen", "test/testfiles"))
                .Concat(UnitTestDocuments("test/Invoice-unit-UBL"))
                .Concat(UnitTestDocuments("test/CreditNote-unit-UBL"))
                .Concat(UnitTestDocuments("test/cii"))
                .Concat(Files("xrechnung-testsuite", "src/test"))
                .Concat(Files("xrechnung-schematron", "test/instances"))
                .Concat(Files("facturx", "documentation/zugferd240en/Examples"));
            return withMutants ? documents.Concat(Mutants()) : documents;
        }
    }


    /// <summary>
    /// A test of the CEN unit tests (format of the vefa validator): a document and the rules it must pass or fail.
    /// </summary>
    internal sealed class VefaTest
    {
        private static readonly XNamespace _Vefa = "http://difi.no/xsd/vefa/validator/1.0";


        public required string Id { get; init; }
        public required string Description { get; init; }
        public required byte[] Document { get; init; }

        /// <summary>(kind, rule id, expected count or null): kind is success, error or warning.</summary>
        public required IReadOnlyList<(string Kind, string Rule, int? Count)> Expectations { get; init; }


        /// <summary>The tests of the test set files in a directory of the CEN artefacts.</summary>
        public static IEnumerable<VefaTest> Load(string directory)
        {
            string root = TestData.Upstream("cen");
            foreach (string file in Directory.GetFiles(Path.Combine(root, directory), "*.xml").OrderBy(f => f, StringComparer.Ordinal))
            {
                XDocument testSet = XDocument.Load(file, LoadOptions.PreserveWhitespace);
                int index = 0;
                foreach (XElement test in testSet.Root!.Elements(_Vefa + "test"))
                {
                    index++;
                    XElement assert = test.Element(_Vefa + "assert")!;
                    XElement document = test.Elements().Single(e => e.Name != _Vefa + "assert");
                    yield return new VefaTest
                    {
                        Id = "cen/" + directory + "/" + Path.GetFileName(file) + "#" + index,
                        Description = ((string?)assert.Element(_Vefa + "description") ?? "").Trim(),
                        Document = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting)),
                        Expectations = assert.Elements().Where(e => e.Name.LocalName is "success" or "error" or "warning")
                            .Select(e => (e.Name.LocalName, e.Value.Trim(), (int?)e.Attribute("number"))).ToList()
                    };
                }
            }
        }
    }
}
