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
using Schematron.Net.XPath;

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
        internal SchematronDiagnostic(string id, string text, string language)
        {
            Id = id;
            Text = text;
            Language = language;
        }


        /// <summary>The id of the sch:diagnostic.</summary>
        public string Id { get; }

        /// <summary>The text of the diagnostic.</summary>
        public string Text { get; }

        /// <summary>The xml:lang of the sch:diagnostic, null if it has none.</summary>
        public string Language { get; }
    }


    /// <summary>
    /// A property referenced by an assertion (sch:property, svrl:property-reference).
    /// </summary>
    public sealed class SchematronPropertyReference
    {
        internal SchematronPropertyReference(string id, string role, string scheme, string text, IReadOnlyList<XdmNode> nodes)
        {
            Id = id;
            Role = role;
            Scheme = scheme;
            Text = text;
            Nodes = nodes;
        }


        /// <summary>The id of the sch:property.</summary>
        public string Id { get; }

        /// <summary>The role attribute of the property.</summary>
        public string Role { get; }

        /// <summary>The scheme attribute of the property.</summary>
        public string Scheme { get; }

        /// <summary>The text of the property (with sch:value-of and sch:name evaluated).</summary>
        public string Text { get; }

        /// <summary>The nodes selected by the xsl:copy-of elements of the property, copied into the SVRL output.</summary>
        public IReadOnlyList<XdmNode> Nodes { get; }
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

        /// <summary>The xml:lang of the assertion, null if it has none.</summary>
        public string Language { get; internal set; }

        /// <summary>
        /// The location of the node the assertion failed for, as XPath in the notation of SchXslt and the KoSIT
        /// validator, e.g. /Q{urn:...:Invoice-2}Invoice[1]/Q{urn:...}AccountingSupplierParty[1].
        /// </summary>
        public string Location { get; internal set; }

        /// <summary>Line of the node in the validated document (if known).</summary>
        public int? LineNumber { get; internal set; }

        /// <summary>Column of the node in the validated document (if known).</summary>
        public int? LinePosition { get; internal set; }

        /// <summary>
        /// The URI of the document the node belongs to: the validated document, or a subordinate document of
        /// sch:pattern/@documents. Null if unknown.
        /// </summary>
        public string DocumentUri { get; internal set; }

        /// <summary>The id of the pattern the assertion belongs to.</summary>
        public string PatternId { get; internal set; }

        /// <summary>The context of the rule the assertion belongs to.</summary>
        public string RuleContext { get; internal set; }

        /// <summary>The diagnostics referenced by the assertion.</summary>
        public IReadOnlyList<SchematronDiagnostic> Diagnostics { get; internal set; } = new SchematronDiagnostic[0];

        /// <summary>The properties referenced by the assertion.</summary>
        public IReadOnlyList<SchematronPropertyReference> Properties { get; internal set; } = new SchematronPropertyReference[0];


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
        internal SchematronActivePattern(string id, string name, IReadOnlyList<string> documents)
        {
            Id = id;
            Name = name;
            Documents = documents;
        }


        /// <summary>The id of the pattern.</summary>
        public string Id { get; }

        /// <summary>The name (title) of the pattern.</summary>
        public string Name { get; }

        /// <summary>
        /// The URIs of the subordinate documents the pattern validated (sch:pattern/@documents); empty if it
        /// validated the document itself.
        /// </summary>
        public IReadOnlyList<string> Documents { get; }
    }


    /// <summary>
    /// A dynamic error that occurred while evaluating a rule, reported instead of thrown with
    /// <see cref="SchematronDynamicErrorHandling.Report"/>.
    /// </summary>
    public sealed class SchematronError
    {
        internal SchematronError(string code, string message, string patternId, string ruleContext, string assertionId, string location, string documentUri)
        {
            Code = code;
            Message = message;
            PatternId = patternId;
            RuleContext = ruleContext;
            AssertionId = assertionId;
            Location = location;
            DocumentUri = documentUri;
        }


        /// <summary>The XPath / XSLT error code, e.g. FORG0001.</summary>
        public string Code { get; }

        /// <summary>The description of the error, with pattern, rule and location.</summary>
        public string Message { get; }

        /// <summary>The pattern that was evaluated.</summary>
        public string PatternId { get; }

        /// <summary>The context of the rule that was evaluated; null for an error in sch:pattern/@documents.</summary>
        public string RuleContext { get; }

        /// <summary>The id of the assertion that was evaluated, null for an error in a rule context or variable.</summary>
        public string AssertionId { get; }

        /// <summary>The location of the context node in the notation of SchXslt.</summary>
        public string Location { get; }

        /// <summary>The URI of the document of the context node, null if unknown.</summary>
        public string DocumentUri { get; }


        /// <inheritdoc/>
        public override string ToString()
        {
            return Message;
        }
    }


    /// <summary>
    /// A rule that fired for a node (svrl:fired-rule); the messages of the firing start at <see cref="FirstMessage"/>.
    /// </summary>
    internal sealed class SchematronFiredRule
    {
        public SchematronFiredRule(string patternId, string ruleId, string context, string flag, string role, int firstMessage)
        {
            PatternId = patternId;
            RuleId = ruleId;
            Context = context;
            Flag = flag;
            Role = role;
            FirstMessage = firstMessage;
        }


        public string PatternId { get; }

        public string RuleId { get; }

        public string Context { get; }

        public string Flag { get; }

        public string Role { get; }

        public int FirstMessage { get; }
    }


    /// <summary>
    /// The result of a Schematron validation, the equivalent of an SVRL document.
    /// </summary>
    public sealed class SchematronResult
    {
        private static readonly XNamespace _Svrl = "http://purl.oclc.org/dsdl/svrl";


        private readonly IReadOnlyList<SchematronFiredRule> _FiredRules;


        internal SchematronResult(string title, string schemaVersion, string phase, IReadOnlyList<KeyValuePair<string, string>> namespaces, IReadOnlyList<SchematronActivePattern> activePatterns,
                                  IReadOnlyList<SchematronMessage> messages, IReadOnlyList<SchematronError> errors, IReadOnlyList<SchematronFiredRule> firedRules)
        {
            Title = title;
            SchemaVersion = schemaVersion;
            Phase = phase;
            Namespaces = namespaces;
            ActivePatterns = activePatterns;
            Messages = messages;
            Errors = errors;
            _FiredRules = firedRules;
        }


        /// <summary>The title of the Schematron schema.</summary>
        public string Title { get; }

        /// <summary>The schemaVersion of the Schematron schema.</summary>
        public string SchemaVersion { get; }

        /// <summary>The phase that was validated: a phase id or #ALL.</summary>
        public string Phase { get; }

        /// <summary>The namespaces declared with sch:ns.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Namespaces { get; }

        /// <summary>The patterns that were active.</summary>
        public IReadOnlyList<SchematronActivePattern> ActivePatterns { get; }

        /// <summary>The failed assertions and successful reports in the order SchXslt reports them.</summary>
        public IReadOnlyList<SchematronMessage> Messages { get; }

        /// <summary>
        /// The dynamic errors of the rules, with <see cref="SchematronDynamicErrorHandling.Report"/> (otherwise the
        /// first error is thrown and this list is empty).
        /// </summary>
        public IReadOnlyList<SchematronError> Errors { get; }

        /// <summary>
        /// True if no assertion failed and no dynamic error was reported (ISO Schematron validity: a successful
        /// report does not make a document invalid). Warnings are failed assertions too; filter
        /// <see cref="FailedAssertions"/> by <see cref="SchematronMessage.Flag"/> or <see cref="SchematronMessage.Role"/>
        /// to ignore them.
        /// </summary>
        public bool IsValid => Errors.Count == 0 && !FailedAssertions.Any();

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
            if (Phase != null && Phase != "#ALL")
            {
                root.SetAttributeValue("phase", Phase);
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
                if (pattern.Documents.Count > 0)
                {
                    active.SetAttributeValue("documents", String.Join(" ", pattern.Documents));
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
                    fired.SetAttributeValue("flag", firedRule.Flag);
                    fired.SetAttributeValue("role", firedRule.Role);
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
                element.Add(new XElement(_Svrl + "diagnostic-reference", new XAttribute("diagnostic", diagnostic.Id), _Text(diagnostic.Text, diagnostic.Language)));
            }
            foreach (SchematronPropertyReference property in message.Properties)
            {
                XElement text = _Text(property.Text, null);
                foreach (XdmNode node in property.Nodes)
                {
                    if (node.Kind == XdmNodeKind.Attribute)
                    {
                        if (text.Attribute(XName.Get(node.LocalName, node.NamespaceUri)) == null)
                        {
                            text.Add(new XAttribute(XName.Get(node.LocalName, node.NamespaceUri), node.StringValue));
                        }
                        continue;
                    }
                    text.Add(NodeCopy.ToXNodes(node));
                }
                XElement reference = new XElement(_Svrl + "property-reference", new XAttribute("property", property.Id), text);
                reference.SetAttributeValue("role", property.Role);
                reference.SetAttributeValue("scheme", property.Scheme);
                element.Add(reference);
            }
            element.Add(_Text(message.Text, message.Language));
            return element;
        }


        private static XElement _Text(string text, string language)
        {
            XElement element = new XElement(_Svrl + "text", text);
            if (language != null)
            {
                element.SetAttributeValue(XNamespace.Xml + "lang", language);
            }
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
