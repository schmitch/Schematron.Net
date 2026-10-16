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
    /// Options for loading a Schematron schema.
    /// </summary>
    public sealed class SchematronOptions
    {
        /// <summary>The phase to validate, null for the default phase (or all patterns).</summary>
        public string Phase { get; set; }

        /// <summary>The notation of the node locations in the results.</summary>
        public SchematronLocationFormat LocationFormat { get; set; } = SchematronLocationFormat.EQName;

        /// <summary>
        /// Opens the schema, the files it includes and the documents its rules load with doc() / document().
        /// Default: <see cref="FileResourceResolver"/> (local files only).
        /// </summary>
        public IResourceResolver ResourceResolver { get; set; }
    }


    /// <summary>
    /// A compiled ISO Schematron schema (query binding xslt2 / xslt3) that can validate XML documents.
    ///
    /// The schema is evaluated by the XPath 2.0 / 3.1 engine of this library: no XSLT processor and no Java are
    /// required. The results are the same as with the SchXslt / ISO skeleton pipelines on Saxon.
    /// Instances are immutable and thread-safe; compile a schema once and reuse it.
    /// </summary>
    public sealed class SchematronSchema
    {
        private readonly CompiledSchematron _Schema;
        private readonly SchematronLocationFormat _LocationFormat;


        private SchematronSchema(CompiledSchematron schema, SchematronLocationFormat locationFormat)
        {
            _Schema = schema;
            _LocationFormat = locationFormat;
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
            return new SchematronSchema(SchematronCompiler.Compile(uri.AbsoluteUri, options.Phase, options.ResourceResolver), options.LocationFormat);
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
            return new SchematronSchema(SchematronCompiler.Compile(document, uri, options.Phase, options.ResourceResolver), options.LocationFormat);
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
        /// The ids of the patterns that are validated.
        /// </summary>
        public IReadOnlyList<string> PatternIds => _Schema.Patterns.Select(p => p.Id).ToList();


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
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            return new SchematronRun(_Schema, document, _LocationFormat).Execute();
        }
    }


    /// <summary>
    /// One validation of a document: evaluates the rules of all active patterns.
    /// </summary>
    internal sealed class SchematronRun
    {
        private readonly CompiledSchematron _Schema;
        private readonly XdmDocument _Document;
        private readonly XPathDynamicContext _Environment;
        private readonly GlobalVariables _Globals;
        private readonly SchematronLocationFormat _LocationFormat;


        public SchematronRun(CompiledSchematron schema, XdmDocument document, SchematronLocationFormat locationFormat)
        {
            _Schema = schema;
            _Document = document;
            _LocationFormat = locationFormat;
            _Environment = new XPathDynamicContext(schema.Documents)
            {
                Keys = schema.Keys
            };
            _Globals = new GlobalVariables(schema.GlobalLets, _Environment, document.Root);
            _Environment.GlobalVariables = _Globals;
        }


        public SchematronResult Execute()
        {
            List<SchematronMessage> messages = new List<SchematronMessage>();
            List<SchematronActivePattern> activePatterns = new List<SchematronActivePattern>();
            List<SchematronFiredRule> firedRules = new List<SchematronFiredRule>();

            foreach (CompiledPattern pattern in _Schema.Patterns)
            {
                activePatterns.Add(new SchematronActivePattern(pattern.Id, pattern.Name));

                // the first rule (in schema order) whose context matches a node fires for it
                Dictionary<XdmNode, int> ruleOfNode = new Dictionary<XdmNode, int>();
                for (int i = 0; i < pattern.Rules.Count; i++)
                {
                    CompiledRule rule = pattern.Rules[i];
                    XdmValue matches;
                    try
                    {
                        matches = rule.Context.Evaluate(_Document.Root, _Environment, _Globals);
                    }
                    catch (XPathException e)
                    {
                        throw _Error(e, pattern, rule, null, _Document.Root);
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
                    continue;
                }
                List<XdmNode> nodes = ruleOfNode.Keys.ToList();
                nodes.Sort(XdmNode.CompareOrder);
                foreach (XdmNode node in nodes)
                {
                    CompiledRule rule = pattern.Rules[ruleOfNode[node]];
                    firedRules.Add(new SchematronFiredRule(pattern.Id, rule.Id, rule.ContextText, messages.Count));
                    _Fire(pattern, rule, node, messages);
                }
            }
            return new SchematronResult(_Schema.Title, _Schema.SchemaVersion, _Schema.Namespaces, activePatterns, messages, firedRules);
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


        private void _Fire(CompiledPattern pattern, CompiledRule rule, XdmNode node, List<SchematronMessage> messages)
        {
            RuleVariables variables = rule.Lets.Count == 0 ? null : new RuleVariables(rule.Lets, _Environment, node, _Globals);
            IXPathVariableResolver resolver = (IXPathVariableResolver)variables ?? _Globals;
            foreach (CompiledCheck check in rule.Checks)
            {
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
                        PatternId = pattern.Id,
                        RuleContext = rule.ContextText,
                        Text = _BuildText(check.Message, resolver, node)
                    };
                    if (check.DiagnosticIds.Length > 0)
                    {
                        List<SchematronDiagnostic> diagnostics = new List<SchematronDiagnostic>();
                        foreach (string id in check.DiagnosticIds)
                        {
                            CompiledDiagnostic diagnostic;
                            if (_Schema.Diagnostics.TryGetValue(id, out diagnostic))
                            {
                                diagnostics.Add(new SchematronDiagnostic(id, _BuildText(diagnostic.Message, resolver, node)));
                            }
                        }
                        message.Diagnostics = diagnostics;
                    }
                    messages.Add(message);
                }
                catch (XPathException e)
                {
                    throw _Error(e, pattern, rule, check, node);
                }
            }
        }


        private string _BuildText(List<MessagePart> parts, IXPathVariableResolver variables, XdmNode node)
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
                else
                {
                    // the string values of the atomized items: without a schema, a node atomizes to its string value
                    XdmValue value = part.ValueOf.Evaluate(node, _Environment, variables);
                    sb.Append(String.Join(" ", value.Select(i => i.StringValue)));
                }
            }
            return sb.ToString();
        }


        private static SchematronException _Error(XPathException e, CompiledPattern pattern, CompiledRule rule, CompiledCheck check, XdmNode node)
        {
            string where = "pattern " + pattern.Id + ", rule \"" + rule.ContextText + "\"" + (check != null ? ", " + (check.IsAssert ? "assert " : "report ") + (check.Id ?? "") + " test \"" + check.TestText + "\"" : "");
            return new SchematronException("Dynamic error in " + where + " at " + NodeLocations.EQName(node) + ": " + e.Message, e.Code, e)
            {
                PatternId = pattern.Id,
                RuleContext = rule.ContextText,
                AssertionId = check?.Id,
                Location = NodeLocations.EQName(node)
            };
        }
    }


    /// <summary>
    /// The global variables (schema, phase and pattern lets), evaluated lazily with the document node as context item.
    /// </summary>
    internal sealed class GlobalVariables : IXPathVariableResolver
    {
        private readonly List<CompiledLet> _Lets;
        private readonly XPathDynamicContext _Environment;
        private readonly XdmNode _ContextNode;
        private readonly Dictionary<string, int> _Index = new Dictionary<string, int>();
        private readonly XdmValue[] _Values;
        private readonly bool[] _Evaluating;


        public GlobalVariables(List<CompiledLet> lets, XPathDynamicContext environment, XdmNode contextNode)
        {
            _Lets = lets;
            _Environment = environment;
            _ContextNode = contextNode;
            _Values = new XdmValue[lets.Count];
            _Evaluating = new bool[lets.Count];
            for (int i = 0; i < lets.Count; i++)
            {
                if (!_Index.ContainsKey(lets[i].Name))
                {
                    _Index[lets[i].Name] = i;
                }
            }
        }


        public bool TryResolve(string expandedName, out XdmValue value)
        {
            int index;
            if (!_Index.TryGetValue(expandedName, out index))
            {
                value = null;
                return false;
            }
            value = _Values[index];
            if (value != null)
            {
                return true;
            }
            if (_Evaluating[index])
            {
                throw new XPathException("XTDE0640", "Circular definition of variable $" + expandedName);
            }
            _Evaluating[index] = true;
            try
            {
                CompiledLet let = _Lets[index];
                value = let.Value.Evaluate(_ContextNode, _Environment, this);
                if (let.As != null)
                {
                    value = let.As.Convert(value, "the value of variable $" + let.Name);
                }
                _Values[index] = value;
            }
            finally
            {
                _Evaluating[index] = false;
            }
            return true;
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
            CompiledLet let = _Lets[index];
            XdmValue value = let.Value.Evaluate(_ContextNode, _Environment, new View(this, index));
            if (let.As != null)
            {
                value = let.As.Convert(value, "the value of variable $" + let.Name);
            }
            return value;
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
