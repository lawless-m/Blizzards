# Blizzard WASM Scenario Builder - Implementation Plan

**Created**: 2026-01-22
**Branch**: claude/review-project-spec-Cr18N
**Status**: Development Ready

---

## Quick Start

### Prerequisites

```bash
# Install Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# Install wasm-pack
cargo install wasm-pack

# Install Node.js (for local testing)
# https://nodejs.org/
```

### Initial Setup

```bash
# Create Rust project
cargo new --lib blizzard-wasm
cd blizzard-wasm

# Configure Cargo.toml for WASM
# (see below for configuration)

# Build
wasm-pack build --target web --release
```

---

## Phase 1: ARIMA Port - Step-by-Step Implementation

### Step 1.1: Project Setup (Day 1)

**File**: `Cargo.toml`

```toml
[package]
name = "blizzard-wasm"
version = "0.1.0"
edition = "2021"

[lib]
crate-type = ["cdylib", "rlib"]

[dependencies]
wasm-bindgen = "0.2"
serde = { version = "1.0", features = ["derive"] }
serde_json = "1.0"

[dev-dependencies]
rand = "0.8"

[profile.release]
opt-level = "s"          # Optimize for size
lto = true               # Link-time optimization
codegen-units = 1        # Better optimization
panic = "abort"          # Smaller binary
```

**Action Items**:
- [ ] Create `blizzard-wasm` directory
- [ ] Copy scaffolded `src/` files from spec
- [ ] Configure Cargo.toml as shown above
- [ ] Run `cargo build` to verify setup
- [ ] Run `wasm-pack build --target web` to verify WASM compilation

### Step 1.2: Easter Calculation (Day 1)

**File**: `src/easter.rs`

The spec includes complete Easter calculation code. Copy from `blizzard-wasm-spec/src/easter.rs` and implement:

```rust
pub fn easter_sunday(year: i32) -> (u32, u32)
pub fn easter_invoice_month(easter_year: i32) -> (i32, u32)
pub fn create_easter_regressor(start_year: i32, start_month: u32, length: usize) -> Vec<f64>
```

**Tests to write**:
```rust
#[test]
fn test_easter_dates_2024_2030() {
    // Known Easter dates
    assert_eq!(easter_sunday(2024), (3, 31));
    assert_eq!(easter_sunday(2025), (4, 20));
    assert_eq!(easter_sunday(2026), (4, 5));
}

#[test]
fn test_easter_invoice_months() {
    // Easter Apr 20, 2025 → invoice month Jan 2025
    assert_eq!(easter_invoice_month(2025), (2025, 1));
    // Easter Mar 31, 2024 → invoice month Dec 2023
    assert_eq!(easter_invoice_month(2024), (2023, 12));
}

#[test]
fn test_easter_regressor_array() {
    let regressor = create_easter_regressor(2023, 1, 36); // 3 years
    // Count should match number of Easters in period
    let count = regressor.iter().filter(|&&x| x > 0.5).count();
    assert_eq!(count, 3); // 2023, 2024, 2025
}
```

**Action Items**:
- [ ] Implement `easter.rs` functions
- [ ] Write and pass all Easter tests
- [ ] Document any differences from spec

### Step 1.3: Statistical Helpers (Days 2-3)

**File**: `src/stats.rs`

Implement in this order (each with tests):

#### 1. Mean
```rust
pub fn mean(data: &[f64]) -> f64 {
    if data.is_empty() { return 0.0; }
    data.iter().sum::<f64>() / data.len() as f64
}

#[test]
fn test_mean() {
    assert_eq!(mean(&[1.0, 2.0, 3.0, 4.0, 5.0]), 3.0);
    assert_eq!(mean(&[]), 0.0);
}
```

#### 2. Autocorrelation
```rust
pub fn autocorrelation(series: &[f64], max_lag: usize) -> Vec<f64>
```

Reference: `ARIMA_ALGORITHM.md` lines 273-295

**Tests**:
- AR(1) process with known ρ should produce declining autocorrelation
- White noise should produce near-zero autocorrelation at lag > 0

#### 3. Difference
```rust
pub fn difference(series: &[f64], d: usize) -> Vec<f64>
```

Already implemented in `arima.rs` scaffold - just test it.

**Tests**:
```rust
#[test]
fn test_first_difference() {
    let series = vec![10.0, 12.0, 15.0, 14.0, 18.0];
    let diff = difference(&series, 1);
    assert_eq!(diff, vec![2.0, 3.0, -1.0, 4.0]);
}

#[test]
fn test_second_difference() {
    let series = vec![1.0, 3.0, 6.0, 10.0, 15.0];
    let diff = difference(&series, 2);
    // First diff: [2, 3, 4, 5]
    // Second diff: [1, 1, 1]
    assert_eq!(diff, vec![1.0, 1.0, 1.0]);
}
```

#### 4. Undifference
```rust
pub fn undifference(differenced: &[f64], original: &[f64], d: usize) -> Vec<f64>
```

