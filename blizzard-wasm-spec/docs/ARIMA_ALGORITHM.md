# ARIMA Algorithm Implementation Details

This document provides the mathematical details needed to port the C# ARIMA implementation to Rust.

## Model Overview

The system uses **ARIMA(2,1,1)** with:
- **p=2**: Two autoregressive terms
- **d=1**: First-order differencing
- **q=1**: One moving average term
- **Seasonal period=12**: Monthly data with yearly seasonality
- **Easter regressor**: ARIMAX extension for Easter-related sales spikes

## Processing Pipeline

```
Raw Sales Data
      │
      ▼
┌─────────────────────────┐
│ 1. Regress out Easter   │  ← Remove Easter effect FIRST
│    effect (if present)  │     before seasonal decomposition
└─────────────────────────┘
      │
      ▼
┌─────────────────────────┐
│ 2. Calculate seasonal   │  ← Multiplicative seasonal factors
│    factors              │     (12 values, one per month)
└─────────────────────────┘
      │
      ▼
┌─────────────────────────┐
│ 3. Deseasonalize        │  ← Divide by seasonal factors
└─────────────────────────┘
      │
      ▼
┌─────────────────────────┐
│ 4. Apply differencing   │  ← d=1: y'[t] = y[t] - y[t-1]
└─────────────────────────┘
      │
      ▼
┌─────────────────────────┐
│ 5. Estimate AR/MA       │  ← Yule-Walker for AR
│    coefficients         │     Residual correlation for MA
└─────────────────────────┘
      │
      ▼
┌─────────────────────────┐
│ 6. Generate forecasts   │  ← Predict differenced series
└─────────────────────────┘
      │
      ▼
┌─────────────────────────┐
│ 7. Undifference         │  ← Cumulative sum to undo differencing
└─────────────────────────┘
      │
      ▼
┌─────────────────────────┐
│ 8. Reseasonalize        │  ← Multiply by seasonal factors
└─────────────────────────┘
      │
      ▼
┌─────────────────────────┐
│ 9. Add back Easter      │  ← For forecast months with Easter
│    effect               │
└─────────────────────────┘
      │
      ▼
Final Forecast
```

## 1. Easter Date Calculation (Computus)

The Anonymous Gregorian algorithm calculates Easter Sunday:

```rust
pub fn easter_sunday(year: i32) -> (u32, u32) {
    let a = year % 19;
    let b = year / 100;
    let c = year % 100;
    let d = b / 4;
    let e = b % 4;
    let f = (b + 8) / 25;
    let g = (b - f + 1) / 3;
    let h = (19 * a + b - d - g + 15) % 30;
    let i = c / 4;
    let k = c % 4;
    let l = (32 + 2 * e + 2 * i - h - k) % 7;
    let m = (a + 11 * h + 22 * l) / 451;
    let month = (h + l - 7 * m + 114) / 31;
    let day = ((h + l - 7 * m + 114) % 31) + 1;
    
    (month as u32, day as u32)
}
```

The **invoice month** is 3 months before Easter (business orders ahead of Easter sales):

```rust
pub fn easter_invoice_month(easter_year: i32) -> (i32, u32) {
    let (month, _day) = easter_sunday(easter_year);
    let invoice_month = if month <= 3 {
        (easter_year - 1, month + 9)  // Jan-Mar Easter → Oct-Dec previous year
    } else {
        (easter_year, month - 3)       // Apr Easter → Jan same year
    };
    invoice_month
}
```

### Easter Regressor Array

For a time series starting at `(start_year, start_month)` with length `n`:

```rust
pub fn create_easter_regressor(start_year: i32, start_month: u32, length: usize) -> Vec<f64> {
    let mut regressor = vec![0.0; length];
    
    // Pre-calculate Easter invoice months for all relevant years
    let mut easter_months = HashSet::new();
    for year in start_year..(start_year + (length as i32 / 12) + 3) {
        easter_months.insert(easter_invoice_month(year));
    }
    
    // Fill regressor
    let mut year = start_year;
    let mut month = start_month;
    
    for i in 0..length {
        if easter_months.contains(&(year, month)) {
            regressor[i] = 1.0;
        }
        
        month += 1;
        if month > 12 {
            month = 1;
            year += 1;
        }
    }
    
    regressor
}
```

## 2. Regress Out Exogenous Variables

For sparse binary regressors like Easter, use mean-difference estimation:

```
coefficient = mean(Y where X=1) - mean(Y where X=0)
```

