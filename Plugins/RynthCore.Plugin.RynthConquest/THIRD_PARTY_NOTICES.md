# Third-party notices: RynthConquest

RynthConquest's Conquest features (the bank, advanced and enlightenment augmentations, XP bonuses
and levels past 275, the fellowship list, the /top boards, the custom quest worklist, the
auto-deposit and the Void damage-over-time duration rule) are ported from **Oracle of Dereth**, a
Decal plugin for Asheron's Call by **Advis Eveldan** (GitHub:
[advis61](https://github.com/advis61/OracleOfDereth)). They were part of RynthOracle until
2026-10-20 and moved here so that only Conquest players carry them. Thank you, Advis!

- Upstream: https://github.com/advis61/OracleOfDereth
- Ported from commit `ee618c6847b98b656a47ca87cc347441d206c547` (2026-10-01, version 2.3.0).
- Licence: MIT. The upstream README states "License: MIT". The repository has no separate
  LICENSE file and names no copyright line, so the standard MIT text is reproduced below
  with the author and the years of the repository's history.

What came from upstream: `Resources\customquests.csv` (copied unchanged), and the logic of the
features above, rewritten for RynthCore. Ported files say so in their header. RynthOracle's quest
flag parser (`QuestFlags.cs`, also ported from upstream) is compiled in as shared source.

---

MIT License

Copyright (c) 2024-2026 Advis Eveldan (GitHub: advis61) and contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
