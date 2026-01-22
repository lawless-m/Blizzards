/**
 * Scenario Builder Main Application
 * Part of Phase 2: Scenario Builder UI
 */

import BlizzardCache from './indexeddb-cache.js';
import {
    createScenario,
    createAdjustment,
    applyAdjustments,
    getAdjustmentSummary,
    validateAdjustment
} from './scenario-model.js';

// Import WASM module
import init, { forecast } from '../pkg/blizzard_wasm.js';

// Global state
let cache = null;
let wasmInitialized = false;
let currentScenarioId = null;
let scenarios = [];
let baselineData = null;
let baselineForecast = null;

/**
 * Initialize the application
 */
async function initApp() {
    try {
        showLoading('Initializing...');

        // Initialize IndexedDB
        cache = new BlizzardCache();
        await cache.init();
        console.log('IndexedDB initialized');

        // Initialize WASM
        await init();
        wasmInitialized = true;
        console.log('WASM module initialized');

        // Load baseline data
        await loadBaselineData();

        // Load scenarios from cache
        scenarios = await cache.getAllScenarios();
        console.log(`Loaded ${scenarios.length} scenarios from cache`);

        // Render UI
        renderScenarioList();
        selectScenario(null); // Start with baseline

        // Set up event listeners
        setupEventListeners();

        hideLoading();
    } catch (error) {
        console.error('Initialization error:', error);
        alert(`Failed to initialize: ${error.message}`);
        hideLoading();
    }
}

/**
 * Load baseline data from server or cache
 */
async function loadBaselineData() {
    const cached = await cache.getBaseline();

    if (cached) {
        console.log('Using cached baseline data');
        baselineData = cached.data;

        // TODO: Check if cache is stale by comparing Last-Modified header
        // For now, we'll use the cache
    } else {
        console.log('No cached data, using sample data for development');
        // For development, create sample baseline data
        baselineData = createSampleBaselineData();
        await cache.saveBaseline(baselineData, new Date().toISOString());
    }

    // Calculate baseline forecast
    if (baselineData.overall && baselineData.overall.historical) {
        baselineForecast = calculateForecast(baselineData);
    }
}

/**
 * Create sample baseline data for development
 */
function createSampleBaselineData() {
    // Generate 36 months of sample historical data with seasonality
    const historicalData = [];
    const startYear = 2022;
    const startMonth = 1;

    for (let i = 0; i < 36; i++) {
        const year = startYear + Math.floor(i / 12);
        const month = ((startMonth - 1 + i) % 12) + 1;

        // Base trend with seasonality
        const trend = 1000 + (i * 20); // Growing trend
        const seasonal = Math.sin((month / 12) * 2 * Math.PI) * 200; // Seasonal variation
        const noise = (Math.random() - 0.5) * 100; // Random noise

        historicalData.push(trend + seasonal + noise);
    }

    return {
        overall: {
            historical: {
                rows: historicalData.map((value, i) => {
                    const month = ((startMonth - 1 + i) % 12) + 1;
                    const year = startYear + Math.floor(i / 12);
                    return [`${year}-${month.toString().padStart(2, '0')}`, value];
                })
            }
        },
        customers: ['ACME Corp', 'BigCo Industries', 'MegaRetail', 'SuperStore'],
        product_groups: ['S5', 'S1', 'S2'],
        geographies: ['MEAEDU', 'MEAEAR', 'EUR']
    };
}

/**
 * Calculate forecast using WASM module
 */
function calculateForecast(data, adjustments = []) {
    if (!wasmInitialized) {
        throw new Error('WASM not initialized');
    }

    // Apply adjustments to data
    const adjustedData = adjustments.length > 0
        ? applyAdjustments(data, adjustments)
        : data;

    // Extract time series for ARIMA
    const series = adjustedData.overall.historical.rows.map(r => r[1]);

    // Parse start date from first row
    const [startDate] = adjustedData.overall.historical.rows[0];
    const [startYear, startMonth] = startDate.split('-').map(Number);

    // Prepare input for WASM
    const input = {
        series: series,
        start_year: startYear,
        start_month: startMonth,
        forecast_months: 12,
        use_easter: true
    };

    // Call WASM forecast function
    const resultJson = forecast(JSON.stringify(input));
    const result = JSON.parse(resultJson);

    return result;
}

/**
 * Render scenario list
 */
function renderScenarioList() {
    const container = document.getElementById('scenario-items');
    container.innerHTML = '';

    // Baseline (always present)
    const baselineItem = document.createElement('div');
    baselineItem.className = 'scenario-item baseline';
    if (currentScenarioId === null) {
        baselineItem.classList.add('active');
    }
    baselineItem.textContent = 'Baseline (no adjustments)';
    baselineItem.onclick = () => selectScenario(null);
    container.appendChild(baselineItem);

    // User scenarios
    for (const scenario of scenarios) {
        const item = document.createElement('div');
        item.className = 'scenario-item';
        if (currentScenarioId === scenario.id) {
            item.classList.add('active');
        }
        item.textContent = scenario.name;
        item.onclick = () => selectScenario(scenario.id);
        container.appendChild(item);
    }
}

