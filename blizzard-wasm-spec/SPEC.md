# Blizzard WASM Scenario Builder - Technical Specification

## Project Overview

Extend the existing Blizzard sales forecasting system with a client-side scenario builder powered by WebAssembly. This allows salespeople to create "what if" scenarios that adjust the statistical baseline forecast with their business knowledge.

**Core value proposition:** Statistical baseline + Sales team knowledge = Better forecasts

## Goals

1. **Exact replica first**: Port the existing ARIMA implementation to Rust/WASM, ensure it produces identical results
2. **Add scenario builder**: UI for creating adjustments (scale customers, add new business, remove customers)
3. **Instant feedback**: Recalculate forecasts client-side without server round-trips
4. **Offline capable**: Cache baseline data, work without connectivity

## What Salespeople Know (That ARIMA Doesn't)

- "We're about to lose Acme Corp"
- "BigCo is expanding into Germany, expect growth"
- "New product launch in March"
- "Competitor raised prices, we'll gain share"
- "Chilled is starting in the Middle East" ← NEW BUSINESS

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Browser                                                     │
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────┐ │
│  │  Scenario UI    │  │  Chart (D3)     │  │  IndexedDB   │ │
│  │  - Scale        │  │  - Baseline     │  │  - Cached    │ │
│  │  - Remove       │  │  - Adjusted     │  │    baseline  │ │
│  │  - New business │  │  - Comparison   │  │  - Scenarios │ │
│  └────────┬────────┘  └────────▲────────┘  └──────▲───────┘ │
│           │                    │                   │         │
│           ▼                    │                   │         │
│  ┌─────────────────────────────┴───────────────────┴───────┐ │
│  │  JS Orchestrator                                        │ │
│  │  - Loads baseline from server or cache                  │ │
│  │  - Applies scenario transforms to input data            │ │
│  │  - Calls WASM for ARIMA forecasting                     │ │
│  │  - Merges results for display                           │ │
│  └─────────────────────────────┬───────────────────────────┘ │
│                                │                             │
│                                ▼                             │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │  Rust WASM Module                                       │ │
│  │  - ARIMA(2,1,1) with seasonal period 12                 │ │
│  │  - Easter regressor (ARIMAX)                            │ │
│  │  - Yule-Walker / Levinson-Durbin coefficient estimation │ │
│  │  - Forecast generation with confidence intervals        │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
           │
           │ Initial load / cache refresh
           ▼
┌─────────────────────┐
│  Existing CGI       │
│  /cgi-bin/blizzard  │
│  (baseline data)    │
└─────────────────────┘
```

## Phase 1: WASM ARIMA Port (Exact Replica)

### Requirements

The Rust WASM module must produce **identical results** to the C# implementation. This means:

1. Same ARIMA(2,1,1) model with seasonal period 12
2. Same Easter regressor calculation (Computus algorithm, -3 month invoice lag)
3. Same coefficient estimation (Yule-Walker via Levinson-Durbin)
4. Same seasonal factor calculation (multiplicative)
5. Same differencing/undifferencing
6. Same confidence interval calculation (80%)

### WASM Interface

```rust
// Input: JSON string containing time series and parameters
// Output: JSON string containing forecast results
#[wasm_bindgen]
pub fn forecast(input_json: &str) -> String;

// Input structure (JSON)
{
  "series": [1000.0, 1200.0, ...],      // Historical values
  "start_year": 2019,                    // Start year of series
  "start_month": 1,                      // Start month (1-12)
  "forecast_months": 12,                 // Number of months to forecast
  "p": 2,                                // AR order
  "d": 1,                                // Differencing order
  "q": 1,                                // MA order
  "seasonal_period": 12,                 // Seasonal period
  "use_easter_regressor": true           // Include Easter effect
}

