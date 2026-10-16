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
using System.Xml.Linq;
using Schematron.Net.XPath;

namespace Schematron.Net.XPath.Tests
{
    internal enum Qt3Status
    {
        Pass,
        Fail,
        Skip
    }


    internal sealed record Qt3Outcome(Qt3Status Status, string Detail)
    {
        public static readonly Qt3Outcome Passed = new Qt3Outcome(Qt3Status.Pass, "");

        public static Qt3Outcome Failed(string detail) => new Qt3Outcome(Qt3Status.Fail, detail);

        public static Qt3Outcome Skipped(string reason) => new Qt3Outcome(Qt3Status.Skip, reason);
    }


    /// <summary>
    /// A minimal driver for the W3C XQuery / XPath test suite (QT3, https://github.com/w3c/qt3tests): runs the
    /// XPath test cases of selected test sets with the engine of this library.
    ///
    /// The engine follows Saxon 12, which implements XPath 3.1 semantics also for XSLT 2.0 stylesheets, so the
    /// test cases for XPath 3.1 apply (spec XP20+, XP30+, XP31, XP31+), not those that only describe XPath 2.0 or
    /// 3.0 behaviour that later versions changed. Test cases that need optional features (schema awareness,
    /// higher-order functions, ...), XQuery or environment parts the driver does not provide (collations,
    /// collections, decimal formats) are skipped.
    /// </summary>
    internal sealed class Qt3Driver
    {
        private static readonly XNamespace _Q = "http://www.w3.org/2010/09/qt-fots-catalog";

        /// <summary>Optional features the engine supports.</summary>
        private static readonly HashSet<string> _SupportedFeatures = new HashSet<string> { "namespace-axis" };

        private readonly string _Root;
        private readonly Dictionary<string, XElement> _CatalogEnvironments;


        public Qt3Driver(string root)
        {
            _Root = root;
            XDocument catalog = XDocument.Load(Path.Combine(root, "catalog.xml"));
            _CatalogEnvironments = catalog.Root!.Elements(_Q + "environment").ToDictionary(e => (string)e.Attribute("name")!);
            TestSetFiles = catalog.Root.Elements(_Q + "test-set").ToDictionary(e => (string)e.Attribute("name")!, e => Path.Combine(root, (string)e.Attribute("file")!));
        }


        public IReadOnlyDictionary<string, string> TestSetFiles { get; }


        /// <summary>Runs all test cases of a test set: (test case name, outcome).</summary>
        public IEnumerable<(string Name, Qt3Outcome Outcome)> Run(string testSet)
        {
            string file = TestSetFiles[testSet];
            XDocument document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
            XElement set = document.Root!;
            Dictionary<string, XElement> localEnvironments = set.Elements(_Q + "environment").Where(e => e.Attribute("name") != null).ToDictionary(e => (string)e.Attribute("name")!);
            List<XElement> setDependencies = set.Elements(_Q + "dependency").ToList();
            foreach (XElement testCase in set.Elements(_Q + "test-case"))
            {
                string name = (string)testCase.Attribute("name")!;
                Qt3Outcome outcome;
                try
                {
                    outcome = _Run(testCase, setDependencies, localEnvironments, Path.GetDirectoryName(file)!);
                }
                catch (Exception e) when (e is not AssertFailedException)
                {
                    outcome = Qt3Outcome.Failed("driver/engine exception " + e.GetType().Name + ": " + e.Message);
                }
                yield return (name, outcome);
            }
        }


