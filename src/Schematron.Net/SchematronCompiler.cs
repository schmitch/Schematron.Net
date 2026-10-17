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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Schematron.Net.XPath;

namespace Schematron.Net
{
    // --------------------------------------------------------------------------------------------------------------
    // compiled model
    // --------------------------------------------------------------------------------------------------------------

    internal sealed class CompiledSchematron
    {
        public string Title;
        public string SchemaVersion;
        public string BaseUri;

        /// <summary>The defaultPhase attribute, null if there is none.</summary>
        public string DefaultPhase;
        public List<KeyValuePair<string, string>> Namespaces = new List<KeyValuePair<string, string>>();
        public List<CompiledLet> SchemaLets = new List<CompiledLet>();
        public Dictionary<string, CompiledPhase> Phases = new Dictionary<string, CompiledPhase>(StringComparer.Ordinal);

        /// <summary>All concrete patterns in schema order, whether a phase activates them or not.</summary>
        public List<PatternEntry> Patterns = new List<PatternEntry>();
        public Dictionary<string, XslKey> Keys = new Dictionary<string, XslKey>();
        public Dictionary<string, CompiledDiagnostic> Diagnostics = new Dictionary<string, CompiledDiagnostic>();
        public Dictionary<string, CompiledProperty> Properties = new Dictionary<string, CompiledProperty>(StringComparer.Ordinal);
        public DocumentCache Documents;
        public IResourceResolver Resolver;

        private readonly ConcurrentDictionary<string, PhasePlan> _Plans = new ConcurrentDictionary<string, PhasePlan>(StringComparer.Ordinal);


        /// <summary>
        /// Resolves a phase name: null and #DEFAULT select the defaultPhase attribute (or #ALL), #ALL all patterns.
        /// </summary>
        public string ResolvePhase(string phase)
        {
            if (phase == null || phase == "#DEFAULT")
            {
                return DefaultPhase ?? "#ALL";
            }
            return phase;
        }


        /// <summary>
        /// The patterns and variables of a phase, checked once and then cached.
        /// </summary>
        /// <exception cref="SchematronException">The phase does not exist, or one of its patterns is invalid</exception>
        public PhasePlan GetPlan(string phase)
        {
            PhasePlan plan;
            if (_Plans.TryGetValue(phase, out plan))
            {
                return plan;
            }
            plan = PhasePlan.Create(this, phase);
            return _Plans.GetOrAdd(phase, plan);
        }
    }


    internal sealed class CompiledLet
    {
        /// <summary>The expanded name (local name or {uri}local), as resolved by <see cref="IXPathVariableResolver"/>.</summary>
        public string Name;

        /// <summary>The name as written in the schema.</summary>
        public string LexicalName;

        /// <summary>The value attribute; null for a variable with element content.</summary>
        public XPathExpression Value;

        /// <summary>The value of a variable with element content: a document node with a copy of the content.</summary>
        public XdmValue Constant;
        public XPathSequenceType As;


        /// <summary>
        /// Evaluates the variable (with the context node of its scope) and converts it to its declared type.
        /// </summary>
        public XdmValue Evaluate(XdmNode contextNode, XPathDynamicContext environment, IXPathVariableResolver variables)
        {
            XdmValue value = Constant ?? Value.Evaluate(contextNode, environment, variables);
            if (As != null)
            {
                value = As.Convert(value, "the value of variable $" + LexicalName);
            }
            return value;
        }
    }


    internal sealed class CompiledPhase
    {
        public string Id;
        public List<string> ActivePatterns = new List<string>();
        public List<CompiledLet> Lets = new List<CompiledLet>();
    }


    /// <summary>
    /// A concrete pattern: compiled, or the error that made its compilation fail (raised when a phase that activates
    /// the pattern is used).
    /// </summary>
    internal sealed class PatternEntry
    {
        /// <summary>The id attribute, null for an anonymous pattern.</summary>
        public string SourceId;
        public CompiledPattern Pattern;
        public SchematronException Error;
    }


    internal sealed class CompiledPattern
    {
        public string Id;
        public string Name;
        public List<CompiledLet> Lets = new List<CompiledLet>();

        /// <summary>sch:pattern/@documents: the subordinate documents the rules apply to, null for the instance document.</summary>
        public XPathExpression Documents;
        public List<CompiledRule> Rules = new List<CompiledRule>();
    }


    internal sealed class CompiledRule
    {
        public string Id;
        public string ContextText;
        public XPathExpression Context;
        public XPathExpression Subject;
        public string Flag;
        public string Role;
        public List<CompiledLet> Lets = new List<CompiledLet>();
        public List<CompiledCheck> Checks = new List<CompiledCheck>();
    }


    internal sealed class CompiledCheck
    {
        public bool IsAssert;
        public string Id;
        public string TestText;
        public XPathExpression Test;
        public string Flag;
        public string Role;
        public string See;
        public string Icon;
        public string Fpi;
        public XPathExpression Subject;
        public List<MessagePart> Message = new List<MessagePart>();

        /// <summary>The xml:lang of the assertion, null if it has none.</summary>
        public string Language;
        public string[] DiagnosticIds = new string[0];
        public string[] PropertyIds = new string[0];
    }


    internal sealed class CompiledDiagnostic
    {
        public string Id;
        public string Language;
        public List<MessagePart> Message = new List<MessagePart>();
    }


    internal sealed class CompiledProperty
    {
        public string Id;
        public string Role;
        public string Scheme;
        public List<MessagePart> Message = new List<MessagePart>();
    }


    /// <summary>
    /// A part of an assertion message (or of a diagnostic or property): literal text, sch:value-of, sch:name or
    /// xsl:copy-of.
    /// </summary>
    internal sealed class MessagePart
    {
        public string Text;
        public XPathExpression ValueOf;
        public bool IsName;
        public XPathExpression NamePath;
        public XPathExpression CopyOf;
    }


