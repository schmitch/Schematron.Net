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
using System.IO;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// Opens the resources a schema refers to: included Schematron files (sch:include, sch:extends/@href) and the
    /// documents loaded with doc() and document(), e.g. code lists. Relative references are resolved against the
    /// URI of the referring file with the rules of <see cref="Uri"/>, so any hierarchical URI scheme works
    /// (file:, or e.g. embedded:/rules/ubl.sch for rules embedded into an assembly).
    /// </summary>
    public interface IResourceResolver
    {
        /// <summary>
        /// Opens the resource with the given absolute URI.
        /// </summary>
        /// <exception cref="FileNotFoundException">The resource does not exist</exception>
        /// <exception cref="NotSupportedException">The resolver does not load resources with this URI</exception>
        public Stream Open(Uri uri);
    }


    /// <summary>
    /// Opens local files (file: URIs). Resources with any other scheme, in particular network resources, are never
    /// loaded. This is the default resolver.
    /// </summary>
    public sealed class FileResourceResolver : IResourceResolver
    {
        /// <summary>The shared instance.</summary>
        public static FileResourceResolver Instance { get; } = new FileResourceResolver();


        /// <inheritdoc />
        public Stream Open(Uri uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            if (!uri.IsAbsoluteUri || !uri.IsFile)
            {
                throw new NotSupportedException("Only local files can be loaded, cannot open " + uri);
            }
            return File.OpenRead(uri.LocalPath);
        }
    }


    internal static class ResourceLoader
    {
        public static Stream Open(IResourceResolver resolver, string uri)
        {
            Uri parsed;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out parsed))
            {
                // a relative reference without base URI: a path relative to the working directory
                parsed = new Uri(Path.GetFullPath(uri));
            }
            return (resolver ?? FileResourceResolver.Instance).Open(parsed);
        }


        public static string ToLocalPath(string uri)
        {
            Uri parsed;
            if (Uri.TryCreate(uri, UriKind.Absolute, out parsed))
            {
                return parsed.IsFile ? parsed.LocalPath : null;
            }
            return uri;
        }


        /// <summary>
        /// Returns the URI of a local file path.
        /// </summary>
        public static string FileUri(string path)
        {
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }
    }


    /// <summary>
    /// Loads the documents requested with doc() / document() (code lists etc.) through an
    /// <see cref="IResourceResolver"/> and keeps them; thread-safe.
    /// </summary>
    public sealed class DocumentCache : IDocumentResolver
    {
        private readonly ConcurrentDictionary<string, XdmDocument> _Documents = new ConcurrentDictionary<string, XdmDocument>();
        private readonly IResourceResolver _Resolver;


        /// <summary>
        /// Creates a cache.
        /// </summary>
        /// <param name="resolver">Opens the documents; null for <see cref="FileResourceResolver"/></param>
        public DocumentCache(IResourceResolver resolver = null)
        {
            _Resolver = resolver ?? FileResourceResolver.Instance;
        }


        /// <inheritdoc />
        public XdmDocument Resolve(string absoluteUri)
        {
            return _Documents.GetOrAdd(absoluteUri, uri =>
            {
                using (Stream stream = ResourceLoader.Open(_Resolver, uri))
                {
                    return XdmDocumentBuilder.Load(stream, uri);
                }
            });
        }
    }
}
