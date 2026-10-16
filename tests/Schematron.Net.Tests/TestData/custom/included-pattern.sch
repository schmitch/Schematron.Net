<?xml version="1.0" encoding="UTF-8"?>
<pattern xmlns="http://purl.oclc.org/dsdl/schematron" id="included">
  <rule context="o:Line/o:Product">
    <assert id="P-01" flag="fatal" test="matches(., '^[A-Z]{3}-\d+$')">Product codes have the form ABC-123, found "<value-of select="."/>".</assert>
  </rule>
</pattern>