// Output structure (JSON)
{
  "forecast": [1500.0, 1600.0, ...],     // Point forecasts
  "lower": [1400.0, 1480.0, ...],        // 80% CI lower bound
  "upper": [1600.0, 1720.0, ...],        // 80% CI upper bound
  "seasonal_factors": [0.9, 1.1, ...],   // 12 seasonal factors
  "easter_coefficient": 45000.0,         // Estimated Easter effect
  "ar_coefficients": [0.5, 0.2],         // AR coefficients
  "ma_coefficients": [0.3],              // MA coefficients
  "intercept": 1000.0                    // Model intercept
}
```

### Validation Strategy

1. Generate test cases from C# implementation with known inputs/outputs
2. Run same inputs through Rust WASM
3. Compare outputs within floating-point tolerance (1e-6)
4. Include edge cases: short series, missing data, zero values

## Phase 2: Scenario Builder UI

### Scenario Types

| Type | Example | Implementation |
|------|---------|----------------|
| Scale existing | "Customer X grows 20%" | Multiply customer's historical data by factor |
| Remove existing | "We lose Customer Y" | Zero out customer's contribution |
| New business | "Chilled starts in ME" | Add synthetic series based on ramp profile |
| Timing shift | "Order moves to Feb" | Move values between months (future enhancement) |

### Scenario Data Model

```typescript
interface Scenario {
  id: string;                    // UUID
  name: string;                  // "Chilled expansion ME"
  created: string;               // ISO timestamp
  modified: string;              // ISO timestamp
  adjustments: Adjustment[];
}

interface Adjustment {
  id: string;                    // UUID
  type: 'scale' | 'remove' | 'new_business';
  note: string;                  // "Dubai distributor confirmed"
  
  // For scale/remove
  target_type?: 'customer' | 'product_group' | 'geography';
  target_key?: string;           // Customer name, product group code, geo code
  factor?: number;               // Scale factor (0 = remove, 1.15 = +15%)
  
  // For new_business
  product_group?: string;        // e.g., "S5" for Chilled
  geography?: string;            // e.g., "MEAEDU" for Dubai
  start_month?: string;          // ISO month "2025-09"
  year1_value?: number;          // First year total
  year2_value?: number;          // Second year total
  year3_value?: number;          // Third year total
}
```

### New Business Ramp Profile

When adding new business, the system:

1. Takes year 1/2/3 annual totals from user
2. Applies seasonal pattern from similar existing products in that geography
3. Interpolates monthly values with ramp-up curve
4. Adds to baseline data before running ARIMA

```
Year 1: £50k  → Monthly profile based on product seasonality, ramping up
Year 2: £100k → Full year with seasonal pattern
Year 3: £150k → Growth continues
```

### UI Layout

```
┌─────────────────────────────────────────────────────────────────┐
│  [Overall] [Products] [Customers] [Geography] [🆕 Scenarios]    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─── Scenarios ───────────────────────────────────────────┐   │
│  │ [Baseline (no adjustments)]  [+ New Scenario]           │   │
│  │ [📁 Chilled ME Expansion]  [📁 Conservative 2026]       │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  ┌─── Current: Chilled ME Expansion ───────────────────────┐   │
│  │                                                         │   │
│  │  Adjustments:                                           │   │
│  │  ┌─────────────────────────────────────────────────┐   │   │
│  │  │ ➕ New Business: Chilled in Middle East          │   │   │
│  │  │    Start: Sep 2025, Y1: £50k, Y2: £100k, Y3: £150k│   │   │
│  │  │    Note: "Dubai distributor confirmed"       [✏️❌]│   │   │
│  │  ├─────────────────────────────────────────────────┤   │   │
│  │  │ 📉 Scale: ACME Corp → 85%                        │   │   │
│  │  │    Note: "Losing shelf space"                [✏️❌]│   │   │
│  │  └─────────────────────────────────────────────────┘   │   │
│  │                                                         │   │
│  │  [+ Add Adjustment]                                     │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  ┌─── Chart ───────────────────────────────────────────────┐   │
│  │                                                         │   │
│  │     -------- Baseline forecast                          │   │
│  │     ======== Scenario forecast                          │   │
│  │                                                         │   │
│  │         📈 [D3 chart showing both forecasts]            │   │
│  │                                                         │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  ┌─── Summary ─────────────────────────────────────────────┐   │
│  │  Baseline 2026: £2.4M    Scenario 2026: £2.6M (+8.3%)   │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Add Adjustment Modal