/**
 * Select a scenario
 */
async function selectScenario(scenarioId) {
    currentScenarioId = scenarioId;

    // Update UI
    renderScenarioList();

    if (scenarioId === null) {
        // Baseline selected
        document.getElementById('current-scenario-name').textContent = 'Baseline (no adjustments)';
        document.getElementById('rename-scenario-btn').style.display = 'none';
        document.getElementById('delete-scenario-btn').style.display = 'none';
        document.getElementById('add-adjustment-btn').style.display = 'none';

        document.getElementById('adjustment-items').innerHTML =
            '<p class="empty-state">No adjustments. This is the baseline forecast.</p>';
    } else {
        // User scenario selected
        const scenario = scenarios.find(s => s.id === scenarioId);
        if (!scenario) return;

        document.getElementById('current-scenario-name').textContent = scenario.name;
        document.getElementById('rename-scenario-btn').style.display = 'inline-block';
        document.getElementById('delete-scenario-btn').style.display = 'inline-block';
        document.getElementById('add-adjustment-btn').style.display = 'inline-block';

        renderAdjustmentList(scenario.adjustments);
    }

    // Recalculate forecast
    await recalculateScenario();
}

/**
 * Render adjustment list
 */
function renderAdjustmentList(adjustments) {
    const container = document.getElementById('adjustment-items');

    if (!adjustments || adjustments.length === 0) {
        container.innerHTML = '<p class="empty-state">No adjustments yet. Click "Add Adjustment" to create one.</p>';
        return;
    }

    container.innerHTML = '';

    for (const adj of adjustments) {
        const item = document.createElement('div');
        item.className = 'adjustment-item';

        let icon = '';
        let summary = '';

        if (adj.type === 'scale') {
            icon = '📊';
            summary = `<strong>${icon} Scale:</strong> ${adj.target_key} → ${(adj.factor * 100).toFixed(0)}%`;
        } else if (adj.type === 'remove') {
            icon = '📉';
            summary = `<strong>${icon} Remove:</strong> ${adj.target_key}`;
        } else if (adj.type === 'new_business') {
            icon = '➕';
            const year1k = (adj.year1_value / 1000).toFixed(0);
            summary = `<strong>${icon} New Business:</strong> ${adj.product_group} in ${adj.geography}<br>
                       Start: ${adj.start_month}, Y1: £${year1k}k`;
        }

        item.innerHTML = `
            ${summary}
            ${adj.note ? `<br><em>${adj.note}</em>` : ''}
            <div class="adjustment-actions">
                <button onclick="window.editAdjustment('${adj.id}')">✏️</button>
                <button onclick="window.deleteAdjustment('${adj.id}')">❌</button>
            </div>
        `;

        container.appendChild(item);
    }
}

/**
 * Recalculate scenario forecast
 */
async function recalculateScenario() {
    try {
        showLoading('Calculating forecast...');

        // Get current scenario adjustments
        let adjustments = [];
        if (currentScenarioId !== null) {
            const scenario = scenarios.find(s => s.id === currentScenarioId);
            if (scenario) {
                adjustments = scenario.adjustments;
            }
        }

        // Calculate forecast
        const scenarioForecast = calculateForecast(baselineData, adjustments);

        // Update summary
        updateSummary(baselineForecast, scenarioForecast);

        // Update chart
        updateChart(baselineForecast, scenarioForecast);

        hideLoading();
    } catch (error) {
        console.error('Forecast calculation error:', error);
        alert(`Failed to calculate forecast: ${error.message}`);
        hideLoading();
    }
}

/**
 * Update summary section
 */
function updateSummary(baseline, scenario) {
    const baselineTotal = baseline.forecast.reduce((a, b) => a + b, 0);
    const scenarioTotal = scenario.forecast.reduce((a, b) => a + b, 0);
    const delta = scenarioTotal - baselineTotal;
    const deltaPercent = (delta / baselineTotal * 100);

    document.getElementById('baseline-total').textContent =
        `£${(baselineTotal / 1000).toFixed(1)}k`;
    document.getElementById('scenario-total').textContent =
        `£${(scenarioTotal / 1000).toFixed(1)}k`;

    const deltaEl = document.getElementById('delta-value');
    deltaEl.textContent =
        `${delta >= 0 ? '+' : ''}£${(delta / 1000).toFixed(1)}k (${deltaPercent >= 0 ? '+' : ''}${deltaPercent.toFixed(1)}%)`;
    deltaEl.className = 'summary-value summary-delta ' + (delta >= 0 ? 'positive' : 'negative');
}

