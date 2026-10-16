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

namespace Schematron.Net.XPath.Tests
{
    /// <summary>
    /// Compares the XPath engine with Saxon-HE 12.
    ///
    /// TestData/xpath-expected.txt holds the results of TestData/xpath-expressions.txt evaluated by Saxon against
    /// TestData/xpath-context.xml (one line per expression: "OK:" + the string values joined by "|", or "ERROR:" + the error code).
    /// </summary>
    [TestClass]
    public class XPathEngineTests
    {
        private static XPathStaticContext _CreateStaticContext()
        {
            return new XPathStaticContext(new Dictionary<string, string>
            {
                ["ubl"] = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2",
                ["cac"] = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2",
                ["cbc"] = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
            });
        }


        private static string _Evaluate(string expression, XdmItem contextItem, XPathStaticContext staticContext)
        {
            try
            {
                XdmValue result = XPathExpression.Compile(expression, staticContext).Evaluate(contextItem);
                return "OK:" + string.Join("|", result.Select(i => i.StringValue.Replace("\n", "\\n")));
            }
            catch (XPathException e)
            {
                return "ERROR:" + e.Code;
            }
        }


        [TestMethod]
        public void ResultsMatchSaxon()
        {
            XdmDocument document;
            using (FileStream stream = File.OpenRead(TestData.Local("xpath-context.xml")))
            {
                document = XdmDocument.Load(stream, "file:///xpath-context.xml");
            }
            string[] expressions = File.ReadAllLines(TestData.Local("xpath-expressions.txt"));
            string[] expected = File.ReadAllLines(TestData.Local("xpath-expected.txt"));
            Assert.HasCount(expressions.Length, expected);

            List<string> failures = new List<string>();
            for (int i = 0; i < expressions.Length; i++)
            {
                string actual = _Evaluate(expressions[i], document.Root, _CreateStaticContext());
                if (actual != expected[i])
                {
                    failures.Add($"{expressions[i]}\n    expected {expected[i]}\n    actual   {actual}");
                }
            }
            Assert.IsEmpty(failures, failures.Count + " of " + expressions.Length + " expressions differ from Saxon:\n" + string.Join("\n", failures));
        }


        /// <summary>
        /// Saxon evaluates the right operand of "and" and "or" first when the left operand raises a dynamic error
        /// and only reports the error if the right operand does not decide the result. Schematron rules depend on
        /// this (e.g. tests with xs:decimal() of a sequence followed by an existence check). The expected values
        /// were determined with Saxon-HE 12.
        /// </summary>
        [TestMethod]
        [DataRow("(xs:decimal(a) = 1) or not(b)", "OK:true")]
        [DataRow("(xs:decimal(a) = 1) or exists(b)", "ERROR:XPTY0004")]
        [DataRow("(xs:decimal(a) = 1) and exists(b)", "OK:false")]
        [DataRow("(xs:decimal(a) = 1) and not(b)", "ERROR:XPTY0004")]
        [DataRow("not(b) or (xs:decimal(a) = 1)", "OK:true")]
        [DataRow("exists(b) and (xs:decimal(a) = 1)", "OK:false")]
        public void LogicalOperatorsDeferErrorsLikeSaxon(string expression, string expected)
        {
            XdmDocument document = XdmDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes("<t><a>1</a><a>2</a></t>")));
            Assert.AreEqual(expected, _Evaluate(expression, document.Root.Children.Single(), new XPathStaticContext()));
        }
    }
}
