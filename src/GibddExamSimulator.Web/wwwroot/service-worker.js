const imageCacheName = 'gibdd-question-images-v1';

self.addEventListener('message', event => {
    if (event.data?.type === 'SKIP_WAITING') self.skipWaiting();
});

self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);
    if (event.request.method !== 'GET' || !url.pathname.includes('/question-bank/ab/images/')) return;
    event.respondWith((async () => {
        const cache = await caches.open(imageCacheName);
        const cached = await cache.match(event.request);
        if (cached) return cached;
        const response = await fetch(event.request);
        if (response.ok) await cache.put(event.request, response.clone());
        return response;
    })());
});