```
┌─── Add Adjustment ──────────────────────────────────────────┐
│                                                             │
│  Type: [Scale Existing ▼]                                   │
│                                                             │
│  ┌─ Scale Existing ─────────────────────────────────────┐  │
│  │  Target: [Customer ▼]                                 │  │
│  │  Select: [🔍 Type to search customers...]             │  │
│  │  Factor: [____85___] %  (100 = no change, 0 = remove) │  │
│  │  Note:   [Losing shelf space to competitor_________]  │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌─ OR: New Business ───────────────────────────────────┐  │
│  │  Product Group: [Chilled (S5) ▼]                      │  │
│  │  Geography:     [Middle East > UAE > Dubai ▼]         │  │
│  │  Start Month:   [September ▼] [2025 ▼]                │  │
│  │  Year 1 Value:  £[____50,000___]                      │  │
│  │  Year 2 Value:  £[___100,000___]                      │  │
│  │  Year 3 Value:  £[___150,000___]                      │  │
│  │  Note:          [Dubai distributor confirmed_______]  │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│                              [Cancel]  [Add Adjustment]     │
└─────────────────────────────────────────────────────────────┘
```

## Phase 3: Integration

### Data Flow

1. **Initial load**: Fetch baseline JSON from `/cgi-bin/blizzard`, store in IndexedDB
2. **Cache validation**: Check `Last-Modified` header, refresh if stale
3. **Scenario creation**: User creates adjustments, stored in IndexedDB
4. **Forecast calculation**:
   - Apply adjustments to baseline input data
   - Call WASM module for each relevant series (overall, by customer, etc.)
   - Merge adjusted forecasts with baseline for comparison
5. **Display**: Show both baseline and scenario forecasts on chart

### Offline Support

- Baseline data cached in IndexedDB after first load
- Scenarios stored locally, persist across sessions
- WASM module loaded once, cached by browser
- Full functionality available offline (after initial load)

## Technical Details

### Rust Crate Structure

```
blizzard-wasm/
├── Cargo.toml
├── src/
│   ├── lib.rs           # WASM entry points
│   ├── arima.rs         # Core ARIMA implementation
│   ├── easter.rs        # Easter date calculation
│   ├── seasonal.rs      # Seasonal decomposition
│   └── types.rs         # Data structures
└── tests/
    ├── validation.rs    # Compare against C# outputs
    └── fixtures/        # Test data from C# implementation
```

### Build Configuration

```toml
# Cargo.toml
[package]
name = "blizzard-wasm"
version = "0.1.0"
edition = "2021"

[lib]
crate-type = ["cdylib"]

[dependencies]
wasm-bindgen = "0.2"
serde = { version = "1.0", features = ["derive"] }
serde_json = "1.0"

[profile.release]
opt-level = "s"     # Optimize for size
lto = true          # Link-time optimization
```

### Build Commands

```bash
# Install wasm-pack if not present
cargo install wasm-pack

# Build for web
wasm-pack build --target web --release

# Output in pkg/ directory:
# - blizzard_wasm.js      (JS bindings)
# - blizzard_wasm_bg.wasm (WASM binary)
# - blizzard_wasm.d.ts    (TypeScript types)
```

### Deployment

Files to deploy alongside existing dashboard:

```
/var/www/html/dw/
├── blizzard.html          # Updated dashboard with scenario UI
├── blizzard_wasm.js       # WASM JS bindings
├── blizzard_wasm_bg.wasm  # WASM binary
└── sarah.jpg              # Keep the mascot
```

