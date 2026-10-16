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
using System.Collections.Concurrent;
using Saxon.Api;

namespace Schematron.Net.Tests.Upstream
{
    /// <summary>
    /// The reference implementation: the compiled XSLT a rule set is published with, run by Saxon (SaxonCS-HE), and
    /// its SVRL output reduced to <see cref="Finding"/>s.
    /// </summary>
    internal static class SaxonReference
    {
        private const string _Svrl = "http://purl.oclc.org/dsdl/svrl";
        private static readonly Processor _Processor = new Processor(false);
        private static readonly ConcurrentDictionary<string, Lazy<XsltExecutable>> _Executables = new ConcurrentDictionary<string, Lazy<XsltExecutable>>();


        private static XsltExecutable _Compile(string path)
        {
            return _Executables.GetOrAdd(Path.GetFullPath(path), p => new Lazy<XsltExecutable>(() =>
            {
                XsltCompiler compiler = _Processor.NewXsltCompiler();
                compiler.ErrorReporter = _ => { };
                compiler.BaseUri = new Uri(p);
                using FileStream stream = File.OpenRead(p);
                return compiler.Compile(stream);
            })).Value;
        }


        public static Outcome Validate(string xsltPath, byte[] document)
        {
            XsltExecutable executable = _Compile(xsltPath);
            return Outcome.Of(() =>
            {
                DocumentBuilder builder = _Processor.NewDocumentBuilder();
                builder.BaseUri = new Uri("file:///document.xml");
                XdmNode input = builder.Build(new MemoryStream(document));
                Xslt30Transformer transformer = executable.Load30();
                transformer.ErrorReporter = _ => { };
                transformer.MessageListener = _ => { };
                transformer.GlobalContextItem = input;
                XdmDestination destination = new XdmDestination();
                transformer.ApplyTemplates(input, destination);
                return _Findings(destination.XdmNode);
            }, e => e is SaxonApiException saxon ? (saxon.ErrorCode?.LocalName ?? "UNKNOWN") : null);
        }


        private static List<Finding> _Findings(XdmNode svrl)
        {
            List<Finding> findings = new List<Finding>();
            foreach (XdmNode node in svrl.Select(Steps.Descendant()).OfType<XdmNode>())
            {
                if (node.NodeKind != XdmNodeKind.Element || node.NodeName.Uri != _Svrl || (node.NodeName.LocalName != "failed-assert" && node.NodeName.LocalName != "successful-report"))
                {
                    continue;
                }
                XdmNode? text = node.Select(Steps.Child(_Svrl, "text")).OfType<XdmNode>().FirstOrDefault();
                findings.Add(new Finding(node.NodeName.LocalName, _Attribute(node, "id"), _Attribute(node, "flag"), _Attribute(node, "role"),
                    _Attribute(node, "location"), Finding.NormalizeText(text?.StringValue)));
            }
            return findings;
        }


        private static string? _Attribute(XdmNode node, string name)
        {
            return node.GetAttributeValue(new QName("", name));
        }
    }
}