Reference: `ARIMA_ALGORITHM.md` lines 255-269

**Tests**:
```rust
#[test]
fn test_difference_undifference_roundtrip() {
    let original = vec![10.0, 12.0, 15.0, 14.0, 18.0, 20.0];
    let diff = difference(&original, 1);
    let reconstructed = undifference(&diff, &original, 1);

    // Should match original from index 1 onward
    for i in 0..reconstructed.len() {
        assert!((reconstructed[i] - original[i + 1]).abs() < 1e-10);
    }
}
```

**Action Items**:
- [ ] Implement all functions in `stats.rs`
- [ ] Write comprehensive tests for each
- [ ] Verify numerical stability with edge cases (zeros, negatives, large numbers)

### Step 1.4: Seasonal Decomposition (Days 3-4)

**File**: `src/seasonal.rs`

#### 1. Calculate Seasonal Factors
```rust
pub fn calculate_seasonal_factors(series: &[f64], period: usize) -> Vec<f64>
```

Reference: `ARIMA_ALGORITHM.md` lines 188-217

**Algorithm**:
1. Calculate average value for each month (only positive values)
2. Calculate overall average
3. Factor[month] = month_average / overall_average

**Tests**:
```rust
#[test]
fn test_seasonal_factors_perfect_pattern() {
    // Perfect 12-month pattern repeating
    let series: Vec<f64> = (0..36)
        .map(|i| {
            let month = i % 12;
            100.0 * (1.0 + 0.5 * (month as f64 / 12.0))
        })
        .collect();

    let factors = calculate_seasonal_factors(&series, 12);
    assert_eq!(factors.len(), 12);

    // Factors should sum to 12.0 (average of 1.0)
    let sum: f64 = factors.iter().sum();
    assert!((sum - 12.0).abs() < 0.1);
}

#[test]
fn test_seasonal_factors_with_zeros() {
    let mut series = vec![100.0; 24];
    series[0] = 0.0;  // Zero value should be ignored
    series[12] = 0.0;

    let factors = calculate_seasonal_factors(&series, 12);
    // Month 0 should still get a reasonable factor (based on other months)
    assert!(factors[0] > 0.5 && factors[0] < 1.5);
}
```

#### 2. Deseasonalize
```rust
pub fn deseasonalize(series: &[f64], factors: &[f64]) -> Vec<f64>
```

Already implemented in `arima.rs` scaffold.

#### 3. Reseasonalize
```rust
pub fn reseasonalize(series: &[f64], factors: &[f64], start_idx: usize) -> Vec<f64>
```

Already implemented in `arima.rs` scaffold.

**Tests**:
```rust
#[test]
fn test_deseasonalize_reseasonalize_roundtrip() {
    let series = vec![90.0, 100.0, 110.0, 120.0];
    let factors = vec![0.9, 1.0, 1.1, 1.2];

    let deseas = deseasonalize(&series, &factors);
    let reseas = reseasonalize(&deseas, &factors, 0);

    for i in 0..series.len() {
        assert!((reseas[i] - series[i]).abs() < 1e-10);
    }
}
```

**Action Items**:
- [ ] Implement seasonal decomposition functions
- [ ] Write tests for perfect patterns, edge cases
- [ ] Verify factors sum to period (multiplicative property)

### Step 1.5: Levinson-Durbin Algorithm (Days 5-6)

**File**: `src/yule_walker.rs`

This is the most critical and numerically sensitive component.

```rust
pub fn solve_yule_walker(autocorr: &[f64]) -> Vec<f64>
```

Reference: `ARIMA_ALGORITHM.md` lines 297-339

**Implementation Notes**:
- `v` (variance) must not go below `1e-10` - add stability check
- Reflection coefficients should be in [-1, 1] for stable AR process
- Copy `phi_prev` before updating `phi` - order matters

**Tests**:
```rust
#[test]
fn test_yule_walker_ar1() {
    // AR(1): x[t] = 0.5 * x[t-1] + ε
    // Theoretical autocorrelation: ρ[k] = 0.5^k
    let autocorr = vec![1.0, 0.5];
    let phi = solve_yule_walker(&autocorr);

    assert_eq!(phi.len(), 1);
    assert!((phi[0] - 0.5).abs() < 1e-6);
}

#[test]
fn test_yule_walker_ar2() {
    // AR(2): x[t] = 0.5 * x[t-1] + 0.3 * x[t-2] + ε
    // Generate autocorrelation from known process
    let autocorr = vec![1.0, 0.625, 0.5375]; // Computed from AR(2) equations
    let phi = solve_yule_walker(&autocorr);

    assert_eq!(phi.len(), 2);
    assert!((phi[0] - 0.5).abs() < 1e-3);
    assert!((phi[1] - 0.3).abs() < 1e-3);
}

#[test]
fn test_yule_walker_numerical_stability() {
    // Near-zero variance case
    let autocorr = vec![1.0, 0.99999, 0.99998]; // Nearly constant
    let phi = solve_yule_walker(&autocorr);

    // Should not panic, should produce reasonable coefficients
    assert!(phi.iter().all(|&x| x.is_finite()));
}
```

