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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Schematron.Net
{
    /// <summary>
    /// The kind of a Schematron result message.
    /// </summary>
    public enum SchematronMessageKind
    {
        /// <summary>An sch:assert whose test evaluated to false (svrl:failed-assert).</summary>
        FailedAssert,

        /// <summary>An sch:report whose test evaluated to true (svrl:successful-report).</summary>
        SuccessfulReport
    }


    /// <summary>
    /// A diagnostic referenced by an assertion (svrl:diagnostic-reference).
    /// </summary>
    public sealed class SchematronDiagnostic
    {
        internal SchematronDiagnostic(string id, string text)
        {
            Id = id;
            Text = text;
        }


        /// <summary>The id of the sch:diagnostic.</summary>
        public string Id { get; }

        /// <summary>The text of the diagnostic.</summary>
        public string Text { get; }
    }


    /// <summary>
    /// A failed assertion or successful report, as written to SVRL.
    /// </summary>
    public sealed class SchematronMessage
    {
        internal SchematronMessage()
        {
        }


        /// <summary>Failed assertion or successful report.</summary>
        public SchematronMessageKind Kind { get; internal set; }

        /// <summary>The id of the assertion (e.g. BR-01), the "code" of the message in the KoSIT report.</summary>
        public string Id { get; internal set; }

        /// <summary>The flag of the assertion (fatal, error, warning, information, ...).</summary>
        public string Flag { get; internal set; }

        /// <summary>The role of the assertion.</summary>
        public string Role { get; internal set; }

        /// <summary>The see attribute of the assertion.</summary>
        public string See { get; internal set; }

        /// <summary>The icon attribute of the assertion.</summary>
        public string Icon { get; internal set; }

        /// <summary>The fpi attribute of the assertion.</summary>
        public string Fpi { get; internal set; }

        /// <summary>The test expression of the assertion.</summary>
        public string Test { get; internal set; }

        /// <summary>The message text of the assertion (with sch:value-of and sch:name evaluated).</summary>
        public string Text { get; internal set; }

        /// <summary>
        /// The location of the node the assertion failed for, as XPath in the notation of SchXslt and the KoSIT
        /// validator, e.g. /Q{urn:...:Invoice-2}Invoice[1]/Q{urn:...}AccountingSupplierParty[1].
        /// </summary>
        public string Location { get; internal set; }

        /// <summary>Line of the node in the validated document (if known).</summary>
        public int? LineNumber { get; internal set; }

        /// <summary>Column of the node in the validated document (if known).</summary>
        public int? LinePosition { get; internal set; }

        /// <summary>The id of the pattern the assertion belongs to.</summary>
        public string PatternId { get; internal set; }

        /// <summary>The context of the rule the assertion belongs to.</summary>
        public string RuleContext { get; internal set; }

        /// <summary>The diagnostics referenced by the assertion.</summary>
        public IReadOnlyList<SchematronDiagnostic> Diagnostics { get; internal set; } = new SchematronDiagnostic[0];


        /// <inheritdoc/>
        public override string ToString()
        {
            return (Id ?? "") + " (" + (Flag ?? Role ?? "") + ") " + Location + ": " + (Text ?? "").Trim();
        }
    }


    /// <summary>
    /// A pattern that was active during the validation (svrl:active-pattern).
    /// </summary>
    public sealed class SchematronActivePattern
    {
        internal SchematronActivePattern(string id, string name)
        {
            Id = id;
            Name = name;
        }


        /// <summary>The id of the pattern.</summary>
        public string Id { get; }

        /// <summary>The name (title) of the pattern.</summary>
        public string Name { get; }
    }


    /// <summary>
    /// A rule that fired for a node (svrl:fired-rule); the messages of the firing start at <see cref="FirstMessage"/>.
    /// </summary>
    internal sealed class SchematronFiredRule
    {
        public SchematronFiredRule(string patternId, string ruleId, string context, int firstMessage)
        {
            PatternId = patternId;
            RuleId = ruleId;
            Context = context;
            FirstMessage = firstMessage;
        }


        public string PatternId { get; }

        public string RuleId { get; }

        public string Context { get; }

        public int FirstMessage { get; }
    }


    /// <summary>
    /// The result of a Schematron validation, the equivalent of an SVRL document.
    /// </summary>
    public sealed class SchematronResult
    {
        private static readonly XNamespace _Svrl = "http://purl.oclc.org/dsdl/svrl";


        private readonly IReadOnlyList<SchematronFiredRule> _FiredRules;


        internal SchematronResult(string title, string schemaVersion, IReadOnlyList<KeyValuePair<string, string>> namespaces, IReadOnlyList<SchematronActivePattern> activePatterns, IReadOnlyList<SchematronMessage> messages, IReadOnlyList<SchematronFiredRule> firedRules)
        {
            Title = title;
            SchemaVersion = schemaVersion;
            Namespaces = namespaces;
            ActivePatterns = activePatterns;
            Messages = messages;
            _FiredRules = firedRules;
        }


        /// <summary>The title of the Schematron schema.</summary>
        public string Title { get; }

        /// <summary>The schemaVersion of the Schematron schema.</summary>
        public string SchemaVersion { get; }

        /// <summary>The namespaces declared with sch:ns.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Namespaces { get; }

        /// <summary>The patterns that were active.</summary>
        public IReadOnlyList<SchematronActivePattern> ActivePatterns { get; }

        /// <summary>The failed assertions and successful reports in the order SchXslt reports them.</summary>
        public IReadOnlyList<SchematronMessage> Messages { get; }

        /// <summary>How many times a rule fired.</summary>
        public int FiredRuleCount => _FiredRules.Count;

        /// <summary>The failed assertions.</summary>
        public IEnumerable<SchematronMessage> FailedAssertions => Messages.Where(m => m.Kind == SchematronMessageKind.FailedAssert);

        /// <summary>The successful reports.</summary>
        public IEnumerable<SchematronMessage> SuccessfulReports => Messages.Where(m => m.Kind == SchematronMessageKind.SuccessfulReport);


        /// <summary>
        /// Returns the result as Schematron Validation Report Language (SVRL) document.
        /// </summary>
        public XDocument ToSvrl()
        {
            XElement root = new XElement(_Svrl + "schematron-output", new XAttribute(XNamespace.Xmlns + "svrl", _Svrl.NamespaceName));
            if (Title != null)
            {
                root.SetAttributeValue("title", Title);
            }
            if (SchemaVersion != null)
            {
                root.SetAttributeValue("schemaVersion", SchemaVersion);
            }
            foreach (KeyValuePair<string, string> ns in Namespaces)
            {
                root.Add(new XElement(_Svrl + "ns-prefix-in-attribute-values", new XAttribute("prefix", ns.Key), new XAttribute("uri", ns.Value)));
            }
            // the fired rules in the order they fired, each followed by the messages of the firing
            Dictionary<string, List<int>> firingsByPattern = Enumerable.Range(0, _FiredRules.Count).GroupBy(i => _FiredRules[i].PatternId ?? "").ToDictionary(g => g.Key, g => g.ToList());
            foreach (SchematronActivePattern pattern in ActivePatterns)
            {
                XElement active = new XElement(_Svrl + "active-pattern");
                if (pattern.Id != null)
                {
                    active.SetAttributeValue("id", pattern.Id);
                }
                if (pattern.Name != null)
                {
                    active.SetAttributeValue("name", pattern.Name);
                }
                root.Add(active);
                List<int> firings;
                if (!firingsByPattern.TryGetValue(pattern.Id ?? "", out firings))
                {
                    continue;
                }
                foreach (int i in firings)
                {
                    SchematronFiredRule firedRule = _FiredRules[i];
                    XElement fired = new XElement(_Svrl + "fired-rule", new XAttribute("context", firedRule.Context ?? ""));
                    fired.SetAttributeValue("id", firedRule.RuleId);
                    root.Add(fired);
                    int end = i + 1 < _FiredRules.Count ? _FiredRules[i + 1].FirstMessage : Messages.Count;
                    for (int m = firedRule.FirstMessage; m < end; m++)
                    {
                        root.Add(_ToSvrl(Messages[m]));
                    }
                }
            }
            return new XDocument(root);
        }


        private static XElement _ToSvrl(SchematronMessage message)
        {
            XElement element = new XElement(_Svrl + (message.Kind == SchematronMessageKind.FailedAssert ? "failed-assert" : "successful-report"));
            element.SetAttributeValue("location", message.Location);
            element.SetAttributeValue("role", message.Role);
            element.SetAttributeValue("flag", message.Flag);
            element.SetAttributeValue("id", message.Id);
            element.SetAttributeValue("see", message.See);
            element.SetAttributeValue("icon", message.Icon);
            element.SetAttributeValue("fpi", message.Fpi);
            element.SetAttributeValue("test", message.Test);
            foreach (SchematronDiagnostic diagnostic in message.Diagnostics)
            {
                element.Add(new XElement(_Svrl + "diagnostic-reference", new XAttribute("diagnostic", diagnostic.Id), new XElement(_Svrl + "text", diagnostic.Text)));
            }
            element.Add(new XElement(_Svrl + "text", message.Text));
            return element;
        }
    }


    /// <summary>
    /// A Schematron schema could not be loaded or a dynamic error occurred while evaluating it.
    /// </summary>
    public sealed class SchematronException : Exception
    {
        internal SchematronException(string message, string errorCode = null, Exception inner = null) : base(message, inner)
        {
            ErrorCode = errorCode;
        }


        /// <summary>The XPath / XSLT error code (e.g. XPTY0004), if any.</summary>
        public string ErrorCode { get; }

        /// <summary>The pattern that was evaluated when a dynamic error occurred.</summary>
        public string PatternId { get; internal set; }

        /// <summary>The context of the rule that was evaluated when a dynamic error occurred.</summary>
        public string RuleContext { get; internal set; }

        /// <summary>The assertion that was evaluated when a dynamic error occurred.</summary>
        public string AssertionId { get; internal set; }

        /// <summary>The location of the context node when a dynamic error occurred.</summary>
        public string Location { get; internal set; }
    }
}
