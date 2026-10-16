# Schematron.Net

ISO Schematron for .NET. It validates XML documents with Schematron schemas that use the `xslt2` /
`xslt3` query binding, read from their `.sch` source, without Java or an XSLT processor. The XPath
engine is [Schematron.Net.XPath](https://www.nuget.org/packages/Schematron.Net.XPath).

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

- **Thread safety:** a `SchematronSchema` is immutable and thread-safe; compile it once.
- **Resources:** the schema, its `sch:include`s and the documents its rules read with `document()`
  (code lists) are opened by an `IResourceResolver` (`SchematronOptions.ResourceResolver`). The
  default only opens local files. Implement your own to load rules embedded into an assembly; any
  hierarchical URI scheme such as `embedded:/rules/ubl.sch` works.
- **Locations:** `SchematronLocationFormat` selects how node locations are written. `EQName`
  matches SchXslt, which the KoSIT validator uses. `IsoSkeleton` matches the ISO skeleton XSLT
  published with the CEN and Factur-X rules.
- **Errors:** a dynamic error in a rule (e.g. `xs:decimal('abc')`) raises a `SchematronException`
  with the XPath error code, pattern, rule and location, like the XSLT does.

Not supported:

- `sch:extends/@href`
- `sch:pattern/@documents`
- properties
- `sch:let` with element content
- some static checks

See the [known conformance gaps](https://github.com/schmitch/Schematron.Net/blob/main/tests/Schematron.Net.Tests/TestData/schematron-conformance-known-failures.txt).
