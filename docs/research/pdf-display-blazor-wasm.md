# Research: showing an encrypted PDF in Blazor WebAssembly

Issue: peterderkoala/easy#36. Researched 2026-10-12.
Context: S06 split view (PDF left, grading sections right). MainApi decrypts the SSE-C object (`DocumentStore.Get`, returns the whole object as `byte[]`) and must serve it to the WASM client, which holds a 5-minute Keycloak bearer token (no cookies). Max 15 MB (S06). No download endpoint exists yet (`UploadDocument.cs`: "no download endpoint yet").

## Recommendation

**Use the browser's native PDF viewer: fetch with the bearer token -> `Blob` (`application/pdf`) -> object URL -> `<iframe>`.** This is Microsoft's documented Blazor pattern, needs no library, and S06 does not ask for page sync ("Direct grading in the PDF viewer is too complex. Instead, split view").
**Switch to pdf.js (Apache-2.0) only if page position must be read or synced** (e.g. a grading section jumps to "page 7", or both examiners scroll together). The native viewer has no API for that. Use pdf.js's own `PDFViewer` component through a small JS module of your own, not a Blazor wrapper. The endpoint and fetch path stay the same for both options, so the switch later costs only the viewer.

## Findings

### 1. Getting the bytes to the browser (bearer token, not cookies)

- An `<iframe src>` cannot carry an `Authorization` header, so the client must `fetch` the bytes itself and hand the viewer a local URL. Microsoft documents exactly this for PDFs: `HttpClient` -> `DotNetStreamReference` -> JS `stream.arrayBuffer()` -> `new Blob([...], {type})` -> `URL.createObjectURL` -> set `src` on `<iframe>`/`<embed>`/`<object>`. Source: https://learn.microsoft.com/aspnet/core/blazor/images-and-documents?view=aspnetcore-10.0#stream-image-or-document-data
- Using the app's normal authorized `HttpClient` keeps token attach and refresh in the existing handler. The 5-minute token only has to live for one request.
- .NET 10: browser `HttpClient` response streaming is on by default; `ReadAsStreamAsync` returns `BrowserHttpReadStream` (async only), not a `MemoryStream`. Source: https://learn.microsoft.com/dotnet/core/compatibility/networking/10.0/default-http-streaming
- Microsoft's sizing guidance: the stream-to-`ArrayBuffer` approach "loads the entire file into the client's memory" and is recommended below 250 MB. 15 MB is well inside that. Source: https://learn.microsoft.com/aspnet/core/blazor/file-downloads?view=aspnetcore-10.0#download-from-a-stream
- Revoke the object URL to avoid leaking memory. The MS sample revokes it in the element's `load` handler. Source: same images-and-documents page; MDN: "To release an object URL, call `revokeObjectURL()`", https://developer.mozilla.org/en-US/docs/Web/API/URL/createObjectURL_static
  - Inference, not verified: revoking on `load` could break the native viewer's own save or print in some browsers. Revoking on component dispose and on document switch is the safer choice.

### 2. Native viewer: browser support

