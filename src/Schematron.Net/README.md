# Schematron.Net

ISO Schematron for .NET. It validates XML documents with Schematron schemas read from their `.sch`
source, without Java or an XSLT processor. The XPath engine is
[Schematron.Net.XPath](https://www.nuget.org/packages/Schematron.Net.XPath).

It runs the official EN 16931, XRechnung (with Peppol) and ZUGFeRD / Factur-X rules with the same
results as Saxon running their published XSLT.

```csharp
using Schematron.Net;
using Schematron.Net.XPath;

SchematronSchema rules = SchematronSchema.Load("EN16931-CII-validation.sch",
    new SchematronOptions { LocationFormat = SchematronLocationFormat.IsoSkeleton });

XdmDocument invoice = XdmDocument.Load("invoice.xml");      // parse once, validate with several schemas
SchematronResult result = rules.Validate(invoice);
foreach (SchematronMessage message in result.FailedAssertions)
{
    Console.WriteLine($"{message.Flag} {message.Id} at {message.Location}: {message.Text}");
}
XDocument svrl = result.ToSvrl();
```

A validation engine for documents from outside (an upload, an e-mail, an API call) sets a time
limit and reports rule errors instead of throwing them:

```csharp
using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
SchematronValidationOptions options = new SchematronValidationOptions
{
    Phase = "strict",                                          // any phase of the schema, per validation
    DynamicErrors = SchematronDynamicErrorHandling.Report,     // xs:decimal('abc') becomes a result entry
    CancellationToken = timeout.Token,
    RegexTimeout = TimeSpan.FromSeconds(1)
};
options.Parameters["currency"] = XdmValue.Of("EUR");           // replaces <sch:let name="currency" .../>

SchematronResult result = rules.Validate(XdmDocument.Load(upload), options);
bool valid = result.IsValid;                                   // no failed assertion, no error
```

- **Thread safety:** a `SchematronSchema` is immutable and thread-safe; compile it once.
- **Query bindings:** `xslt2`, `xslt3`, `xpath2`, `xpath3` and `xpath31` are evaluated as XPath 2.0
  with the XPath 3.0 operators. `xslt`, `xpath`, `exslt`, a schema without `queryBinding` and
  Schematron 1.5 schemas use XPath 1.0 compatibility mode, as XSLT 2.0 processors do for XSLT 1.0
  stylesheets. Other bindings (`xquery`, `stx`, …) are rejected.
- **Checked when the schema is loaded:** XPath syntax, undeclared and ambiguous variables
  (XPST0008), variables declared twice in one scope, phases that activate unknown patterns, unknown
  abstract patterns and rules. A pattern that fails to compile only fails the phases that activate it.
- **Variables:** a rule variable is visible after its declaration in the rule; a pattern variable in
  its pattern (and in other patterns if no other pattern declares the name); phase and schema
  variables everywhere. A pattern or phase variable shadows a schema variable of the same name, so
  every instance of an abstract pattern has its own variables.
- **Phases:** `SchematronOptions.Phase` selects the default; `SchematronValidationOptions.Phase` any
  phase (or `#ALL`) per validation.
- **Resources:** the schema, its `sch:include`s and `sch:extends/@href` files, the documents its rules
  read with `document()` (code lists) and the subordinate documents of `sch:pattern/@documents` are
  opened by an `IResourceResolver` (`SchematronOptions.ResourceResolver`). The default only opens
  local files. Implement your own to load rules embedded into an assembly (any hierarchical URI
  scheme such as `embedded:/rules/ubl.sch` works), and to limit what `@documents` can open when the
  documents are untrusted.
- **Untrusted documents:** DTDs are rejected, nothing is loaded from the network, and elements nested
  more than 1000 levels deep are rejected (`XdmLoadOptions.MaxDepth`).
- **Locations:** `SchematronLocationFormat` selects how node locations are written. `EQName`
  matches SchXslt, which the KoSIT validator uses. `IsoSkeleton` matches the ISO skeleton XSLT
  published with the CEN and Factur-X rules.
- **Errors:** by default, a dynamic error in a rule (e.g. `xs:decimal('abc')`) raises a
  `SchematronException` with the XPath error code, pattern, rule and location, like the XSLT does.
  With `SchematronDynamicErrorHandling.Report` it is added to `SchematronResult.Errors` and the
  validation continues.
- **SVRL:** active patterns (with their documents), fired rules (with `flag` and `role`), failed
  assertions and successful reports with diagnostics, properties (`sch:property`,
  `svrl:property-reference`, with `xsl:copy-of`) and `xml:lang`.

Not supported:

- `xsl:key` with element content instead of `@use`
- `sch:name/@path` gives the name of the selected node (as the ISO skeleton), not its value (as SchXslt)

See the [known conformance gaps](https://github.com/schmitch/Schematron.Net/blob/main/tests/Schematron.Net.Tests/TestData/schematron-conformance-known-failures.txt).