    /// <summary>
    /// The patterns a phase activates and its variables. Creating a plan checks that every variable the expressions
    /// reference is declared where it is used (XPST0008).
    /// </summary>
    internal sealed class PhasePlan
    {
        public string Phase;
        public List<CompiledPattern> Patterns = new List<CompiledPattern>();
        public List<CompiledLet> PhaseLets = new List<CompiledLet>();


        public static PhasePlan Create(CompiledSchematron schema, string phase)
        {
            PhasePlan plan = new PhasePlan { Phase = phase };
            List<PatternEntry> entries;
            if (phase == "#ALL")
            {
                entries = schema.Patterns;
            }
            else
            {
                CompiledPhase compiled;
                if (!schema.Phases.TryGetValue(phase, out compiled))
                {
                    throw new SchematronException("The schema has no phase '" + phase + "'");
                }
                HashSet<string> active = new HashSet<string>(compiled.ActivePatterns, StringComparer.Ordinal);
                entries = schema.Patterns.Where(p => p.SourceId != null && active.Contains(p.SourceId)).ToList();
                plan.PhaseLets = compiled.Lets;
            }
            foreach (PatternEntry entry in entries)
            {
                if (entry.Error != null)
                {
                    throw new SchematronException(entry.Error.Message, entry.Error.ErrorCode, entry.Error);
                }
                plan.Patterns.Add(entry.Pattern);
            }
            new VariableCheck(schema, plan).Run();
            return plan;
        }
    }


    /// <summary>
    /// The static check of variable references: an expression may use the variables of its rule (those declared
    /// before it, for a rule variable), of its pattern, of the phase and of the schema, and the variables of other
    /// active patterns (pattern variables are global) if only one pattern declares the name.
    /// </summary>
    internal sealed class VariableCheck
    {
        private readonly CompiledSchematron _Schema;
        private readonly PhasePlan _Plan;
        private readonly HashSet<string> _Global = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _PatternVariables = new Dictionary<string, List<string>>(StringComparer.Ordinal);


        public VariableCheck(CompiledSchematron schema, PhasePlan plan)
        {
            _Schema = schema;
            _Plan = plan;
            _Global.UnionWith(schema.SchemaLets.Select(l => l.Name));
            _Global.UnionWith(plan.PhaseLets.Select(l => l.Name));
            foreach (CompiledPattern pattern in plan.Patterns)
            {
                foreach (CompiledLet let in pattern.Lets)
                {
                    List<string> owners;
                    if (!_PatternVariables.TryGetValue(let.Name, out owners))
                    {
                        owners = new List<string>();
                        _PatternVariables[let.Name] = owners;
                    }
                    owners.Add(pattern.Id);
                }
            }
        }


        public void Run()
        {
            foreach (CompiledLet let in _Schema.SchemaLets.Concat(_Plan.PhaseLets))
            {
                _Check(let.Value, null, null, "sch:let $" + let.LexicalName);
            }
            foreach (XslKey key in _Schema.Keys.Values)
            {
                _Check(key.Match, null, null, "xsl:key " + key.Name);
                _Check(key.Use, null, null, "xsl:key " + key.Name);
            }
            foreach (CompiledPattern pattern in _Plan.Patterns)
            {
                string where = " in pattern " + pattern.Id;
                foreach (CompiledLet let in pattern.Lets)
                {
                    _Check(let.Value, pattern, null, "sch:let $" + let.LexicalName + where);
                }
                _Check(pattern.Documents, pattern, null, "sch:pattern/@documents" + where);
                foreach (CompiledRule rule in pattern.Rules)
                {
                    string inRule = " of rule \"" + rule.ContextText + "\"" + where;
                    _Check(rule.Context, pattern, null, "the context" + inRule);
                    List<string> ruleVariables = new List<string>();
                    foreach (CompiledLet let in rule.Lets)
                    {
                        _Check(let.Value, pattern, ruleVariables, "sch:let $" + let.LexicalName + inRule);
                        ruleVariables.Add(let.Name);
                    }
                    _Check(rule.Subject, pattern, ruleVariables, "the subject" + inRule);
                    foreach (CompiledCheck check in rule.Checks)
                    {
                        string what = (check.IsAssert ? "assert" : "report") + (check.Id != null ? " " + check.Id : "") + inRule;
                        _Check(check.Test, pattern, ruleVariables, "the test of " + what);
                        _Check(check.Subject, pattern, ruleVariables, "the subject of " + what);
                        foreach (MessagePart part in check.Message)
                        {
                            _Check(part.ValueOf, pattern, ruleVariables, "the message of " + what);
                            _Check(part.NamePath, pattern, ruleVariables, "the message of " + what);
                            _Check(part.CopyOf, pattern, ruleVariables, "the message of " + what);
                        }
                    }
                }
            }
        }


        private void _Check(XPathExpression expression, CompiledPattern pattern, List<string> ruleVariables, string where)
        {
            if (expression == null)
            {
                return;
            }
            foreach (string name in expression.VariableReferences)
            {
                if ((ruleVariables != null && ruleVariables.Contains(name)) || (pattern != null && pattern.Lets.Any(l => l.Name == name)) || _Global.Contains(name))
                {
                    continue;
                }
                List<string> owners;
                if (_PatternVariables.TryGetValue(name, out owners))
                {
                    if (owners.Count == 1)
                    {
                        continue;
                    }
                    throw new SchematronException("Variable $" + name + " in " + where + " is ambiguous: the patterns " + String.Join(", ", owners) + " declare it", "XPST0008");
                }
                throw new SchematronException("Variable $" + name + " in " + where + " is not declared", "XPST0008");
            }
        }
    }


    // --------------------------------------------------------------------------------------------------------------
    // source model (after includes, before abstract patterns are instantiated)
    // --------------------------------------------------------------------------------------------------------------

    internal abstract class RawRuleItem
    {
        public XdmNode Node;
    }


    internal sealed class RawLet : RawRuleItem
    {
        public string Name;

        /// <summary>The value attribute; null if the element content is the value.</summary>
        public string Value;
        public string As;
    }