**Validation Strategy**:
1. Test with known AR(1) and AR(2) processes
2. Compare with C# output on same autocorrelation inputs
3. Check stability bounds (coefficients in reasonable range)

**Action Items**:
- [ ] Implement Levinson-Durbin carefully, following spec exactly
- [ ] Add numerical stability checks (variance floor, finite checks)
- [ ] Test with known AR processes
- [ ] Generate test fixtures from C# code for comparison

### Step 1.6: Exogenous Regression (Day 7)

**File**: `src/exogenous.rs`

```rust
pub fn regress_out_exogenous(series: &[f64], exog: &[f64]) -> (Vec<f64>, f64)
```

Reference: `ARIMA_ALGORITHM.md` lines 135-185

**Algorithm**:
1. Separate observations where exog=1 vs exog=0
2. coefficient = mean(Y where X=1) - mean(Y where X=0)
3. Subtract coefficient from affected observations

**Tests**:
```rust
#[test]
fn test_regress_out_exogenous_known_coefficient() {
    // Base values: 100, 110, 120, 130, 140, 150
    // Easter effect: +50 at indices 1 and 4
    let series = vec![100.0, 160.0, 120.0, 130.0, 190.0, 150.0];
    let exog = vec![0.0, 1.0, 0.0, 0.0, 1.0, 0.0];

    let (residuals, coef) = regress_out_exogenous(&series, &exog);

    // Coefficient should be approximately 50
    assert!((coef - 50.0).abs() < 5.0);

    // Residuals should have Easter effect removed
    assert!((residuals[1] - 110.0).abs() < 5.0);
    assert!((residuals[4] - 140.0).abs() < 5.0);
}

#[test]
fn test_regress_out_exogenous_synthetic_easter() {
    // From ARIMA_ALGORITHM.md lines 548-580
    // This is the validation test from the spec

    // TODO: Implement full synthetic test with trend, seasonality, noise
}
```

**Action Items**:
- [ ] Implement exogenous regression
- [ ] Test with simple known coefficients
- [ ] Implement full synthetic Easter test from spec
- [ ] Verify coefficient within 20% of true value (500.0)

### Step 1.7: MA Coefficient Estimation (Day 8)

**File**: `src/ma.rs`

```rust
pub fn estimate_ma_coefficients(residuals: &[f64], q: usize) -> Vec<f64>
```

Reference: `ARIMA_ALGORITHM.md` lines 342-356

**Simplified algorithm**:
```rust
let autocorr = autocorrelation(residuals, q);
(1..=q).map(|i| autocorr[i] * 0.5).collect()
```

**Tests**:
```rust
#[test]
fn test_ma_coefficient_estimation() {
    // Generate MA(1) residuals: ε[t] = η[t] + θ * η[t-1]
    // For MA(1), autocorr[1] = θ / (1 + θ²)
    // With θ = 0.5, autocorr[1] ≈ 0.4
    // Simplified estimator: θ_est ≈ 0.5 * 0.4 = 0.2

    // TODO: Generate synthetic MA(1) process
    // TODO: Verify estimated coefficient is reasonable
}
```

**Action Items**:
- [ ] Implement MA estimation
- [ ] Test with synthetic MA processes
- [ ] Compare with C# output on same residuals

### Step 1.8: Forecast Generation (Days 9-10)

**File**: `src/forecast.rs`

```rust
pub fn generate_forecast(
    differenced: &[f64],
    ar_coeffs: &[f64],
    ma_coeffs: &[f64],
    residuals: &[f64],
    intercept: f64,
    steps: usize
) -> Vec<f64>
```

Reference: `ARIMA_ALGORITHM.md` lines 373-410

**Algorithm**:
```
For each future step:
  prediction = intercept

  For each AR term:
    prediction += ar_coeff[i] * (previous_value[i] - intercept)

  For each MA term:
    prediction += ma_coeff[i] * previous_residual[i]

  Future residuals = 0 (can't predict errors)
```

**Tests**:
```rust
#[test]
fn test_forecast_constant_series() {
    // Constant series should forecast constant value
    let differenced = vec![0.0, 0.0, 0.0, 0.0]; // All zero differences
    let ar_coeffs = vec![0.0, 0.0];
    let ma_coeffs = vec![0.0];
    let residuals = vec![0.0; 4];
    let intercept = 0.0;

    let forecast = generate_forecast(&differenced, &ar_coeffs, &ma_coeffs, &residuals, intercept, 12);

    // Should forecast zeros
    for &val in &forecast {
        assert!(val.abs() < 1e-10);
    }
}

#[test]
fn test_forecast_ar1_process() {
    // AR(1): x[t] = 0.5 * x[t-1]
    // Starting from x[0] = 10, should decay: 10, 5, 2.5, 1.25, ...

    // TODO: Implement test with known AR(1) decay
}
```

