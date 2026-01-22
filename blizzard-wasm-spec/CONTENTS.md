# Blizzard WASM Scenario Builder - Package Contents

## Start Here

1. **Read `SPEC.md`** - The main specification document covering architecture, requirements, and implementation plan
2. **Review `docs/ARIMA_ALGORITHM.md`** - Deep dive into the ARIMA implementation that must be ported
3. **Check `reference/`** - Original C# source code and HTML dashboard

## Directory Structure

```
blizzard-wasm-spec/
│
├── CONTENTS.md              ← You are here
├── SPEC.md                  ← Main specification (START HERE)
│
├── docs/
│   ├── ARIMA_ALGORITHM.md   ← Detailed ARIMA/ARIMAX implementation
│   ├── SCENARIO_EXAMPLES.md ← Example scenarios and expected behavior
│   └── UI_MOCKUPS.md        ← Detailed UI mockups in ASCII
│
├── reference/
│   ├── Arima.cs             ← Original C# ARIMA (PORT THIS)
│   ├── Program.cs           ← C# backend showing data structures
│   ├── ValidateArimax.cs    ← Validation tests
│   ├── blizzard.html        ← Original dashboard (1659 lines)
│   ├── CLAUDE.md            ← Deployment notes
│   └── sarah.jpg            ← Dashboard header image
│
└── src/
    ├── lib.rs               ← Rust WASM entry point scaffold
    ├── arima.rs             ← ARIMA implementation scaffold
    └── easter.rs            ← Easter calculation (complete)
```

## Key Documents

### SPEC.md
The main specification covering:
- Project goals and architecture
- WASM interface design
- Scenario data model
- UI layout and interactions
- Testing strategy
- Success criteria

### docs/ARIMA_ALGORITHM.md
Detailed breakdown of:
- ARIMA(p,d,q) model mathematics
- Seasonal decomposition (multiplicative)
- Easter regressor for ARIMAX
- Yule-Walker equations and Levinson-Durbin algorithm
- Differencing and undifferencing
- Confidence interval calculation

### reference/Arima.cs
The C# implementation to port. Key methods:
- `Fit()` - Model fitting with exogenous variables
- `Forecast()` - Generate predictions
- `CalculateSeasonalFactors()` - Multiplicative seasonality
- `SolveYuleWalker()` - Levinson-Durbin algorithm
- `EasterCalculator` - Computus algorithm

### reference/blizzard.html
The existing dashboard (preserve this functionality):
- D3.js charts with zoom and cumulative views
- Tabs: Overall, Products, Customers, Geography
- Backtest mode with variance analysis
- Cascading dropdowns for hierarchical selection

## Implementation Order

### Phase 1: WASM ARIMA Port
1. Port `EasterCalculator` (see `src/easter.rs` - already done)
2. Port seasonal decomposition
3. Port differencing/undifferencing
4. Port Yule-Walker/Levinson-Durbin
5. Port forecast generation
6. Validate against C# output

### Phase 2: Scenario Builder
1. Add Scenarios tab to dashboard
2. Implement IndexedDB storage
3. Build adjustment UI (scale, remove, new business)
4. Implement scenario application logic
5. Add comparison chart view

### Phase 3: Integration
1. Cache baseline data in IndexedDB
2. Wire up offline support
3. Deploy alongside existing dashboard
4. Test end-to-end

## Validation

Generate test fixtures by running the C# code:
```bash
dotnet run --validate
```

This outputs synthetic test data with known coefficients that can be used to verify the Rust implementation produces identical results.

## Deployment

Target location (same server as existing):
```
/var/www/html/dw/
├── blizzard.html          # Updated with scenario builder
├── blizzard_wasm.js       # WASM bindings
├── blizzard_wasm_bg.wasm  # WASM binary
└── sarah.jpg              # Keep this!
```

The CGI backend at `/cgi-bin/blizzard` remains unchanged - it provides baseline data.