        private Qt3Outcome _Run(XElement testCase, List<XElement> setDependencies, Dictionary<string, XElement> localEnvironments, string directory)
        {
            List<XElement> dependencies = testCase.Elements(_Q + "dependency").ToList();
            // a spec dependency of the test case replaces the one of the test set
            if (!dependencies.Any(d => (string?)d.Attribute("type") == "spec"))
            {
                dependencies.AddRange(setDependencies.Where(d => (string?)d.Attribute("type") == "spec"));
            }
            dependencies.AddRange(setDependencies.Where(d => (string?)d.Attribute("type") != "spec"));
            string? unsatisfied = dependencies.Select(_Unsatisfied).FirstOrDefault(r => r != null);
            if (unsatisfied != null)
            {
                return Qt3Outcome.Skipped(unsatisfied);
            }

            // environment
            XElement? environmentElement = testCase.Element(_Q + "environment");
            string environmentDirectory = directory;
            if (environmentElement != null && environmentElement.Attribute("ref") != null)
            {
                string reference = (string)environmentElement.Attribute("ref")!;
                if (localEnvironments.TryGetValue(reference, out XElement? local))
                {
                    environmentElement = local;
                }
                else if (_CatalogEnvironments.TryGetValue(reference, out XElement? global))
                {
                    environmentElement = global;
                    environmentDirectory = _Root;
                }
                else
                {
                    return Qt3Outcome.Skipped("unknown environment " + reference);
                }
            }
            Qt3Environment environment = new Qt3Environment();
            if (environmentElement != null)
            {
                string? unsupported = environment.Load(environmentElement, environmentDirectory);
                if (unsupported != null)
                {
                    return Qt3Outcome.Skipped(unsupported);
                }
            }

            XElement test = testCase.Element(_Q + "test")!;
            string expression = test.Attribute("file") != null ? File.ReadAllText(Path.Combine(directory, (string)test.Attribute("file")!)) : test.Value;
            Qt3Result result = environment.Evaluate(expression);
            return _Check(testCase.Element(_Q + "result")!.Elements().Single(), result, environment, directory);
        }


        private static string? _Unsatisfied(XElement dependency)
        {
            string type = (string?)dependency.Attribute("type") ?? "";
            string value = ((string?)dependency.Attribute("value") ?? "").Trim();
            bool satisfied = (string?)dependency.Attribute("satisfied") != "false";
            switch (type)
            {
                case "spec":
                    string[] specs = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    return specs.Any(s => s is "XP20+" or "XP30+" or "XP31" or "XP31+") ? null : "spec " + value;
                case "feature":
                    return _SupportedFeatures.Contains(value) == satisfied ? null : "feature " + value + (satisfied ? "" : " (unsatisfied)");
                case "xml-version":
                    return value.Contains("1.0", StringComparison.Ordinal) == satisfied ? null : "xml-version " + value;
                case "xsd-version":
                    // Saxon 12 uses the XSD 1.1 data types
                    return (value == "1.1") == satisfied ? null : "xsd-version " + value;
                case "default-language":
                case "language":
                    return (value == "en") == satisfied ? null : type + " " + value;
                default:
                    return type + " " + value;
            }
        }


        private Qt3Outcome _Check(XElement assertion, Qt3Result result, Qt3Environment environment, string directory)
        {
            string name = assertion.Name.LocalName;
            switch (name)
            {
                case "all-of":
                    {
                        foreach (XElement child in assertion.Elements())
                        {
                            Qt3Outcome outcome = _Check(child, result, environment, directory);
                            if (outcome.Status != Qt3Status.Pass)
                            {
                                return outcome;
                            }
                        }
                        return Qt3Outcome.Passed;
                    }
                case "any-of":
                    {
                        List<Qt3Outcome> outcomes = assertion.Elements().Select(c => _Check(c, result, environment, directory)).ToList();
                        if (outcomes.Any(o => o.Status == Qt3Status.Pass))
                        {
                            return Qt3Outcome.Passed;
                        }
                        return outcomes.FirstOrDefault(o => o.Status == Qt3Status.Fail) ?? outcomes[0];
                    }
                case "not":
                    {
                        Qt3Outcome outcome = _Check(assertion.Elements().Single(), result, environment, directory);
                        return outcome.Status switch
                        {
                            Qt3Status.Pass => Qt3Outcome.Failed("not(" + assertion.Elements().Single().Name.LocalName + ") holds"),
                            Qt3Status.Fail => Qt3Outcome.Passed,
                            _ => outcome
                        };
                    }
                case "error":
                    {
                        string code = (string)assertion.Attribute("code")!;
                        if (result.Error == null)
                        {
                            return Qt3Outcome.Failed("expected error " + code + ", got " + result.Describe());
                        }
                        return code == "*" || result.Error == code ? Qt3Outcome.Passed : Qt3Outcome.Failed("expected error " + code + ", got error " + result.Error);
                    }
                case "serialization-matches":
                case "assert-serialization-error":
                case "assert-message":
                case "assert-result-document":
                    return Qt3Outcome.Skipped("assertion " + name);
            }

            if (result.Error != null)
            {
                return Qt3Outcome.Failed("error " + result.Error + " (" + result.ErrorMessage + "), expected " + name + " " + assertion.Value.Trim());
            }
            XdmValue value = result.Value!;
            string text = assertion.Value;
            bool ok;
            switch (name)
            {
                case "assert-true":
                    ok = _IsBoolean(value, true);
                    break;
                case "assert-false":
                    ok = _IsBoolean(value, false);
                    break;
                case "assert-empty":
                    ok = value.Count == 0;
                    break;
                case "assert-count":
                    ok = value.Count == int.Parse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "assert-eq":
                case "assert-deep-eq":
                    ok = environment.Holds("deep-equal($result, (" + text + "))", value);
                    break;
                case "assert-type":
                    ok = environment.Holds("$result instance of " + text, value);
                    break;
                case "assert":
                    ok = environment.Holds(text, value);
                    break;
                case "assert-string-value":
                    {
                        string actual = string.Join(" ", value.Select(Qt3Result.StringValue));
                        bool normalize = (string?)assertion.Attribute("normalize-space") == "true";
                        ok = normalize ? _NormalizeSpace(actual) == _NormalizeSpace(text) : actual == text;
                        break;
                    }
                case "assert-permutation":
                    {
                        Qt3Result expected = environment.Evaluate(text);
                        ok = expected.Value != null && value.Select(Qt3Result.Key).OrderBy(k => k, StringComparer.Ordinal)
                            .SequenceEqual(expected.Value.Select(Qt3Result.Key).OrderBy(k => k, StringComparer.Ordinal));
                        break;
                    }
                case "assert-xml":
                    {
                        string xml = assertion.Attribute("file") != null ? File.ReadAllText(Path.Combine(directory, (string)assertion.Attribute("file")!)) : text;
                        ok = environment.EqualsXml(value, xml);
                        break;
                    }
                default:
                    return Qt3Outcome.Skipped("assertion " + name);
            }
            return ok ? Qt3Outcome.Passed : Qt3Outcome.Failed(name + " " + text.Trim() + " does not hold for " + result.Describe());
        }


