# elkjs (vendored)

`elk.bundled.js` is `lib/elk.bundled.js` from the npm package `elkjs@0.12.0`, copied unchanged
(SHA-256 `1222e44f953ce7746af23801e723708f8e6f436b8b377a6a5fc7552f34a307b3`). `LICENSE.md` is the
package's licence (EPL-2.0 OR GPL-3.0-or-later). The source code of this version is at
<https://github.com/kieler/elkjs/tree/0.12.0>. It compiles the Java sources of the Eclipse Layout
Kernel of the same minor version, at <https://github.com/eclipse/elk/tree/v0.12.0>.

The workflow editor's dependency graph (`wwwroot/js/dependency-graph.js`, in the Graph tab and the dependency chain slide-over) lays its graphs out with it,
off the main thread in `wwwroot/js/dependency-graph.worker.js`.

To upgrade, download the new package tarball from the npm registry, replace both files and update the
version and hash above.
