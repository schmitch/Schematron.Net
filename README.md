# Schematron.Net

ISO Schematron and XPath 2.0 for .NET, without Java or an XSLT processor.

| Package | What it is |
|---|---|
| [`Schematron.Net`](src/Schematron.Net) | ISO Schematron from the `.sch` source (query bindings `xslt2` / `xslt3` / `xpath2` / `xpath3` / `xpath31`; `xslt` / `xpath` in XPath 1.0 compatibility mode): phases, includes, abstract patterns and rules, `let`, `value-of`, `name`, diagnostics, properties, subordinate documents, external rules, `xsl:function`, `xsl:key`, SVRL. Validation options for untrusted documents: parameters, phase per validation, reported rule errors, cancellation, limits. |
| [`Schematron.Net.XPath`](src/Schematron.Net.XPath) | The XPath 2.0 engine it is built on, usable on its own: the XDM data model, compiled expressions and XSLT patterns, variables, XPath 1.0 compatibility mode, the XSLT 2.0 functions, `format-date` and friends, `xsl:function` and `xsl:key`. |

`Schematron.Net` only uses the public API of `Schematron.Net.XPath`, so another host language (or a
validator that selects rules with XPath) can use the XPath package the same way.

The main use is validating e-invoices: the official EN 16931, XRechnung (with the Peppol rules)
and ZUGFeRD / Factur-X rules use XPath 2.0 and a small part of XSLT 2.0, never XSLT 3.0. Run from
their `.sch` sources, they give the same results as Saxon running the compiled XSLT published with
them (see [Tests](#tests)).

```csharp
using Schematron.Net;
using Schematron.Net.XPath;

SchematronSchema rules = SchematronSchema.Load("XRechnung-UBL-validation.sch");   // compile once, thread-safe
XdmDocument invoice = XdmDocument.Load("invoice.xml");                            // DTDs rejected, no network access

foreach (SchematronMessage message in rules.Validate(invoice).FailedAssertions)
{
    Console.WriteLine($"{message.Flag} {message.Id} at {message.Location}: {message.Text}");
}

// plain XPath on the same document
XPathStaticContext context = new XPathStaticContext(new Dictionary<string, string>
{
    ["cbc"] = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
});
bool isXRechnung = XPathExpression.Compile("//cbc:CustomizationID = 'urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0'", context)
    .EvaluateBoolean(invoice.Root);
```

For documents from outside, validate with a time limit and collect rule errors instead of throwing
them (see [Schematron.Net](src/Schematron.Net)):

```csharp
using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
SchematronResult result = rules.Validate(invoice, new SchematronValidationOptions
{
    DynamicErrors = SchematronDynamicErrorHandling.Report,
    CancellationToken = timeout.Token
});
Console.WriteLine(result.IsValid ? "valid" : $"{result.FailedAssertions.Count()} failed assertions, {result.Errors.Count} errors");
```

Both packages target .NET 10.

## Tests

The upstream test data is not committed. [`eng/fetch-testdata.sh`](eng/fetch-testdata.sh)
downloads it (about 120 MB, a few seconds) at the revisions pinned in
[`eng/testdata.lock`](eng/testdata.lock):

```shell
eng/fetch-testdata.sh
dotnet test
```

Without the data, those tests are skipped (inconclusive). With `SCHEMATRON_REQUIRE_TESTDATA=1`, as
in CI, they fail instead.

| Suite | Project | What is checked | Result |
|---|---|---|---|
| Comparison with Saxon | Schematron.Net | 9 rule sets (CEN EN 16931 1.3.17 UBL + CII, XRechnung 2.6.0 UBL + CII with the Peppol rules, Factur-X 1.09.2 MINIMUM … EXTENDED). Each validates every document of its syntax twice: from the `.sch` with this library, and from the publisher's compiled XSLT with Saxon (SaxonCS-HE 13.1). Findings (id, flag, role, location, text, order) and dynamic errors must be equal. | 2,569 UBL + 883 CII documents, about 41,000 findings: identical |
| CEN unit tests | Schematron.Net | `eInvoicing-EN16931/test`: the rules each test document must pass or fail | 1,140 tests: all pass |
| XRechnung rule tests | Schematron.Net | `xrechnung-schematron/test/instances` (XML-Mutate): 1,754 mutated documents and their expectations | 1,793 expectations: all met |
| Schematron conformance | Schematron.Net | [Schematron/schematron-conformance](https://github.com/Schematron/schematron-conformance) | 44 of 50 pass |
| W3C QT3 | Schematron.Net.XPath | 140 test sets: the functions of the rules, `format-date` and friends, `sort`, `analyze-string`, operators, path / cast / comparison / quantified expressions, with XPath 3.1 semantics (as Saxon 12) | 13,439 pass, 424 fail, 2,705 skipped (XQuery, schema awareness, …) |
| Saxon parity | Schematron.Net.XPath | 402 expressions compared with Saxon-HE 12 | all pass |
| API and engine tests | both | Public API, Schematron features, variable scopes, query bindings, validation options, limits for untrusted documents, resource resolution | all pass |

The document corpus for the Saxon comparison has these parts:

- the CEN examples and the documents inside the CEN unit tests
- the XRechnung test suite and its rule test instances, with every mutation applied
- the ZUGFeRD 2.4 examples

QT3 and the conformance suite do not pass completely. Their failures are listed in
[`qt3-known-failures.txt`](tests/Schematron.Net.XPath.Tests/TestData/qt3-known-failures.txt) and
[`schematron-conformance-known-failures.txt`](tests/Schematron.Net.Tests/TestData/schematron-conformance-known-failures.txt).
A test fails if a case that is not listed fails (a regression), or if a listed case passes (then
remove it). Regenerate the QT3 list with `QT3_UPDATE_BASELINE=1 dotnet test`.

The known failures are mostly:

- maps, arrays and higher-order functions (XPath 3.1)
- regular-expression edge cases
- collations
- error codes that differ from the specification
- conformance test cases that contradict each other (a pattern variable shadowing a schema variable)
  or expect SchXslt where this library follows the ISO skeleton (`sch:name/@path`, extending an
  abstract rule of another pattern), and `xsl:key` with element content

None of them is used by the e-invoice rules.

To test newer rules, point the entry in `eng/testdata.lock` to the new release (commit or
checksum), then run `eng/fetch-testdata.sh` and `dotnet test`.

## Origin and licence

The engine comes from the native validator of
[Envisia.InvoiceXml](https://github.com/envisia/Envisia.InvoiceXml). Licensed under the
[Apache License 2.0](LICENSE).

The tests download third-party data under its own licences (EUPL-1.2, Apache-2.0, W3C, MIT; see
`eng/testdata.lock`). The Schematron test project references SaxonCS-HE (MPL-2.0) as the reference
implementation; the packages do not.