Then subtract the effect from affected observations:

```rust
fn regress_out_exogenous(series: &[f64], exog: &[f64]) -> (Vec<f64>, f64) {
    let mut residuals = series.to_vec();
    
    // Separate observations by exog value
    let with_exog: Vec<f64> = series.iter().zip(exog.iter())
        .filter(|(_, &x)| x > 0.5)
        .map(|(&y, _)| y)
        .collect();
    
    let without_exog: Vec<f64> = series.iter().zip(exog.iter())
        .filter(|(_, &x)| x <= 0.5)
        .map(|(&y, _)| y)
        .collect();
    
    let coefficient = if !with_exog.is_empty() && !without_exog.is_empty() {
        mean(&with_exog) - mean(&without_exog)
    } else {
        0.0
    };
    
    // Remove effect from affected observations
    for (i, &x) in exog.iter().enumerate() {
        if x > 0.5 {
            residuals[i] -= coefficient;
        }
    }
    
    (residuals, coefficient)
}
```

## 3. Seasonal Factors (Multiplicative)

Calculate average value for each month, then normalize by overall average:

```rust
fn calculate_seasonal_factors(series: &[f64], period: usize) -> Vec<f64> {
    let mut sums = vec![0.0; period];
    let mut counts = vec![0usize; period];
    
    // Only include positive values
    let overall_mean = series.iter().filter(|&&x| x > 0.0).sum::<f64>() 
        / series.iter().filter(|&&x| x > 0.0).count() as f64;
    
    for (i, &value) in series.iter().enumerate() {
        if value > 0.0 {
            let month_idx = i % period;
            sums[month_idx] += value;
            counts[month_idx] += 1;
        }
    }
    
    // Factor = month_average / overall_average
    (0..period).map(|i| {
        if counts[i] > 0 {
            (sums[i] / counts[i] as f64) / overall_mean
        } else {
            1.0
        }
    }).collect()
}
```

## 4. Deseasonalize / Reseasonalize

```rust
fn deseasonalize(series: &[f64], factors: &[f64]) -> Vec<f64> {
    series.iter().enumerate().map(|(i, &value)| {
        let factor = factors[i % factors.len()];
        if factor > 0.0 { value / factor } else { value }
    }).collect()
}

fn reseasonalize(series: &[f64], factors: &[f64], start_month: usize) -> Vec<f64> {
    series.iter().enumerate().map(|(i, &value)| {
        let month_idx = (start_month + i) % factors.len();
        value * factors[month_idx]
    }).collect()
}
```

## 5. Differencing

First-order differencing (d=1):

```rust
fn difference(series: &[f64], d: usize) -> Vec<f64> {
    let mut result = series.to_vec();
    
    for _ in 0..d {
        let temp: Vec<f64> = (1..result.len())
            .map(|i| result[i] - result[i-1])
            .collect();
        result = temp;
    }
    
    result
}

fn undifference(differenced: &[f64], original: &[f64], d: usize) -> Vec<f64> {
    let mut result = differenced.to_vec();
    
    for _ in 0..d {
        let last_original = original[original.len() - 1];
        let mut cumsum = vec![last_original + result[0]];
        
        for i in 1..result.len() {
            cumsum.push(cumsum[i-1] + result[i]);
        }
        result = cumsum;
    }
    
    result
}
```

## 6. Autocorrelation

```rust
fn autocorrelation(series: &[f64], max_lag: usize) -> Vec<f64> {
    let n = series.len();
    let mean = series.iter().sum::<f64>() / n as f64;
    let centered: Vec<f64> = series.iter().map(|&x| x - mean).collect();
    
    let variance: f64 = centered.iter().map(|&x| x * x).sum::<f64>() / n as f64;
    
    if variance < 1e-10 {
        let mut result = vec![0.0; max_lag + 1];
        result[0] = 1.0;
        return result;
    }
    
    (0..=max_lag).map(|lag| {
        let sum: f64 = (lag..n)
            .map(|i| centered[i] * centered[i - lag])
            .sum();
        sum / (n as f64 * variance)
    }).collect()
}
```

## 7. Yule-Walker via Levinson-Durbin

Solve for AR coefficients:

```rust
fn solve_yule_walker(autocorr: &[f64]) -> Vec<f64> {
    let p = autocorr.len() - 1;
    if p == 0 {
        return vec![];
    }
    
    let mut phi = vec![0.0; p];
    let mut phi_prev = vec![0.0; p];
    
    // Initialize
    phi[0] = autocorr[1];
    let mut v = 1.0 - phi[0] * phi[0];
    
    for i in 1..p {
        phi_prev.copy_from_slice(&phi);
        
        // Calculate reflection coefficient
        let mut num = autocorr[i + 1];
        for j in 0..i {
            num -= phi_prev[j] * autocorr[i - j];
        }
        
        phi[i] = num / v;
        
        // Update coefficients
        for j in 0..i {
            phi[j] = phi_prev[j] - phi[i] * phi_prev[i - 1 - j];
        }
        
        v *= 1.0 - phi[i] * phi[i];
        if v < 1e-10 {
            break;
        }
    }
    
    phi
}
```

## 8. MA Coefficient Estimation

Simplified estimation from residual autocorrelation:

```rust
fn estimate_ma_coefficients(residuals: &[f64], q: usize) -> Vec<f64> {
    if q == 0 {
        return vec![];
    }
    
    let autocorr = autocorrelation(residuals, q);
    
    // Simplified: MA coefficient ≈ 0.5 × residual autocorrelation
    (1..=q).map(|i| autocorr[i] * 0.5).collect()
}
```

## 9. Residual Calculation

```rust
fn calculate_residuals(series: &[f64], ar_coeffs: &[f64]) -> Vec<f64> {
    series.iter().enumerate().map(|(i, &value)| {
        let predicted: f64 = ar_coeffs.iter().enumerate()
            .filter(|(j, _)| i > *j)
            .map(|(j, &coef)| coef * series[i - j - 1])
            .sum();
        value - predicted
    }).collect()
}
```

## 10. Forecast Generation

```rust
fn generate_forecast(
    differenced: &[f64],
    ar_coeffs: &[f64],
    ma_coeffs: &[f64],
    residuals: &[f64],
    intercept: f64,
    steps: usize
) -> Vec<f64> {
    let mut extended = differenced.to_vec();
    let mut extended_residuals = residuals.to_vec();
    
    for _ in 0..steps {
        let mut prediction = intercept;
        
        // AR component
        for (i, &coef) in ar_coeffs.iter().enumerate() {
            if i < extended.len() {
                prediction += coef * (extended[extended.len() - 1 - i] - intercept);
            }
        }
        
        // MA component (residuals are 0 for future)
        for (i, &coef) in ma_coeffs.iter().enumerate() {
            let res_idx = extended_residuals.len() - 1 - i;
            if res_idx < residuals.len() {
                prediction += coef * extended_residuals[res_idx];
            }
        }
        
        extended.push(prediction);
        extended_residuals.push(0.0);  // Future residuals are 0
    }
    
    extended[differenced.len()..].to_vec()
}
```

## 11. Confidence Intervals

```rust
fn confidence_intervals(
    forecast: &[f64],
    residuals: &[f64],
    seasonal_factors: &[f64],
    start_month: usize,
    confidence: f64
) -> (Vec<f64>, Vec<f64>) {
    // Standard error from residuals
    let se = (residuals.iter().map(|&r| r * r).sum::<f64>() / residuals.len() as f64).sqrt();
    
    // Z-score for confidence level
    let z = match confidence {
        c if (c - 0.99).abs() < 0.001 => 2.576,
        c if (c - 0.95).abs() < 0.001 => 1.96,
        c if (c - 0.90).abs() < 0.001 => 1.645,
        c if (c - 0.80).abs() < 0.001 => 1.28,
        _ => 1.96
    };
    
    let lower: Vec<f64> = forecast.iter().enumerate().map(|(i, &f)| {
        // Error grows with horizon
        let horizon_se = se * (1.0 + i as f64 * 0.1).sqrt();
        // Scale by seasonal factor
        let seasonal_scale = seasonal_factors[(start_month + i) % seasonal_factors.len()];
        let interval = z * horizon_se * seasonal_scale;
        (f - interval).max(0.0)
    }).collect();
    
    let upper: Vec<f64> = forecast.iter().enumerate().map(|(i, &f)| {
        let horizon_se = se * (1.0 + i as f64 * 0.1).sqrt();
        let seasonal_scale = seasonal_factors[(start_month + i) % seasonal_factors.len()];
        let interval = z * horizon_se * seasonal_scale;
        f + interval
    }).collect();
    
    (lower, upper)
}
```

## Complete Pipeline

