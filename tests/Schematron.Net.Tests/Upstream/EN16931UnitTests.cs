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
    /// Runs the unit tests of the CEN EN 16931 validation artefacts (test/*-unit-UBL and test/cii, format of the vefa
    /// validator) against the CEN Schematron sources: every test names the rules its document must pass or fail.
    /// </summary>
    [TestClass]
    public class EN16931UnitTests
    {
        public TestContext TestContext { get; set; } = null!;


        [TestMethod]
        [DataRow("test/Invoice-unit-UBL", "cen-ubl")]
        [DataRow("test/CreditNote-unit-UBL", "cen-ubl")]
        [DataRow("test/cii", "cen-cii")]
        public void UnitTestsPass(string directory, string ruleSet)
        {
            SchematronSchema schema = RuleSet.Get(ruleSet).Schema;
            List<VefaTest> tests = VefaTest.Load(directory).ToList();
            List<string> failures = new List<string>();
            foreach (VefaTest test in tests)
            {
                List<SchematronMessage> failed = schema.Validate(new MemoryStream(test.Document)).FailedAssertions.ToList();
                foreach ((string kind, string rule, int? count) in test.Expectations)
                {
                    List<SchematronMessage> matches = failed.Where(m => m.Id == rule).ToList();
                    string where = test.Id + " (" + test.Description + "): ";
                    if (kind == "success")
                    {
                        if (matches.Count > 0)
                        {
                            failures.Add(where + rule + " was expected to pass but failed: " + matches[0].Text.Trim());
                        }
                        continue;
                    }
                    string flag = kind == "error" ? "fatal" : "warning";
                    int actual = matches.Count(m => m.Flag == flag);
                    if (count.HasValue ? actual != count.Value : actual == 0)
                    {
                        failures.Add(where + rule + " was expected as " + flag + (count.HasValue ? " " + count + " times" : "") + ", found " + actual);
                    }
                }
            }
            TestContext.WriteLine(directory + ": " + tests.Count + " tests, " + tests.Sum(t => t.Expectations.Count) + " expectations");
            Assert.IsNotEmpty(tests, "no tests found in " + directory);
            Assert.IsEmpty(failures, failures.Count + " of " + tests.Count + " unit tests failed:\n" + string.Join("\n", failures));
        }
    }
}
