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
using System.Threading;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// Resolves the variables an expression references but does not bind itself, e.g. the sch:let variables of a
    /// Schematron schema or the parameters of a host language.
    /// </summary>
    public interface IXPathVariableResolver
    {
        /// <summary>
        /// Returns the value of a variable.
        /// </summary>
        /// <param name="expandedName">The name: the local name, or {namespace-uri}local-name for a prefixed name</param>
        /// <param name="value">The value of the variable</param>
        /// <returns>False if the variable is unknown (the evaluation then fails with XPST0008)</returns>
        public bool TryResolve(string expandedName, out XdmValue value);
    }


    /// <summary>
    /// Loads the documents requested by fn:doc() and document().
    /// </summary>
    public interface IDocumentResolver
    {
        /// <summary>
        /// Returns the document with the given absolute URI, or throws an exception if it cannot be loaded.
        /// </summary>
        public XdmDocument Resolve(string absoluteUri);
    }


    /// <summary>
    /// The state shared by the evaluations of one run, e.g. the validation of one document: the documents fn:doc()
    /// and document() can load, the keys key() looks up, the global variables and the current date and time.
    /// Not thread-safe (key() builds its indexes here); use one instance per run.
    /// </summary>
    public sealed class XPathDynamicContext
    {
        /// <summary>Cache of built key indexes: (document id, key name) → value → nodes.</summary>
        internal readonly Dictionary<string, Dictionary<string, List<XdmNode>>> KeyIndexes = new Dictionary<string, Dictionary<string, List<XdmNode>>>();


        /// <summary>
        /// Creates a dynamic context.
        /// </summary>
        /// <param name="documents">Loads the documents requested with doc() and document(); null: they cannot be loaded</param>
        public XPathDynamicContext(IDocumentResolver documents = null)
        {
            Documents = documents;
        }


        /// <summary>Loads the documents requested with doc() and document().</summary>
        public IDocumentResolver Documents { get; }

        /// <summary>The value of fn:current-dateTime(), stable during the run.</summary>
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Now;

        /// <summary>The keys declared with xsl:key, by expanded name, for key().</summary>
        public IReadOnlyDictionary<string, XslKey> Keys { get; set; }

        /// <summary>The global variables: used when an evaluation gets no variables, and by the key definitions.</summary>
        public IXPathVariableResolver GlobalVariables { get; set; }

        /// <summary>
        /// Stops long evaluations: checked while predicates, for and quantified expressions and function calls are
        /// evaluated. A cancelled evaluation throws an <see cref="OperationCanceledException"/>.
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// The time limit of one match of a regular expression (matches(), replace(), tokenize()); a match that takes
        /// longer raises XPDY0130. Default: 10 seconds.
        /// </summary>
        public TimeSpan RegexTimeout
        {
            get => _RegexTimeout;
            set => _RegexTimeout = value > TimeSpan.Zero || value == Timeout.InfiniteTimeSpan
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "The regular expression timeout must be positive or infinite");
        }

        private TimeSpan _RegexTimeout = TimeSpan.FromSeconds(10);
    }


    /// <summary>
    /// An xsl:key declaration, for the key() function.
    /// </summary>
    public sealed class XslKey
    {
        /// <summary>
        /// Creates a key.
        /// </summary>
        /// <param name="expandedName">The name: local name or {namespace-uri}local-name</param>
        /// <param name="match">The nodes the key indexes, compiled with <see cref="XPathExpression.CompilePattern"/></param>
        /// <param name="use">The values of a node in the index, evaluated with the node as context item</param>
        public XslKey(string expandedName, XPathExpression match, XPathExpression use)
        {
            Name = expandedName ?? throw new ArgumentNullException(nameof(expandedName));
            Match = match ?? throw new ArgumentNullException(nameof(match));
            Use = use ?? throw new ArgumentNullException(nameof(use));
        }


        /// <summary>The expanded name of the key.</summary>
        public string Name { get; }

        /// <summary>The match pattern, compiled to select all matching nodes of a tree.</summary>
        public XPathExpression Match { get; }

        /// <summary>The expression that computes the key values of a node.</summary>
        public XPathExpression Use { get; }
    }


    /// <summary>
    /// The dynamic context of an evaluation. The focus (context item, position, size) and the local variables are
    /// mutated while the expression tree is evaluated; every expression restores what it changed.
    /// </summary>
    internal sealed class EvalContext
    {
        public XdmItem ContextItem;
        public int Position;
        public int Size;

        /// <summary>Slots of the variables bound inside the expression (for, let, some/every, function parameters).</summary>
        public XdmValue[] Locals;

        /// <summary>The item returned by current() (the context item at the start of the outermost evaluation).</summary>
        public XdmItem CurrentItem;
        public IXPathVariableResolver Variables;
        public XPathDynamicContext Environment;
        public int CallDepth;


        public EvalContext(XPathDynamicContext environment)
        {
            Environment = environment;
        }


        /// <summary>
        /// Throws an <see cref="OperationCanceledException"/> if the evaluation was cancelled.
        /// </summary>
        public void CheckCancellation()
        {
            Environment.CancellationToken.ThrowIfCancellationRequested();
        }


        public XdmNode ContextNode
        {
            get
            {
                if (ContextItem == null)
                {
                    throw new XPathException("XPDY0002", "The context item is absent");
                }
                XdmNode node = ContextItem as XdmNode;
                if (node == null)
                {
                    throw new XPathException("XPTY0020", "The context item is not a node");
                }
                return node;
            }
        }
    }
}
