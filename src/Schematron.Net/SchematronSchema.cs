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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using Schematron.Net.XPath;

namespace Schematron.Net
{
    /// <summary>
    /// The notation of the node locations in the results (svrl:failed-assert/@location).
    /// </summary>
    public enum SchematronLocationFormat
    {
        /// <summary>
        /// The notation of SchXslt, e.g. used by the KoSIT validator for the rules it compiles itself (XRechnung):
        /// /Q{urn:...:Invoice-2}Invoice[1]/Q{urn:...}AccountingSupplierParty[1]
        /// </summary>
        EQName,

        /// <summary>
        /// The notation of the ISO Schematron skeleton, e.g. used by the compiled XSLT published with the CEN EN 16931
        /// and the Factur-X rules: /*:Invoice[namespace-uri()='urn:...:Invoice-2'][1]/*:AccountingSupplierParty[namespace-uri()='urn:...'][1]
        /// </summary>
        IsoSkeleton
    }


    /// <summary>
    /// What a validation does when a rule raises a dynamic error (e.g. xs:decimal('abc') on a value of the document).
    /// </summary>
    public enum SchematronDynamicErrorHandling
    {
        /// <summary>
        /// The validation stops with a <see cref="SchematronException"/>, as the XSLT compiled from the schema does.
        /// </summary>
        Throw,

        /// <summary>
        /// The error is added to <see cref="SchematronResult.Errors"/> and the validation continues with the next
        /// assertion (or, for an error in a rule context, the next rule). The document is then not valid.
        /// </summary>
        Report
    }


    /// <summary>
    /// Options for loading a Schematron schema.
    /// </summary>
    public sealed class SchematronOptions
    {
        /// <summary>
        /// The phase to validate when a validation selects none: null for the default phase (or all patterns),
        /// #ALL for all patterns.
        /// </summary>
        public string Phase { get; set; }

        /// <summary>The notation of the node locations in the results.</summary>
        public SchematronLocationFormat LocationFormat { get; set; } = SchematronLocationFormat.EQName;

        /// <summary>
        /// Opens the schema, the files it includes, the documents its rules load with doc() / document() and the
        /// subordinate documents of sch:pattern/@documents. Default: <see cref="FileResourceResolver"/> (local files
        /// only).
        /// </summary>
        public IResourceResolver ResourceResolver { get; set; }
    }


    /// <summary>
    /// Options for one validation.
    /// </summary>
    public sealed class SchematronValidationOptions
    {
        private TimeSpan _RegexTimeout = TimeSpan.FromSeconds(10);


        /// <summary>
        /// The phase to validate: null for the phase the schema was loaded with (<see cref="SchematronOptions.Phase"/>,
        /// else the default phase or all patterns), #DEFAULT for the default phase, #ALL for all patterns.
        /// </summary>
        public string Phase { get; set; }

        /// <summary>
        /// Values for the variables declared with sch:let in the schema element (not in a phase, pattern or rule),
        /// by name as written in the schema (e.g. "currency" or "x:currency") or by expanded name
        /// ("{urn:example}currency"). They replace the value the schema computes; an unknown name is an error.
        /// </summary>
        public IDictionary<string, XdmValue> Parameters { get; } = new Dictionary<string, XdmValue>(StringComparer.Ordinal);

        /// <summary>What to do when a rule raises a dynamic error. Default: <see cref="SchematronDynamicErrorHandling.Throw"/>.</summary>
        public SchematronDynamicErrorHandling DynamicErrors { get; set; } = SchematronDynamicErrorHandling.Throw;