**Action Items**:
- [ ] Implement forecast generation
- [ ] Test with known AR processes (verify decay/growth)
- [ ] Test edge cases (no AR terms, no MA terms)

### Step 1.9: Confidence Intervals (Day 11)

**File**: `src/confidence.rs`

```rust
pub fn confidence_intervals(
    forecast: &[f64],
    residuals: &[f64],
    seasonal_factors: &[f64],
    start_month: usize,
    confidence: f64
) -> (Vec<f64>, Vec<f64>)
```

Reference: `ARIMA_ALGORITHM.md` lines 414-452

**Algorithm**:
1. Standard error from residuals: `se = sqrt(mean(residuals²))`
2. Z-score for confidence level (0.80 → 1.28)
3. Error grows with horizon: `horizon_se = se * sqrt(1 + i * 0.1)`
4. Scale by seasonal factor
5. Interval = forecast ± z * horizon_se * seasonal_scale

**Tests**:
```rust
#[test]
fn test_confidence_intervals_width_grows() {
    let forecast = vec![100.0; 12];
    let residuals = vec![5.0, -5.0, 3.0, -3.0, 2.0, -2.0]; // se ≈ 3.56
    let seasonal_factors = vec![1.0; 12];

    let (lower, upper) = confidence_intervals(&forecast, &residuals, &seasonal_factors, 0, 0.80);

    // Width should increase with horizon
    for i in 1..12 {
        let width_i = upper[i] - lower[i];
        let width_prev = upper[i-1] - lower[i-1];
        assert!(width_i > width_prev);
    }
}

#[test]
fn test_confidence_intervals_no_negative_values() {
    let forecast = vec![10.0; 12];
    let residuals = vec![20.0; 10]; // Large errors
    let seasonal_factors = vec![1.0; 12];

    let (lower, _upper) = confidence_intervals(&forecast, &residuals, &seasonal_factors, 0, 0.80);

    // Lower bound should not go negative (clamped to 0)
    for &val in &lower {
        assert!(val >= 0.0);
    }
}
```

**Action Items**:
- [ ] Implement confidence interval calculation
- [ ] Test interval width growth with horizon
- [ ] Verify z-scores for different confidence levels
- [ ] Ensure non-negative bounds

### Step 1.10: Integration (Days 12-13)

**File**: `src/arima.rs`

Bring it all together in the `fit_and_forecast` function.

Reference: `ARIMA_ALGORITHM.md` lines 456-538

**Pipeline**:
1. Regress out Easter (if enabled)
2. Calculate seasonal factors
3. Deseasonalize
4. Difference
5. Estimate AR coefficients (Yule-Walker)
6. Calculate residuals
7. Estimate MA coefficients
8. Generate forecast (differenced space)
9. Undifference
10. Reseasonalize
11. Add back Easter effect

**Tests**:
```rust
#[test]
fn test_end_to_end_synthetic_data() {
    // Generate 84 months (7 years) of synthetic data
    // with known trend, seasonality, Easter effect

    // TODO: Full end-to-end test
}
```

**Action Items**:
- [ ] Implement complete pipeline in `fit_and_forecast`
- [ ] Test with synthetic data
- [ ] Verify each intermediate step (log values for debugging)

### Step 1.11: C# Validation (Days 14-15)

**Critical**: Generate test fixtures from C# code.

#### Generate Test Fixtures

```bash
cd reference/
dotnet run --validate > ../tests/fixtures/csharp_output.json
```

#### Create Rust Validation Tests

**File**: `tests/validation.rs`

```rust
use blizzard_wasm::forecast;
use serde_json::Value;

#[test]
fn test_matches_csharp_output() {
    // Load C# test fixture
    let csharp_json = include_str!("fixtures/csharp_output.json");
    let csharp: Value = serde_json::from_str(csharp_json).unwrap();

    // Extract input parameters
    let input = /* extract from csharp fixture */;

    // Run Rust forecast
    let rust_output = forecast(serde_json::to_string(&input).unwrap());
    let rust: Value = serde_json::from_str(&rust_output).unwrap();

    // Compare forecasts
    let csharp_forecast = csharp["forecast"].as_array().unwrap();
    let rust_forecast = rust["forecast"].as_array().unwrap();

    for (i, (c, r)) in csharp_forecast.iter().zip(rust_forecast.iter()).enumerate() {
        let diff = (c.as_f64().unwrap() - r.as_f64().unwrap()).abs();
        assert!(diff < 0.01, "Month {}: C#={}, Rust={}, diff={}", i, c, r, diff);
    }

    // Compare Easter coefficient
    let csharp_easter = csharp["easter_coefficient"].as_f64().unwrap();
    let rust_easter = rust["easter_coefficient"].as_f64().unwrap();
    assert!((csharp_easter - rust_easter).abs() < 0.01);

    // Compare AR/MA coefficients
    // TODO: Add comparisons for all coefficients
}
```

**Action Items**:
- [ ] Run C# code to generate test fixtures
- [ ] Create Rust validation tests
- [ ] Compare all outputs: forecast, lower, upper, coefficients
- [ ] Document any divergences > 0.01
- [ ] If divergences exist, debug intermediate values

