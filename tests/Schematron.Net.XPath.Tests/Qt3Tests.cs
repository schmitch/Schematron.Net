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

namespace Schematron.Net.XPath.Tests
{
    /// <summary>
    /// Runs the test sets of the W3C QT3 test suite for the functions, operators and expressions that Schematron
    /// rules use (all functions of the EN 16931, XRechnung, Peppol and Factur-X rules and the XPath 2.0 core).
    ///
    /// Not every test case passes: TestData/qt3-known-failures.txt lists the failing ones. A test set fails if a test
    /// case fails that is not listed (a regression), or if a listed one passes (then the list must be updated).
    /// Set QT3_UPDATE_BASELINE=1 to rewrite the list from the current results.
    /// </summary>
    [TestClass]
    public class Qt3Tests
    {
        private const string _BaselineFile = "qt3-known-failures.txt";

        /// <summary>The test sets that are run.</summary>
        internal static readonly string[] TestSets =
        {
            // functions used by the official rules, and their relatives
            "fn-abs", "fn-avg", "fn-boolean", "fn-ceiling", "fn-codepoint-equal", "fn-codepoints-to-string", "fn-compare", "fn-concat",
            "fn-contains", "fn-count", "fn-data", "fn-deep-equal", "fn-distinct-values", "fn-empty", "fn-ends-with", "fn-exactly-one",
            "fn-exists", "fn-false", "fn-floor", "fn-index-of", "fn-insert-before", "fn-last", "fn-local-name", "fn-lower-case",
            "fn-matches", "fn-matches.re", "fn-max", "fn-min", "fn-name", "fn-namespace-uri", "fn-node-name", "fn-normalize-space",
            "fn-not", "fn-number", "fn-one-or-more", "fn-position", "fn-remove", "fn-replace", "fn-reverse", "fn-root", "fn-round",
            "fn-round-half-to-even", "fn-starts-with", "fn-string", "fn-string-join", "fn-string-length", "fn-string-to-codepoints",
            "fn-subsequence", "fn-substring", "fn-substring-after", "fn-substring-before", "fn-sum", "fn-tokenize", "fn-translate",
            "fn-true", "fn-upper-case", "fn-zero-or-one", "fn-year-from-date", "fn-month-from-date", "fn-day-from-date",
            // operators
            "op-numeric-add", "op-numeric-subtract", "op-numeric-multiply", "op-numeric-divide", "op-numeric-integer-divide",
            "op-numeric-mod", "op-numeric-unary-minus", "op-numeric-unary-plus", "op-numeric-equal", "op-numeric-less-than",
            "op-numeric-greater-than", "op-boolean-equal", "op-boolean-less-than", "op-boolean-greater-than", "op-string-equal",
            "op-string-less-than", "op-string-greater-than", "op-date-equal", "op-date-less-than", "op-date-greater-than",
            "op-subtract-dates", "op-to", "op-union", "op-intersect", "op-except", "op-concat", "op-bang", "op-is-same-node",
            "op-node-before", "op-node-after",
            // expressions
            "prod-AxisStep", "prod-AxisStep.abbr", "prod-AxisStep.ancestor", "prod-AxisStep.ancestor-or-self",
            "prod-AxisStep.following", "prod-AxisStep.following-sibling", "prod-AxisStep.preceding",
            "prod-AxisStep.preceding-sibling", "prod-AxisStep.unabbr", "prod-CastableExpr", "prod-CastExpr", "prod-ContextItemExpr",
            "prod-EQName", "prod-FunctionCall", "prod-GeneralComp.eq", "prod-GeneralComp.ge", "prod-GeneralComp.gt",
            "prod-GeneralComp.le", "prod-GeneralComp.lt", "prod-GeneralComp.ne", "prod-IfExpr", "prod-InstanceofExpr",
            "prod-Literal", "prod-NameTest", "prod-NodeTest", "prod-OrExpr", "prod-ParenthesizedExpr", "prod-PathExpr",
            "prod-Predicate", "prod-QuantifiedExpr", "prod-SequenceType", "prod-StepExpr", "prod-TreatExpr", "prod-ValueComp",
            "prod-ForClause", "prod-LetClause",
            // data types
            "xs-double", "xs-float", "xs-numeric", "xs-normalizedString", "xs-token"
        };

