<?xml version="1.0" encoding="UTF-8"?>
<schema xmlns="http://purl.oclc.org/dsdl/schematron" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" queryBinding="xslt2" defaultPhase="all">
  <title>Test rules</title>
  <ns prefix="o" uri="urn:example:order"/>
  <ns prefix="f" uri="urn:example:functions"/>
  <phase id="all">
    <active pattern="header"/>
    <active pattern="lines-instance"/>
    <active pattern="included"/>
  </phase>
  <phase id="header-only">
    <active pattern="header"/>
  </phase>
  <let name="max-quantity" value="100"/>
  <xsl:function name="f:net" as="xs:decimal">
    <xsl:param name="line" as="element()"/>
    <xsl:variable name="price" select="xs:decimal($line/o:Price)"/>
    <xsl:sequence select="$price * xs:decimal($line/o:Quantity)"/>
  </xsl:function>
  <xsl:key name="product" match="o:Line" use="o:Product"/>
  <pattern id="header">
    <rule context="/o:Order">
      <let name="lines" value="count(o:Line)"/>
      <assert id="H-01" flag="fatal" test="o:Id">An order must have an id.</assert>
      <assert id="H-02" flag="warning" test="$lines &lt;= 3" diagnostics="d-lines">An order should have at most 3 lines, found <value-of select="$lines"/>.</assert>
      <report id="H-03" flag="information" test="o:Note">The order <name/> has a note: <value-of select="o:Note"/></report>
    </rule>
  </pattern>
  <pattern id="lines" abstract="true">
    <rule context="$line">
      <extends rule="quantity-rule"/>
      <assert id="L-02" flag="fatal" test="f:net(.) = xs:decimal(o:Total)">Line <value-of select="o:Product"/>: total must be price times quantity (<value-of select="f:net(.)"/>).</assert>
      <assert id="L-03" flag="warning" test="count(key('product', o:Product)) = 1" subject="o:Product">Product <value-of select="o:Product"/> is ordered more than once.</assert>
    </rule>
    <rule context="$line-fallback">
      <assert id="L-99" test="false()">never reached for lines</assert>
    </rule>
  </pattern>
  <pattern id="lines-instance" is-a="lines">
    <param name="line" value="o:Line"/>
    <param name="line-fallback" value="o:Line | o:Note"/>
  </pattern>
  <pattern>
    <rule abstract="true" id="quantity-rule">
      <assert id="L-01" flag="error" role="error" test="xs:decimal(o:Quantity) &lt;= $max-quantity">Quantity must not exceed <value-of select="$max-quantity"/>.</assert>
    </rule>
  </pattern>
  <include href="included-pattern.sch"/>
  <diagnostics>
    <diagnostic id="d-lines">There are <value-of select="count(o:Line)"/> lines.</diagnostic>
  </diagnostics>
</schema>