### Step 1.12: WASM Build & Testing (Day 16)

#### Build WASM

```bash
wasm-pack build --target web --release
```

Output files in `pkg/`:
- `blizzard_wasm.js`
- `blizzard_wasm_bg.wasm`
- `blizzard_wasm.d.ts`

#### Test in Browser

**File**: `test.html`

```html
<!DOCTYPE html>
<html>
<head>
    <title>Blizzard WASM Test</title>
</head>
<body>
    <h1>WASM Forecast Test</h1>
    <pre id="output"></pre>

    <script type="module">
        import init, { forecast, get_easter_dates, version } from './pkg/blizzard_wasm.js';

        async function run() {
            await init();

            console.log('Version:', version());

            // Test Easter dates
            const easterDates = JSON.parse(get_easter_dates(2024, 2030));
            console.log('Easter dates:', easterDates);

            // Test forecast
            const input = {
                series: [/* ... 60+ data points ... */],
                start_year: 2019,
                start_month: 1,
                forecast_months: 12,
                use_easter_regressor: true
            };

            const result = JSON.parse(forecast(JSON.stringify(input)));
            console.log('Forecast:', result);

            document.getElementById('output').textContent = JSON.stringify(result, null, 2);
        }

        run().catch(console.error);
    </script>
</body>
</html>
```

**Test locally**:
```bash
python3 -m http.server 8000
# Open http://localhost:8000/test.html
```

**Action Items**:
- [ ] Build WASM successfully
- [ ] Verify WASM binary size < 500KB
- [ ] Test in browser with sample data
- [ ] Verify JSON serialization works correctly
- [ ] Check for any WASM runtime errors

---

## Phase 2: Scenario Builder UI

### Step 2.1: IndexedDB Wrapper (Days 17-18)

**File**: `web/indexeddb-cache.js`

```javascript
class BlizzardCache {
    constructor(dbName = 'blizzard-cache', version = 1) {
        this.dbName = dbName;
        this.version = version;
        this.db = null;
    }

    async init() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(this.dbName, this.version);

            request.onerror = () => reject(request.error);
            request.onsuccess = () => {
                this.db = request.result;
                resolve();
            };

            request.onupgradeneeded = (event) => {
                const db = event.target.result;

                // Store baseline data from server
                if (!db.objectStoreNames.contains('baseline')) {
                    db.createObjectStore('baseline', { keyPath: 'id' });
                }

                // Store scenarios
                if (!db.objectStoreNames.contains('scenarios')) {
                    db.createObjectStore('scenarios', { keyPath: 'id' });
                }
            };
        });
    }

    async saveBaseline(data, lastModified) {
        const tx = this.db.transaction(['baseline'], 'readwrite');
        const store = tx.objectStore('baseline');

        await store.put({
            id: 'current',
            data: data,
            lastModified: lastModified,
            cached: new Date().toISOString()
        });
    }

    async getBaseline() {
        const tx = this.db.transaction(['baseline'], 'readonly');
        const store = tx.objectStore('baseline');
        return await store.get('current');
    }

    async saveScenario(scenario) {
        const tx = this.db.transaction(['scenarios'], 'readwrite');
        const store = tx.objectStore('scenarios');
        await store.put(scenario);
    }

    async getScenario(id) {
        const tx = this.db.transaction(['scenarios'], 'readonly');
        const store = tx.objectStore('scenarios');
        return await store.get(id);
    }

    async getAllScenarios() {
        const tx = this.db.transaction(['scenarios'], 'readonly');
        const store = tx.objectStore('scenarios');
        return await store.getAll();
    }

    async deleteScenario(id) {
        const tx = this.db.transaction(['scenarios'], 'readwrite');
        const store = tx.objectStore('scenarios');
        await store.delete(id);
    }
}
```

**Action Items**:
- [ ] Implement IndexedDB wrapper
- [ ] Test in multiple browsers (Chrome, Firefox, Safari)
- [ ] Handle errors gracefully (private mode, storage quota)
- [ ] Add cache invalidation based on Last-Modified header

### Step 2.2: Scenario Data Model (Day 19)

**File**: `web/scenario-model.js`

