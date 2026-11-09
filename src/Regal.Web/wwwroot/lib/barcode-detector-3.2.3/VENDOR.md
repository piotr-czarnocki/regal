# barcode-detector 3.2.3 (served locally)

Copied by hand from the npm packages, because the project has no JavaScript build step.

| File | Source | SHA-256 |
|---|---|---|
| `ponyfill.js` | `barcode-detector@3.2.3`, `dist/iife/ponyfill.js` | `915a6febe3edd6dab6a7c5a21f69cadd54e1d095cdde9a3eb62605b3db71bf60` |
| `zxing_reader.wasm` | `zxing-wasm@3.1.6`, `dist/reader/zxing_reader.wasm` | `5003fb4b88c4fbd1fd518a2fd8c8ad365c519e183a08c03a322dddfea3cea84e` |
| `LICENSE-barcode-detector`, `LICENSE-zxing-wasm` | the packages' MIT licenses | |

The WASM checksum equals `ZXING_WASM_SHA256` built into `ponyfill.js`, so the two files belong together.
`BarcodeScannerDialog.razor.js` points the library's `locateFile` here, so no request goes to a CDN.

To update: download both packages from `registry.npmjs.org` into an empty folder, copy the same two
files, check that the new WASM checksum equals the new `ZXING_WASM_SHA256`, and update this table.
