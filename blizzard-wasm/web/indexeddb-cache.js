/**
 * IndexedDB wrapper for caching baseline data and scenarios
 * Part of Phase 2: Scenario Builder UI
 */

class BlizzardCache {
    constructor(dbName = 'blizzard-cache', version = 1) {
        this.dbName = dbName;
        this.version = version;
        this.db = null;
    }

    /**
     * Initialize the IndexedDB database
     * Creates object stores for baseline and scenarios
     */
    async init() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(this.dbName, this.version);

            request.onerror = () => reject(new Error(`Failed to open database: ${request.error}`));

            request.onsuccess = () => {
                this.db = request.result;
                console.log('BlizzardCache initialized successfully');
                resolve();
            };

            request.onupgradeneeded = (event) => {
                const db = event.target.result;

                // Store baseline data from server
                if (!db.objectStoreNames.contains('baseline')) {
                    const baselineStore = db.createObjectStore('baseline', { keyPath: 'id' });
                    console.log('Created baseline object store');
                }

                // Store scenarios
                if (!db.objectStoreNames.contains('scenarios')) {
                    const scenarioStore = db.createObjectStore('scenarios', { keyPath: 'id' });
                    console.log('Created scenarios object store');
                }
            };
        });
    }

    /**
     * Save baseline data with Last-Modified timestamp for cache validation
     * @param {Object} data - The baseline forecast data from server
     * @param {string} lastModified - Last-Modified header from server response
     */
    async saveBaseline(data, lastModified) {
        if (!this.db) {
            throw new Error('Database not initialized. Call init() first.');
        }

        const tx = this.db.transaction(['baseline'], 'readwrite');
        const store = tx.objectStore('baseline');

        const record = {
            id: 'current',
            data: data,
            lastModified: lastModified,
            cached: new Date().toISOString()
        };

        return new Promise((resolve, reject) => {
            const request = store.put(record);
            request.onsuccess = () => {
                console.log('Baseline data cached successfully');
                resolve();
            };
            request.onerror = () => reject(new Error(`Failed to save baseline: ${request.error}`));
        });
    }

    /**
     * Get cached baseline data
     * @returns {Object|null} Baseline record or null if not cached
     */
    async getBaseline() {
        if (!this.db) {
            throw new Error('Database not initialized. Call init() first.');
        }

        const tx = this.db.transaction(['baseline'], 'readonly');
        const store = tx.objectStore('baseline');

        return new Promise((resolve, reject) => {
            const request = store.get('current');
            request.onsuccess = () => resolve(request.result || null);
            request.onerror = () => reject(new Error(`Failed to get baseline: ${request.error}`));
        });
    }

    /**
     * Save a scenario
     * @param {Object} scenario - Scenario object with id, name, adjustments, etc.
     */
    async saveScenario(scenario) {
        if (!this.db) {
            throw new Error('Database not initialized. Call init() first.');
        }

        const tx = this.db.transaction(['scenarios'], 'readwrite');
        const store = tx.objectStore('scenarios');

        return new Promise((resolve, reject) => {
            const request = store.put(scenario);
            request.onsuccess = () => {
                console.log(`Scenario '${scenario.name}' saved successfully`);
                resolve();
            };
            request.onerror = () => reject(new Error(`Failed to save scenario: ${request.error}`));
        });
    }

    /**
     * Get a scenario by ID
     * @param {string} id - Scenario UUID
     * @returns {Object|null} Scenario object or null if not found
     */
    async getScenario(id) {
        if (!this.db) {
            throw new Error('Database not initialized. Call init() first.');
        }

        const tx = this.db.transaction(['scenarios'], 'readonly');
        const store = tx.objectStore('scenarios');

        return new Promise((resolve, reject) => {
            const request = store.get(id);
            request.onsuccess = () => resolve(request.result || null);
            request.onerror = () => reject(new Error(`Failed to get scenario: ${request.error}`));
        });
    }

    /**
     * Get all scenarios
     * @returns {Array} Array of scenario objects
     */
    async getAllScenarios() {
        if (!this.db) {
            throw new Error('Database not initialized. Call init() first.');
        }

        const tx = this.db.transaction(['scenarios'], 'readonly');
        const store = tx.objectStore('scenarios');

        return new Promise((resolve, reject) => {
            const request = store.getAll();
            request.onsuccess = () => resolve(request.result || []);
            request.onerror = () => reject(new Error(`Failed to get scenarios: ${request.error}`));
        });
    }

    /**
     * Delete a scenario
     * @param {string} id - Scenario UUID
     */
    async deleteScenario(id) {
        if (!this.db) {
            throw new Error('Database not initialized. Call init() first.');
        }

        const tx = this.db.transaction(['scenarios'], 'readwrite');
        const store = tx.objectStore('scenarios');

        return new Promise((resolve, reject) => {
            const request = store.delete(id);
            request.onsuccess = () => {
                console.log(`Scenario ${id} deleted successfully`);
                resolve();
            };
            request.onerror = () => reject(new Error(`Failed to delete scenario: ${request.error}`));
        });
    }

    /**
     * Clear all cached data (baseline and scenarios)
     * Useful for troubleshooting or manual cache refresh
     */
    async clearAll() {
        if (!this.db) {
            throw new Error('Database not initialized. Call init() first.');
        }

        const tx = this.db.transaction(['baseline', 'scenarios'], 'readwrite');

        return new Promise((resolve, reject) => {
            const baselineClear = tx.objectStore('baseline').clear();
            const scenarioClear = tx.objectStore('scenarios').clear();

            tx.oncomplete = () => {
                console.log('All cache cleared successfully');
                resolve();
            };
            tx.onerror = () => reject(new Error(`Failed to clear cache: ${tx.error}`));
        });
    }

    /**
     * Check if database is initialized
     * @returns {boolean}
     */
    isInitialized() {
        return this.db !== null;
    }
}

// Export for ES6 modules
export default BlizzardCache;
