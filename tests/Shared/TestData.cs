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
namespace Schematron.Net.Testing
{
    /// <summary>
    /// Locates the test data: the files below TestData/ that are part of this repository, and the upstream suites
    /// (rules, conformance tests and sample documents of third parties) that eng/fetch-testdata.sh downloads at the
    /// revisions pinned in eng/testdata.lock.
    /// </summary>
    internal static class TestData
    {
        /// <summary>Set to 1 to fail instead of skipping (inconclusive) tests whose upstream data is missing.</summary>
        public const string RequireVariable = "SCHEMATRON_REQUIRE_TESTDATA";

        /// <summary>Overrides the directory of the upstream data (default: .testdata in the repository root).</summary>
        public const string DirectoryVariable = "SCHEMATRON_TESTDATA";

        private static readonly Lazy<string?> _UpstreamRoot = new Lazy<string?>(_FindUpstreamRoot);


        /// <summary>A file below TestData/ of the test project.</summary>
        public static string Local(params string[] path)
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", Path.Combine(path));
        }


        /// <summary>
        /// The directory of an upstream suite (e.g. "cen" for the CEN EN 16931 artefacts). Marks the test as
        /// inconclusive if it has not been downloaded, or fails it if <see cref="RequireVariable"/> is set.
        /// </summary>
        public static string Upstream(string name)
        {
            string? root = _UpstreamRoot.Value;
            string? directory = root == null ? null : Path.Combine(root, name);
            if (directory == null || !Directory.Exists(directory))
            {
                string message = "The upstream test data '" + name + "' is missing; run eng/fetch-testdata.sh (looked in " + (directory ?? ".testdata") + ").";
                if (Environment.GetEnvironmentVariable(RequireVariable) == "1")
                {
                    Assert.Fail(message);
                }
                Assert.Inconclusive(message);
            }
            return directory!;
        }


        /// <summary>The source directory of the test project (to update baseline files), null if not found.</summary>
        public static string? SourceDirectory()
        {
            for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (directory.GetFiles("*.Tests.csproj").Length > 0)
                {
                    return directory.FullName;
                }
            }
            return null;
        }


        private static string? _FindUpstreamRoot()
        {
            string? configured = Environment.GetEnvironmentVariable(DirectoryVariable);
            if (!string.IsNullOrEmpty(configured))
            {
                return Path.GetFullPath(configured);
            }
            for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "eng", "testdata.lock")))
                {
                    return Path.Combine(directory.FullName, ".testdata");
                }
            }
            return null;
        }
    }
}
