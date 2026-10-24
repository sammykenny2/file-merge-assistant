# Czkawka adaptation notice

`src/images.rs` adapts `compute_hashes_for_image` and its validity rule from
`czkawka_core/src/tools/similar_images/core.rs` at commit
`eb8b91dbb4d25dff416202674ee0de84ee4b5e5c`. The upstream decoder is used through
its public API. File Workbench owns the restricted format/resource policy, per-file evidence
encoding and pairwise grouping; it does not call the upstream private grouping.
Other dependencies and their full distribution notices are tracked for packaging.

MIT License

Copyright (c) 2020-2026 Rafał Mikrut

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