    internal sealed class RawExtends : RawRuleItem
    {
        public string RuleId;
    }


    internal sealed class RawMessagePart
    {
        public string Text;
        public string ValueOfSelect;
        public bool IsName;
        public string NamePath;
        public string CopyOfSelect;
        public XdmNode Node;
    }


    internal sealed class RawCheck : RawRuleItem
    {
        public bool IsAssert;
        public string Test;
        public string Id;
        public string Flag;
        public string Role;
        public string See;
        public string Icon;
        public string Fpi;
        public string Subject;
        public string Diagnostics;
        public string Properties;
        public string Language;
        public List<RawMessagePart> Message = new List<RawMessagePart>();
    }


    internal sealed class RawRule
    {
        public XdmNode Node;
        public string Id;
        public string Context;
        public bool IsAbstract;
        public string Subject;
        public string Flag;
        public string Role;
        public List<RawRuleItem> Items = new List<RawRuleItem>();
    }


    internal sealed class RawPattern
    {
        public XdmNode Node;
        public string Id;
        public string Name;
        public bool IsAbstract;
        public string IsA;
        public string Documents;
        public List<KeyValuePair<string, string>> Params = new List<KeyValuePair<string, string>>();
        public List<RawLet> Lets = new List<RawLet>();
        public List<RawRule> Rules = new List<RawRule>();

        /// <summary>The abstract rules declared in the pattern: only the rules of the pattern can extend them.</summary>
        public Dictionary<string, RawRule> AbstractRules = new Dictionary<string, RawRule>(StringComparer.Ordinal);
    }


    internal sealed class RawPhase
    {
        public string Id;
        public List<string> ActivePatterns = new List<string>();
        public List<RawLet> Lets = new List<RawLet>();
    }


    /// <summary>
    /// Loads an ISO Schematron schema (includes, abstract patterns and rules, phases, sch:let, xsl:function and
    /// xsl:key) and compiles all expressions.
    /// </summary>
    internal sealed class SchematronCompiler
    {
        private static readonly HashSet<string> _SchematronNamespaces = new HashSet<string>
        {
            SchematronNamespaces.Schematron,
            "http://www.ascc.net/xml/schematron"
        };

        /// <summary>The query bindings of XPath 2.0 and later.</summary>
        private static readonly HashSet<string> _XPath2Bindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "xslt2", "xslt3", "xpath2", "xpath3", "xpath31"
        };

