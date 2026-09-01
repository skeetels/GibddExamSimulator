(() => {
    async function scan() {
        if (!navigator.mediaDevices?.getUserMedia || !("BarcodeDetector" in window)) {
            throw new Error("camera_not_supported");
        }

        const detector = new BarcodeDetector({ formats: ["qr_code"] });
        const stream = await navigator.mediaDevices.getUserMedia({
            audio: false,
            video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 }, height: { ideal: 720 } }
        });
        const overlay = document.createElement("div");
        overlay.className = "qr-camera-overlay";
        overlay.setAttribute("role", "dialog");
        overlay.setAttribute("aria-label", "Сканирование QR-кода");
        const title = document.createElement("p");
        title.textContent = "Наведите камеру на QR-код на экране компьютера";
        const video = document.createElement("video");
        video.autoplay = true;
        video.playsInline = true;
        video.muted = true;
        video.srcObject = stream;
        const cancel = document.createElement("button");
        cancel.type = "button";
        cancel.textContent = "Отмена";
        overlay.append(title, video, cancel);
        document.body.appendChild(overlay);

        return await new Promise((resolve, reject) => {
            let stopped = false;
            const finish = (callback) => {
                if (stopped) return;
                stopped = true;
                for (const track of stream.getTracks()) track.stop();
                overlay.remove();
                callback();
            };
            cancel.addEventListener("click", () => finish(() => reject(new Error("camera_cancelled"))), { once: true });

            const detect = async () => {
                if (stopped) return;
                try {
                    if (video.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA) {
                        const results = await detector.detect(video);
                        const value = results.find(item => item.rawValue)?.rawValue?.trim();
                        if (value) {
                            finish(() => resolve(value));
                            return;
                        }
                    }
                } catch (error) {
                    finish(() => reject(error));
                    return;
                }
                requestAnimationFrame(detect);
            };
            video.addEventListener("loadeddata", () => requestAnimationFrame(detect), { once: true });
            video.play().catch(error => finish(() => reject(error)));
        });
    }

    window.gibddQr = { scan };
})();