```javascript
function createScenario(name) {
    return {
        id: crypto.randomUUID(),
        name: name,
        created: new Date().toISOString(),
        modified: new Date().toISOString(),
        adjustments: []
    };
}

function createAdjustment(type, params) {
    const base = {
        id: crypto.randomUUID(),
        type: type,
        note: params.note || ''
    };

    if (type === 'scale' || type === 'remove') {
        return {
            ...base,
            target_type: params.target_type,
            target_key: params.target_key,
            factor: type === 'remove' ? 0 : params.factor
        };
    } else if (type === 'new_business') {
        return {
            ...base,
            product_group: params.product_group,
            geography: params.geography,
            start_month: params.start_month,
            year1_value: params.year1_value,
            year2_value: params.year2_value,
            year3_value: params.year3_value
        };
    }
}

function applyAdjustments(baselineData, adjustments) {
    let adjusted = JSON.parse(JSON.stringify(baselineData)); // Deep copy

    for (const adj of adjustments) {
        if (adj.type === 'scale') {
            adjusted = applyScaleAdjustment(adjusted, adj);
        } else if (adj.type === 'remove') {
            adjusted = applyRemoveAdjustment(adjusted, adj);
        } else if (adj.type === 'new_business') {
            adjusted = applyNewBusinessAdjustment(adjusted, adj);
        }
    }

    return adjusted;
}

function applyScaleAdjustment(data, adj) {
    // TODO: Scale historical data for specific customer/product/geography
    return data;
}

function applyRemoveAdjustment(data, adj) {
    // TODO: Zero out contribution from specific entity
    return data;
}

function applyNewBusinessAdjustment(data, adj) {
    // TODO: Add synthetic series with ramp profile
    return data;
}
```

**Action Items**:
- [ ] Implement scenario/adjustment creation functions
- [ ] Implement adjustment application logic
- [ ] Test with sample baseline data
- [ ] Validate JSON structure matches spec

### Step 2.3: UI Components (Days 20-23)

#### Scenario List (Day 20)

```html
<div class="scenario-list">
    <button id="new-scenario-btn">+ New Scenario</button>
    <div id="scenario-items">
        <!-- Dynamically populated -->
    </div>
</div>
```

```javascript
function renderScenarioList(scenarios) {
    const container = document.getElementById('scenario-items');
    container.innerHTML = '';

    // Baseline (always present)
    const baselineItem = document.createElement('div');
    baselineItem.className = 'scenario-item';
    baselineItem.textContent = 'Baseline (no adjustments)';
    baselineItem.onclick = () => loadScenario(null);
    container.appendChild(baselineItem);

    // User scenarios
    for (const scenario of scenarios) {
        const item = document.createElement('div');
        item.className = 'scenario-item';
        item.innerHTML = `
            <span>${scenario.name}</span>
            <button onclick="editScenario('${scenario.id}')">Edit</button>
            <button onclick="deleteScenario('${scenario.id}')">Delete</button>
        `;
        item.onclick = () => loadScenario(scenario.id);
        container.appendChild(item);
    }
}
```

#### Adjustment List (Day 21)

```html
<div class="adjustment-list">
    <h3>Adjustments</h3>
    <div id="adjustment-items">
        <!-- Dynamically populated -->
    </div>
    <button id="add-adjustment-btn">+ Add Adjustment</button>
</div>
```

```javascript
function renderAdjustmentList(adjustments) {
    const container = document.getElementById('adjustment-items');
    container.innerHTML = '';

    for (const adj of adjustments) {
        const item = document.createElement('div');
        item.className = 'adjustment-item';

        if (adj.type === 'scale') {
            item.innerHTML = `
                <strong>📊 Scale:</strong> ${adj.target_key} → ${adj.factor * 100}%<br>
                <em>${adj.note}</em>
                <button onclick="editAdjustment('${adj.id}')">✏️</button>
                <button onclick="deleteAdjustment('${adj.id}')">❌</button>
            `;
        } else if (adj.type === 'remove') {
            item.innerHTML = `
                <strong>📉 Remove:</strong> ${adj.target_key}<br>
                <em>${adj.note}</em>
                <button onclick="editAdjustment('${adj.id}')">✏️</button>
                <button onclick="deleteAdjustment('${adj.id}')">❌</button>
            `;
        } else if (adj.type === 'new_business') {
            item.innerHTML = `
                <strong>➕ New Business:</strong> ${adj.product_group} in ${adj.geography}<br>
                Start: ${adj.start_month}, Y1: £${adj.year1_value.toLocaleString()}<br>
                <em>${adj.note}</em>
                <button onclick="editAdjustment('${adj.id}')">✏️</button>
                <button onclick="deleteAdjustment('${adj.id}')">❌</button>
            `;
        }

        container.appendChild(item);
    }
}
```

#### Add Adjustment Modal (Days 22-23)

```html
<dialog id="add-adjustment-modal">
    <h2>Add Adjustment</h2>

    <label>Type:</label>
    <select id="adjustment-type">
        <option value="scale">Scale Existing</option>
        <option value="remove">Remove Existing</option>
        <option value="new_business">New Business</option>
    </select>

    <div id="scale-fields" class="adjustment-fields">
        <label>Target Type:</label>
        <select id="scale-target-type">
            <option value="customer">Customer</option>
            <option value="product_group">Product Group</option>
            <option value="geography">Geography</option>
        </select>

        <label>Select:</label>
        <input type="text" id="scale-target-search" placeholder="Type to search...">
        <select id="scale-target-select" size="5"></select>

        <label>Scale Factor (%):</label>
        <input type="number" id="scale-factor" value="100" min="0" step="1">

        <label>Note:</label>
        <input type="text" id="scale-note" placeholder="Why this adjustment?">
    </div>

    <div id="new-business-fields" class="adjustment-fields" style="display:none;">
        <label>Product Group:</label>
        <select id="nb-product-group"></select>

        <label>Geography:</label>
        <select id="nb-geography"></select>

        <label>Start Month:</label>
        <input type="month" id="nb-start-month">

        <label>Year 1 Value (£):</label>
        <input type="number" id="nb-year1" min="0" step="1000">

        <label>Year 2 Value (£):</label>
        <input type="number" id="nb-year2" min="0" step="1000">

        <label>Year 3 Value (£):</label>
        <input type="number" id="nb-year3" min="0" step="1000">

        <label>Note:</label>
        <input type="text" id="nb-note" placeholder="Business justification">
    </div>

    <button id="cancel-adjustment">Cancel</button>
    <button id="save-adjustment">Add Adjustment</button>
</dialog>
```

