# Blizzard WASM - ARIMA Forecasting Engine

Phase 1 implementation of ARIMA(2,1,1) time series forecasting with seasonal support and Easter regressor (ARIMAX).

## ✅ Phase 1 Complete

This implementation provides a complete port of the C# ARIMA forecasting algorithm to Rust/WASM.

### Features

- **ARIMA(2,1,1)** model with:
  - p=2 autoregressive terms
  - d=1 first-order differencing
  - q=1 moving average term
- **Seasonal decomposition** with 12-month period (multiplicative)
- **Easter regressor** (ARIMAX extension) for holiday sales spikes
- **80% confidence intervals** for forecasts
- **Optimized WASM binary**: 154KB uncompressed, 66KB gzipped

### Implementation Details

#### Core Algorithms

1. **Easter Date Calculation**: Computus algorithm for Gregorian calendar
2. **Exogenous Regression**: Mean-difference approach for sparse binary regressors
3. **Seasonal Decomposition**: Multiplicative factors (12 monthly values)
4. **Differencing**: First-order differencing for stationarity
5. **Yule-Walker**: Levinson-Durbin algorithm for AR coefficient estimation
6. **MA Estimation**: Residual autocorrelation method
7. **Forecasting**: Multi-step-ahead predictions with confidence bounds

#### Files

- `src/lib.rs` - WASM entry point with JSON interface
- `src/easter.rs` - Easter date calculation and regressor creation
- `src/arima.rs` - Complete ARIMA/ARIMAX implementation
- `test.html` - Browser-based validation tests

### Building

```bash
# Build WASM module
wasm-pack build --target web --release

# Run Rust tests
cargo test

# Test in browser
# Open test.html in a browser (requires local web server)
python3 -m http.server 8000
# Navigate to http://localhost:8000/test.html
```

### API

#### JavaScript/TypeScript Interface

```typescript
// Forecast function
function forecast(input: string): string;

// Input format
{
  series: number[];          // Historical time series data
  start_year: number;        // Starting year
  start_month: number;       // Starting month (1-12)
  forecast_months: number;   // Number of periods to forecast
  use_easter: boolean;       // Enable Easter regressor
}

// Output format
{
  forecast: number[];           // Point forecasts
  lower: number[];              // Lower 80% confidence bound
  upper: number[];              // Upper 80% confidence bound
  seasonal_factors: number[];   // 12 monthly seasonal factors
  easter_coefficient: number;   // Estimated Easter effect
  ar_coefficients: number[];    // AR(2) coefficients
  ma_coefficients: number[];    // MA(1) coefficient
  intercept: number;            // Model intercept
}
```

#### Helper Functions

```typescript
// Get Easter dates for a range of years
function get_easter_dates(start_year: number, end_year: number): string;

// Get version
function version(): string;
```

### Success Criteria (Phase 1)

- ✅ All ARIMA functions implemented
- ✅ Levinson-Durbin algorithm working
- ✅ Easter regressor calculation complete
- ✅ Confidence intervals implemented
- ✅ WASM binary < 500KB (154KB achieved)
- ✅ All Rust tests passing
- ✅ Browser validation working

### Performance

- **Binary size**: 154KB (uncompressed), 66KB (gzipped)
- **Load time**: < 100ms on modern browsers
- **Forecast computation**: < 50ms for typical datasets

### Next Steps (Phase 2)

Phase 2 will add:
- Scenario builder UI
- IndexedDB for local data persistence
- Real-time chart updates with D3.js
- Baseline data management
- Scenario comparison tools

### References

- Algorithm specification: `../blizzard-wasm-spec/docs/ARIMA_ALGORITHM.md`
- C# reference implementation: `../blizzard-wasm-spec/reference/Arima.cs`
- Implementation plan: `../IMPLEMENTATION_PLAN.md`

## License

See parent project for license information.