/**
 * Update chart with baseline and scenario forecasts
 */
function updateChart(baseline, scenario) {
    const container = document.getElementById('chart-container');

    // Simple text-based chart for now
    // TODO: Integrate with D3.js for proper visualization
    let html = '<div style="padding: 20px;">';
    html += '<h4>Forecast Comparison (12 months)</h4>';
    html += '<table style="width:100%; border-collapse: collapse; margin-top: 15px;">';
    html += '<tr style="border-bottom: 2px solid #ddd;"><th style="text-align:left; padding:8px;">Month</th><th style="text-align:right; padding:8px;">Baseline</th><th style="text-align:right; padding:8px;">Scenario</th><th style="text-align:right; padding:8px;">Delta</th></tr>';

    for (let i = 0; i < 12; i++) {
        const b = baseline.forecast[i];
        const s = scenario.forecast[i];
        const delta = s - b;
        const deltaPercent = (delta / b * 100).toFixed(1);

        html += `<tr style="border-bottom: 1px solid #eee;">
            <td style="padding:8px;">Month ${i + 1}</td>
            <td style="text-align:right; padding:8px;">£${(b / 1000).toFixed(1)}k</td>
            <td style="text-align:right; padding:8px;">£${(s / 1000).toFixed(1)}k</td>
            <td style="text-align:right; padding:8px; color:${delta >= 0 ? 'green' : 'red'}">
                ${delta >= 0 ? '+' : ''}£${(delta / 1000).toFixed(1)}k (${deltaPercent}%)
            </td>
        </tr>`;
    }

    html += '</table></div>';
    container.innerHTML = html;
}

/**
 * Set up event listeners
 */
function setupEventListeners() {
    // New scenario button
    document.getElementById('new-scenario-btn').addEventListener('click', () => {
        showNewScenarioModal();
    });

    // Add adjustment button
    document.getElementById('add-adjustment-btn').addEventListener('click', () => {
        showAdjustmentModal();
    });

    // Delete scenario button
    document.getElementById('delete-scenario-btn').addEventListener('click', async () => {
        if (confirm('Are you sure you want to delete this scenario?')) {
            await deleteCurrentScenario();
        }
    });

    // Adjustment type selector
    document.getElementById('adjustment-type').addEventListener('change', (e) => {
        updateAdjustmentFieldsVisibility(e.target.value);
    });

    // Modal controls - New Scenario
    document.getElementById('new-scenario-close-btn').addEventListener('click', () => {
        document.getElementById('new-scenario-modal').close();
    });

    document.getElementById('cancel-new-scenario-btn').addEventListener('click', () => {
        document.getElementById('new-scenario-modal').close();
    });

    document.getElementById('create-scenario-btn').addEventListener('click', () => {
        createNewScenario();
    });

    // Modal controls - Adjustment
    document.getElementById('modal-close-btn').addEventListener('click', () => {
        document.getElementById('adjustment-modal').close();
    });

    document.getElementById('cancel-adjustment-btn').addEventListener('click', () => {
        document.getElementById('adjustment-modal').close();
    });

    document.getElementById('save-adjustment-btn').addEventListener('click', () => {
        saveAdjustment();
    });

    // Populate target search
    document.getElementById('scale-target-search').addEventListener('input', (e) => {
        filterTargets(e.target.value);
    });
}

/**
 * Show new scenario modal
 */
function showNewScenarioModal() {
    document.getElementById('new-scenario-name').value = '';
    document.getElementById('new-scenario-modal').showModal();
    document.getElementById('new-scenario-name').focus();
}

/**
 * Create new scenario
 */
async function createNewScenario() {
    const name = document.getElementById('new-scenario-name').value.trim();

    if (!name) {
        alert('Please enter a scenario name');
        return;
    }

    const scenario = createScenario(name);
    scenarios.push(scenario);
    await cache.saveScenario(scenario);

    document.getElementById('new-scenario-modal').close();
    renderScenarioList();
    selectScenario(scenario.id);
}

/**
 * Delete current scenario
 */
async function deleteCurrentScenario() {
    if (currentScenarioId === null) return;

    await cache.deleteScenario(currentScenarioId);
    scenarios = scenarios.filter(s => s.id !== currentScenarioId);

    renderScenarioList();
    selectScenario(null);
}

/**
 * Show adjustment modal
 */
function showAdjustmentModal(editId = null) {
    // Reset form
    document.getElementById('adjustment-type').value = 'scale';
    updateAdjustmentFieldsVisibility('scale');
    document.getElementById('modal-errors').style.display = 'none';

    // Populate target list
    populateTargetList('customer');

    document.getElementById('adjustment-modal').showModal();
}

/**
 * Update adjustment fields visibility based on type
 */
