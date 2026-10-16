# Schematron.Net.XPath

An XPath 2.0 engine for .NET. It is the XPath engine of
[Schematron.Net](https://www.nuget.org/packages/Schematron.Net) and usable on its own.

- **XPath 2.0** with the XPath 3.0 operators `let`, `||`, `!` and `=>`, all 13 axes, about 130
  functions and the `xs:*` constructors.
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
  - `XdmDocument`
  - `XdmNode` (read-only navigation: `Kind`, names, `Parent`, `Children`, `Attributes`, line numbers)
  - `XdmAtomicValue` (`TypeName`, `StringValue`)
  - `XdmValue` (a sequence of items)
- **Compiling:**
  - `XPathStaticContext` (namespaces, base URI, `XPathFunctionLibrary` with `DeclareXslFunctions`)
  - `XPathExpression.Compile` / `CompilePattern`
  - `XPathSequenceType`
- **Evaluating:**
  - `Evaluate` / `EvaluateBoolean` with a context item
  - an optional `XPathDynamicContext` (documents for `doc()`, `XslKey`s for `key()`, global variables)
  - an optional `IXPathVariableResolver`
- **Resources:** `IResourceResolver` opens what `doc()` / `document()` load; `DocumentCache` caches
  the parsed documents. The default `FileResourceResolver` only opens local files.

Errors are `XPathException`s with the W3C error code (`Code`).

Not supported:

- maps and arrays
- higher-order functions
- `format-date` and similar functions
- collations
- schema awareness

The [W3C QT3 results](https://github.com/schmitch/Schematron.Net/blob/main/tests/Schematron.Net.XPath.Tests/TestData/qt3-known-failures.txt)
list the known gaps.
