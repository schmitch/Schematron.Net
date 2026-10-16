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
    /// <summary>
    /// Runs the XML-Mutate test instances of the XRechnung Schematron (test/instances): every mutated document must
    /// pass the rules marked schematron-valid and fail the rules marked schematron-invalid, as checked by the KoSIT
    /// build of the XRechnung Schematron.
    /// </summary>
    [TestClass]
    public class XRechnungMutationTests
    {
        public TestContext TestContext { get; set; } = null!;


        [TestMethod]
        public void ExpectationsAreMet()
        {
            List<XmlMutate> mutants = XmlMutate.LoadAll().ToList();
            List<string> failures = new List<string>();
            int checks = 0;
            foreach (XmlMutate mutant in mutants)
            {
                Dictionary<string, HashSet<string>> failedByPrefix = new Dictionary<string, HashSet<string>>();
                foreach ((string rule, bool expectValid) in mutant.Valid.Select(r => (r, true)).Concat(mutant.Invalid.Select(r => (r, false))))
                {
                    checks++;
                    int colon = rule.IndexOf(':');
                    string prefix = rule.Substring(0, colon);
                    string id = rule.Substring(colon + 1);
                    if (!failedByPrefix.TryGetValue(prefix, out HashSet<string>? failed))
                    {
                        SchematronResult result = XmlMutate.RuleSetOf(prefix).Schema.Validate(new MemoryStream(mutant.Document));
                        failed = result.FailedAssertions.Select(m => m.Id ?? "").ToHashSet();
                        failedByPrefix[prefix] = failed;
                    }
                    if (failed.Contains(id) == expectValid)
                    {
                        failures.Add(mutant.Id + ": " + id + " should " + (expectValid ? "pass" : "fail") + " (" + mutant.Description + ")");
                    }
                }
            }
            TestContext.WriteLine(mutants.Count + " mutated documents, " + checks + " expectations");
            Assert.IsGreaterThan(200, mutants.Count, "too few test instances");
            Assert.IsEmpty(failures, failures.Count + " of " + checks + " expectations of " + mutants.Count + " mutated documents are not met:\n" + string.Join("\n", failures));
        }
    }
}
