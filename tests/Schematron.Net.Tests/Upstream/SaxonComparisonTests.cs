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
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Schematron.Net.Tests.Upstream
{
    /// <summary>
    /// Compares this library with the reference: every official rule set validates every document of the corpus of
    /// its syntax (examples, unit test documents, test suites and the mutated XRechnung instances) twice, once from its
    /// Schematron source with this library and once from the XSLT its publisher compiled, run by Saxon. The findings
    /// (assertion id, flag, role, location and text, in order) and dynamic errors must be the same.
    /// </summary>
    [TestClass]
    public class SaxonComparisonTests
    {
        private static readonly Lazy<List<CorpusDocument>> _Corpus = new Lazy<List<CorpusDocument>>(() => Corpus.All(withMutants: false).ToList());
        private static readonly Lazy<List<CorpusDocument>> _Mutants = new Lazy<List<CorpusDocument>>(() => Corpus.Mutants().ToList());

        public TestContext TestContext { get; set; } = null!;


        public static IEnumerable<object[]> RuleSets => RuleSet.All.Select(r => new object[] { r.Name });


        [TestMethod]
        [DynamicData(nameof(RuleSets))]
        public void SameFindingsAsSaxon(string name)
        {
            RuleSet ruleSet = RuleSet.Get(name);
            // the mutated XRechnung instances exercise the error paths of the CEN and XRechnung rules
            List<CorpusDocument> documents = _Corpus.Value.Concat(name.StartsWith("facturx-", StringComparison.Ordinal) ? Enumerable.Empty<CorpusDocument>() : _Mutants.Value)
                .Where(d => d.Syntax == ruleSet.Syntax).ToList();
            SchematronSchema schema = ruleSet.Schema;
            string xslt = ruleSet.Xslt;

            ConcurrentBag<string> differences = new ConcurrentBag<string>();
            int findings = 0;
            TimeSpan ours = TimeSpan.Zero;
            object sync = new object();
            Parallel.ForEach(documents, document =>
            {
                Outcome expected = SaxonReference.Validate(xslt, document.Content);
                long start = Stopwatch.GetTimestamp();
                Outcome actual = Outcome.Of(() => Finding.Of(schema.Validate(new MemoryStream(document.Content))), e => e is SchematronException s ? (s.ErrorCode ?? "SCHEMATRON") : null);
                lock (sync)
                {
                    ours += Stopwatch.GetElapsedTime(start);
                    findings += expected.Findings.Count;
                }
                string? diff = actual.Diff(expected);
                if (diff != null)
                {
                    differences.Add(document.Id + ": " + diff);
                }
            });

            TestContext.WriteLine($"{name}: {documents.Count} documents, {findings} findings, this library {ours.TotalMilliseconds / Math.Max(1, documents.Count):F1} ms per document");
            Assert.IsGreaterThan(20, documents.Count, "too few documents for " + name);
            List<string> sorted = differences.OrderBy(d => d, StringComparer.Ordinal).ToList();
            Assert.IsEmpty(sorted, sorted.Count + " of " + documents.Count + " documents differ from Saxon:\n" + string.Join("\n", sorted.Take(40)));
        }
    }
}
