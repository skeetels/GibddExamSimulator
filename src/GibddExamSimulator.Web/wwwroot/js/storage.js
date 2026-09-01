(() => {
    const databaseName = 'gibdd-study-v2';
    const databaseVersion = 2;
    const storeNames = ['meta', 'sessions', 'outbox', 'auth', 'drafts', 'profiles', 'sync', 'crypto'];
    const authKeyName = 'auth-aes-gcm-v1';
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

    function bytesToBase64(bytes) {
        let binary = '';
        for (const byte of bytes) binary += String.fromCharCode(byte);
        return btoa(binary);
    }

    function base64ToBytes(value) {
        const binary = atob(value);
        return Uint8Array.from(binary, character => character.charCodeAt(0));
    }

    async function getOrCreateAuthKey() {
        const existing = await get('crypto', authKeyName);
        if (existing) return existing;
        // Keep the per-origin key non-exportable; only IndexedDB's structured clone stores it.
        const generated = await crypto.subtle.generateKey(
            { name: 'AES-GCM', length: 256 },
            false,
            ['encrypt', 'decrypt']);
        await put('crypto', authKeyName, generated);
        return generated;
    }

    async function securePut(storeName, key, value) {
        if (storeName !== 'auth') throw new Error('Secure storage is restricted to authentication state.');
        const cryptoKey = await getOrCreateAuthKey();
        const iv = crypto.getRandomValues(new Uint8Array(12));
        const plaintext = new TextEncoder().encode(JSON.stringify(value));
        const encrypted = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, cryptoKey, plaintext);
        await put(storeName, key, {
            version: 1,
            algorithm: 'AES-GCM',
            iv: bytesToBase64(iv),
            ciphertext: bytesToBase64(new Uint8Array(encrypted))
        });
    }

    async function secureGet(storeName, key) {
        if (storeName !== 'auth') throw new Error('Secure storage is restricted to authentication state.');
        const stored = await get(storeName, key);
        if (!stored) return null;
        if (stored.version !== 1 || stored.algorithm !== 'AES-GCM' || !stored.iv || !stored.ciphertext) {
            // Migrate a 2.0.1 plaintext IndexedDB session in place once.
            await securePut(storeName, key, stored);
            return stored;
        }
        const cryptoKey = await getOrCreateAuthKey();
        const plaintext = await crypto.subtle.decrypt(
            { name: 'AES-GCM', iv: base64ToBytes(stored.iv) },
            cryptoKey,
            base64ToBytes(stored.ciphertext));
        return JSON.parse(new TextDecoder().decode(plaintext));
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

    async function mergeUserScope(sourceUserId, targetUserId) {
        const db = await openDatabase();
        const stores = ['sessions', 'outbox', 'drafts', 'profiles', 'sync'];
        const snapshots = {
            sessions: await getAll('sessions'),
            outbox: await getAll('outbox'),
            drafts: await get('drafts', sourceUserId),
            profiles: await get('profiles', sourceUserId),
            sync: await get('sync', sourceUserId)
        };
        await new Promise((resolve, reject) => {
            const tx = db.transaction(stores, 'readwrite');
            const sessionStore = tx.objectStore('sessions');
            for (const record of snapshots.sessions.filter(item => item.userId === sourceUserId)) {
                const updated = { ...record, key: `${targetUserId}:${record.sessionId}`, userId: targetUserId };
                sessionStore.put(updated, updated.key);
                sessionStore.delete(record.key);
            }
            const outboxStore = tx.objectStore('outbox');
            for (const record of snapshots.outbox.filter(item => item.userId === sourceUserId)) {
                const updated = { ...record, key: `${targetUserId}:${record.sessionId}`, userId: targetUserId };
                outboxStore.put(updated, updated.key);
                outboxStore.delete(record.key);
            }
            for (const name of ['drafts', 'profiles', 'sync']) {
                const store = tx.objectStore(name);
                const source = snapshots[name];
                if (source) store.put(source, targetUserId);
                store.delete(sourceUserId);
            }
            tx.oncomplete = resolve;
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error ?? new Error('User-scope merge aborted.'));
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
        secureGet,
        securePut,
        secureDelete: remove,
        saveCompletedSession,
        applyRemotePage,
        mergeUserScope
    };
})();
