// Reads barcodes from the camera. The native BarcodeDetector is used where the browser has one
// that reads EAN-13; otherwise the bundled ZXing-based ponyfill, served from this application,
// never from a CDN.
const libraryPath = 'lib/barcode-detector-3.2.3/';
const formats = ['ean_13', 'ean_8', 'upc_a', 'upc_e', 'code_128'];
const acceptable = /^[0-9A-Za-z.\-]{1,50}$/;
let loading;

function libraryUrl(file) {
    return new URL(libraryPath + file, document.baseURI).href;
}

function loadLibrary() {
    if (window.BarcodeDetectionAPI) {
        return Promise.resolve(window.BarcodeDetectionAPI);
    }

    loading ??= new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = libraryUrl('ponyfill.js');
        script.onload = () => resolve(window.BarcodeDetectionAPI);
        script.onerror = () => {
            loading = undefined;
            reject(new Error('LibraryError'));
        };
        document.head.appendChild(script);
    });
    return loading;
}

async function createDetector() {
    if ('BarcodeDetector' in window) {
        const supported = await window.BarcodeDetector.getSupportedFormats();
        if (supported.includes('ean_13')) {
            return new window.BarcodeDetector({ formats: formats.filter(f => supported.includes(f)) });
        }
    }

    const api = await loadLibrary();
    api.prepareZXingModule({
        overrides: {
            locateFile: (path, prefix) => path.endsWith('.wasm') ? libraryUrl(path) : prefix + path,
        },
    });
    return new api.BarcodeDetector({ formats });
}

export function cameraAvailable() {
    return window.isSecureContext && !!navigator.mediaDevices?.getUserMedia;
}

// Calls dotnet.OnDetected(code) once, then stops the camera. Errors carry the DOMException name.
export async function start(video, dotnet) {
    let stream;
    try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
    } catch (error) {
        throw new Error(error?.name ?? 'CameraError');
    }

    let stopped = false;
    video._regalStop = () => {
        stopped = true;
        stream.getTracks().forEach(track => track.stop());
        video.srcObject = null;
    };

    video.srcObject = stream;
    await video.play();
    const detector = await createDetector();

    const tick = async () => {
        if (stopped) {
            return;
        }

        try {
            if (video.readyState >= 2) {
                const found = (await detector.detect(video)).map(code => code.rawValue).find(value => acceptable.test(value));
                if (found) {
                    video._regalStop();
                    await dotnet.invokeMethodAsync('OnDetected', found);
                    return;
                }
            }
        } catch {
            // A frame that cannot be read is skipped; the next one is tried.
        }

        setTimeout(tick, 150);
    };
    tick();
}

export function stop(video) {
    video?._regalStop?.();
}
