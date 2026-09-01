(() => {
    if (!('serviceWorker' in navigator)) return;

    let waitingWorker = null;
    let reloading = false;
    const banner = document.getElementById('pwa-update-banner');
    const updateButton = document.getElementById('pwa-update-button');

    function offerUpdate(worker) {
        waitingWorker = worker;
        if (banner) banner.hidden = false;
    }

    function watchInstalling(registration) {
        const worker = registration.installing;
        if (!worker) return;
        worker.addEventListener('statechange', () => {
            if (worker.state === 'installed' && navigator.serviceWorker.controller) {
                offerUpdate(registration.waiting ?? worker);
            }
        });
    }

    window.addEventListener('load', async () => {
        try {
            const registration = await navigator.serviceWorker.register('service-worker.js', {
                updateViaCache: 'none'
            });
            if (registration.waiting && navigator.serviceWorker.controller) {
                offerUpdate(registration.waiting);
            }
            registration.addEventListener('updatefound', () => watchInstalling(registration));
            await registration.update();
        } catch {
            // The application remains usable when service workers are unavailable.
        }
    });

    updateButton?.addEventListener('click', () => {
        updateButton.disabled = true;
        waitingWorker?.postMessage({ type: 'SKIP_WAITING' });
    });

    navigator.serviceWorker.addEventListener('controllerchange', () => {
        if (reloading) return;
        reloading = true;
        window.location.reload();
    });
})();
