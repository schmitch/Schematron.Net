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
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Schematron.Net.Tests.Upstream
{
    /// <summary>
    /// The test instances of the XRechnung Schematron in the format of KoSIT's XML-Mutate: a processing instruction
    /// &lt;?xmute mutator="..." schematron-valid="xrubl:BR-DE-1" schematron-invalid="..."?&gt; before an element
    /// describes a change of that element and the rules the changed document must pass (valid) or fail (invalid).
    /// Implements the mutators the instances use: identity, empty, remove and code (values, separator, attribute).
    /// </summary>
    internal sealed class XmlMutate
    {
        private static readonly Regex _PseudoAttribute = new Regex("([A-Za-z][\\w-]*)\\s*=\\s*\"([^\"]*)\"", RegexOptions.Compiled);
        private static readonly Regex _RuleSeparator = new Regex("[\\s,]+", RegexOptions.Compiled);


        public required string Id { get; init; }
        public required string Description { get; init; }
        public required byte[] Document { get; init; }

        /// <summary>The rules (prefix:id, e.g. xrubl:BR-DE-1) the document must pass.</summary>
        public required IReadOnlyList<string> Valid { get; init; }

        /// <summary>The rules the document must fail.</summary>
        public required IReadOnlyList<string> Invalid { get; init; }


        /// <summary>The rule set a prefix of the instances stands for (as configured in the build of the XRechnung Schematron).</summary>
        public static RuleSet RuleSetOf(string prefix)
        {
            return prefix switch
            {
                "xrubl" => RuleSet.XRechnungUbl,
                "xrcii" => RuleSet.XRechnungCii,
                _ => throw new InvalidOperationException("Unknown Schematron prefix " + prefix)
            };
        }


        public static IEnumerable<XmlMutate> LoadAll()
        {
            string root = TestData.Upstream("xrechnung-schematron");
            foreach (string file in Directory.GetFiles(Path.Combine(root, "test", "instances"), "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                foreach (XmlMutate mutant in Load(file, "xrechnung-schematron/" + Path.GetRelativePath(root, file).Replace('\\', '/')))
                {
                    yield return mutant;
                }
            }
        }


        public static IEnumerable<XmlMutate> Load(string file, string id)
        {
            XDocument original = XDocument.Load(file, LoadOptions.PreserveWhitespace);
            int count = original.DescendantNodes().OfType<XProcessingInstruction>().Count(p => p.Target == "xmute");
            for (int index = 0; index < count; index++)
            {
                XDocument copy = new XDocument(original);
                List<XProcessingInstruction> instructions = copy.DescendantNodes().OfType<XProcessingInstruction>().Where(p => p.Target == "xmute").ToList();
                XProcessingInstruction instruction = instructions[index];
                // a few instances repeat schematron-valid / schematron-invalid instead of listing several rules
                Dictionary<string, string> attributes = _PseudoAttribute.Matches(instruction.Data)
                    .GroupBy(m => m.Groups[1].Value)
                    .ToDictionary(g => g.Key, g => string.Join(" ", g.Select(m => m.Groups[2].Value)));
                XElement? target = instruction.NodesAfterSelf().OfType<XElement>().FirstOrDefault();
                if (target == null)
                {
                    throw new InvalidOperationException(id + ": xmute instruction " + (index + 1) + " has no target element");
                }
                string mutator = attributes.GetValueOrDefault("mutator", "identity");
                string name = attributes.GetValueOrDefault("id", "#" + (index + 1));
                string description = attributes.GetValueOrDefault("description", "");
                IReadOnlyList<string> valid = _Rules(attributes.GetValueOrDefault("schematron-valid"));
                IReadOnlyList<string> invalid = _Rules(attributes.GetValueOrDefault("schematron-invalid"));
                attributes.TryGetValue("attribute", out string? attribute);
                // every mutation starts from the original document; the instructions themselves are removed
                foreach (XProcessingInstruction pi in instructions)
                {
                    pi.Remove();
                }

                // one variant per value for "code", otherwise one
                IEnumerable<string?> values = mutator == "code"
                    ? attributes["values"].Split(attributes.GetValueOrDefault("separator", ",")).Select(v => (string?)v.Trim()).Where(v => v!.Length > 0)
                    : new string?[] { null };
                foreach (string? value in values)
                {
                    XDocument variant = new XDocument(copy);
                    _Apply(id, mutator, _Find(variant, target), attribute, value);
                    yield return new XmlMutate
                    {
                        Id = id + " " + name + " (" + mutator + (value != null ? "=" + value : "") + ")",
                        Description = description,
                        Document = Encoding.UTF8.GetBytes(variant.ToString(SaveOptions.DisableFormatting)),
                        Valid = valid,
                        Invalid = invalid
                    };
                }
            }
        }


        private static void _Apply(string id, string mutator, XElement target, string? attribute, string? value)
        {
            switch (mutator)
            {
                case "identity":
                    break;
                case "empty":
                    if (attribute != null)
                    {
                        target.SetAttributeValue(attribute, "");
                    }
                    else
                    {
                        target.RemoveNodes();
                    }
                    break;
                case "remove":
                    if (attribute != null)
                    {
                        target.Attribute(attribute)?.Remove();
                    }
                    else
                    {
                        target.Remove();
                    }
                    break;
                case "code":
                    if (attribute != null)
                    {
                        target.SetAttributeValue(attribute, value);
                    }
                    else
                    {
                        target.Value = value!;
                    }
                    break;
                default:
                    throw new NotSupportedException(id + ": XML-Mutate mutator '" + mutator + "' is not implemented");
            }
        }


        /// <summary>The element of a copy of the document that corresponds to an element of the original.</summary>
        private static XElement _Find(XDocument copy, XElement original)
        {
            List<int> path = new List<int>();
            for (XElement? e = original; e != null && e.Parent != null; e = e.Parent)
            {
                path.Insert(0, e.ElementsBeforeSelf().Count());
            }
            XElement current = copy.Root!;
            foreach (int index in path)
            {
                current = current.Elements().ElementAt(index);
            }
            return current;
        }


        private static IReadOnlyList<string> _Rules(string? value)
        {
            return value == null ? Array.Empty<string>() : _RuleSeparator.Split(value).Where(r => r.Length > 0).ToList();
        }


        public override string ToString()
        {
            return Id;
        }
    }
}