function updateAdjustmentFieldsVisibility(type) {
    const scaleFields = document.getElementById('scale-fields');
    const newBusinessFields = document.getElementById('new-business-fields');
    const scaleFactorGroup = document.getElementById('scale-factor-group');

    if (type === 'scale') {
        scaleFields.style.display = 'block';
        newBusinessFields.style.display = 'none';
        scaleFactorGroup.style.display = 'block';
    } else if (type === 'remove') {
        scaleFields.style.display = 'block';
        newBusinessFields.style.display = 'none';
        scaleFactorGroup.style.display = 'none';
    } else if (type === 'new_business') {
        scaleFields.style.display = 'none';
        newBusinessFields.style.display = 'block';
    }
}

/**
 * Populate target list for scale/remove adjustments
 */
function populateTargetList(targetType) {
    const select = document.getElementById('scale-target-select');
    select.innerHTML = '';

    let items = [];
    if (targetType === 'customer' && baselineData.customers) {
        items = baselineData.customers;
    } else if (targetType === 'product_group' && baselineData.product_groups) {
        items = baselineData.product_groups;
    } else if (targetType === 'geography' && baselineData.geographies) {
        items = baselineData.geographies;
    }

    for (const item of items) {
        const option = document.createElement('option');
        option.value = item;
        option.textContent = item;
        select.appendChild(option);
    }
}

/**
 * Filter targets based on search
 */
function filterTargets(searchText) {
    const targetType = document.getElementById('scale-target-type').value;
    populateTargetList(targetType);

    if (!searchText) return;

    const select = document.getElementById('scale-target-select');
    const options = Array.from(select.options);

    for (const option of options) {
        if (!option.textContent.toLowerCase().includes(searchText.toLowerCase())) {
            option.style.display = 'none';
        }
    }
}

/**
 * Save adjustment
 */
async function saveAdjustment() {
    const type = document.getElementById('adjustment-type').value;
    let params = {};

    if (type === 'scale' || type === 'remove') {
        params = {
            target_type: document.getElementById('scale-target-type').value,
            target_key: document.getElementById('scale-target-select').value,
            factor: type === 'remove' ? 0 : (parseFloat(document.getElementById('scale-factor').value) / 100),
            note: document.getElementById('scale-note').value
        };
    } else if (type === 'new_business') {
        params = {
            product_group: document.getElementById('nb-product-group').value,
            geography: document.getElementById('nb-geography').value,
            start_month: document.getElementById('nb-start-month').value,
            year1_value: parseFloat(document.getElementById('nb-year1').value),
            year2_value: parseFloat(document.getElementById('nb-year2').value),
            year3_value: parseFloat(document.getElementById('nb-year3').value),
            note: document.getElementById('nb-note').value
        };
    }

    // Validate
    const validation = validateAdjustment(type, params);
    if (!validation.valid) {
        showValidationErrors(validation.errors);
        return;
    }

    // Create adjustment
    const adjustment = createAdjustment(type, params);

    // Add to current scenario
    const scenario = scenarios.find(s => s.id === currentScenarioId);
    if (!scenario) return;

    scenario.adjustments.push(adjustment);
    scenario.modified = new Date().toISOString();
    await cache.saveScenario(scenario);

    document.getElementById('adjustment-modal').close();

    renderAdjustmentList(scenario.adjustments);
    await recalculateScenario();
}

/**
 * Show validation errors in modal
 */
function showValidationErrors(errors) {
    const errorDiv = document.getElementById('modal-errors');
    errorDiv.innerHTML = '<ul>' + errors.map(e => `<li>${e}</li>`).join('') + '</ul>';
    errorDiv.style.display = 'block';
}

/**
 * Delete adjustment (called from rendered HTML)
 */
window.deleteAdjustment = async function(adjustmentId) {
    if (!confirm('Delete this adjustment?')) return;

    const scenario = scenarios.find(s => s.id === currentScenarioId);
    if (!scenario) return;

    scenario.adjustments = scenario.adjustments.filter(a => a.id !== adjustmentId);
    scenario.modified = new Date().toISOString();
    await cache.saveScenario(scenario);

    renderAdjustmentList(scenario.adjustments);
    await recalculateScenario();
};

/**
 * Edit adjustment (called from rendered HTML)
 */
window.editAdjustment = function(adjustmentId) {
    // TODO: Implement edit functionality
    alert('Edit functionality coming soon!');
};

/**
 * Show loading overlay
 */
function showLoading(message = 'Loading...') {
    const overlay = document.getElementById('loading-overlay');
    overlay.querySelector('p').textContent = message;
    overlay.style.display = 'flex';
}

/**
 * Hide loading overlay
 */
function hideLoading() {
    document.getElementById('loading-overlay').style.display = 'none';
}

// Initialize app when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initApp);
} else {
    initApp();
}
