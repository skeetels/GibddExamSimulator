(() => {
    const imageCacheName = 'gibdd-question-images-v1';
    let activeController = null;

    async function downloadImages(urls, dotnetReference) {
        if (activeController) throw new Error('Загрузка офлайн-пакета уже выполняется.');
        activeController = new AbortController();
        const cache = await caches.open(imageCacheName);
        try {
            let completed = 0;
            const total = urls.length;
            for (const url of urls) {
                if (activeController.signal.aborted) throw new DOMException('Загрузка отменена.', 'AbortError');
                const request = new Request(url, { cache: 'no-cache', signal: activeController.signal });
                if (!(await cache.match(request))) {
                    const response = await fetch(request);
                    if (!response.ok) throw new Error(`Не удалось загрузить изображение: ${url}`);
                    await cache.put(request, response.clone());
                }
                completed++;
                await dotnetReference.invokeMethodAsync('ReportProgress', completed, total);
            }
        } finally {
            activeController = null;
        }
    }

    function cancelDownload() {
        activeController?.abort();
    }

    async function estimateStorage() {
        if (!navigator.storage?.estimate) return null;
        const estimate = await navigator.storage.estimate();
        const usage = Math.max(0, estimate.usage ?? 0);
        const quota = Math.max(0, estimate.quota ?? 0);
        return { usage, quota, available: Math.max(0, quota - usage) };
    }

    async function clearImages() {
        await caches.delete(imageCacheName);
    }

    async function countImages() {
        const cache = await caches.open(imageCacheName);
        return (await cache.keys()).length;
    }

    window.gibddOffline = { downloadImages, cancelDownload, estimateStorage, clearImages, countImages };
})();