        private static readonly Lazy<Qt3Driver> _Driver = new Lazy<Qt3Driver>(() => new Qt3Driver(TestData.Upstream("qt3")));
        private static readonly ConcurrentDictionary<string, List<(string Name, Qt3Outcome Outcome)>> _Results = new ConcurrentDictionary<string, List<(string, Qt3Outcome)>>();

        public TestContext TestContext { get; set; } = null!;


        public static IEnumerable<object[]> TestSetNames => TestSets.Select(s => new object[] { s });


        private static HashSet<string> _KnownFailures()
        {
            return File.ReadAllLines(TestData.Local(_BaselineFile))
                .Select(l => l.Split('#')[0].Trim())
                .Where(l => l.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
        }


        [TestMethod]
        [DynamicData(nameof(TestSetNames))]
        public void TestSet(string testSet)
        {
            List<(string Name, Qt3Outcome Outcome)> results = _Driver.Value.Run(testSet).ToList();
            _Results[testSet] = results;
            int passed = results.Count(r => r.Outcome.Status == Qt3Status.Pass);
            int failed = results.Count(r => r.Outcome.Status == Qt3Status.Fail);
            TestContext.WriteLine($"{testSet}: {passed} passed, {failed} failed, {results.Count - passed - failed} skipped");
            if (Environment.GetEnvironmentVariable("QT3_UPDATE_BASELINE") == "1")
            {
                return;
            }

            HashSet<string> known = _KnownFailures();
            List<string> regressions = results.Where(r => r.Outcome.Status == Qt3Status.Fail && !known.Contains(testSet + "/" + r.Name))
                .Select(r => r.Name + ": " + r.Outcome.Detail).ToList();
            List<string> fixedCases = results.Where(r => r.Outcome.Status != Qt3Status.Fail && known.Contains(testSet + "/" + r.Name))
                .Select(r => r.Name + " (" + r.Outcome.Status + ")").ToList();
            Assert.IsEmpty(regressions, regressions.Count + " test cases fail that are not in " + _BaselineFile + ":\n" + string.Join("\n", regressions));
            Assert.IsEmpty(fixedCases, fixedCases.Count + " test cases in " + _BaselineFile + " no longer fail, remove them (or set QT3_UPDATE_BASELINE=1):\n" + string.Join("\n", fixedCases));
        }


        [ClassCleanup]
        public static void WriteBaseline()
        {
            if (Environment.GetEnvironmentVariable("QT3_UPDATE_BASELINE") != "1" || _Results.IsEmpty)
            {
                return;
            }
            string? source = TestData.SourceDirectory();
            if (source == null)
            {
                return;
            }
            List<string> lines = new List<string>
            {
                "# QT3 test cases that fail with this library (see Conformance/Qt3Tests.cs). One <test set>/<test case> per",
                "# line, followed by the reason. Generated with QT3_UPDATE_BASELINE=1; remove lines when a fix makes them pass.",
                ""
            };
            foreach (string testSet in TestSets.Where(_Results.ContainsKey))
            {
                foreach ((string name, Qt3Outcome outcome) in _Results[testSet].Where(r => r.Outcome.Status == Qt3Status.Fail))
                {
                    string detail = outcome.Detail.Length > 160 ? outcome.Detail.Substring(0, 160) + "..." : outcome.Detail;
                    lines.Add(testSet + "/" + name + "  # " + _Printable(detail));
                }
            }
            File.WriteAllLines(Path.Combine(source, "TestData", _BaselineFile), lines);
        }


        /// <summary>Escapes line breaks, control characters and lone surrogates of a failure detail.</summary>
        private static string _Printable(string value)
        {
            System.Text.StringBuilder result = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    result.Append(c).Append(value[++i]);
                }
                else if (char.IsControl(c) || char.IsSurrogate(c))
                {
                    result.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    result.Append(c);
                }
            }
            return result.ToString();
        }
    }
}
