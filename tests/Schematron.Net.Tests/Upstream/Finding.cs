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
using System.Text.RegularExpressions;

namespace Schematron.Net.Tests.Upstream
{
    /// <summary>
    /// A failed assertion or successful report, normalized for comparing this library with Saxon: the message text
    /// with collapsed whitespace (the SVRL serializers indent differently).
    /// </summary>
    internal sealed record Finding(string Kind, string? Id, string? Flag, string? Role, string? Location, string Text)
    {
        private static readonly Regex _Whitespace = new Regex(@"\s+", RegexOptions.Compiled);


        public static string NormalizeText(string? text)
        {
            return _Whitespace.Replace(text ?? "", " ").Trim();
        }


        public static IReadOnlyList<Finding> Of(SchematronResult result)
        {
            return result.Messages.Select(m => new Finding(m.Kind == SchematronMessageKind.FailedAssert ? "failed-assert" : "successful-report",
                m.Id, m.Flag, m.Role, m.Location, NormalizeText(m.Text))).ToList();
        }


        public override string ToString()
        {
            return Kind + " " + Id + " flag=" + Flag + (Role != null ? " role=" + Role : "") + " at " + Location + ": " + Text;
        }
    }


    /// <summary>
    /// The outcome of validating one document with one rule set: the findings, or the code of the dynamic error that
    /// aborted the validation.
    /// </summary>
    internal sealed record Outcome(IReadOnlyList<Finding> Findings, string? Error)
    {
        public static Outcome Of(Func<IReadOnlyList<Finding>> validate, Func<Exception, string?> errorCode)
        {
            try
            {
                return new Outcome(validate(), null);
            }
            catch (Exception e)
            {
                string? code = errorCode(e);
                if (code == null)
                {
                    throw;
                }
                return new Outcome(Array.Empty<Finding>(), code);
            }
        }


        /// <summary>Describes how this outcome differs from the expected one, null if they are the same.</summary>
        public string? Diff(Outcome expected)
        {
            if (Error != expected.Error)
            {
                return "error " + (Error ?? "none") + ", Saxon: " + (expected.Error ?? "none")
                    + (Error == null ? " (findings: " + Findings.Count + ")" : "");
            }
            for (int i = 0; i < Math.Max(Findings.Count, expected.Findings.Count); i++)
            {
                Finding? actual = i < Findings.Count ? Findings[i] : null;
                Finding? reference = i < expected.Findings.Count ? expected.Findings[i] : null;
                if (actual != reference)
                {
                    return "finding " + (i + 1) + " of " + Findings.Count + " (Saxon: " + expected.Findings.Count + ")\n      actual: " + (actual?.ToString() ?? "-")
                        + "\n      Saxon:  " + (reference?.ToString() ?? "-");
                }
            }
            return null;
        }
    }
}