        /// <summary>The query bindings of XPath 1.0 (xslt is the default), evaluated in XPath 1.0 compatibility mode.</summary>
        private static readonly HashSet<string> _XPath1Bindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "xslt", "xpath", "exslt"
        };

        private XdmNode _Root;
        private string _Title;
        private string _SchemaVersion;
        private string _DefaultPhase;
        private bool _XPath1;
        private readonly List<KeyValuePair<string, string>> _Namespaces = new List<KeyValuePair<string, string>>();
        private readonly List<RawLet> _Lets = new List<RawLet>();
        private readonly List<RawPhase> _Phases = new List<RawPhase>();
        private readonly List<RawPattern> _Patterns = new List<RawPattern>();

        /// <summary>The abstract rules of sch:rules, which every rule can extend.</summary>
        private readonly Dictionary<string, RawRule> _GlobalAbstractRules = new Dictionary<string, RawRule>(StringComparer.Ordinal);

        /// <summary>
        /// The abstract rules of all patterns: a rule may extend an abstract rule of another pattern, as with the ISO
        /// skeleton (ISO Schematron 2016 only allows the rules of the same pattern and of sch:rules).
        /// </summary>
        private readonly Dictionary<string, RawRule> _PatternAbstractRules = new Dictionary<string, RawRule>(StringComparer.Ordinal);
        private readonly List<XdmNode> _Functions = new List<XdmNode>();
        private readonly List<XdmNode> _Keys = new List<XdmNode>();
        private readonly Dictionary<string, XdmNode> _Diagnostics = new Dictionary<string, XdmNode>();
        private readonly Dictionary<string, XdmNode> _Properties = new Dictionary<string, XdmNode>(StringComparer.Ordinal);
        private int _IncludeDepth;
        private int _ExtendsDepth;
        private IResourceResolver _Resolver;


        public static CompiledSchematron Compile(string schemaUri, IResourceResolver resolver)
        {
            XdmDocument document;
            using (Stream stream = Resources.Open(resolver, schemaUri))
            {
                document = LoadSchemaDocument(stream, schemaUri);
            }
            return Compile(document, schemaUri, resolver);
        }


        public static CompiledSchematron Compile(XdmDocument document, string schemaUri, IResourceResolver resolver)
        {
            SchematronCompiler compiler = new SchematronCompiler();
            compiler._Resolver = resolver ?? FileResourceResolver.Instance;
            XdmNode root = document.Root.Children.FirstOrDefault(n => n.Kind == XdmNodeKind.Element);
            if (root == null || !_IsSch(root, "schema"))
            {
                throw new SchematronException("The document " + schemaUri + " is not a Schematron schema (expected sch:schema)");
            }
            compiler._Root = root;
            compiler._ReadSchema(root);
            return compiler._Compile(schemaUri);
        }


        internal static XdmDocument LoadSchemaDocument(Stream stream, string uri)
        {
            System.Xml.XmlReaderSettings settings = new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Ignore,
                XmlResolver = null,
                CloseInput = false
            };
            using (System.Xml.XmlReader reader = System.Xml.XmlReader.Create(stream, settings, uri))
            {
                return XdmDocument.Load(reader);
            }
        }


        // ----------------------------------------------------------------------------------------------------------
        // reading the source
        // ----------------------------------------------------------------------------------------------------------

        private static bool _IsSch(XdmNode node, string localName)
        {
            return node.Kind == XdmNodeKind.Element && node.LocalName == localName && _SchematronNamespaces.Contains(node.NamespaceUri);
        }


        private static bool _IsXsl(XdmNode node, string localName)
        {
            return node.Kind == XdmNodeKind.Element && node.LocalName == localName && node.NamespaceUri == SchematronNamespaces.Xsl;
        }


        private static IEnumerable<XdmNode> _Elements(XdmNode node)
        {
            return node.Children.Where(c => c.Kind == XdmNodeKind.Element);
        }


        private static string _Attribute(XdmNode node, string name)
        {
            return node.GetAttribute(name);
        }


        /// <summary>
        /// Resolves sch:include: returns the elements to process instead of the given element.
        /// </summary>
        private IEnumerable<XdmNode> _Expand(XdmNode element)
        {
            if (!_IsSch(element, "include"))
            {
                yield return element;
                yield break;
            }
            if (++_IncludeDepth > 50)
            {
                throw new SchematronException("Too many nested sch:include elements (circular include?)");
            }
            XdmNode target = _LoadReferenced(element, "sch:include");
            foreach (XdmNode expanded in _Expand(target))
            {
                yield return expanded;
            }
            _IncludeDepth--;
        }


        /// <summary>
        /// Loads the element an href attribute (sch:include, sch:extends) refers to: the document element, or the
        /// element with the id of the fragment identifier. The href is relative to the file of the referring element.
        /// </summary>
        private XdmNode _LoadReferenced(XdmNode element, string what)
        {
            string href = _Attribute(element, "href");
            if (String.IsNullOrEmpty(href))
            {
                throw new SchematronException(what + " without href in " + element.Document.DocumentUri);
            }
            string fragment = null;
            int hash = href.IndexOf('#');
            if (hash >= 0)
            {
                fragment = href.Substring(hash + 1);
                href = href.Substring(0, hash);
            }
            string uri = Resources.Resolve(element.Document.DocumentUri, href);
            XdmDocument included;
            try
            {
                using (Stream stream = Resources.Open(_Resolver, uri))
                {
                    included = LoadSchemaDocument(stream, uri);
                }
            }
            catch (Exception e) when (!(e is SchematronException))
            {
                throw new SchematronException("Cannot load the Schematron file " + uri + " (" + what + "): " + e.Message, null, e);
            }
            XdmNode target = included.Root.Children.FirstOrDefault(n => n.Kind == XdmNodeKind.Element);
            if (fragment != null)
            {
                target = included.Root.Descendants().FirstOrDefault(n => n.Kind == XdmNodeKind.Element && (n.GetAttribute("id") == fragment || n.GetAttribute("id", SchematronNamespaces.Xml) == fragment));
            }
            if (target == null)
            {
                throw new SchematronException("The Schematron file " + uri + " (" + what + ") has no element " + (fragment ?? ""));
            }
            return target;
        }


        private void _ReadSchema(XdmNode schema)
        {
            _SchemaVersion = _Attribute(schema, "schemaVersion");
            _DefaultPhase = _Attribute(schema, "defaultPhase");
            string queryBinding = _Attribute(schema, "queryBinding");
            if (String.IsNullOrWhiteSpace(queryBinding) || _XPath1Bindings.Contains(queryBinding.Trim()))
            {
                // the default query binding is xslt, i.e. XPath 1.0
                _XPath1 = true;
            }
            else if (!_XPath2Bindings.Contains(queryBinding.Trim()))
            {
                throw new SchematronException("The query language binding '" + queryBinding + "' is not supported (supported: xslt2, xslt3, xpath2, xpath3, xpath31; xslt, xpath and exslt in XPath 1.0 compatibility mode)");
            }
            foreach (XdmNode child in _Elements(schema).SelectMany(_Expand))
            {
                _ReadSchemaChild(child);
            }
        }


        private void _ReadSchemaChild(XdmNode child)
        {
            if (_IsSch(child, "title"))
            {
                _Title = Text.Collapse(child.StringValue);
            }
            else if (_IsSch(child, "ns"))
            {
                _Namespaces.Add(new KeyValuePair<string, string>(_Attribute(child, "prefix") ?? "", _Attribute(child, "uri") ?? ""));
            }
            else if (_IsSch(child, "let"))
            {
                _Lets.Add(_ReadLet(child));
            }
            else if (_IsSch(child, "phase"))
            {
                _Phases.Add(_ReadPhase(child));
            }
            else if (_IsSch(child, "pattern"))
            {
                _Patterns.Add(_ReadPattern(child));
            }
            else if (_IsSch(child, "rules"))
            {
                foreach (XdmNode rule in _Elements(child).SelectMany(_Expand).Where(r => _IsSch(r, "rule")))
                {
                    _AddAbstractRule(_ReadRule(rule), _GlobalAbstractRules);
                }
            }
            else if (_IsSch(child, "diagnostics"))
            {
                foreach (XdmNode diagnostic in _Elements(child).SelectMany(_Expand).Where(d => _IsSch(d, "diagnostic")))
                {
                    string id = _Attribute(diagnostic, "id");
                    if (id != null)
                    {
                        _Diagnostics[id] = diagnostic;
                    }
                }
            }
            else if (_IsSch(child, "properties"))
            {
                foreach (XdmNode property in _Elements(child).SelectMany(_Expand).Where(d => _IsSch(d, "property")))
                {
                    string id = _Attribute(property, "id");
                    if (id != null && !_Properties.ContainsKey(id))
                    {
                        _Properties[id] = property;
                    }
                }
            }
            else if (_IsSch(child, "schema"))
            {
                // an included complete schema: merge its content
                foreach (XdmNode nested in _Elements(child).SelectMany(_Expand))
                {
                    _ReadSchemaChild(nested);
                }
            }
            else if (_IsXsl(child, "function"))
            {
                _Functions.Add(child);
            }
            else if (_IsXsl(child, "key"))
            {
                _Keys.Add(child);
            }
            else if (_IsXsl(child, "include") || _IsXsl(child, "import"))
            {
                throw new SchematronException("xsl:include / xsl:import in Schematron schemas is not supported");
            }
            // sch:p, foreign elements: ignored
        }


        private static void _AddAbstractRule(RawRule rule, Dictionary<string, RawRule> abstractRules)
        {
            if (rule.IsAbstract && rule.Id != null && !abstractRules.ContainsKey(rule.Id))
            {
                abstractRules[rule.Id] = rule;
            }
        }


        private static RawLet _ReadLet(XdmNode node)
        {
            return new RawLet { Node = node, Name = _Attribute(node, "name"), Value = _Attribute(node, "value"), As = _Attribute(node, "as") };
        }


        private RawPhase _ReadPhase(XdmNode node)
        {
            RawPhase phase = new RawPhase { Id = _Attribute(node, "id") };
            foreach (XdmNode child in _Elements(node).SelectMany(_Expand))
            {
                if (_IsSch(child, "active"))
                {
                    phase.ActivePatterns.Add(_Attribute(child, "pattern"));
                }
                else if (_IsSch(child, "let"))
                {
                    phase.Lets.Add(_ReadLet(child));
                }
            }
            return phase;
        }


        private RawPattern _ReadPattern(XdmNode node)
        {
            RawPattern pattern = new RawPattern
            {
                Node = node,
                Id = _Attribute(node, "id"),
                IsAbstract = _Attribute(node, "abstract") == "true",
                IsA = _Attribute(node, "is-a"),
                Documents = _Attribute(node, "documents")
            };
            pattern.Name = _Attribute(node, "name");
            foreach (XdmNode child in _Elements(node).SelectMany(_Expand))
            {
                if (_IsSch(child, "title"))
                {
                    pattern.Name = pattern.Name ?? Text.Collapse(child.StringValue);
                }
                else if (_IsSch(child, "param"))
                {
                    pattern.Params.Add(new KeyValuePair<string, string>(_Attribute(child, "name"), _Attribute(child, "value") ?? ""));
                }
                else if (_IsSch(child, "let"))
                {
                    pattern.Lets.Add(_ReadLet(child));
                }
                else if (_IsSch(child, "rule"))
                {
                    RawRule rule = _ReadRule(child);
                    _AddAbstractRule(rule, pattern.AbstractRules);
                    if (!pattern.IsAbstract)
                    {
                        _AddAbstractRule(rule, _PatternAbstractRules);
                    }
                    pattern.Rules.Add(rule);
                }
            }
            return pattern;
        }


        private RawRule _ReadRule(XdmNode node)
        {
            RawRule rule = new RawRule
            {
                Node = node,
                Id = _Attribute(node, "id"),
                Context = _Attribute(node, "context"),
                IsAbstract = _Attribute(node, "abstract") == "true",
                Subject = _Attribute(node, "subject"),
                Flag = _Attribute(node, "flag"),
                Role = _Attribute(node, "role")
            };
            foreach (XdmNode child in _Elements(node).SelectMany(_Expand))
            {
                if (_IsSch(child, "let"))
                {
                    rule.Items.Add(_ReadLet(child));
                }
                else if (_IsSch(child, "assert") || _IsSch(child, "report"))
                {
                    rule.Items.Add(_ReadCheck(child));
                }
                else if (_IsSch(child, "extends"))
                {
                    string ruleId = _Attribute(child, "rule");
                    if (ruleId != null)
                    {
                        rule.Items.Add(new RawExtends { Node = child, RuleId = ruleId });
                    }
                    else
                    {
                        rule.Items.AddRange(_ReadExternalRule(child).Items);
                    }
                }
            }
            return rule;
        }


        /// <summary>
        /// sch:extends/@href: the content of the external rule (a sch:rule in another file); its own references are
        /// relative to its file.
        /// </summary>
        private RawRule _ReadExternalRule(XdmNode extends)
        {
            if (++_ExtendsDepth > 50)
            {
                throw new SchematronException("Too many nested sch:extends elements (circular reference?)");
            }
            XdmNode target = _LoadReferenced(extends, "sch:extends");
            if (!_IsSch(target, "rule"))
            {
                throw new SchematronException("sch:extends/@href must refer to a sch:rule, found " + target.Name + " in " + target.Document.DocumentUri);
            }
            RawRule rule = _ReadRule(target);
            _ExtendsDepth--;
            return rule;
        }


        private RawCheck _ReadCheck(XdmNode node)
        {
            RawCheck check = new RawCheck
            {
                Node = node,
                IsAssert = node.LocalName == "assert",
                Test = _Attribute(node, "test"),
                Id = _Attribute(node, "id"),
                Flag = _Attribute(node, "flag"),
                Role = _Attribute(node, "role"),
                See = _Attribute(node, "see"),
                Icon = _Attribute(node, "icon"),
                Fpi = _Attribute(node, "fpi"),
                Subject = _Attribute(node, "subject"),
                Diagnostics = _Attribute(node, "diagnostics"),
                Properties = _Attribute(node, "properties"),
                Language = node.GetAttribute("lang", SchematronNamespaces.Xml)
            };
            if (check.Test == null)
            {
                throw new SchematronException("sch:" + node.LocalName + " without test attribute" + (check.Id != null ? " (" + check.Id + ")" : ""));
            }
            _ReadMessage(node, check.Message);
            return check;
        }


        private static void _ReadMessage(XdmNode node, List<RawMessagePart> parts)
        {
            foreach (XdmNode child in node.Children)
            {
                switch (child.Kind)
                {
                    case XdmNodeKind.Text:
                        parts.Add(new RawMessagePart { Text = child.StringValue });
                        break;
                    case XdmNodeKind.Element:
                        if (_IsSch(child, "value-of"))
                        {
                            parts.Add(new RawMessagePart { ValueOfSelect = _Attribute(child, "select"), Node = child });
                        }
                        else if (_IsSch(child, "name"))
                        {
                            parts.Add(new RawMessagePart { IsName = true, NamePath = _Attribute(child, "path"), Node = child });
                        }
                        else if (_IsXsl(child, "copy-of"))
                        {
                            parts.Add(new RawMessagePart { CopyOfSelect = _Attribute(child, "select") ?? ".", Node = child });
                        }
                        else
                        {
                            // sch:emph, sch:dir, sch:span and foreign markup: only the text content is kept
                            _ReadMessage(child, parts);
                        }
                        break;
                }
            }
        }


        // ----------------------------------------------------------------------------------------------------------
        // abstract patterns and rules
        // ----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Replaces the parameter references ($name) of an abstract pattern instance in one pass: a reference is
        /// replaced only if the name ends there, so a parameter x does not change $xy, and the longest matching name
        /// wins.
        /// </summary>
        private static string _ReplaceParams(string source, List<KeyValuePair<string, string>> parameters)
        {
            if (source == null || parameters.Count == 0 || source.IndexOf('$') < 0)
            {
                return source;
            }
            List<KeyValuePair<string, string>> longestFirst = parameters.Where(p => !String.IsNullOrEmpty(p.Key)).OrderByDescending(p => p.Key.Length).ToList();
            StringBuilder sb = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                if (source[i] == '$')
                {
                    int start = i + 1;
                    int match = longestFirst.FindIndex(p => String.CompareOrdinal(source, start, p.Key, 0, p.Key.Length) == 0 && !_IsNameChar(source, start + p.Key.Length));
                    if (match >= 0)
                    {
                        sb.Append(longestFirst[match].Value);
                        i = start + longestFirst[match].Key.Length;
                        continue;
                    }
                }
                sb.Append(source[i]);
                i++;
            }
            return sb.ToString();
        }


        /// <summary>
        /// True if the character at the position continues a variable name (an NCName, or a QName with a prefix).
        /// </summary>
        private static bool _IsNameChar(string s, int position)
        {
            if (position >= s.Length)
            {
                return false;
            }
            char ch = s[position];
            return Char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.' || ch == ':' || ch == '·';
        }


        private static RawRule _Instantiate(RawRule rule, List<KeyValuePair<string, string>> parameters)
        {
            RawRule copy = new RawRule
            {
                Node = rule.Node,
                Id = rule.Id,
                Context = _ReplaceParams(rule.Context, parameters),
                IsAbstract = rule.IsAbstract,
                Subject = _ReplaceParams(rule.Subject, parameters),
                Flag = rule.Flag,
                Role = rule.Role
            };
            foreach (RawRuleItem item in rule.Items)
            {
                switch (item)
                {
                    case RawLet let:
                        copy.Items.Add(new RawLet { Node = let.Node, Name = let.Name, Value = _ReplaceParams(let.Value, parameters), As = let.As });
                        break;
                    case RawCheck check:
                        RawCheck c = new RawCheck
                        {
                            Node = check.Node,
                            IsAssert = check.IsAssert,
                            Test = _ReplaceParams(check.Test, parameters),
                            Id = check.Id,
                            Flag = check.Flag,
                            Role = check.Role,
                            See = check.See,
                            Icon = check.Icon,
                            Fpi = check.Fpi,
                            Subject = _ReplaceParams(check.Subject, parameters),
                            Diagnostics = check.Diagnostics,
                            Properties = check.Properties,
                            Language = check.Language
                        };
                        foreach (RawMessagePart part in check.Message)
                        {
                            c.Message.Add(new RawMessagePart
                            {
                                Text = part.Text,
                                ValueOfSelect = _ReplaceParams(part.ValueOfSelect, parameters),
                                IsName = part.IsName,
                                NamePath = _ReplaceParams(part.NamePath, parameters),
                                CopyOfSelect = _ReplaceParams(part.CopyOfSelect, parameters),
                                Node = part.Node
                            });
                        }
                        copy.Items.Add(c);
                        break;
                    default:
                        copy.Items.Add(item);
                        break;
                }
            }
            return copy;
        }


        private List<RawRuleItem> _ExpandExtends(List<RawRuleItem> items, RawPattern pattern, int depth)
        {
            if (depth > 20)
            {
                throw new SchematronException("Too many nested sch:extends (circular abstract rules?)");
            }
            List<RawRuleItem> result = new List<RawRuleItem>();
            foreach (RawRuleItem item in items)
            {
                if (item is RawExtends extends)
                {
                    RawRule abstractRule;
                    // the pattern's own abstract rules (instantiated with the parameters of an abstract pattern), sch:rules, other patterns
                    if (!pattern.AbstractRules.TryGetValue(extends.RuleId, out abstractRule) && !_GlobalAbstractRules.TryGetValue(extends.RuleId, out abstractRule)
                        && !_PatternAbstractRules.TryGetValue(extends.RuleId, out abstractRule))
                    {
                        throw new SchematronException("The schema defines no abstract rule named '" + extends.RuleId + "'");
                    }
                    result.AddRange(_ExpandExtends(abstractRule.Items, pattern, depth + 1));
                }
                else
                {
                    result.Add(item);
                }
            }
            return result;
        }


        /// <summary>
        /// Returns the concrete patterns (abstract patterns instantiated) in schema order.
        /// </summary>
        private List<RawPattern> _ConcretePatterns()
        {
            List<RawPattern> result = new List<RawPattern>();
            foreach (RawPattern pattern in _Patterns)
            {
                if (pattern.IsAbstract)
                {
                    continue;
                }
                if (pattern.IsA == null)
                {
                    result.Add(pattern);
                    continue;
                }
                RawPattern abstractPattern = _Patterns.FirstOrDefault(p => p.IsAbstract && p.Id == pattern.IsA);
                if (abstractPattern == null)
                {
                    throw new SchematronException("The schema defines no abstract pattern named '" + pattern.IsA + "'");
                }
                RawPattern instance = new RawPattern
                {
                    Node = pattern.Node,
                    Id = pattern.Id,
                    Name = pattern.Name ?? abstractPattern.Name,
                    Documents = pattern.Documents ?? _ReplaceParams(abstractPattern.Documents, pattern.Params),
                    Params = pattern.Params
                };
                foreach (RawLet let in abstractPattern.Lets)
                {
                    instance.Lets.Add(new RawLet { Node = let.Node, Name = let.Name, Value = _ReplaceParams(let.Value, pattern.Params), As = let.As });
                }
                instance.Lets.AddRange(pattern.Lets);
                foreach (RawRule rule in abstractPattern.Rules)
                {
                    RawRule copy = _Instantiate(rule, pattern.Params);
                    _AddAbstractRule(copy, instance.AbstractRules);
                    instance.Rules.Add(copy);
                }
                result.Add(instance);
            }
            return result;
        }


        // ----------------------------------------------------------------------------------------------------------
        // compilation
        // ----------------------------------------------------------------------------------------------------------

        private Dictionary<string, string> _NamespacesOf(XdmNode node)
        {
            Dictionary<string, string> namespaces = node.GetInScopeNamespaces();
            foreach (KeyValuePair<string, string> ns in _Namespaces)
            {
                namespaces[ns.Key] = ns.Value;
            }
            return namespaces;
        }


        private CompiledSchematron _Compile(string schemaUri)
        {
            CompiledSchematron result = new CompiledSchematron
            {
                Title = _Title,
                SchemaVersion = _SchemaVersion,
                BaseUri = schemaUri,
                DefaultPhase = _DefaultPhase,
                Namespaces = _Namespaces.ToList(),
                Documents = new DocumentCache(_Resolver),
                Resolver = _Resolver
            };

            XPathFunctionLibrary functions = new XPathFunctionLibrary();
            Func<XdmNode, XPathStaticContext> contextFor = node => new XPathStaticContext(_NamespacesOf(node ?? _Root), schemaUri, functions)
            {
                XPath1CompatibilityMode = _XPath1
            };

            try
            {
                functions.DeclareXslFunctions(_Functions, contextFor);
            }
            catch (XPathException e)
            {
                throw new SchematronException(e.Message, e.Code, e);
            }
            foreach (XdmNode key in _Keys)
            {
                _CompileKey(key, contextFor(key), result);
            }

            foreach (RawLet let in _Lets)
            {
                result.SchemaLets.Add(_CompileLet(let, contextFor(let.Node)));
            }
            _CheckUnique(result.SchemaLets, "the schema");

            foreach (string diagnosticId in _Diagnostics.Keys)
            {
                XdmNode node = _Diagnostics[diagnosticId];
                List<RawMessagePart> parts = new List<RawMessagePart>();
                _ReadMessage(node, parts);
                result.Diagnostics[diagnosticId] = new CompiledDiagnostic
                {
                    Id = diagnosticId,
                    Language = node.GetAttribute("lang", SchematronNamespaces.Xml),
                    Message = _CompileMessage(parts, contextFor(node))
                };
            }
            foreach (KeyValuePair<string, XdmNode> property in _Properties)
            {
                List<RawMessagePart> parts = new List<RawMessagePart>();
                _ReadMessage(property.Value, parts);
                result.Properties[property.Key] = new CompiledProperty
                {
                    Id = property.Key,
                    Role = _Attribute(property.Value, "role"),
                    Scheme = _Attribute(property.Value, "scheme"),
                    Message = _CompileMessage(parts, contextFor(property.Value))
                };
            }

            // all concrete patterns; an error in a pattern is raised when a phase that activates it is used
            int anonymous = 0;
            foreach (RawPattern pattern in _ConcretePatterns())
            {
                PatternEntry entry = new PatternEntry { SourceId = pattern.Id };
                string id = pattern.Id ?? ("pattern-" + (++anonymous));
                try
                {
                    entry.Pattern = _CompilePattern(pattern, id, contextFor);
                }
                catch (SchematronException e)
                {
                    entry.Error = e;
                }
                result.Patterns.Add(entry);
            }

            HashSet<string> patternIds = new HashSet<string>(result.Patterns.Where(p => p.SourceId != null).Select(p => p.SourceId), StringComparer.Ordinal);
            foreach (RawPhase phase in _Phases)
            {
                if (String.IsNullOrEmpty(phase.Id))
                {
                    throw new SchematronException("sch:phase without id");
                }
                CompiledPhase compiled = new CompiledPhase { Id = phase.Id };
                foreach (string active in phase.ActivePatterns)
                {
                    if (active == null || !patternIds.Contains(active))
                    {
                        throw new SchematronException("The phase '" + phase.Id + "' activates the pattern '" + active + "', which the schema does not define");
                    }
                    compiled.ActivePatterns.Add(active);
                }
                foreach (RawLet let in phase.Lets)
                {
                    compiled.Lets.Add(_CompileLet(let, contextFor(let.Node)));
                }
                _CheckUnique(compiled.Lets, "the phase '" + phase.Id + "'");
                if (!result.Phases.ContainsKey(phase.Id))
                {
                    result.Phases[phase.Id] = compiled;
                }
            }
            return result;
        }


        private CompiledPattern _CompilePattern(RawPattern pattern, string id, Func<XdmNode, XPathStaticContext> contextFor)
        {
            CompiledPattern compiled = new CompiledPattern
            {
                Id = id,
                Name = pattern.Name ?? pattern.Id,
                Documents = pattern.Documents != null ? _CompileExpression(pattern.Documents, contextFor(pattern.Node), "sch:pattern/@documents") : null
            };
            foreach (RawLet let in pattern.Lets)
            {
                compiled.Lets.Add(_CompileLet(let, contextFor(let.Node)));
            }
            _CheckUnique(compiled.Lets, "the pattern " + id);
            foreach (RawRule rule in pattern.Rules)
            {
                if (rule.IsAbstract)
                {
                    continue;
                }
                compiled.Rules.Add(_CompileRule(rule, pattern, contextFor));
            }
            return compiled;
        }


        /// <summary>
        /// A variable may be declared only once in a scope (the schema, a phase, a pattern, a rule).
        /// </summary>
        private static void _CheckUnique(List<CompiledLet> lets, string scope)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (CompiledLet let in lets)
            {
                if (!names.Add(let.Name))
                {
                    throw new SchematronException("The variable $" + let.LexicalName + " is declared more than once in " + scope, "XTSE0630");
                }
            }
        }


        private static XPathExpression _CompileExpression(string text, XPathStaticContext context, string what)
        {
            try
            {
                return XPathExpression.Compile(text, context);
            }
            catch (XPathException e)
            {
                throw new SchematronException("Invalid XPath expression in " + what + ": " + e.Message, e.Code, e);
            }
        }


        private static XPathExpression _CompileMatchPattern(string text, XPathStaticContext context, string what)
        {
            try
            {
                return XPathExpression.CompilePattern(text, context);
            }
            catch (XPathException e)
            {
                throw new SchematronException("Invalid pattern in " + what + ": " + e.Message, e.Code, e);
            }
        }


        private static CompiledLet _CompileLet(RawLet let, XPathStaticContext context)
        {
            CompiledLet compiled = new CompiledLet
            {
                Name = _ExpandVariableName(let.Name, context),
                LexicalName = let.Name
            };
            if (let.Value != null)
            {
                compiled.Value = _CompileExpression(let.Value, context, "sch:let $" + let.Name);
            }
            else
            {
                // the element content is the value, as with xsl:variable: a document node (temporary tree)
                compiled.Constant = XdmValue.Of(NodeCopy.ContentAsDocument(let.Node, context.BaseUri));
            }
            if (!String.IsNullOrEmpty(let.As))
            {
                try
                {
                    compiled.As = XPathSequenceType.Parse(let.As, context);
                }
                catch (XPathException e)
                {
                    throw new SchematronException("Invalid type in sch:let $" + let.Name + ": " + e.Message, e.Code, e);
                }
            }
            return compiled;
        }


        private static string _ExpandVariableName(string name, XPathStaticContext context)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw new SchematronException("sch:let without name");
            }
            try
            {
                return context.ExpandName(name);
            }
            catch (XPathException e)
            {
                throw new SchematronException("Invalid variable name '" + name + "': " + e.Message, e.Code, e);
            }
        }


        private CompiledRule _CompileRule(RawRule rule, RawPattern pattern, Func<XdmNode, XPathStaticContext> contextFor)
        {
            XPathStaticContext context = contextFor(rule.Node);
            if (String.IsNullOrEmpty(rule.Context))
            {
                throw new SchematronException("sch:rule without context" + (rule.Id != null ? " (" + rule.Id + ")" : ""));
            }
            CompiledRule compiled = new CompiledRule
            {
                Id = rule.Id,
                ContextText = rule.Context,
                Context = _CompileMatchPattern(rule.Context, context, "rule context \"" + rule.Context + "\""),
                Subject = rule.Subject != null ? _CompileExpression(rule.Subject, context, "rule subject") : null,
                Flag = rule.Flag,
                Role = rule.Role
            };
            foreach (RawRuleItem item in _ExpandExtends(rule.Items, pattern, 0))
            {
                XPathStaticContext itemContext = contextFor(item.Node);
                switch (item)
                {
                    case RawLet let:
                        compiled.Lets.Add(_CompileLet(let, itemContext));
                        break;
                    case RawCheck check:
                        compiled.Checks.Add(_CompileCheck(check, itemContext));
                        break;
                }
            }
            _CheckUnique(compiled.Lets, "the rule \"" + rule.Context + "\"");
            return compiled;
        }


        private static CompiledCheck _CompileCheck(RawCheck check, XPathStaticContext context)
        {
            string what = (check.IsAssert ? "assert" : "report") + (check.Id != null ? " " + check.Id : "") + " test \"" + check.Test + "\"";
            CompiledCheck compiled = new CompiledCheck
            {
                IsAssert = check.IsAssert,
                Id = check.Id,
                TestText = check.Test,
                Test = _CompileExpression(check.Test, context, what),
                Flag = check.Flag,
                Role = check.Role,
                See = check.See,
                Icon = check.Icon,
                Fpi = check.Fpi,
                Subject = check.Subject != null ? _CompileExpression(check.Subject, context, what) : null,
                Message = _CompileMessage(check.Message, context),
                Language = check.Language,
                DiagnosticIds = _Idrefs(check.Diagnostics),
                PropertyIds = _Idrefs(check.Properties)
            };
            return compiled;
        }


        private static string[] _Idrefs(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? new string[0] : value.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        }


        private static List<MessagePart> _CompileMessage(List<RawMessagePart> parts, XPathStaticContext context)
        {
            List<MessagePart> result = new List<MessagePart>();
            foreach (RawMessagePart part in parts)
            {
                if (part.Text != null)
                {
                    result.Add(new MessagePart { Text = part.Text });
                }
                else if (part.IsName)
                {
                    result.Add(new MessagePart { IsName = true, NamePath = part.NamePath != null ? _CompileExpression(part.NamePath, context, "sch:name/@path") : null });
                }
                else if (part.CopyOfSelect != null)
                {
                    result.Add(new MessagePart { CopyOf = _CompileExpression(part.CopyOfSelect, context, "xsl:copy-of/@select") });
                }
                else
                {
                    result.Add(new MessagePart { ValueOf = _CompileExpression(part.ValueOfSelect ?? ".", context, "sch:value-of/@select") });
                }
            }
            return result;
        }


        // ----------------------------------------------------------------------------------------------------------
        // XSLT declarations
        // ----------------------------------------------------------------------------------------------------------

        private void _CompileKey(XdmNode node, XPathStaticContext context, CompiledSchematron result)
        {
            string name = _Attribute(node, "name");
            string match = _Attribute(node, "match");
            string use = _Attribute(node, "use");
            if (name == null || match == null || use == null)
            {
                throw new SchematronException("xsl:key requires name, match and use attributes");
            }
            string expanded = _ExpandVariableName(name, context);
            result.Keys[expanded] = new XslKey(expanded, _CompileMatchPattern(match, context, "xsl:key " + name), _CompileExpression(use, context, "xsl:key " + name));
        }
    }
}