```rust
pub fn fit_and_forecast(
    series: &[f64],
    start_year: i32,
    start_month: u32,
    forecast_months: usize,
    use_easter: bool
) -> ForecastResult {
    let p = 2;
    let d = 1;
    let q = 1;
    let seasonal_period = 12;
    
    // 1. Easter regressor (if enabled)
    let (adjusted_series, easter_coef) = if use_easter {
        let regressor = create_easter_regressor(start_year, start_month, series.len());
        regress_out_exogenous(series, &regressor)
    } else {
        (series.to_vec(), 0.0)
    };
    
    // 2. Seasonal factors
    let seasonal_factors = calculate_seasonal_factors(&adjusted_series, seasonal_period);
    
    // 3. Deseasonalize
    let deseasonalized = deseasonalize(&adjusted_series, &seasonal_factors);
    
    // 4. Difference
    let differenced = difference(&deseasonalized, d);
    
    // 5. Estimate coefficients
    let intercept = differenced.iter().sum::<f64>() / differenced.len() as f64;
    let centered: Vec<f64> = differenced.iter().map(|&x| x - intercept).collect();
    let autocorr = autocorrelation(&centered, p);
    let ar_coeffs = solve_yule_walker(&autocorr);
    let residuals = calculate_residuals(&centered, &ar_coeffs);
    let ma_coeffs = estimate_ma_coefficients(&residuals, q);
    
    // 6. Generate forecast (differenced space)
    let forecast_diff = generate_forecast(
        &differenced, &ar_coeffs, &ma_coeffs, &residuals, intercept, forecast_months
    );
    
    // 7. Undifference
    let forecast_deseas = undifference(&forecast_diff, &deseasonalized, d);
    
    // 8. Reseasonalize
    let start_month_idx = series.len() % seasonal_period;
    let forecast_seasonal = reseasonalize(&forecast_deseas, &seasonal_factors, start_month_idx);
    
    // 9. Add back Easter effect
    let last_month = start_month as usize + series.len() - 1;
    let last_year = start_year + (last_month / 12) as i32;
    let last_month_of_year = ((last_month - 1) % 12) + 1;
    
    let future_easter = create_easter_regressor(
        last_year, 
        (last_month_of_year + 1) as u32,  // Start from month after last historical
        forecast_months
    );
    
    let forecast: Vec<f64> = forecast_seasonal.iter().enumerate().map(|(i, &f)| {
        let with_easter = f + easter_coef * future_easter[i];
        with_easter.max(0.0)
    }).collect();
    
    // 10. Confidence intervals
    let (lower, upper) = confidence_intervals(
        &forecast, &residuals, &seasonal_factors, start_month_idx, 0.80
    );
    
    ForecastResult {
        forecast,
        lower,
        upper,
        seasonal_factors,
        easter_coefficient: easter_coef,
        ar_coefficients: ar_coeffs,
        ma_coefficients: ma_coeffs,
        intercept,
    }
}
```

## Validation

The C# code includes a validation test that:
1. Generates synthetic data with a known Easter coefficient (500.0)
2. Fits the ARIMAX model
3. Checks that estimated coefficient is within 20% of true value

Run this in Rust to verify the port is correct:

```rust
#[test]
fn test_easter_coefficient_estimation() {
    // Known parameters
    let base_value = 1000.0;
    let true_easter_coef = 500.0;
    let months = 84;  // 7 years
    
    // Sparse exogenous spikes at indices 3, 17, 29, 41, 55, 67, 79
    let spike_months = vec![3, 17, 29, 41, 55, 67, 79];
    
    // Generate synthetic series
    let mut rng = /* seeded RNG with seed 42 */;
    let mut series = vec![0.0; months];
    let mut exog = vec![0.0; months];
    
    for (i, x) in series.iter_mut().enumerate() {
        *x = base_value + (i as f64 * 2.0);  // Trend
        *x *= 1.0 + 0.1 * (2.0 * PI * (i % 12) as f64 / 12.0).sin();  // Seasonal
        if spike_months.contains(&i) {
            exog[i] = 1.0;
            *x += true_easter_coef;
        }
        *x += rng.gen_range(-50.0..50.0);  // Noise
    }
    
    let (_, estimated_coef) = regress_out_exogenous(&series, &exog);
    
    let error_pct = ((estimated_coef - true_easter_coef).abs() / true_easter_coef) * 100.0;
    assert!(error_pct < 20.0, "Easter coefficient error: {}%", error_pct);
}
```
