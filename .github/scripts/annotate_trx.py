#!/usr/bin/env python3
"""Emit GitHub Actions error annotations for failed tests found in TestResults/*.trx.

Used by the ci/release workflows so a test failure names the offending test(s) in the
run summary without needing to download logs. Parses TRX produced by
`dotnet test --logger trx`. Input is our own self-generated test output (not untrusted).
"""
import glob
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

found = False
for path in glob.glob("TestResults/**/*.trx", recursive=True):
    try:
        tree = ET.parse(path)
    except Exception:
        continue
    for result in tree.findall(".//t:UnitTestResult", NS):
        if result.get("outcome") == "Failed":
            found = True
            name = result.get("testName")
            message = " ".join("".join(result.itertext()).split())[:500]
            print(f"::error::FAILED {name}: {message}")

if not found:
    print("::error::test step failed but no failed test was parsed from TRX")
