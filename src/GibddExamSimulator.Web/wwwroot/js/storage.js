(() => {
    const databaseName = 'gibdd-study-v2';
    const databaseVersion = 1;
    const storeNames = ['meta', 'sessions', 'outbox', 'auth', 'drafts', 'profiles', 'sync'];
    let databasePromise;

    function openDatabase() {
        if (databasePromise) return databasePromise;
        databasePromise = new Promise((resolve, reject) => {
            const request = indexedDB.open(databaseName, databaseVersion);
            request.onupgradeneeded = () => {
                const db = request.result;
                for (const name of storeNames) {
                    if (!db.objectStoreNames.contains(name)) db.createObjectStore(name);
                }
            };
            request.onsuccess = () => {
                resolve(request.result);
            };
            request.onerror = () => reject(request.error);
            request.onblocked = () => reject(new Error('IndexedDB upgrade is blocked by another application tab.'));
        });
        return databasePromise;
    }

    function requestResult(request) {
        return new Promise((resolve, reject) => {
            request.onsuccess = () => resolve(request.result ?? null);
            request.onerror = () => reject(request.error);
        });
    }

    async function transaction(storeName, mode, callback) {
        const db = await openDatabase();
        const tx = db.transaction(storeName, mode);
        const store = tx.objectStore(storeName);
        // Subscribe before awaiting the request: fast IndexedDB transactions may
        // complete between request success and a late oncomplete assignment.
        const completion = new Promise((resolve, reject) => {
            tx.oncomplete = resolve;
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error ?? new Error('IndexedDB transaction aborted.'));
        });
        const result = await callback(store);
        await completion;
        return result;
    }

    async function get(storeName, key) {
        return transaction(storeName, 'readonly', store => requestResult(store.get(key)));
    }

    async function put(storeName, key, value) {
        return transaction(storeName, 'readwrite', store => requestResult(store.put(value, key)));
    }

    async function remove(storeName, key) {
        return transaction(storeName, 'readwrite', store => requestResult(store.delete(key)));
    }

    async function getAll(storeName) {
        return transaction(storeName, 'readonly', store => requestResult(store.getAll()));
    }

    async function saveCompletedSession(sessionRecord, outboxRecord) {
        const db = await openDatabase();
        await new Promise((resolve, reject) => {
            const tx = db.transaction(['sessions', 'outbox'], 'readwrite');
            tx.objectStore('sessions').put(sessionRecord, sessionRecord.key);
            tx.objectStore('outbox').put(outboxRecord, outboxRecord.key);
            tx.oncomplete = resolve;
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error ?? new Error('Atomic session save aborted.'));
        });
    }

    async function applyRemotePage(sessionRecords, syncKey, syncRecord) {
        const db = await openDatabase();
        await new Promise((resolve, reject) => {
            const tx = db.transaction(['sessions', 'sync'], 'readwrite');
            const sessions = tx.objectStore('sessions');
            for (const record of sessionRecords) sessions.put(record, record.key);
            tx.objectStore('sync').put(syncRecord, syncKey);
            tx.oncomplete = resolve;
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error ?? new Error('Atomic remote page save aborted.'));
        });
    }

    window.gibddStorage = {
        initialize: async () => {
            await openDatabase();
            return true;
        },
        get,
        put,
        delete: remove,
        getAll,
        saveCompletedSession,
        applyRemotePage
    };
})();