**Action Items**:
- [ ] Implement scenario list rendering
- [ ] Implement adjustment list rendering
- [ ] Build add/edit adjustment modal
- [ ] Wire up form validation
- [ ] Add cascading dropdowns (geography hierarchy)

### Step 2.4: Chart Integration (Day 24)

Update existing D3 chart to show baseline + scenario comparison.

```javascript
function updateChartWithScenario(baselineForecast, scenarioForecast) {
    // Assuming existing chart variable: chart

    // Add baseline line (dashed)
    chart.append('path')
        .datum(baselineForecast)
        .attr('class', 'baseline-line')
        .attr('d', line)
        .style('stroke', '#999')
        .style('stroke-dasharray', '5,5');

    // Add scenario line (solid)
    chart.append('path')
        .datum(scenarioForecast)
        .attr('class', 'scenario-line')
        .attr('d', line)
        .style('stroke', '#007bff')
        .style('stroke-width', 2);

    // Add legend
    const legend = chart.append('g')
        .attr('class', 'legend')
        .attr('transform', 'translate(20, 20)');

    legend.append('line')
        .attr('x1', 0).attr('x2', 30)
        .style('stroke', '#999')
        .style('stroke-dasharray', '5,5');
    legend.append('text')
        .attr('x', 35).attr('y', 5)
        .text('Baseline forecast');

    legend.append('line')
        .attr('x1', 0).attr('x2', 30).attr('y1', 20).attr('y2', 20)
        .style('stroke', '#007bff');
    legend.append('text')
        .attr('x', 35).attr('y', 25)
        .text('Scenario forecast');
}
```

**Action Items**:
- [ ] Study existing D3 chart code in blizzard.html
- [ ] Add scenario comparison lines
- [ ] Add legend
- [ ] Ensure zoom/pan still works with multiple lines

### Step 2.5: WASM Integration (Day 25)

Wire up scenario adjustments → WASM forecast → chart update.

```javascript
async function recalculateScenario(scenarioId) {
    // 1. Load baseline data
    const baseline = await cache.getBaseline();

    // 2. Load scenario
    const scenario = scenarioId ? await cache.getScenario(scenarioId) : null;

    // 3. Apply adjustments
    const adjustedData = scenario
        ? applyAdjustments(baseline.data, scenario.adjustments)
        : baseline.data;

    // 4. Run WASM forecast
    const input = {
        series: adjustedData.overall.historical.rows.map(r => r[1]),
        start_year: 2019, // TODO: Extract from data
        start_month: 1,
        forecast_months: 12,
        use_easter_regressor: true
    };

    const result = JSON.parse(forecast(JSON.stringify(input)));

    // 5. Update chart
    const baselineForecast = baseline.data.overall.forecast.rows.map(r => r[1]);
    const scenarioForecast = result.forecast;

    updateChartWithScenario(baselineForecast, scenarioForecast);

    // 6. Update summary
    const baselineTotal = baselineForecast.reduce((a, b) => a + b, 0);
    const scenarioTotal = scenarioForecast.reduce((a, b) => a + b, 0);
    const delta = ((scenarioTotal - baselineTotal) / baselineTotal * 100).toFixed(1);

    document.getElementById('summary').innerHTML = `
        Baseline 2026: £${(baselineTotal / 1000).toFixed(1)}k
        Scenario 2026: £${(scenarioTotal / 1000).toFixed(1)}k
        (${delta > 0 ? '+' : ''}${delta}%)
    `;
}
```

**Action Items**:
- [ ] Wire up scenario selection to recalculation
- [ ] Wire up adjustment changes to recalculation
- [ ] Add loading indicator during WASM execution
- [ ] Handle errors gracefully

---

## Phase 3: Integration & Deployment

### Step 3.1: Baseline Data Caching (Day 26)

```javascript
async function loadBaselineData() {
    // Check cache first
    const cached = await cache.getBaseline();

    if (cached) {
        // Check if stale
        const response = await fetch('/cgi-bin/blizzard', { method: 'HEAD' });
        const serverModified = response.headers.get('Last-Modified');

        if (serverModified === cached.lastModified) {
            console.log('Using cached baseline data');
            return cached.data;
        }
    }

    // Fetch fresh data
    console.log('Fetching fresh baseline data');
    const response = await fetch('/cgi-bin/blizzard');
    const data = await response.json();
    const lastModified = response.headers.get('Last-Modified');

    // Cache it
    await cache.saveBaseline(data, lastModified);

    return data;
}
```

