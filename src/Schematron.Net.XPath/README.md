# Schematron.Net.XPath

An XPath 2.0 engine for .NET. It is the XPath engine of
[Schematron.Net](https://www.nuget.org/packages/Schematron.Net) and usable on its own.

- **XPath 2.0** with the XPath 3.0 operators `let`, `||`, `!` and `=>`, all 13 axes, about 140
  functions and the `xs:*` constructors, including `format-date` / `format-dateTime` /
  `format-time` / `format-integer` (English and German names and words), `sort`, `analyze-string`
  and `adjust-*-to-timezone`.
- **XPath 1.0 compatibility mode** (`XPathStaticContext.XPath1CompatibilityMode`) for expressions
  written for XPath 1.0: function arguments take the first item, arithmetic and comparisons convert
  as XPath 1.0 does.
- **Semantics of Saxon 12:**
  - `xs:decimal` arithmetic and `round()`
  - comparisons of untyped values with numbers
  - the order in which `and` / `or` raise errors
  - XSD regular expressions
- **What XSLT hosts need:**
  - the functions `current()`, `document()`, `key()`, `generate-id()`, `system-property()`, …
  - XSLT patterns
  - `xsl:function` (with `xsl:variable`, `xsl:sequence`, `xsl:value-of`, `xsl:text`, `xsl:if`,
    `xsl:choose`) and `xsl:key`

```csharp
using Schematron.Net.XPath;

XdmDocument document = XdmDocument.Load("invoice.xml");     // DTDs rejected, nothing loaded from the network

XPathStaticContext context = new XPathStaticContext(new Dictionary<string, string>
{
    ["cbc"] = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
});
XPathExpression total = XPathExpression.Compile("sum(//cbc:LineExtensionAmount ! xs:decimal(.))", context);  // thread-safe
XdmValue result = total.Evaluate(document.Root);
Console.WriteLine(result.Single().StringValue);
```

Types:

- **Data model:**
  - `XdmDocument` (`Load` with `XdmLoadOptions`: the maximum nesting depth, 1000 by default)
  - `XdmNode` (read-only navigation: `Kind`, names, `Parent`, `Children`, `Attributes`, line numbers)
  - `XdmAtomicValue` (`TypeName`, `StringValue`)
  - `XdmValue` (a sequence of items; `XdmValue.Of` for strings, booleans, integers, decimals and doubles)
- **Compiling:**
  - `XPathStaticContext` (namespaces, base URI, XPath 1.0 compatibility mode, `XPathFunctionLibrary`
    with `DeclareXslFunctions`)
  - `XPathExpression.Compile` / `CompilePattern`; `VariableReferences` lists the variables an
    expression uses, so a host can check that it declares them
  - `XPathSequenceType`
- **Evaluating:**
  - `Evaluate` / `EvaluateBoolean` with a context item
  - an optional `XPathDynamicContext`: documents for `doc()`, `XslKey`s for `key()`, global variables,
    a `CancellationToken` and the time limit of regular expressions (`RegexTimeout`, XPDY0130)
  - an optional `IXPathVariableResolver`
- **Resources:** `IResourceResolver` opens what `doc()` / `document()` load; `DocumentCache` caches
  the parsed documents. The default `FileResourceResolver` only opens local files.

Errors are `XPathException`s with the W3C error code (`Code`).

Not supported:

- maps and arrays
- higher-order functions (function items, `sort` with a key function)
- collations other than the codepoint collation
- schema awareness

The [W3C QT3 results](https://github.com/schmitch/Schematron.Net/blob/main/tests/Schematron.Net.XPath.Tests/TestData/qt3-known-failures.txt)
list the known gaps.