## Testing

### Unit Tests (Rust)

```rust
#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_easter_dates() {
        assert_eq!(easter_sunday(2024), (3, 31)); // March 31
        assert_eq!(easter_sunday(2025), (4, 20)); // April 20
    }
    
    #[test]
    fn test_arima_forecast_matches_csharp() {
        let input = include_str!("../tests/fixtures/input_001.json");
        let expected = include_str!("../tests/fixtures/output_001.json");
        
        let result = forecast(input);
        let result: ForecastOutput = serde_json::from_str(&result).unwrap();
        let expected: ForecastOutput = serde_json::from_str(expected).unwrap();
        
        for (r, e) in result.forecast.iter().zip(expected.forecast.iter()) {
            assert!((r - e).abs() < 1e-6);
        }
    }
}
```

### Integration Tests (Browser)

```javascript
// Test that WASM produces same results as server
async function testWasmMatchesServer() {
  const serverResponse = await fetch('/cgi-bin/blizzard').then(r => r.json());
  
  const wasmResult = wasmForecast({
    series: serverResponse.overall.historical.rows.map(r => r[1]),
    start_year: 2019,
    start_month: 1,
    forecast_months: 12,
    use_easter_regressor: true
  });
  
  const serverForecast = serverResponse.overall.forecast.rows.map(r => r[1]);
  
  for (let i = 0; i < 12; i++) {
    const diff = Math.abs(wasmResult.forecast[i] - serverForecast[i]);
    console.assert(diff < 0.01, `Month ${i}: WASM=${wasmResult.forecast[i]}, Server=${serverForecast[i]}`);
  }
}
```

## Success Criteria

### Phase 1 Complete When:
- [ ] Rust WASM module produces identical forecasts to C# (within 0.01)
- [ ] Easter coefficient estimation matches
- [ ] Confidence intervals match
- [ ] Build produces <500KB WASM binary

### Phase 2 Complete When:
- [ ] Scenarios can be created, saved, loaded, deleted
- [ ] Scale/remove adjustments work correctly
- [ ] New business with ramp profile works
- [ ] Chart shows baseline vs scenario comparison
- [ ] Scenarios persist in IndexedDB

### Phase 3 Complete When:
- [ ] Baseline data caches correctly
- [ ] Offline mode works after initial load
- [ ] Integrates with existing tabs (Overall, Products, etc.)
- [ ] Deployed alongside existing dashboard

## Files Included in This Package

```
blizzard-wasm-spec/
├── CONTENTS.md              # This index
├── SPEC.md                  # This specification
├── docs/
│   ├── ARIMA_ALGORITHM.md   # Detailed ARIMA implementation notes
│   ├── SCENARIO_EXAMPLES.md # Example scenarios and expected behavior
│   └── UI_MOCKUPS.md        # ASCII art UI mockups
├── reference/
│   ├── Arima.cs             # Original C# ARIMA implementation
│   ├── Program.cs           # Original C# backend
│   ├── blizzard.html        # Original dashboard HTML
│   ├── ValidateArimax.cs    # C# validation tests
│   └── sarah.jpg            # Dashboard mascot image
└── src/
    ├── lib.rs               # Starter Rust WASM code
    ├── arima.rs             # ARIMA implementation scaffold
    └── easter.rs            # Easter calculation scaffold
```

## Notes for Claude Code

1. **Start with exact replication** - Get the ARIMA producing identical results before adding any new features
2. **Test continuously** - Generate test fixtures from C# implementation
3. **The Easter regressor is critical** - The business has seasonal spikes 3 months before Easter
4. **Keep the boundary simple** - JSON in, JSON out. No complex objects crossing WASM boundary
5. **IndexedDB for persistence** - Don't use localStorage (size limits)
6. **Preserve existing functionality** - Scenario builder is an addition, not a replacement