        /// <summary>
        /// Stops the validation: checked between the rules and nodes and inside long evaluations. A cancelled
        /// validation throws an <see cref="OperationCanceledException"/>; use
        /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> for a time limit.
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// The time limit of one match of a regular expression (matches(), replace(), tokenize()); a match that takes
        /// longer raises the dynamic error XPDY0130. Default: 10 seconds.
        /// </summary>
        public TimeSpan RegexTimeout
        {
            get => _RegexTimeout;
            set => _RegexTimeout = value > TimeSpan.Zero || value == Timeout.InfiniteTimeSpan
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "The regular expression timeout must be positive or infinite");
        }
    }


    /// <summary>
    /// A compiled ISO Schematron schema that can validate XML documents. The query bindings xslt2, xslt3, xpath2,
    /// xpath3 and xpath31 are evaluated as XPath 2.0 (with the XPath 3.0 operators); xslt, xpath and exslt (and a
    /// schema without query binding) in XPath 1.0 compatibility mode.
    ///
    /// The schema is evaluated by the XPath engine of this library: no XSLT processor and no Java are required. The
    /// results are the same as with the SchXslt / ISO skeleton pipelines on Saxon.
    /// Instances are immutable and thread-safe; compile a schema once and reuse it.
    /// </summary>
    public sealed class SchematronSchema
    {
        private readonly CompiledSchematron _Schema;
        private readonly SchematronLocationFormat _LocationFormat;
        private readonly string _Phase;


        private SchematronSchema(CompiledSchematron schema, SchematronOptions options)
        {
            _Schema = schema;
            _LocationFormat = options.LocationFormat;
            _Phase = schema.ResolvePhase(options.Phase);
            // the default phase is checked when the schema is loaded
            schema.GetPlan(_Phase);
        }


        /// <summary>
        /// Loads a Schematron schema from a file. Includes and documents loaded with document() are resolved relative to it.
        /// </summary>
        /// <param name="path">Path of the .sch file</param>
        /// <param name="phase">The phase to validate, null for the default phase (or all patterns)</param>
        /// <exception cref="SchematronException">The schema is invalid or uses an unsupported feature</exception>
        public static SchematronSchema Load(string path, string phase = null)
        {
            return Load(path, new SchematronOptions { Phase = phase });
        }


        /// <summary>
        /// Loads a Schematron schema from a file. Includes and documents loaded with document() are resolved relative to it.
        /// </summary>
        /// <param name="path">Path of the .sch file</param>
        /// <param name="options">Phase, location format and resource resolver</param>
        /// <exception cref="SchematronException">The schema is invalid or uses an unsupported feature</exception>
        public static SchematronSchema Load(string path, SchematronOptions options)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }
            return Load(new Uri(Path.GetFullPath(path)), options);
        }


        /// <summary>
        /// Loads a Schematron schema with the <see cref="SchematronOptions.ResourceResolver"/>, e.g. from the
        /// resources embedded into an assembly. Includes and documents loaded with document() are resolved relative to
        /// the URI.
        /// </summary>
        /// <param name="uri">The absolute URI of the .sch file</param>
        /// <param name="options">Phase, location format and resource resolver</param>
        /// <exception cref="SchematronException">The schema is invalid or uses an unsupported feature</exception>
        public static SchematronSchema Load(Uri uri, SchematronOptions options = null)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            if (!uri.IsAbsoluteUri)
            {
                throw new ArgumentException("The URI of the schema must be absolute: " + uri, nameof(uri));
            }
            options = options ?? new SchematronOptions();
            return new SchematronSchema(SchematronCompiler.Compile(uri.AbsoluteUri, options.ResourceResolver), options);
        }


        /// <summary>
        /// Loads a Schematron schema from a stream.
        /// </summary>
        /// <param name="stream">The Schematron schema</param>
        /// <param name="baseUri">The location the schema was read from (file path or absolute URI); needed to resolve sch:include and document()</param>
        /// <param name="phase">The phase to validate, null for the default phase (or all patterns)</param>
        /// <exception cref="SchematronException">The schema is invalid or uses an unsupported feature</exception>
        public static SchematronSchema Load(Stream stream, string baseUri = null, string phase = null)
        {
            return Load(stream, baseUri, new SchematronOptions { Phase = phase });
        }


        /// <summary>
        /// Loads a Schematron schema from a stream.
        /// </summary>
        /// <param name="stream">The Schematron schema</param>
        /// <param name="baseUri">The location the schema was read from (file path or absolute URI); needed to resolve sch:include and document()</param>
        /// <param name="options">Phase, location format and resource resolver</param>
        /// <exception cref="SchematronException">The schema is invalid or uses an unsupported feature</exception>
        public static SchematronSchema Load(Stream stream, string baseUri, SchematronOptions options)
        {
            options = options ?? new SchematronOptions();
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            string uri = baseUri == null ? null : (Uri.TryCreate(baseUri, UriKind.Absolute, out Uri parsed) && !parsed.IsFile ? baseUri : Resources.FileUri(Resources.ToLocalPath(baseUri)));
            XdmDocument document = SchematronCompiler.LoadSchemaDocument(stream, uri);
            return new SchematronSchema(SchematronCompiler.Compile(document, uri, options.ResourceResolver), options);
        }


        /// <summary>
        /// The notation of the node locations in the results.
        /// </summary>
        public SchematronLocationFormat LocationFormat => _LocationFormat;


        /// <summary>
        /// The title of the schema.
        /// </summary>
        public string Title => _Schema.Title;


        /// <summary>
        /// The phase a validation uses when it selects none: a phase id or #ALL.
        /// </summary>
        public string Phase => _Phase;


        /// <summary>
        /// The ids of the phases the schema declares.
        /// </summary>
        public IReadOnlyList<string> Phases => _Schema.Phases.Keys.ToList();


        /// <summary>
        /// The ids of the patterns that are validated in <see cref="Phase"/>.
        /// </summary>
        public IReadOnlyList<string> PatternIds => _Schema.GetPlan(_Phase).Patterns.Select(p => p.Id).ToList();


        /// <summary>
        /// Validates the XML file.
        /// </summary>
        /// <exception cref="XmlException">The document is not well-formed</exception>
        /// <exception cref="SchematronException">A dynamic error occurred while evaluating the rules</exception>
        public SchematronResult Validate(string path)
        {
            return Validate(XdmDocument.Load(path));
        }


        /// <summary>
        /// Validates the XML document read from the stream.
        /// </summary>
        /// <exception cref="XmlException">The document is not well-formed</exception>
        /// <exception cref="SchematronException">A dynamic error occurred while evaluating the rules</exception>
        public SchematronResult Validate(Stream stream, string documentUri = null)
        {
            return Validate(XdmDocument.Load(stream, documentUri));
        }


        /// <summary>
        /// Validates a parsed document. Parse a document once with <see cref="XdmDocument.Load(Stream, string)"/> to
        /// validate it with several schemas.
        /// </summary>
        /// <exception cref="SchematronException">A dynamic error occurred while evaluating the rules</exception>
        public SchematronResult Validate(XdmDocument document)
        {
            return Validate(document, null);
        }


        /// <summary>
        /// Validates a parsed document with the given phase, parameters, error handling and cancellation.
        /// </summary>
        /// <param name="document">The document</param>
        /// <param name="options">The options of this validation; null for the defaults</param>
        /// <exception cref="SchematronException">The phase does not exist, or a dynamic error occurred (with <see cref="SchematronDynamicErrorHandling.Throw"/>)</exception>
        /// <exception cref="ArgumentException">A parameter is not a variable of the schema element</exception>
        /// <exception cref="OperationCanceledException">The validation was cancelled</exception>
        public SchematronResult Validate(XdmDocument document, SchematronValidationOptions options)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            options = options ?? new SchematronValidationOptions();
            PhasePlan plan = _Schema.GetPlan(options.Phase == null ? _Phase : _Schema.ResolvePhase(options.Phase));
            return new SchematronRun(_Schema, plan, document, _LocationFormat, options).Execute();
        }
    }


    /// <summary>
    /// One validation of a document: evaluates the rules of all active patterns.
    /// </summary>
    internal sealed class SchematronRun
    {
        private readonly CompiledSchematron _Schema;
        private readonly PhasePlan _Plan;
        private readonly XdmDocument _Document;
        private readonly XPathDynamicContext _Environment;
        private readonly SchematronLocationFormat _LocationFormat;
        private readonly SchematronValidationOptions _Options;
        private readonly CancellationToken _Cancellation;
        private readonly VariableScope _Global;
        private readonly Dictionary<CompiledPattern, VariableScope> _PatternScopes = new Dictionary<CompiledPattern, VariableScope>();
        private readonly Dictionary<string, XdmDocument> _Subordinates = new Dictionary<string, XdmDocument>(StringComparer.Ordinal);
        private readonly List<SchematronMessage> _Messages = new List<SchematronMessage>();
        private readonly List<SchematronError> _Errors = new List<SchematronError>();
        private readonly List<SchematronFiredRule> _FiredRules = new List<SchematronFiredRule>();


        public SchematronRun(CompiledSchematron schema, PhasePlan plan, XdmDocument document, SchematronLocationFormat locationFormat, SchematronValidationOptions options)
        {
            _Schema = schema;
            _Plan = plan;
            _Document = document;
            _LocationFormat = locationFormat;
            _Options = options;
            _Cancellation = options.CancellationToken;
            _Environment = new XPathDynamicContext(schema.Documents)
            {
                Keys = schema.Keys,
                CancellationToken = options.CancellationToken,
                RegexTimeout = options.RegexTimeout
            };

            // variables: rule → pattern → phase → schema → the variables of other patterns (if only one declares the name)
            RunVariables values = new RunVariables(_Environment, document.Root);
            _Global = new VariableScope(null, values);
            Dictionary<string, List<KeyValuePair<CompiledLet, VariableScope>>> patternVariables = new Dictionary<string, List<KeyValuePair<CompiledLet, VariableScope>>>(StringComparer.Ordinal);
            foreach (CompiledPattern pattern in plan.Patterns)
            {
                VariableScope scope = new VariableScope(_Global, values);
                foreach (CompiledLet let in pattern.Lets)
                {
                    scope.Bind(let, scope);
                    List<KeyValuePair<CompiledLet, VariableScope>> declarations;
                    if (!patternVariables.TryGetValue(let.Name, out declarations))
                    {
                        declarations = new List<KeyValuePair<CompiledLet, VariableScope>>();
                        patternVariables[let.Name] = declarations;
                    }
                    declarations.Add(new KeyValuePair<CompiledLet, VariableScope>(let, scope));
                }
                _PatternScopes[pattern] = scope;
            }
            foreach (KeyValuePair<string, List<KeyValuePair<CompiledLet, VariableScope>>> declarations in patternVariables)
            {
                if (declarations.Value.Count == 1)
                {
                    _Global.Bind(declarations.Value[0].Key, declarations.Value[0].Value);
                }
                else
                {
                    _Global.MarkAmbiguous(declarations.Key);
                }
            }
            foreach (CompiledLet let in schema.SchemaLets.Concat(plan.PhaseLets))
            {
                _Global.Bind(let, _Global);
            }
            _Environment.GlobalVariables = _Global;

            foreach (KeyValuePair<string, XdmValue> parameter in options.Parameters)
            {
                CompiledLet let = schema.SchemaLets.FirstOrDefault(l => l.Name == parameter.Key || l.LexicalName == parameter.Key);
                if (let == null)
                {
                    throw new ArgumentException("The schema declares no variable '" + parameter.Key + "' (parameters set the sch:let variables of the schema element)", nameof(options));
                }
                XdmValue value = parameter.Value ?? XdmValue.Empty;
                if (let.As != null)
                {
                    try
                    {
                        value = let.As.Convert(value, "the parameter $" + let.LexicalName);
                    }
                    catch (XPathException e)
                    {
                        throw new ArgumentException(e.Message, nameof(options), e);
                    }
                }
                values.Set(let, value);
            }
        }


        public SchematronResult Execute()
        {
            List<SchematronActivePattern> activePatterns = new List<SchematronActivePattern>();
            foreach (CompiledPattern pattern in _Plan.Patterns)
            {
                _Cancellation.ThrowIfCancellationRequested();
                VariableScope scope = _PatternScopes[pattern];
                List<XdmDocument> documents;
                if (pattern.Documents == null)
                {
                    documents = new List<XdmDocument> { _Document };
                    activePatterns.Add(new SchematronActivePattern(pattern.Id, pattern.Name, new string[0]));
                }
                else
                {
                    documents = _SubordinateDocuments(pattern, scope);
                    activePatterns.Add(new SchematronActivePattern(pattern.Id, pattern.Name, documents.Select(d => d.DocumentUri).ToList()));
                }
                foreach (XdmDocument document in documents)
                {
                    _ValidatePattern(pattern, scope, document);
                }
            }
            return new SchematronResult(_Schema.Title, _Schema.SchemaVersion, _Plan.Phase, _Schema.Namespaces, activePatterns, _Messages, _Errors, _FiredRules);
        }


        private void _ValidatePattern(CompiledPattern pattern, VariableScope scope, XdmDocument document)
        {
            // the first rule (in schema order) whose context matches a node fires for it
            Dictionary<XdmNode, int> ruleOfNode = new Dictionary<XdmNode, int>();
            for (int i = 0; i < pattern.Rules.Count; i++)
            {
                _Cancellation.ThrowIfCancellationRequested();
                CompiledRule rule = pattern.Rules[i];
                XdmValue matches;
                try
                {
                    matches = rule.Context.Evaluate(document.Root, _Environment, scope);
                }
                catch (XPathException e)
                {
                    _Failed(e, pattern, rule, null, document.Root);
                    continue;
                }
                foreach (XdmItem item in matches)
                {
                    if (item is XdmNode node && !ruleOfNode.ContainsKey(node) && _IsVisited(node))
                    {
                        ruleOfNode[node] = i;
                    }
                }
            }
            if (ruleOfNode.Count == 0)
            {
                return;
            }
            List<XdmNode> nodes = ruleOfNode.Keys.ToList();
            nodes.Sort(XdmNode.CompareOrder);
            foreach (XdmNode node in nodes)
            {
                _Cancellation.ThrowIfCancellationRequested();
                CompiledRule rule = pattern.Rules[ruleOfNode[node]];
                _FiredRules.Add(new SchematronFiredRule(pattern.Id, rule.Id, rule.ContextText, rule.Flag, rule.Role, _Messages.Count));
                _Fire(pattern, rule, node, scope);
            }
        }


        /// <summary>
        /// Loads the documents named by sch:pattern/@documents, relative to the URI of the instance document.
        /// </summary>
        private List<XdmDocument> _SubordinateDocuments(CompiledPattern pattern, VariableScope scope)
        {
            List<XdmDocument> documents = new List<XdmDocument>();
            XdmValue uris;
            try
            {
                uris = pattern.Documents.Evaluate(_Document.Root, _Environment, scope);
            }
            catch (XPathException e)
            {
                _Failed(e, pattern, null, null, _Document.Root);
                return documents;
            }
            foreach (XdmItem item in uris)
            {
                string uri = Resources.Resolve(_Document.DocumentUri, item.StringValue.Trim());
                XdmDocument document;
                if (!_Subordinates.TryGetValue(uri, out document))
                {
                    try
                    {
                        using (Stream stream = Resources.Open(_Schema.Resolver, uri))
                        {
                            document = XdmDocument.Load(stream, uri);
                        }
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        // the resolver is user code: whatever it throws, the document cannot be loaded
                        _Failed(new XPathException("FODC0002", "Cannot load the subordinate document " + uri + ": " + e.Message, e), pattern, null, null, _Document.Root);
                        continue;
                    }
                    _Subordinates[uri] = document;
                }
                documents.Add(document);
            }
            return documents;
        }


        /// <summary>
        /// SchXslt visits all nodes; the ISO skeleton only the document node, elements and attributes.
        /// </summary>
        private bool _IsVisited(XdmNode node)
        {
            return _LocationFormat == SchematronLocationFormat.EQName || node.Kind == XdmNodeKind.Element || node.Kind == XdmNodeKind.Attribute || node.Kind == XdmNodeKind.Document;
        }


        private string _Location(XdmNode node)
        {
            return _LocationFormat == SchematronLocationFormat.IsoSkeleton ? NodeLocations.IsoSkeleton(node) : NodeLocations.EQName(node);
        }


        private void _Fire(CompiledPattern pattern, CompiledRule rule, XdmNode node, VariableScope scope)
        {
            RuleVariables variables = rule.Lets.Count == 0 ? null : new RuleVariables(rule.Lets, _Environment, node, scope);
            IXPathVariableResolver resolver = (IXPathVariableResolver)variables ?? scope;
            foreach (CompiledCheck check in rule.Checks)
            {
                _Cancellation.ThrowIfCancellationRequested();
                try
                {
                    bool result = check.Test.EvaluateBoolean(node, _Environment, resolver);
                    if (result == check.IsAssert)
                    {
                        continue;
                    }
                    XdmNode subject = node;
                    XPathExpression subjectExpression = check.Subject ?? rule.Subject;
                    if (subjectExpression != null)
                    {
                        subject = subjectExpression.Evaluate(node, _Environment, resolver).OfType<XdmNode>().FirstOrDefault() ?? node;
                    }
                    SchematronMessage message = new SchematronMessage
                    {
                        Kind = check.IsAssert ? SchematronMessageKind.FailedAssert : SchematronMessageKind.SuccessfulReport,
                        Id = check.Id,
                        Flag = check.Flag,
                        Role = check.Role,
                        See = check.See,
                        Icon = check.Icon,
                        Fpi = check.Fpi,
                        Test = check.TestText,
                        Location = _Location(subject),
                        LineNumber = subject.LineNumber > 0 ? subject.LineNumber : (int?)null,
                        LinePosition = subject.LinePosition > 0 ? subject.LinePosition : (int?)null,
                        DocumentUri = subject.Document.DocumentUri,
                        PatternId = pattern.Id,
                        RuleContext = rule.ContextText,
                        Language = check.Language,
                        Text = _BuildText(check.Message, resolver, node, null)
                    };
                    if (check.DiagnosticIds.Length > 0)
                    {
                        List<SchematronDiagnostic> diagnostics = new List<SchematronDiagnostic>();
                        foreach (string id in check.DiagnosticIds)
                        {
                            CompiledDiagnostic diagnostic;
                            if (_Schema.Diagnostics.TryGetValue(id, out diagnostic))
                            {
                                diagnostics.Add(new SchematronDiagnostic(id, _BuildText(diagnostic.Message, resolver, node, null), diagnostic.Language));
                            }
                        }
                        message.Diagnostics = diagnostics;
                    }
                    if (check.PropertyIds.Length > 0)
                    {
                        List<SchematronPropertyReference> properties = new List<SchematronPropertyReference>();
                        foreach (string id in check.PropertyIds)
                        {
                            CompiledProperty property;
                            if (_Schema.Properties.TryGetValue(id, out property))
                            {
                                List<XdmNode> copies = new List<XdmNode>();
                                string text = _BuildText(property.Message, resolver, node, copies);
                                properties.Add(new SchematronPropertyReference(id, property.Role, property.Scheme, text, copies));
                            }
                        }
                        message.Properties = properties;
                    }
                    _Messages.Add(message);
                }
                catch (XPathException e)
                {
                    _Failed(e, pattern, rule, check, node);
                }
            }
        }


        /// <summary>
        /// The text of a message, diagnostic or property. The nodes selected by xsl:copy-of are added to copies (and
        /// their string values to the text if copies is null).
        /// </summary>
        private string _BuildText(List<MessagePart> parts, IXPathVariableResolver variables, XdmNode node, List<XdmNode> copies)
        {
            StringBuilder sb = new StringBuilder();
            foreach (MessagePart part in parts)
            {
                if (part.Text != null)
                {
                    sb.Append(part.Text);
                }
                else if (part.IsName)
                {
                    XdmNode target = node;
                    if (part.NamePath != null)
                    {
                        target = part.NamePath.Evaluate(node, _Environment, variables).OfType<XdmNode>().FirstOrDefault();
                    }
                    sb.Append(target?.Name ?? "");
                }
                else if (part.CopyOf != null)
                {
                    foreach (XdmItem item in part.CopyOf.Evaluate(node, _Environment, variables))
                    {
                        if (copies != null && item is XdmNode copy)
                        {
                            copies.Add(copy);
                        }
                        else
                        {
                            sb.Append(item.StringValue);
                        }
                    }
                }
                else
                {
                    // the string values of the atomized items: without a schema, a node atomizes to its string value
                    XdmValue value = part.ValueOf.Evaluate(node, _Environment, variables);
                    sb.Append(String.Join(" ", value.Select(i => i.StringValue)));
                }
            }
            return sb.ToString();
        }


        /// <summary>
        /// A dynamic error: throws a <see cref="SchematronException"/>, or records the error to continue.
        /// </summary>
        private void _Failed(XPathException e, CompiledPattern pattern, CompiledRule rule, CompiledCheck check, XdmNode node)
        {
            string location = NodeLocations.EQName(node);
            string where = "pattern " + pattern.Id + (rule != null ? ", rule \"" + rule.ContextText + "\"" : ", sch:pattern/@documents")
                           + (check != null ? ", " + (check.IsAssert ? "assert " : "report ") + (check.Id ?? "") + " test \"" + check.TestText + "\"" : "");
            string message = "Dynamic error in " + where + " at " + location + ": " + e.Message;
            if (_Options.DynamicErrors == SchematronDynamicErrorHandling.Report)
            {
                _Errors.Add(new SchematronError(e.Code, message, pattern.Id, rule?.ContextText, check?.Id, location, node.Document.DocumentUri));
                return;
            }
            throw new SchematronException(message, e.Code, e)
            {
                PatternId = pattern.Id,
                RuleContext = rule?.ContextText,
                AssertionId = check?.Id,
                Location = location
            };
        }
    }


    /// <summary>
    /// The values of the global variables (schema, phase and pattern variables) of one run: evaluated lazily, once,
    /// with the document node as context item.
    /// </summary>
    internal sealed class RunVariables
    {
        private readonly Dictionary<CompiledLet, XdmValue> _Values = new Dictionary<CompiledLet, XdmValue>();
        private readonly HashSet<CompiledLet> _Evaluating = new HashSet<CompiledLet>();
        private readonly XPathDynamicContext _Environment;
        private readonly XdmNode _ContextNode;


        public RunVariables(XPathDynamicContext environment, XdmNode contextNode)
        {
            _Environment = environment;
            _ContextNode = contextNode;
        }


        /// <summary>
        /// Sets the value of a variable (a parameter of the validation).
        /// </summary>
        public void Set(CompiledLet let, XdmValue value)
        {
            _Values[let] = value;
        }


        public XdmValue Get(CompiledLet let, IXPathVariableResolver scope)
        {
            XdmValue value;
            if (_Values.TryGetValue(let, out value))
            {
                return value;
            }
            if (!_Evaluating.Add(let))
            {
                throw new XPathException("XTDE0640", "Circular definition of variable $" + let.LexicalName);
            }
            try
            {
                value = let.Evaluate(_ContextNode, _Environment, scope);
                _Values[let] = value;
                return value;
            }
            finally
            {
                _Evaluating.Remove(let);
            }
        }
    }


    /// <summary>
    /// The global variables or the variables of a pattern: each variable is evaluated in the scope it is declared in.
    /// </summary>
    internal sealed class VariableScope : IXPathVariableResolver
    {
        private readonly Dictionary<string, KeyValuePair<CompiledLet, VariableScope>> _Bindings = new Dictionary<string, KeyValuePair<CompiledLet, VariableScope>>(StringComparer.Ordinal);
        private readonly HashSet<string> _Ambiguous = new HashSet<string>(StringComparer.Ordinal);
        private readonly VariableScope _Parent;
        private readonly RunVariables _Values;


        public VariableScope(VariableScope parent, RunVariables values)
        {
            _Parent = parent;
            _Values = values;
        }


        /// <summary>
        /// Declares a variable (replacing one of the same name), evaluated in the given scope.
        /// </summary>
        public void Bind(CompiledLet let, VariableScope evaluationScope)
        {
            _Bindings[let.Name] = new KeyValuePair<CompiledLet, VariableScope>(let, evaluationScope);
            _Ambiguous.Remove(let.Name);
        }


        /// <summary>
        /// Marks a name that several patterns declare: it cannot be used outside of them.
        /// </summary>
        public void MarkAmbiguous(string name)
        {
            _Ambiguous.Add(name);
        }


        public bool TryResolve(string expandedName, out XdmValue value)
        {
            KeyValuePair<CompiledLet, VariableScope> binding;
            if (_Bindings.TryGetValue(expandedName, out binding))
            {
                value = _Values.Get(binding.Key, binding.Value);
                return true;
            }
            if (_Ambiguous.Contains(expandedName))
            {
                throw new XPathException("XPST0008", "Variable $" + expandedName + " is declared in several patterns");
            }
            if (_Parent != null)
            {
                return _Parent.TryResolve(expandedName, out value);
            }
            value = null;
            return false;
        }
    }


    /// <summary>
    /// The lets of a rule, evaluated lazily with the rule's context node. A let sees the lets declared before it.
    /// </summary>
    internal sealed class RuleVariables : IXPathVariableResolver
    {
        private readonly List<CompiledLet> _Lets;
        private readonly XPathDynamicContext _Environment;
        private readonly XdmNode _ContextNode;
        private readonly IXPathVariableResolver _Parent;
        private readonly XdmValue[] _Values;


        public RuleVariables(List<CompiledLet> lets, XPathDynamicContext environment, XdmNode contextNode, IXPathVariableResolver parent)
        {
            _Lets = lets;
            _Environment = environment;
            _ContextNode = contextNode;
            _Parent = parent;
            _Values = new XdmValue[lets.Count];
        }


        public bool TryResolve(string expandedName, out XdmValue value)
        {
            return _Resolve(expandedName, _Lets.Count, out value);
        }


        private bool _Resolve(string expandedName, int visibleCount, out XdmValue value)
        {
            for (int i = visibleCount - 1; i >= 0; i--)
            {
                if (_Lets[i].Name == expandedName)
                {
                    value = _Values[i] ?? (_Values[i] = _Evaluate(i));
                    return true;
                }
            }
            return _Parent.TryResolve(expandedName, out value);
        }


        private XdmValue _Evaluate(int index)
        {
            return _Lets[index].Evaluate(_ContextNode, _Environment, new View(this, index));
        }


        /// <summary>
        /// The variables visible to the let with the given index.
        /// </summary>
        private sealed class View : IXPathVariableResolver
        {
            private readonly RuleVariables _Owner;
            private readonly int _VisibleCount;


            public View(RuleVariables owner, int visibleCount)
            {
                _Owner = owner;
                _VisibleCount = visibleCount;
            }


            public bool TryResolve(string expandedName, out XdmValue value)
            {
                return _Owner._Resolve(expandedName, _VisibleCount, out value);
            }
        }
    }
}