**Action Items**:
- [ ] Implement baseline caching with staleness check
- [ ] Handle gzip compression from server
- [ ] Add cache refresh button for users
- [ ] Test offline mode after initial load

### Step 3.2: Integration with Existing Dashboard (Days 27-28)

Study `reference/blizzard.html` and integrate scenario builder.

**Tasks**:
- [ ] Extract blizzard.html structure
- [ ] Add "Scenarios" tab to tab bar
- [ ] Ensure existing tabs (Overall, Products, etc.) still work
- [ ] Share D3 chart instance between tabs
- [ ] Test all existing functionality (zoom, backtest, etc.)

### Step 3.3: Final Testing (Days 29-30)

#### End-to-End Test Scenarios

1. **Create scale adjustment**:
   - Create scenario "Conservative 2026"
   - Add adjustment: Scale "Acme Corp" to 85%
   - Verify forecast decreases appropriately
   - Save and reload - verify persistence

2. **Create new business**:
   - Create scenario "Chilled ME Expansion"
   - Add adjustment: New business "Chilled" in "Dubai", start Sep 2025, £50k/£100k/£150k
   - Verify forecast includes ramp-up
   - Check seasonal pattern application

3. **Offline mode**:
   - Load page, wait for baseline cache
   - Disable network
   - Create scenario, add adjustments
   - Verify everything works
   - Re-enable network, verify sync

4. **Performance**:
   - Scenario with 5 adjustments
   - Measure recalculation time
   - Should be < 500ms

#### Browser Testing Matrix

| Browser | Version | Status |
|---------|---------|--------|
| Chrome | Latest | ✅ |
| Firefox | Latest | ✅ |
| Safari | Latest | ✅ |
| Edge | Latest | ✅ |

**Action Items**:
- [ ] Run all end-to-end test scenarios
- [ ] Test in all browsers
- [ ] Performance profiling
- [ ] Fix any discovered issues

### Step 3.4: Deployment (Day 31)

```bash
# Build optimized WASM
cd blizzard-wasm
wasm-pack build --target web --release

# Check binary size
ls -lh pkg/blizzard_wasm_bg.wasm
# Should be < 500KB

# Deploy files
sudo cp pkg/blizzard_wasm.js /var/www/html/dw/
sudo cp pkg/blizzard_wasm_bg.wasm /var/www/html/dw/
sudo cp web/blizzard.html /var/www/html/dw/
sudo cp web/*.js /var/www/html/dw/
sudo cp web/*.css /var/www/html/dw/

# Verify deployment
curl http://localhost/dw/blizzard.html
curl http://localhost/dw/blizzard_wasm_bg.wasm --head
```

**Action Items**:
- [ ] Build production WASM
- [ ] Minify JavaScript
- [ ] Deploy to server
- [ ] Test deployed version
- [ ] Monitor for errors

---

## Success Checklist

### Phase 1: ARIMA Port
- [ ] Easter dates calculated correctly (2024-2030)
- [ ] Seasonal factors sum to `period` (multiplicative property)
- [ ] Levinson-Durbin produces stable AR coefficients
- [ ] Forecasts match C# output within 0.01
- [ ] Easter coefficient estimated within 20% on synthetic data
- [ ] WASM binary < 500KB
- [ ] All unit tests pass

### Phase 2: Scenario Builder
- [ ] Scenarios saved and loaded from IndexedDB
- [ ] Scale adjustment works correctly
- [ ] Remove adjustment zeros out contribution
- [ ] New business adds ramp profile
- [ ] Chart shows baseline vs scenario comparison
- [ ] UI is responsive and intuitive

### Phase 3: Integration
- [ ] Baseline data cached with staleness check
- [ ] Offline mode works after initial load
- [ ] Integrates with existing dashboard tabs
- [ ] No regressions in existing functionality
- [ ] Performance < 500ms for scenario recalculation
- [ ] Cross-browser compatibility verified

---

## Troubleshooting Guide

### WASM doesn't load
- Check browser console for errors
- Verify MIME type: server must serve .wasm as `application/wasm`
- Check CORS headers if serving from different domain

### Forecasts don't match C#
- Compare intermediate values (seasonal factors, AR coefficients)
- Check for floating-point precision issues
- Verify differencing/undifferencing roundtrip
- Ensure same Easter regressor array

### IndexedDB quota exceeded
- Implement cache size limits
- Compress data before storing
- Fall back to localStorage for metadata

### Performance issues
- Profile with browser DevTools
- Consider Web Workers for WASM (non-blocking UI)
- Lazy-load WASM only when Scenarios tab opened
- Cache forecast results (invalidate on adjustment change)

---

**Last Updated**: 2026-01-22
**Status**: Ready for Implementation
