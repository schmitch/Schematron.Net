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
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// The static context of expressions: namespaces, base URI and the available functions.
    /// </summary>
    public sealed class XPathStaticContext
    {
        internal readonly Dictionary<string, string> Namespaces;


        /// <summary>
        /// Creates a static context. The prefixes xml, xs, xsi, fn, math and err are predeclared.
        /// </summary>
        /// <param name="namespaces">Namespace prefixes (prefix, URI); entries with an empty prefix are ignored</param>
        /// <param name="baseUri">The static base URI, for doc(), document() and resolve-uri(); null if unknown</param>
        /// <param name="functions">The functions declared by the host language (e.g. xsl:function); null for the built-in functions only</param>
        public XPathStaticContext(IEnumerable<KeyValuePair<string, string>> namespaces = null, string baseUri = null, XPathFunctionLibrary functions = null)
        {
            Namespaces = new Dictionary<string, string>
            {
                ["xml"] = XmlNamespaces.Xml,
                ["xs"] = XmlNamespaces.Xs,
                ["xsi"] = XmlNamespaces.Xsi,
                ["fn"] = XmlNamespaces.Fn,
                ["math"] = XmlNamespaces.Math,
                ["err"] = XmlNamespaces.Err
            };
            if (namespaces != null)
            {
                foreach (KeyValuePair<string, string> ns in namespaces)
                {
                    if (!String.IsNullOrEmpty(ns.Key))
                    {
                        Namespaces[ns.Key] = ns.Value;
                    }
                }
            }
            BaseUri = baseUri;
            Functions = functions ?? new XPathFunctionLibrary();
        }


        /// <summary>The default namespace of element names in paths, "" for none.</summary>
        public string DefaultElementNamespace { get; set; } = "";

        /// <summary>
        /// XPath 1.0 compatibility mode (XPath 2.0, 3.1.5, 3.4, 3.5.2), for expressions written for XPath 1.0: a
        /// function parameter that takes one item gets the first item of the argument (converted with fn:string() or
        /// fn:number() for string and numeric parameters), arithmetic converts its operands to xs:double, and general
        /// comparisons compare numbers and strings as XPath 1.0 does. Applies to the expressions compiled afterwards.
        /// </summary>
        public bool XPath1CompatibilityMode { get; set; }

        /// <summary>The static base URI, null if unknown.</summary>
        public string BaseUri { get; }

        /// <summary>The user-defined functions.</summary>
        public XPathFunctionLibrary Functions { get; }


        /// <summary>
        /// Expands a lexical QName (e.g. a variable or function name) to the notation of expanded names used by
        /// <see cref="IXPathVariableResolver"/>: the local name, or {namespace-uri}local-name for a prefixed name.
        /// </summary>
        /// <exception cref="XPathException">XPST0081: the prefix is not declared</exception>
        public string ExpandName(string lexicalName)
        {
            if (String.IsNullOrEmpty(lexicalName))
            {
                throw new XPathException("XPST0003", "Missing name");
            }
            int colon = lexicalName.IndexOf(':');
            if (colon < 0)
            {
                return lexicalName;
            }
            return "{" + ResolvePrefix(lexicalName.Substring(0, colon)) + "}" + lexicalName.Substring(colon + 1);
        }


        /// <summary>
        /// Returns the namespace URI bound to a prefix.
        /// </summary>
        /// <exception cref="XPathException">XPST0081: the prefix is not declared</exception>
        public string ResolvePrefix(string prefix)
        {
            string uri;
            if (Namespaces.TryGetValue(prefix, out uri))
            {
                return uri;
            }
            throw new XPathException("XPST0081", "Namespace prefix '" + prefix + "' has not been declared");
        }
    }


    /// <summary>
    /// Compile-time scope of the variables bound inside expressions; assigns every binding a slot in
    /// <see cref="EvalContext.Locals"/>.
    /// </summary>
    internal sealed class CompileScope
    {
        private readonly List<KeyValuePair<string, int>> _Bindings = new List<KeyValuePair<string, int>>();

        public int SlotCount { get; private set; }

        /// <summary>The expanded names of the variables referenced but not bound inside the expression.</summary>
        public readonly HashSet<string> ExternalReferences = new HashSet<string>(StringComparer.Ordinal);


        public int Declare(string expandedName)
        {
            int slot = SlotCount++;
            _Bindings.Add(new KeyValuePair<string, int>(expandedName, slot));
            return slot;
        }


        public void Undeclare(int count)
        {
            _Bindings.RemoveRange(_Bindings.Count - count, count);
        }


        public bool TryResolve(string expandedName, out int slot)
        {
            for (int i = _Bindings.Count - 1; i >= 0; i--)
            {
                if (_Bindings[i].Key == expandedName)
                {
                    slot = _Bindings[i].Value;
                    return true;
                }
            }
            slot = -1;
            return false;
        }
    }


    /// <summary>
    /// A compiled XPath expression. Instances are immutable and thread-safe.
    /// </summary>
    public sealed class XPathExpression
    {
        internal readonly Expr Root;
        internal readonly int SlotCount;


        internal XPathExpression(string text, Expr root, int slotCount, IEnumerable<string> variableReferences = null)
        {
            Text = text;
            Root = root;
            SlotCount = slotCount;
            VariableReferences = variableReferences == null ? new string[0] : variableReferences.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }


        /// <summary>The source of the expression.</summary>
        public string Text { get; }


        /// <summary>
        /// The variables the expression references but does not bind itself (expanded names as passed to
        /// <see cref="IXPathVariableResolver.TryResolve"/>): a host language can check that it declares them.
        /// </summary>
        public IReadOnlyList<string> VariableReferences { get; }


        /// <summary>
        /// Compiles an XPath expression.
        /// </summary>
        /// <param name="text">The expression</param>
        /// <param name="staticContext">Namespaces, base URI and functions; null for the default static context</param>
        /// <exception cref="XPathException">A static error, e.g. XPST0003 (syntax), XPST0081 (undeclared prefix) or XPST0017 (unknown function)</exception>
        public static XPathExpression Compile(string text, XPathStaticContext staticContext = null)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            CompileScope scope = new CompileScope();
            Expr root = XPathParser.Parse(text, staticContext ?? new XPathStaticContext(), scope);
            return new XPathExpression(text, root, scope.SlotCount, scope.ExternalReferences);
        }


        /// <summary>
        /// Compiles an XSLT pattern (e.g. the context of a Schematron rule or the match of xsl:key) into an expression
        /// that, evaluated with a node as context item, selects all nodes of that node's tree that match the pattern.
        /// </summary>
        /// <exception cref="XPathException">The pattern is invalid</exception>
        public static XPathExpression CompilePattern(string pattern, XPathStaticContext staticContext = null)
        {
            if (pattern == null)
            {
                throw new ArgumentNullException(nameof(pattern));
            }
            CompileScope scope = new CompileScope();
            Expr root = XPathParser.ParsePattern(pattern, staticContext ?? new XPathStaticContext(), scope);
            return new XPathExpression(pattern, root, scope.SlotCount, scope.ExternalReferences);
        }


        /// <summary>
        /// Evaluates the expression. The context item is the focus and the result of current().
        /// </summary>
        /// <param name="contextItem">The context item, e.g. <see cref="XdmDocument.Root"/>; null if absent</param>
        /// <param name="dynamicContext">Documents, keys, global variables; null for an empty dynamic context</param>
        /// <param name="variables">The variables; null for <see cref="XPathDynamicContext.GlobalVariables"/></param>
        /// <exception cref="XPathException">A dynamic error</exception>
        public XdmValue Evaluate(XdmItem contextItem, XPathDynamicContext dynamicContext = null, IXPathVariableResolver variables = null)
        {
            return Evaluate(_CreateContext(dynamicContext, variables), contextItem);
        }


        /// <summary>
        /// Evaluates the expression and returns its effective boolean value (fn:boolean()).
        /// </summary>
        /// <exception cref="XPathException">A dynamic error, e.g. FORG0006 if the result has no effective boolean value</exception>
        public bool EvaluateBoolean(XdmItem contextItem, XPathDynamicContext dynamicContext = null, IXPathVariableResolver variables = null)
        {
            return EvaluateBoolean(_CreateContext(dynamicContext, variables), contextItem);
        }


        private static EvalContext _CreateContext(XPathDynamicContext dynamicContext, IXPathVariableResolver variables)
        {
            dynamicContext = dynamicContext ?? new XPathDynamicContext();
            return new EvalContext(dynamicContext) { Variables = variables ?? dynamicContext.GlobalVariables };
        }


        private void _Prepare(EvalContext context, XdmItem contextItem)
        {
            context.ContextItem = contextItem;
            context.Position = contextItem == null ? 0 : 1;
            context.Size = contextItem == null ? 0 : 1;
            context.Locals = SlotCount > 0 ? new XdmValue[SlotCount] : null;
            context.CurrentItem = contextItem;
        }


        /// <summary>
        /// Evaluates the expression with the given context item (the focus and current() are set to it).
        /// </summary>
        internal XdmValue Evaluate(EvalContext context, XdmItem contextItem)
        {
            _Prepare(context, contextItem);
            return Root.Evaluate(context);
        }


        internal bool EvaluateBoolean(EvalContext context, XdmItem contextItem)
        {
            _Prepare(context, contextItem);
            return Root.EffectiveBooleanValue(context);
        }


        /// <inheritdoc />
        public override string ToString()
        {
            return Text;
        }
    }


    /// <summary>
    /// Recursive descent parser for XPath 2.0 (plus the XPath 3.0 operators ||, !, =&gt; and let).
    /// </summary>
    internal sealed class XPathParser
    {
        private static readonly HashSet<string> _KindTests = new HashSet<string>
        {
            "node", "text", "comment", "processing-instruction", "element", "attribute", "document-node",
            "schema-element", "schema-attribute", "namespace-node"
        };

        private static readonly HashSet<string> _ReservedFunctionNames = new HashSet<string>
        {
            "attribute", "comment", "document-node", "element", "empty-sequence", "function", "if", "item", "map", "array",
            "namespace-node", "node", "processing-instruction", "schema-attribute", "schema-element", "switch", "text", "typeswitch"
        };

        private readonly List<Token> _Tokens;
        private readonly string _Expression;
        private readonly XPathStaticContext _Static;
        private readonly CompileScope _Scope;
        private int _Position;


        private XPathParser(string expression, XPathStaticContext staticContext, CompileScope scope)
        {
            _Expression = expression;
            _Tokens = Lexer.Tokenize(expression);
            _Static = staticContext;
            _Scope = scope;
        }


        public static Expr Parse(string expression, XPathStaticContext staticContext, CompileScope scope)
        {
            XPathParser parser = new XPathParser(expression, staticContext, scope);
            Expr result = parser._ParseExpr();
            if (parser._Peek().Kind != TokenKind.End)
            {
                throw parser._Error("Unexpected token " + parser._Peek());
            }
            return result;
        }


        /// <summary>
        /// Parses an XSLT match pattern (e.g. a Schematron rule context) into an expression that selects all
        /// matching nodes when evaluated with the document node as context item: relative path patterns are
        /// evaluated as "//pattern".
        /// </summary>
        public static Expr ParsePattern(string pattern, XPathStaticContext staticContext, CompileScope scope)
        {
            XPathParser parser = new XPathParser(pattern, staticContext, scope);
            Expr result = parser._ParsePatternBranch();
            while (parser._IsSymbol("|") || parser._IsName("union"))
            {
                parser._Next();
                result = new SetExpr(SetOperator.Union, result, parser._ParsePatternBranch());
            }
            if (parser._Peek().Kind != TokenKind.End)
            {
                throw parser._Error("Unexpected token " + parser._Peek() + " in pattern");
            }
            return _MakeErrorTolerant(result);
        }


        /// <summary>
        /// Wraps the predicates of the pattern steps: a dynamic error while evaluating a pattern against a node is
        /// treated as "the node does not match" (XSLT 3.0, 5.5.4).
        /// </summary>
        private static Expr _MakeErrorTolerant(Expr expr)
        {
            switch (expr)
            {
                case SetExpr set:
                    return new SetExpr(set.Operator, _MakeErrorTolerant(set.Left), _MakeErrorTolerant(set.Right));
                case PathExpr path:
                    return new PathExpr(_MakeErrorTolerant(path.Start), path.Steps.Select(_MakeErrorTolerant).ToArray());
                case AxisStepExpr step:
                    return new AxisStepExpr(step.Axis, step.Test, step.Predicates.Select(p => (Expr)new PatternPredicateExpr(p)).ToArray());
                case FilterExpr filter:
                    return new FilterExpr(_MakeErrorTolerant(filter.Primary), filter.Predicates.Select(p => (Expr)new PatternPredicateExpr(p)).ToArray());
                default:
                    return expr;
            }
        }


        private Expr _ParsePatternBranch()
        {
            Token token = _Peek();
            if (token.IsSymbol("/") || token.IsSymbol("//"))
            {
                return _ParsePath();
            }
            if (token.Kind == TokenKind.Name && _IsSymbol("(", 1) && token.Prefix == null && token.Uri == null
                && (token.Value == "id" || token.Value == "key" || token.Value == "doc" || token.Value == "element-with-id" || token.Value == "root"))
            {
                return _ParsePath();
            }
            if (token.IsSymbol("(") || token.IsSymbol("$") || token.IsSymbol("."))
            {
                // XSLT 3.0 patterns such as (/a | /b)[$x] or .[predicate]: the node matches if it is selected by
                // root(.)//(pattern); rooted expressions select the same nodes from every context node.
                Expr expr = _ParsePath();
                if (_IsRooted(expr))
                {
                    return expr;
                }
                return new PathExpr(RootExpr.Instance, new Expr[] { new AxisStepExpr(Axis.DescendantOrSelf, KindTest.AnyNode, new Expr[0]), expr });
            }
            return _ParseRelativePath(RootExpr.Instance, true);
        }


        private static bool _IsRooted(Expr expr)
        {
            switch (expr)
            {
                case RootExpr _:
                    return true;
                case PathExpr path:
                    return _IsRooted(path.Start);
                case SetExpr set:
                    return _IsRooted(set.Left) && _IsRooted(set.Right);
                case FilterExpr filter:
                    return _IsRooted(filter.Primary);
                default:
                    return false;
            }
        }


        // ------------------------------------------------------------------------------------------------------------
        // token helpers
        // ------------------------------------------------------------------------------------------------------------

        private Token _Peek(int offset = 0)
        {
            int index = Math.Min(_Position + offset, _Tokens.Count - 1);
            return _Tokens[index];
        }


        private Token _Next()
        {
            Token token = _Tokens[_Position];
            if (_Position < _Tokens.Count - 1)
            {
                _Position++;
            }
            return token;
        }


        private bool _IsSymbol(string symbol, int offset = 0)
        {
            return _Peek(offset).IsSymbol(symbol);
        }


        private bool _IsName(string name, int offset = 0)
        {
            return _Peek(offset).IsName(name);
        }


        private void _ExpectSymbol(string symbol)
        {
            if (!_IsSymbol(symbol))
            {
                throw _Error("Expected '" + symbol + "' but found " + _Peek());
            }
            _Next();
        }


        private void _ExpectName(string name)
        {
            if (!_IsName(name))
            {
                throw _Error("Expected '" + name + "' but found " + _Peek());
            }
            _Next();
        }


        private XPathException _Error(string message)
        {
            return new XPathException("XPST0003", message + " at position " + _Peek().Position + " in expression \"" + _Expression + "\"");
        }


        // ------------------------------------------------------------------------------------------------------------
        // expressions
        // ------------------------------------------------------------------------------------------------------------

        private Expr _ParseExpr()
        {
            Expr first = _ParseExprSingle();
            if (!_IsSymbol(","))
            {
                return first;
            }
            List<Expr> items = new List<Expr> { first };
            while (_IsSymbol(","))
            {
                _Next();
                items.Add(_ParseExprSingle());
            }
            return new SequenceExpr(items.ToArray());
        }


        private Expr _ParseExprSingle()
        {
            Token token = _Peek();
            if (token.Kind == TokenKind.Name && _IsSymbol("$", 1))
            {
                if (token.IsName("for"))
                {
                    return _ParseFor();
                }
                if (token.IsName("some") || token.IsName("every"))
                {
                    return _ParseQuantified();
                }
                if (token.IsName("let"))
                {
                    return _ParseLet();
                }
            }
            if (token.IsName("if") && _IsSymbol("(", 1))
            {
                return _ParseIf();
            }
            return _ParseOr();
        }


        private string _ParseVariableName()
        {
            _ExpectSymbol("$");
            Token name = _Next();
            if (name.Kind != TokenKind.Name || name.Value == "*" || name.Prefix == "*")
            {
                throw _Error("Expected a variable name");
            }
            return _ExpandedName(name, "");
        }


        private string _ExpandedName(Token name, string defaultNamespace)
        {
            string ns = name.Uri ?? (name.Prefix != null ? _Static.ResolvePrefix(name.Prefix) : defaultNamespace);
            return String.IsNullOrEmpty(ns) ? name.Value : "{" + ns + "}" + name.Value;
        }


        private Expr _ParseFor()
        {
            _Next();
            List<KeyValuePair<int, Expr>> bindings = new List<KeyValuePair<int, Expr>>();
            while (true)
            {
                string name = _ParseVariableName();
                _ExpectName("in");
                Expr input = _ParseExprSingle();
                bindings.Add(new KeyValuePair<int, Expr>(_Scope.Declare(name), input));
                if (_IsSymbol(","))
                {
                    _Next();
                    continue;
                }
                break;
            }
            _ExpectName("return");
            Expr result = _ParseExprSingle();
            _Scope.Undeclare(bindings.Count);
            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                result = new ForExpr(bindings[i].Key, bindings[i].Value, result);
            }
            return result;
        }


        private Expr _ParseLet()
        {
            _Next();
            List<KeyValuePair<int, Expr>> bindings = new List<KeyValuePair<int, Expr>>();
            while (true)
            {
                string name = _ParseVariableName();
                _ExpectSymbol(":=");
                Expr value = _ParseExprSingle();
                bindings.Add(new KeyValuePair<int, Expr>(_Scope.Declare(name), value));
                if (_IsSymbol(","))
                {
                    _Next();
                    if (_IsName("let"))
                    {
                        _Next();
                    }
                    continue;
                }
                if (_IsName("let") && _IsSymbol("$", 1))
                {
                    _Next();
                    continue;
                }
                break;
            }
            _ExpectName("return");
            Expr result = _ParseExprSingle();
            _Scope.Undeclare(bindings.Count);
            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                result = new LetExpr(bindings[i].Key, bindings[i].Value, result);
            }
            return result;
        }


        private Expr _ParseQuantified()
        {
            bool every = _Next().Value == "every";
            List<KeyValuePair<int, Expr>> bindings = new List<KeyValuePair<int, Expr>>();
            while (true)
            {
                string name = _ParseVariableName();
                _ExpectName("in");
                Expr input = _ParseExprSingle();
                bindings.Add(new KeyValuePair<int, Expr>(_Scope.Declare(name), input));
                if (_IsSymbol(","))
                {
                    _Next();
                    continue;
                }
                break;
            }
            _ExpectName("satisfies");
            Expr result = _ParseExprSingle();
            _Scope.Undeclare(bindings.Count);
            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                result = new QuantifiedExpr(every, bindings[i].Key, bindings[i].Value, result);
            }
            return result;
        }


        private Expr _ParseIf()
        {
            _Next();
            _ExpectSymbol("(");
            Expr condition = _ParseExpr();
            _ExpectSymbol(")");
            _ExpectName("then");
            Expr then = _ParseExprSingle();
            _ExpectName("else");
            Expr otherwise = _ParseExprSingle();
            return new IfExpr(condition, then, otherwise);
        }


        private Expr _ParseOr()
        {
            Expr left = _ParseAnd();
            while (_IsName("or"))
            {
                _Next();
                left = new LogicalExpr(false, left, _ParseAnd());
            }
            return left;
        }


        private Expr _ParseAnd()
        {
            Expr left = _ParseComparison();
            while (_IsName("and"))
            {
                _Next();
                left = new LogicalExpr(true, left, _ParseComparison());
            }
            return left;
        }


        private Expr _ParseComparison()
        {
            Expr left = _ParseStringConcat();
            Token token = _Peek();
            if (token.Kind == TokenKind.Symbol)
            {
                switch (token.Value)
                {
                    case "=":
                        _Next();
                        return new GeneralComparisonExpr(ComparisonOperator.Equal, left, _ParseStringConcat(), _Static.XPath1CompatibilityMode);
                    case "!=":
                        _Next();
                        return new GeneralComparisonExpr(ComparisonOperator.NotEqual, left, _ParseStringConcat(), _Static.XPath1CompatibilityMode);
                    case "<":
                        _Next();
                        return new GeneralComparisonExpr(ComparisonOperator.LessThan, left, _ParseStringConcat(), _Static.XPath1CompatibilityMode);
                    case "<=":
                        _Next();
                        return new GeneralComparisonExpr(ComparisonOperator.LessOrEqual, left, _ParseStringConcat(), _Static.XPath1CompatibilityMode);
                    case ">":
                        _Next();
                        return new GeneralComparisonExpr(ComparisonOperator.GreaterThan, left, _ParseStringConcat(), _Static.XPath1CompatibilityMode);
                    case ">=":
                        _Next();
                        return new GeneralComparisonExpr(ComparisonOperator.GreaterOrEqual, left, _ParseStringConcat(), _Static.XPath1CompatibilityMode);
                    case "<<":
                    case ">>":
                        _Next();
                        return new NodeComparisonExpr(token.Value, left, _ParseStringConcat());
                }
            }
            else if (token.Kind == TokenKind.Name && token.Prefix == null && token.Uri == null)
            {
                switch (token.Value)
                {
                    case "eq":
                        _Next();
                        return new ValueComparisonExpr(ComparisonOperator.Equal, left, _ParseStringConcat());
                    case "ne":
                        _Next();
                        return new ValueComparisonExpr(ComparisonOperator.NotEqual, left, _ParseStringConcat());
                    case "lt":
                        _Next();
                        return new ValueComparisonExpr(ComparisonOperator.LessThan, left, _ParseStringConcat());
                    case "le":
                        _Next();
                        return new ValueComparisonExpr(ComparisonOperator.LessOrEqual, left, _ParseStringConcat());
                    case "gt":
                        _Next();
                        return new ValueComparisonExpr(ComparisonOperator.GreaterThan, left, _ParseStringConcat());
                    case "ge":
                        _Next();
                        return new ValueComparisonExpr(ComparisonOperator.GreaterOrEqual, left, _ParseStringConcat());
                    case "is":
                        _Next();
                        return new NodeComparisonExpr("is", left, _ParseStringConcat());
                }
            }
            return left;
        }


        private Expr _ParseStringConcat()
        {
            Expr first = _ParseRange();
            if (!_IsSymbol("||"))
            {
                return first;
            }
            List<Expr> operands = new List<Expr> { first };
            while (_IsSymbol("||"))
            {
                _Next();
                operands.Add(_ParseRange());
            }
            return new StringConcatExpr(operands.ToArray());
        }


        private Expr _ParseRange()
        {
            Expr left = _ParseAdditive();
            if (_IsName("to"))
            {
                _Next();
                return new RangeExpr(left, _ParseAdditive());
            }
            return left;
        }


        private Expr _ParseAdditive()
        {
            Expr left = _ParseMultiplicative();
            while (true)
            {
                if (_IsSymbol("+"))
                {
                    _Next();
                    left = new ArithmeticExpr(ArithmeticOperator.Add, left, _ParseMultiplicative(), _Static.XPath1CompatibilityMode);
                }
                else if (_IsSymbol("-"))
                {
                    _Next();
                    left = new ArithmeticExpr(ArithmeticOperator.Subtract, left, _ParseMultiplicative(), _Static.XPath1CompatibilityMode);
                }
                else
                {
                    return left;
                }
            }
        }


        private Expr _ParseMultiplicative()
        {
            Expr left = _ParseUnion();
            while (true)
            {
                ArithmeticOperator op;
                if (_IsSymbol("*"))
                {
                    op = ArithmeticOperator.Multiply;
                }
                else if (_IsName("div"))
                {
                    op = ArithmeticOperator.Divide;
                }
                else if (_IsName("idiv"))
                {
                    op = ArithmeticOperator.IntegerDivide;
                }
                else if (_IsName("mod"))
                {
                    op = ArithmeticOperator.Modulo;
                }
                else
                {
                    return left;
                }
                _Next();
                left = new ArithmeticExpr(op, left, _ParseUnion(), _Static.XPath1CompatibilityMode);
            }
        }


        private Expr _ParseUnion()
        {
            Expr left = _ParseIntersectExcept();
            while (_IsSymbol("|") || _IsName("union"))
            {
                _Next();
                left = new SetExpr(SetOperator.Union, left, _ParseIntersectExcept());
            }
            return left;
        }


        private Expr _ParseIntersectExcept()
        {
            Expr left = _ParseInstanceOf();
            while (true)
            {
                if (_IsName("intersect"))
                {
                    _Next();
                    left = new SetExpr(SetOperator.Intersect, left, _ParseInstanceOf());
                }
                else if (_IsName("except"))
                {
                    _Next();
                    left = new SetExpr(SetOperator.Except, left, _ParseInstanceOf());
                }
                else
                {
                    return left;
                }
            }
        }


        private Expr _ParseInstanceOf()
        {
            Expr left = _ParseTreat();
            if (_IsName("instance") && _IsName("of", 1))
            {
                _Next();
                _Next();
                return new InstanceOfExpr(left, _ParseSequenceType());
            }
            return left;
        }


        private Expr _ParseTreat()
        {
            Expr left = _ParseCastable();
            if (_IsName("treat") && _IsName("as", 1))
            {
                _Next();
                _Next();
                return new TreatExpr(left, _ParseSequenceType());
            }
            return left;
        }


        private Expr _ParseCastable()
        {
            Expr left = _ParseCast();
            if (_IsName("castable") && _IsName("as", 1))
            {
                _Next();
                _Next();
                bool allowEmpty;
                XsType type = _ParseSingleType(out allowEmpty);
                return new CastExpr(left, type, allowEmpty, true);
            }
            return left;
        }


        private Expr _ParseCast()
        {
            Expr left = _ParseArrow();
            if (_IsName("cast") && _IsName("as", 1))
            {
                _Next();
                _Next();
                bool allowEmpty;
                XsType type = _ParseSingleType(out allowEmpty);
                return new CastExpr(left, type, allowEmpty, false);
            }
            return left;
        }


        private Expr _ParseArrow()
        {
            Expr left = _ParseUnary();
            while (_IsSymbol("=>"))
            {
                _Next();
                Token name = _Next();
                if (name.Kind != TokenKind.Name)
                {
                    throw _Error("Expected a function name after '=>'");
                }
                List<Expr> arguments = new List<Expr> { left };
                arguments.AddRange(_ParseArgumentList());
                left = _BuildFunctionCall(name, arguments);
            }
            return left;
        }


        private Expr _ParseUnary()
        {
            int minus = 0;
            bool hasSign = false;
            while (_IsSymbol("-") || _IsSymbol("+"))
            {
                hasSign = true;
                if (_Next().Value == "-")
                {
                    minus++;
                }
            }
            Expr operand = _ParseSimpleMap();
            if (!hasSign)
            {
                return operand;
            }
            return minus % 2 == 1 ? new UnaryMinusExpr(operand, _Static.XPath1CompatibilityMode) : (Expr)new UnaryPlusExpr(operand, _Static.XPath1CompatibilityMode);
        }


        private Expr _ParseSimpleMap()
        {
            Expr left = _ParsePath();
            while (_IsSymbol("!"))
            {
                _Next();
                left = new SimpleMapExpr(left, _ParsePath());
            }
            return left;
        }


        // ------------------------------------------------------------------------------------------------------------
        // path expressions
        // ------------------------------------------------------------------------------------------------------------

        private bool _CanStartStep(Token token)
        {
            switch (token.Kind)
            {
                case TokenKind.Name:
                case TokenKind.String:
                case TokenKind.Integer:
                case TokenKind.Decimal:
                case TokenKind.Double:
                    return true;
                case TokenKind.Symbol:
                    return token.Value == "*" || token.Value == "@" || token.Value == "." || token.Value == ".." || token.Value == "$" || token.Value == "(";
                default:
                    return false;
            }
        }


        private Expr _ParsePath()
        {
            if (_IsSymbol("/"))
            {
                _Next();
                if (!_CanStartStep(_Peek()))
                {
                    return RootExpr.Instance;
                }
                return _ParseRelativePath(RootExpr.Instance, false);
            }
            if (_IsSymbol("//"))
            {
                _Next();
                return _ParseRelativePath(RootExpr.Instance, true);
            }
            return _ParseRelativePath(null, false);
        }


        private static AxisStepExpr _DescendantOrSelfStep()
        {
            return new AxisStepExpr(Axis.DescendantOrSelf, KindTest.AnyNode, new Expr[0]);
        }


        private Expr _ParseRelativePath(Expr start, bool leadingDoubleSlash)
        {
            List<Expr> steps = new List<Expr>();
            if (leadingDoubleSlash)
            {
                steps.Add(_DescendantOrSelfStep());
            }
            steps.Add(_ParseStep());
            while (_IsSymbol("/") || _IsSymbol("//"))
            {
                if (_Next().Value == "//")
                {
                    steps.Add(_DescendantOrSelfStep());
                }
                steps.Add(_ParseStep());
            }
            _OptimizeSteps(steps);
            if (start == null)
            {
                if (steps.Count == 1)
                {
                    return steps[0];
                }
                return new PathExpr(steps[0], steps.Skip(1).ToArray());
            }
            return new PathExpr(start, steps.ToArray());
        }


        /// <summary>
        /// Rewrites descendant-or-self::node()/child::x[filter] to descendant::x[filter] and
        /// descendant-or-self::node()/attribute::x to an indexed attribute lookup.
        /// </summary>
        private static void _OptimizeSteps(List<Expr> steps)
        {
            for (int i = 0; i < steps.Count - 1; i++)
            {
                AxisStepExpr first = steps[i] as AxisStepExpr;
                if (first == null || first.Axis != Axis.DescendantOrSelf || first.Predicates.Length != 0 || !(first.Test is KindTest kind) || kind.Kind.HasValue)
                {
                    continue;
                }
                AxisStepExpr second = steps[i + 1] as AxisStepExpr;
                if (second == null)
                {
                    continue;
                }
                if (second.Axis == Axis.Child && second.Predicates.All(p => p.IsNonPositionalPredicate))
                {
                    steps[i] = new AxisStepExpr(Axis.Descendant, second.Test, second.Predicates);
                    steps.RemoveAt(i + 1);
                }
                else if (second.Axis == Axis.Attribute && second.Predicates.Length == 0)
                {
                    steps[i] = new DescendantAttributeStepExpr(second.Test);
                    steps.RemoveAt(i + 1);
                }
            }
        }


        private Expr _ParseStep()
        {
            Token token = _Peek();
            if (token.IsSymbol(".."))
            {
                _Next();
                return new AxisStepExpr(Axis.Parent, KindTest.AnyNode, _ParsePredicates());
            }
            if (token.IsSymbol("@"))
            {
                _Next();
                NodeTest test = _ParseNodeTest(XdmNodeKind.Attribute);
                return new AxisStepExpr(Axis.Attribute, test, _ParsePredicates());
            }
            if (token.Kind == TokenKind.Name && _IsSymbol("::", 1) && token.Prefix == null && token.Uri == null)
            {
                Axis axis = _ParseAxis(token.Value);
                _Next();
                _Next();
                XdmNodeKind principal = axis == Axis.Attribute ? XdmNodeKind.Attribute : axis == Axis.Namespace ? XdmNodeKind.Namespace : XdmNodeKind.Element;
                NodeTest test = _ParseNodeTest(principal);
                return new AxisStepExpr(axis, test, _ParsePredicates());
            }
            if (token.Kind == TokenKind.Name && _IsSymbol("(", 1) && token.Prefix == null && token.Uri == null && _KindTests.Contains(token.Value))
            {
                KindTest test = _ParseKindTest();
                Axis axis = test.Kind == XdmNodeKind.Attribute ? Axis.Attribute : Axis.Child;
                return new AxisStepExpr(axis, test, _ParsePredicates());
            }
            if (token.Kind == TokenKind.Name && !_IsSymbol("(", 1))
            {
                NodeTest test = _ParseNodeTest(XdmNodeKind.Element);
                return new AxisStepExpr(Axis.Child, test, _ParsePredicates());
            }
            if (token.IsSymbol("*"))
            {
                NodeTest test = _ParseNodeTest(XdmNodeKind.Element);
                return new AxisStepExpr(Axis.Child, test, _ParsePredicates());
            }
            Expr primary = _ParsePrimary();
            Expr[] predicates = _ParsePredicates();
            return predicates.Length == 0 ? primary : new FilterExpr(primary, predicates);
        }


        private Axis _ParseAxis(string name)
        {
            switch (name)
            {
                case "child":
                    return Axis.Child;
                case "descendant":
                    return Axis.Descendant;
                case "attribute":
                    return Axis.Attribute;
                case "self":
                    return Axis.Self;
                case "descendant-or-self":
                    return Axis.DescendantOrSelf;
                case "following-sibling":
                    return Axis.FollowingSibling;
                case "following":
                    return Axis.Following;
                case "namespace":
                    return Axis.Namespace;
                case "parent":
                    return Axis.Parent;
                case "ancestor":
                    return Axis.Ancestor;
                case "preceding-sibling":
                    return Axis.PrecedingSibling;
                case "preceding":
                    return Axis.Preceding;
                case "ancestor-or-self":
                    return Axis.AncestorOrSelf;
                default:
                    throw _Error("Unknown axis '" + name + "'");
            }
        }


        private Expr[] _ParsePredicates()
        {
            if (!_IsSymbol("["))
            {
                return new Expr[0];
            }
            List<Expr> predicates = new List<Expr>();
            while (_IsSymbol("["))
            {
                _Next();
                predicates.Add(_ParseExpr());
                _ExpectSymbol("]");
            }
            return predicates.ToArray();
        }


        private NodeTest _ParseNodeTest(XdmNodeKind principal)
        {
            Token token = _Peek();
            if (token.Kind == TokenKind.Name && _IsSymbol("(", 1) && token.Prefix == null && token.Uri == null && _KindTests.Contains(token.Value))
            {
                return _ParseKindTest();
            }
            if (token.IsSymbol("*"))
            {
                _Next();
                return new NameTest(null, null, principal);
            }
            if (token.Kind != TokenKind.Name)
            {
                throw _Error("Expected a node test but found " + token);
            }
            _Next();
            return _NameTestFor(token, principal);
        }


        private NameTest _NameTestFor(Token token, XdmNodeKind principal)
        {
            string local = token.Value == "*" ? null : token.Value;
            string ns;
            if (token.Uri != null)
            {
                ns = token.Uri;
            }
            else if (token.Prefix == "*")
            {
                ns = null;
            }
            else if (token.Prefix != null)
            {
                ns = _Static.ResolvePrefix(token.Prefix);
            }
            else
            {
                ns = principal == XdmNodeKind.Element ? _Static.DefaultElementNamespace : "";
            }
            return new NameTest(ns, local, principal);
        }


        private KindTest _ParseKindTest()
        {
            string name = _Next().Value;
            _ExpectSymbol("(");
            KindTest result;
            switch (name)
            {
                case "node":
                    result = KindTest.AnyNode;
                    break;
                case "text":
                    result = new KindTest(XdmNodeKind.Text);
                    break;
                case "comment":
                    result = new KindTest(XdmNodeKind.Comment);
                    break;
                case "namespace-node":
                    result = new KindTest(XdmNodeKind.Namespace);
                    break;
                case "processing-instruction":
                    {
                        NameTest target = null;
                        Token t = _Peek();
                        if (t.Kind == TokenKind.Name || t.Kind == TokenKind.String)
                        {
                            _Next();
                            target = new NameTest(null, t.Value.Trim(), XdmNodeKind.ProcessingInstruction);
                        }
                        result = new KindTest(XdmNodeKind.ProcessingInstruction, target);
                        break;
                    }
                case "element":
                case "attribute":
                case "schema-element":
                case "schema-attribute":
                    {
                        XdmNodeKind kind = name.EndsWith("element", StringComparison.Ordinal) ? XdmNodeKind.Element : XdmNodeKind.Attribute;
                        NameTest nameTest = null;
                        if (_IsSymbol("*"))
                        {
                            _Next();
                        }
                        else if (_Peek().Kind == TokenKind.Name)
                        {
                            nameTest = _NameTestFor(_Next(), kind);
                        }
                        if (_IsSymbol(","))
                        {
                            // type annotation: untyped documents only carry xs:untyped / xs:untypedAtomic
                            _Next();
                            _Next();
                            if (_IsSymbol("?"))
                            {
                                _Next();
                            }
                        }
                        result = new KindTest(kind, nameTest);
                        break;
                    }
                case "document-node":
                    {
                        KindTest element = null;
                        if (_Peek().Kind == TokenKind.Name)
                        {
                            element = _ParseKindTest();
                        }
                        result = new KindTest(XdmNodeKind.Document, null, element);
                        break;
                    }
                default:
                    throw _Error("Unknown kind test " + name + "()");
            }
            _ExpectSymbol(")");
            return result;
        }


        // ------------------------------------------------------------------------------------------------------------
        // types
        // ------------------------------------------------------------------------------------------------------------

        private XPathSequenceType _ParseSequenceType()
        {
            Token token = _Peek();
            if (token.IsName("empty-sequence") && _IsSymbol("(", 1))
            {
                _Next();
                _Next();
                _ExpectSymbol(")");
                return XPathSequenceType.Empty;
            }
            ItemType itemType;
            if (token.IsName("item") && _IsSymbol("(", 1))
            {
                _Next();
                _Next();
                _ExpectSymbol(")");
                itemType = AnyItemType.Instance;
            }
            else if (token.Kind == TokenKind.Name && token.Prefix == null && token.Uri == null && _KindTests.Contains(token.Value) && _IsSymbol("(", 1))
            {
                itemType = new NodeItemType(_ParseKindTest());
            }
            else if (token.Kind == TokenKind.Name)
            {
                _Next();
                itemType = new AtomicItemType(_ResolveAtomicType(token));
            }
            else
            {
                throw _Error("Expected a sequence type but found " + token);
            }
            Occurrence occurrence = Occurrence.ExactlyOne;
            if (_IsSymbol("?"))
            {
                _Next();
                occurrence = Occurrence.ZeroOrOne;
            }
            else if (_IsSymbol("*"))
            {
                _Next();
                occurrence = Occurrence.ZeroOrMore;
            }
            else if (_IsSymbol("+"))
            {
                _Next();
                occurrence = Occurrence.OneOrMore;
            }
            return new XPathSequenceType(itemType, occurrence);
        }


        private XsType _ParseSingleType(out bool allowEmpty)
        {
            Token token = _Next();
            if (token.Kind != TokenKind.Name)
            {
                throw _Error("Expected a type name");
            }
            XsType type = _ResolveAtomicType(token);
            if (type.IsAbstract)
            {
                throw new XPathException("XPST0080", "Cannot cast to the abstract type " + type.QualifiedName);
            }
            allowEmpty = false;
            if (_IsSymbol("?"))
            {
                _Next();
                allowEmpty = true;
            }
            return type;
        }


        private XsType _ResolveAtomicType(Token token)
        {
            string ns = token.Uri ?? (token.Prefix != null ? _Static.ResolvePrefix(token.Prefix) : "");
            XsType type = ns == XmlNamespaces.Xs ? XsType.FromLocalName(token.Value) : null;
            if (type == null)
            {
                throw new XPathException("XPST0051", "Unknown atomic type " + token + " in expression \"" + _Expression + "\"");
            }
            return type;
        }


        /// <summary>
        /// Parses a sequence type outside of an expression (the "as" attribute of XSLT declarations).
        /// </summary>
        public static XPathSequenceType ParseSequenceType(string text, XPathStaticContext staticContext)
        {
            XPathParser parser = new XPathParser(text, staticContext, new CompileScope());
            XPathSequenceType type = parser._ParseSequenceType();
            if (parser._Peek().Kind != TokenKind.End)
            {
                throw parser._Error("Unexpected token " + parser._Peek());
            }
            return type;
        }


        // ------------------------------------------------------------------------------------------------------------
        // primary expressions
        // ------------------------------------------------------------------------------------------------------------

        private Expr _ParsePrimary()
        {
            Token token = _Peek();
            switch (token.Kind)
            {
                case TokenKind.String:
                    _Next();
                    return new LiteralExpr(new StringAtomic(token.Value));
                case TokenKind.Integer:
                    _Next();
                    return new LiteralExpr(IntegerValue.Get(BigInteger.Parse(token.Value, CultureInfo.InvariantCulture)));
                case TokenKind.Decimal:
                    {
                        _Next();
                        decimal value;
                        if (!decimal.TryParse(token.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
                        {
                            throw new XPathException("FOAR0002", "Decimal literal " + token.Value + " is too large");
                        }
                        return new LiteralExpr(new DecimalValue(value));
                    }
                case TokenKind.Double:
                    _Next();
                    return new LiteralExpr(new DoubleValue(double.Parse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture)));
                case TokenKind.Symbol:
                    switch (token.Value)
                    {
                        case "$":
                            {
                                string name = _ParseVariableName();
                                int slot;
                                if (_Scope.TryResolve(name, out slot))
                                {
                                    return new LocalVariableExpr(slot, name);
                                }
                                _Scope.ExternalReferences.Add(name);
                                return new ExternalVariableExpr(name);
                            }
                        case "(":
                            {
                                _Next();
                                if (_IsSymbol(")"))
                                {
                                    _Next();
                                    return EmptySequenceExpr.Instance;
                                }
                                Expr inner = _ParseExpr();
                                _ExpectSymbol(")");
                                return inner;
                            }
                        case ".":
                            _Next();
                            return ContextItemExpr.Instance;
                    }
                    break;
                case TokenKind.Name:
                    if (_IsSymbol("(", 1))
                    {
                        _Next();
                        return _BuildFunctionCall(token, _ParseArgumentList());
                    }
                    break;
            }
            throw _Error("Unexpected token " + token);
        }


        private List<Expr> _ParseArgumentList()
        {
            _ExpectSymbol("(");
            List<Expr> arguments = new List<Expr>();
            if (_IsSymbol(")"))
            {
                _Next();
                return arguments;
            }
            while (true)
            {
                arguments.Add(_ParseExprSingle());
                if (_IsSymbol(","))
                {
                    _Next();
                    continue;
                }
                _ExpectSymbol(")");
                return arguments;
            }
        }


        private Expr _BuildFunctionCall(Token name, List<Expr> arguments)
        {
            if (name.Prefix == null && name.Uri == null && _ReservedFunctionNames.Contains(name.Value))
            {
                throw _Error("'" + name.Value + "' is not a valid function name");
            }
            string ns = name.Uri ?? (name.Prefix != null ? _Static.ResolvePrefix(name.Prefix) : XmlNamespaces.Fn);
            UserFunction user = _Static.Functions.FindUserFunction(ns, name.Value, arguments.Count);
            if (user != null)
            {
                return new UserFunctionCallExpr(user, arguments.ToArray());
            }
            FunctionDefinition builtIn = XPathFunctionLibrary.FindBuiltIn(ns, name.Value, arguments.Count);
            if (builtIn == null)
            {
                throw new XPathException("XPST0017", "Unknown function " + name + "#" + arguments.Count + " in expression \"" + _Expression + "\"");
            }
            return new FunctionCallExpr(builtIn, arguments.ToArray(), _Static);
        }
    }
}
