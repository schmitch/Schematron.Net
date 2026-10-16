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
        public List<KeyValuePair<string, string>> Namespaces = new List<KeyValuePair<string, string>>();
        public List<CompiledLet> GlobalLets = new List<CompiledLet>();
        public List<CompiledPattern> Patterns = new List<CompiledPattern>();
        public Dictionary<string, XslKey> Keys = new Dictionary<string, XslKey>();
        public Dictionary<string, CompiledDiagnostic> Diagnostics = new Dictionary<string, CompiledDiagnostic>();
        public DocumentCache Documents;
    }


    internal sealed class CompiledLet
    {
        public string Name;
        public XPathExpression Value;
        public XPathSequenceType As;
    }


    internal sealed class CompiledPattern
    {
        public string Id;
        public string Name;
        public List<CompiledRule> Rules = new List<CompiledRule>();
    }


    internal sealed class CompiledRule
    {
        public string Id;
        public string ContextText;
        public XPathExpression Context;
        public XPathExpression Subject;
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
        public string[] DiagnosticIds = new string[0];
    }


    internal sealed class CompiledDiagnostic
    {
        public string Id;
        public List<MessagePart> Message = new List<MessagePart>();
    }


    /// <summary>
    /// A part of an assertion message: literal text, sch:value-of or sch:name.
    /// </summary>
    internal sealed class MessagePart
    {
        public string Text;
        public XPathExpression ValueOf;
        public bool IsName;
        public XPathExpression NamePath;
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
        public List<RawMessagePart> Message = new List<RawMessagePart>();
    }


    internal sealed class RawRule
    {
        public XdmNode Node;
        public string Id;
        public string Context;
        public bool IsAbstract;
        public string Subject;
        public List<RawRuleItem> Items = new List<RawRuleItem>();
    }


    internal sealed class RawPattern
    {
        public XdmNode Node;
        public string Id;
        public string Name;
        public bool IsAbstract;
        public string IsA;
        public List<KeyValuePair<string, string>> Params = new List<KeyValuePair<string, string>>();
        public List<RawLet> Lets = new List<RawLet>();
        public List<RawRule> Rules = new List<RawRule>();
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

        private XdmNode _Root;
        private string _Title;
        private string _SchemaVersion;
        private string _DefaultPhase;
        private readonly List<KeyValuePair<string, string>> _Namespaces = new List<KeyValuePair<string, string>>();
        private readonly List<RawLet> _Lets = new List<RawLet>();
        private readonly List<RawPhase> _Phases = new List<RawPhase>();
        private readonly List<RawPattern> _Patterns = new List<RawPattern>();
        private readonly Dictionary<string, RawRule> _AbstractRules = new Dictionary<string, RawRule>();
        private readonly List<XdmNode> _Functions = new List<XdmNode>();
        private readonly List<XdmNode> _Keys = new List<XdmNode>();
        private readonly Dictionary<string, XdmNode> _Diagnostics = new Dictionary<string, XdmNode>();
        private int _IncludeDepth;
        private int _AnonymousPatternCount;
        private IResourceResolver _Resolver;


        public static CompiledSchematron Compile(string schemaUri, string phase, IResourceResolver resolver)
        {
            XdmDocument document;
            using (Stream stream = Resources.Open(resolver, schemaUri))
            {
                document = LoadSchemaDocument(stream, schemaUri);
            }
            return Compile(document, schemaUri, phase, resolver);
        }


        public static CompiledSchematron Compile(XdmDocument document, string schemaUri, string phase, IResourceResolver resolver)
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
            return compiler._Compile(schemaUri, phase);
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
            string href = _Attribute(element, "href");
            if (String.IsNullOrEmpty(href))
            {
                throw new SchematronException("sch:include without href in " + element.Document.DocumentUri);
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
                throw new SchematronException("Cannot load the included Schematron file " + uri + ": " + e.Message, null, e);
            }
            XdmNode target = included.Root.Children.FirstOrDefault(n => n.Kind == XdmNodeKind.Element);
            if (fragment != null)
            {
                target = included.Root.Descendants().FirstOrDefault(n => n.Kind == XdmNodeKind.Element && (n.GetAttribute("id") == fragment || n.GetAttribute("id", SchematronNamespaces.Xml) == fragment));
            }
            if (target == null)
            {
                throw new SchematronException("The included Schematron file " + uri + " has no element " + (fragment ?? ""));
            }
            foreach (XdmNode expanded in _Expand(target))
            {
                yield return expanded;
            }
            _IncludeDepth--;
        }


        private void _ReadSchema(XdmNode schema)
        {
            _SchemaVersion = _Attribute(schema, "schemaVersion");
            _DefaultPhase = _Attribute(schema, "defaultPhase");
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
                    _AddAbstractRule(_ReadRule(rule));
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
            // sch:p, sch:properties, foreign elements: ignored
        }


        private void _AddAbstractRule(RawRule rule)
        {
            if (rule.IsAbstract && rule.Id != null && !_AbstractRules.ContainsKey(rule.Id))
            {
                _AbstractRules[rule.Id] = rule;
            }
        }


        private static RawLet _ReadLet(XdmNode node)
        {
            string value = _Attribute(node, "value");
            if (value == null)
            {
                throw new SchematronException("sch:let \"" + _Attribute(node, "name") + "\" without value attribute is not supported");
            }
            return new RawLet { Node = node, Name = _Attribute(node, "name"), Value = value, As = _Attribute(node, "as") };
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
                IsA = _Attribute(node, "is-a")
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
                    if (rule.IsAbstract)
                    {
                        _AddAbstractRule(rule);
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
                Subject = _Attribute(node, "subject")
            };
            string ruleFlag = _Attribute(node, "flag");
            string ruleRole = _Attribute(node, "role");
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
                    if (ruleId == null)
                    {
                        throw new SchematronException("sch:extends with href is not supported");
                    }
                    rule.Items.Add(new RawExtends { Node = child, RuleId = ruleId });
                }
            }
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
                Diagnostics = _Attribute(node, "diagnostics")
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
        /// Replaces the parameters of an abstract pattern instance (as SchXslt does: longest names first, plain
        /// text replacement of "$name").
        /// </summary>
        private static string _ReplaceParams(string source, List<KeyValuePair<string, string>> parameters)
        {
            if (source == null || parameters.Count == 0)
            {
                return source;
            }
            foreach (KeyValuePair<string, string> parameter in parameters.OrderByDescending(p => p.Key.Length))
            {
                source = source.Replace("$" + parameter.Key, parameter.Value);
            }
            return source;
        }


        private static RawRule _Instantiate(RawRule rule, List<KeyValuePair<string, string>> parameters)
        {
            RawRule copy = new RawRule
            {
                Node = rule.Node,
                Id = rule.Id,
                Context = _ReplaceParams(rule.Context, parameters),
                IsAbstract = rule.IsAbstract,
                Subject = rule.Subject
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
                            Subject = check.Subject,
                            Diagnostics = check.Diagnostics
                        };
                        foreach (RawMessagePart part in check.Message)
                        {
                            c.Message.Add(new RawMessagePart
                            {
                                Text = part.Text,
                                ValueOfSelect = _ReplaceParams(part.ValueOfSelect, parameters),
                                IsName = part.IsName,
                                NamePath = _ReplaceParams(part.NamePath, parameters),
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


        private List<RawRuleItem> _ExpandExtends(List<RawRuleItem> items, int depth)
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
                    if (!_AbstractRules.TryGetValue(extends.RuleId, out abstractRule))
                    {
                        throw new SchematronException("The schema defines no abstract rule named '" + extends.RuleId + "'");
                    }
                    result.AddRange(_ExpandExtends(abstractRule.Items, depth + 1));
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
                    Params = pattern.Params
                };
                foreach (RawLet let in abstractPattern.Lets)
                {
                    instance.Lets.Add(new RawLet { Node = let.Node, Name = let.Name, Value = _ReplaceParams(let.Value, pattern.Params), As = let.As });
                }
                instance.Lets.AddRange(pattern.Lets);
                foreach (RawRule rule in abstractPattern.Rules)
                {
                    instance.Rules.Add(_Instantiate(rule, pattern.Params));
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


        private CompiledSchematron _Compile(string schemaUri, string phase)
        {
            CompiledSchematron result = new CompiledSchematron
            {
                Title = _Title,
                SchemaVersion = _SchemaVersion,
                BaseUri = schemaUri,
                Namespaces = _Namespaces.ToList(),
                Documents = new DocumentCache(_Resolver)
            };

            XPathFunctionLibrary functions = new XPathFunctionLibrary();
            XPathStaticContext schemaContext = new XPathStaticContext(_NamespacesOf(_Root), schemaUri, functions);
            Func<XdmNode, XPathStaticContext> contextFor = node => node == null ? schemaContext : new XPathStaticContext(_NamespacesOf(node), schemaUri, functions);

            try
            {
                functions.DeclareXslFunctions(_Functions, node => new XPathStaticContext(_NamespacesOf(node), schemaUri, functions));
            }
            catch (XPathException e)
            {
                throw new SchematronException(e.Message, e.Code, e);
            }
            foreach (XdmNode key in _Keys)
            {
                _CompileKey(key, contextFor(key), result);
            }

            // active patterns
            string activePhase = phase ?? _DefaultPhase ?? "#ALL";
            if (activePhase == "#DEFAULT")
            {
                activePhase = _DefaultPhase ?? "#ALL";
            }
            List<RawPattern> patterns = _ConcretePatterns();
            RawPhase selectedPhase = null;
            if (activePhase != "#ALL")
            {
                selectedPhase = _Phases.FirstOrDefault(p => p.Id == activePhase);
                if (selectedPhase == null)
                {
                    throw new SchematronException("The schema has no phase '" + activePhase + "'");
                }
            }

            // global variables: schema lets, phase lets and the lets of all patterns
            foreach (RawLet let in _Lets)
            {
                result.GlobalLets.Add(_CompileLet(let, contextFor(let.Node)));
            }
            if (selectedPhase != null)
            {
                foreach (RawLet let in selectedPhase.Lets)
                {
                    result.GlobalLets.Add(_CompileLet(let, contextFor(let.Node)));
                }
            }
            foreach (RawPattern pattern in patterns)
            {
                foreach (RawLet let in pattern.Lets)
                {
                    result.GlobalLets.Add(_CompileLet(let, contextFor(let.Node)));
                }
            }

            foreach (string diagnosticId in _Diagnostics.Keys)
            {
                XdmNode node = _Diagnostics[diagnosticId];
                List<RawMessagePart> parts = new List<RawMessagePart>();
                _ReadMessage(node, parts);
                result.Diagnostics[diagnosticId] = new CompiledDiagnostic { Id = diagnosticId, Message = _CompileMessage(parts, contextFor(node)) };
            }

            foreach (RawPattern pattern in patterns)
            {
                if (selectedPhase != null && !selectedPhase.ActivePatterns.Contains(pattern.Id))
                {
                    continue;
                }
                CompiledPattern compiled = new CompiledPattern
                {
                    Id = pattern.Id ?? ("pattern-" + (++_AnonymousPatternCount)),
                    Name = pattern.Name ?? pattern.Id
                };
                foreach (RawRule rule in pattern.Rules)
                {
                    if (rule.IsAbstract)
                    {
                        continue;
                    }
                    compiled.Rules.Add(_CompileRule(rule, contextFor));
                }
                result.Patterns.Add(compiled);
            }
            return result;
        }


        private static XPathException _Wrap(XPathException e, string what)
        {
            return new XPathException(e.Code, e.Message + " (in " + what + ")", e);
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


        private static XPathExpression _CompilePattern(string text, XPathStaticContext context, string what)
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
                Value = _CompileExpression(let.Value, context, "sch:let $" + let.Name)
            };
            if (!String.IsNullOrEmpty(let.As))
            {
                compiled.As = XPathSequenceType.Parse(let.As, context);
            }
            return compiled;
        }


        private static string _ExpandVariableName(string name, XPathStaticContext context)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw new SchematronException("sch:let without name");
            }
            return context.ExpandName(name);
        }


        private CompiledRule _CompileRule(RawRule rule, Func<XdmNode, XPathStaticContext> contextFor)
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
                Context = _CompilePattern(rule.Context, context, "rule context \"" + rule.Context + "\""),
                Subject = rule.Subject != null ? _CompileExpression(rule.Subject, context, "rule subject") : null
            };
            foreach (RawRuleItem item in _ExpandExtends(rule.Items, 0))
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
                DiagnosticIds = String.IsNullOrWhiteSpace(check.Diagnostics) ? new string[0] : check.Diagnostics.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            };
            return compiled;
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
            result.Keys[expanded] = new XslKey(expanded, _CompilePattern(match, context, "xsl:key " + name), _CompileExpression(use, context, "xsl:key " + name));
        }
    }
}