        private static bool _IsBoolean(XdmValue value, bool expected)
        {
            return value.Count == 1 && value[0] is XdmAtomicValue atomic && atomic.TypeName == "xs:boolean" && atomic.StringValue == (expected ? "true" : "false");
        }


        private static string _NormalizeSpace(string value)
        {
            return string.Join(" ", value.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }


    /// <summary>The result of evaluating a test expression: a sequence or an error code.</summary>
    internal sealed class Qt3Result
    {
        public XdmValue? Value { get; init; }
        public string? Error { get; init; }
        public string? ErrorMessage { get; init; }


        public static string StringValue(XdmItem item)
        {
            return item.StringValue;
        }


        public static string Key(XdmItem item)
        {
            return item is XdmAtomicValue atomic ? atomic.TypeName + ":" + atomic.StringValue : "node:" + StringValue(item);
        }


        public string Describe()
        {
            if (Error != null)
            {
                return "error " + Error;
            }
            string items = string.Join(", ", Value!.Take(5).Select(i => i is XdmAtomicValue a ? a.TypeName + "(\"" + a.StringValue + "\")" : "node(\"" + _Shorten(StringValue(i)) + "\")"));
            return "(" + items + (Value!.Count > 5 ? ", ... " + Value.Count + " items" : "") + ")";
        }


        private static string _Shorten(string value)
        {
            return value.Length > 40 ? value.Substring(0, 40) + "..." : value;
        }
    }


    /// <summary>
    /// The environment of a test case: context document, variables (sources with a $role and params), namespaces
    /// and static base URI.
    /// </summary>
    internal sealed class Qt3Environment : IXPathVariableResolver, IDocumentResolver
    {
        private static readonly XNamespace _Q = "http://www.w3.org/2010/09/qt-fots-catalog";
        private readonly Dictionary<string, XdmValue> _Variables = new Dictionary<string, XdmValue>();
        private readonly Dictionary<string, string> _Namespaces = new Dictionary<string, string>();
        private readonly Dictionary<string, XdmDocument> _Documents = new Dictionary<string, XdmDocument>();
        private string _DefaultElementNamespace = "";
        private string? _BaseUri;
        private XdmItem? _ContextItem;


        /// <summary>Loads the environment; returns the reason why it is not supported, or null.</summary>
        public string? Load(XElement environment, string directory)
        {
            foreach (XElement part in environment.Elements())
            {
                switch (part.Name.LocalName)
                {
                    case "source":
                        {
                            if (part.Attribute("validation") != null && (string?)part.Attribute("validation") != "skip")
                            {
                                return "environment: validated source";
                            }
                            string path = Path.Combine(directory, (string)part.Attribute("file")!);
                            string uri = (string?)part.Attribute("uri") ?? new Uri(path).AbsoluteUri;
                            XdmDocument document;
                            using (FileStream stream = File.OpenRead(path))
                            {
                                document = XdmDocument.Load(stream, new Uri(path).AbsoluteUri);
                            }
                            _Documents[uri] = document;
                            _Documents[new Uri(path).AbsoluteUri] = document;
                            string? role = (string?)part.Attribute("role");
                            if (role == ".")
                            {
                                _ContextItem = document.Root;
                            }
                            else if (role != null && role.StartsWith('$'))
                            {
                                _Variables[role.Substring(1)] = XdmValue.Of(document.Root);
                            }
                            break;
                        }
                    case "param":
                        {
                            if (part.Attribute("source") != null)
                            {
                                return "environment: param with source";
                            }
                            Qt3Result value = Evaluate((string)part.Attribute("select")!);
                            if (value.Error != null)
                            {
                                return "environment: param " + value.Error;
                            }
                            _Variables[(string)part.Attribute("name")!] = value.Value!;
                            break;
                        }
                    case "namespace":
                        {
                            string prefix = (string?)part.Attribute("prefix") ?? "";
                            if (prefix.Length == 0)
                            {
                                _DefaultElementNamespace = (string)part.Attribute("uri")!;
                            }
                            else
                            {
                                _Namespaces[prefix] = (string)part.Attribute("uri")!;
                            }
                            break;
                        }
                    case "static-base-uri":
                        {
                            string uri = (string)part.Attribute("uri")!;
                            _BaseUri = uri == "#UNDEFINED" ? null : uri;
                            break;
                        }
                    case "description":
                    case "created":
                    case "modified":
                        break;
                    default:
                        return "environment: " + part.Name.LocalName;
                }
            }
            return null;
        }


        public Qt3Result Evaluate(string expression, XdmValue? result = null)
        {
            try
            {
                XPathStaticContext context = new XPathStaticContext(_Namespaces, _BaseUri ?? "file:///qt3/") { DefaultElementNamespace = _DefaultElementNamespace };
                XPathExpression compiled = XPathExpression.Compile(expression, context);
                return new Qt3Result { Value = compiled.Evaluate(_ContextItem, new XPathDynamicContext(this), new ResultVariable(this, result)) };
            }
            catch (XPathException e)
            {
                return new Qt3Result { Error = e.Code, ErrorMessage = e.Message };
            }
        }


        /// <summary>Evaluates a boolean assertion about $result.</summary>
        public bool Holds(string expression, XdmValue result)
        {
            Qt3Result outcome = Evaluate(expression, result);
            return outcome.Value != null && _IsTrue(outcome.Value);
        }


        private static bool _IsTrue(XdmValue value)
        {
            return value.Count == 1 && value[0] is XdmAtomicValue atomic && atomic.TypeName == "xs:boolean" && atomic.StringValue == "true";
        }


        /// <summary>Compares the result with a serialized XML fragment (deep-equal of the nodes).</summary>
        public bool EqualsXml(XdmValue result, string xml)
        {
            XdmDocument expected = XdmDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes("<qt3-wrapper>" + xml + "</qt3-wrapper>")));
            XdmNode wrapper = expected.Root.Children.Single();
            List<XdmItem> actual = new List<XdmItem>();
            foreach (XdmItem item in result)
            {
                if (item is XdmNode node && node.Kind == XdmNodeKind.Document)
                {
                    actual.AddRange(node.Children);
                }
                else
                {
                    actual.Add(item);
                }
            }
            _Variables["qt3-expected"] = XdmValue.Of(wrapper.Children);
            return Holds("deep-equal($result, $qt3-expected)", XdmValue.Of(actual));
        }


        public bool TryResolve(string expandedName, out XdmValue value)
        {
            return _Variables.TryGetValue(expandedName, out value!);
        }


        public XdmDocument Resolve(string absoluteUri)
        {
            if (_Documents.TryGetValue(absoluteUri, out XdmDocument? document))
            {
                return document;
            }
            throw new FileNotFoundException("not available in the test environment: " + absoluteUri);
        }


        private sealed class ResultVariable : IXPathVariableResolver
        {
            private readonly Qt3Environment _Environment;
            private readonly XdmValue? _Result;


            public ResultVariable(Qt3Environment environment, XdmValue? result)
            {
                _Environment = environment;
                _Result = result;
            }


            public bool TryResolve(string expandedName, out XdmValue value)
            {
                if (_Result != null && expandedName == "result")
                {
                    value = _Result;
                    return true;
                }
                return _Environment.TryResolve(expandedName, out value);
            }
        }
    }
}