- `navigator.pdfViewerEnabled` tells you whether the browser shows PDFs inline. When it is false, "the PDF is downloaded and may then be handled by some external application". Sources: https://developer.mozilla.org/en-US/docs/Web/API/Navigator/pdfViewerEnabled, https://html.spec.whatwg.org/multipage/system-state.html#pdf-viewing-support (value is "implementation-defined").
- Supported since Chrome 94, Firefox 99, Safari 16.4 (Edge, Opera and mobile entries mirror these). Firefox note: it "Returns `true` even if Firefox is configured to open PDFs in external applications." Source: https://github.com/mdn/browser-compat-data/blob/main/api/Navigator.json
- Mobile is not reliable. Chrome for Android had no inline viewer for a long time and rolled one out behind flags around 2025 (secondary source: https://www.androidauthority.com/google-chrome-native-pdf-reader-3547778/). Not verified against Chromium source. Examiners grade on desktops, so this is a minor risk. Feature-detect and show a "download" fallback.
- MDN, iframe: "The `sandbox` attribute can prevent the built-in PDF viewer from loading ... The browser's PDF viewer already sandboxes all executable content." So do not sandbox the PDF iframe. Source: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/iframe

### 3. Page position

- **Native viewer:** the only standard control is the open fragment `#page=N` (RFC 8118: "page=<pageNum>", 1-based). It is a "may be used to set the document view" hint, not an API. Source: https://www.rfc-editor.org/rfc/rfc8118.html. No spec or browser exposes the current page of the built-in viewer to the page script (no such interface in the HTML spec's PDF section above). So the page can be **set on open (best effort) but not read**, and not synced.
- **pdf.js `PDFViewer`:** `currentPageNumber` is a getter/setter. Every page change dispatches `pagechanging` on the `EventBus` with `{ pageNumber, pageLabel, previous }`. `scrollPageIntoView({ pageNumber, ... })` jumps to a page. `updateviewarea` carries a location (`pageNumber, scale, top, left`) for finer sync. Source: https://github.com/mozilla/pdf.js/blob/master/web/pdf_viewer.js. A minimal embedding (`EventBus`, `PDFLinkService`, `PDFViewer`, `getDocument`, `setDocument`) is in https://github.com/mozilla/pdf.js/blob/master/examples/components/simpleviewer.mjs. Read and sync would then be `pagechanging` -> `[JSInvokable]` -> SignalR.

### 4. pdf.js facts

- License Apache-2.0. Source: https://github.com/mozilla/pdf.js
- Latest release v6.4.299 (2026-10-03). Source: https://github.com/mozilla/pdf.js/releases/tag/v6.4.299. Distributed as `pdfjs-dist` (ES modules `.mjs`, separate worker). `pdf.js` and the worker "must be the exact same version". Source: https://github.com/mozilla/pdf.js/wiki/Frequently-Asked-Questions
- Browser support: the default build targets the latest browsers, a `legacy` build covers older ones. The FAQ table lists Chrome 125+, Firefox ESR+, Safari 18+ ("Mostly"). Source: FAQ above.
- Loading from memory: `getDocument({ data: Uint8Array })`. "Use TypedArrays (Uint8Array) to improve the memory usage". TypedArrays "will generally be transferred to the worker-thread", so no copy. `httpHeaders` exists for `url` loading. Source: https://github.com/mozilla/pdf.js/blob/master/src/display/api.js
- Security: CVE-2024-4367 (arbitrary JS from a crafted font, CVSS 8.8). Fixed in `pdfjs-dist` 4.2.67; older versions are exploitable with the default `isEvalSupported`. Sources: https://github.com/advisories/GHSA-wgrm-67xf-hhpq. Pin a current version, and track updates like any dependency. The native viewer moves this burden to the browser vendor.
- Blazor wrappers: e.g. Blazor.Bootstrap `PdfViewer` (Apache-2.0, https://github.com/vikramlearning/blazorbootstrap) has `OnPageChanged`. It takes a `Url`, or a base64 `data:` URL for bytes (https://docs.blazorbootstrap.com/components/pdf-viewer). A 15 MB file would then cross interop as a ~20 MB string. Commercial viewers (Syncfusion etc.) bring licence costs for what ~60 lines of JS over `PDFViewer` already do. Not recommended.

### 5. Memory for a 15 MB file

- **Native:** one 15 MB `Blob` in the page, plus whatever the browser viewer holds internally. The internals are browser-specific and not documented. Released by `revokeObjectURL` (section 1).
- **Bytes through WASM:** with the MS pattern the data passes through the .NET stream and then becomes a JS `ArrayBuffer`. The peak is roughly one full copy in JS plus streaming chunks. Inference from the docs in section 1, not measured.
- **pdf.js:** the `Uint8Array` is transferred to the worker (no second copy). Rendering costs canvases: "creates, renders, and holds canvases only for visible pages"; about 3.4 MB per letter page, ~14 MB at 2x HiDPI. `maxCanvasPixels` (default 4096x8192) caps a single canvas. Sources: FAQ; `pdf_viewer.js`.
- Neither option has a memory problem at 15 MB. pdf.js memory grows with zoom and visible pages, not file size.

### 6. What the download endpoint should send

Proposed: `GET /api/participants/{participantId}/documents/{documentId}`, same auth policy as the upload, and the same `KeyForgotten` -> 404 rule.

| Header | Value | Why |
|---|---|---|
| `Content-Type` | `application/pdf` | The blob type drives the viewer. The `Results.File` default would be `application/octet-stream`. Source: https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.http.results.stream?view=aspnetcore-10.0 |
| `Content-Disposition` | `inline; filename="<ascii>"; filename*=UTF-8''<pct-encoded>` | Only matters if someone opens the URL directly. `fetch` -> blob ignores it. Use both `filename` and `filename*`. Source: https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Disposition. Passing `fileDownloadName` to `Results.File` sets Content-Disposition (MS docs), but sets it to `attachment` (ASP.NET Core `FileResultHelper.SetContentDispositionHeader`, https://github.com/dotnet/aspnetcore/blob/main/src/Shared/ResultsHelpers/FileResultHelper.cs; from memory of the source, not re-read). Set the header by hand for `inline`. |
| `Cache-Control` | `no-store` | "any caches of any kind (private or shared) should not store this response". MDN: `no-store` alone is enough; stacking `no-cache, private, max-age=0` is "basically meaningless". Source: https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Cache-Control. Without it the decrypted PDF could land in the browser's disk cache, which defeats the S02 crypto-shredding "forget". |
| `X-Content-Type-Options` | `nosniff` | General hardening for user-uploaded content (standard practice; not specific to this research). |
| Range requests | **off** (`enableRangeProcessing: false`, the default) | The client fetches the whole file once. pdf.js range loading would need the token on every lazy chunk (`url` + `httpHeaders`) and would break after the token expires. `DocumentStore.Get` already reads the whole decrypted object. ASP.NET supports it later with one flag (`Results.File(..., enableRangeProcessing: true)`, 206/416 handled by the framework). Source: https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0#file-result-return-values. SeaweedFS supports ranges with SSE (see `seaweedfs-sse-c.md`). |
| `ETag` / `Last-Modified` | omit | Pointless with `no-store`. |

Minimal shape: `Results.File(bytes, "application/pdf")` plus the two headers set on `HttpResponse`. That matches the existing `byte[]` `DocumentStore.Get`; 15 MB in memory per request is the same budget the upload already uses.

## Not verified / follow-ups

- The native viewer's memory and behaviour after an early `revokeObjectURL` (save/print buttons) per browser. Check in a spike.
- Chrome for Android inline-PDF status in the current stable version (secondary source only).
- If a CSP is added later (none in `index.html`/gateway today): the native viewer needs `frame-src blob:` (or `object-src blob:` for `<object>`/`<embed>`); pdf.js needs `worker-src 'self'`. This follows from CSP semantics and was not tested here.
- Whether page sync between the two examiners is a requirement at all. S06 does not say so. That decides native vs. pdf.js; record it in S06 or S10.
