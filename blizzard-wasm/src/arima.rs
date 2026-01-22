//! ARIMA(p,d,q) model implementation with seasonal support and ARIMAX extensions
//!
//! This module ports the C# Arima.cs implementation to Rust for WASM compilation.
//! The model uses:
//! - p=2 AR terms
//! - d=1 differencing
//! - q=1 MA term
//! - Seasonal period of 12 (monthly data)
//! - Optional Easter regressor for ARIMAX

use crate::easter::create_easter_regressor;

/// Result of fitting and forecasting with ARIMA
#[derive(Debug, Clone)]
pub struct ForecastResult {
    /// Point forecasts for each future period
    pub forecast: Vec<f64>,
    /// Lower bound of 80% confidence interval
    pub lower: Vec<f64>,
    /// Upper bound of 80% confidence interval
    pub upper: Vec<f64>,
    /// Seasonal factors (12 values for monthly data)
    pub seasonal_factors: Vec<f64>,
    /// Estimated Easter effect coefficient (if ARIMAX)
    pub easter_coefficient: f64,
    /// Estimated AR coefficients
    pub ar_coefficients: Vec<f64>,
    /// Estimated MA coefficients
    pub ma_coefficients: Vec<f64>,
    /// Model intercept
    pub intercept: f64,
}

/// ARIMA model with optional exogenous variables
pub struct Arima {
    p: usize,              // AR order
    d: usize,              // Differencing order
    q: usize,              // MA order
    seasonal_period: usize, // Seasonal period (12 for monthly)
    
    // Fitted values (populated after fit())
    ar_coeffs: Vec<f64>,
    ma_coeffs: Vec<f64>,
    seasonal_factors: Vec<f64>,
    intercept: f64,
    original_series: Vec<f64>,
    differenced_series: Vec<f64>,
    residuals: Vec<f64>,
    
    // ARIMAX support
    exog_coeffs: Vec<f64>,
    exog_data: Option<Vec<f64>>,
}

impl Arima {
    /// Create a new ARIMA model with specified parameters
    pub fn new(p: usize, d: usize, q: usize, seasonal_period: usize) -> Self {
        Arima {
            p,
            d,
            q,
            seasonal_period,
            ar_coeffs: vec![],
            ma_coeffs: vec![],
            seasonal_factors: vec![],
            intercept: 0.0,
            original_series: vec![],
            differenced_series: vec![],
            residuals: vec![],
            exog_coeffs: vec![],
            exog_data: None,
        }
    }

    /// Fit the model to a time series
    pub fn fit(&mut self, series: &[f64]) {
        self.fit_with_exog(series, None);
    }

    /// Fit the model with optional exogenous variables
    pub fn fit_with_exog(&mut self, series: &[f64], exog: Option<&[f64]>) {
        self.original_series = series.to_vec();
        self.exog_data = exog.map(|e| e.to_vec());

        // 1. Regress out exogenous effects (if present)
        let adjusted_series = if let Some(exog_data) = exog {
            let (adj, coef) = regress_out_exogenous(series, exog_data);
            self.exog_coeffs = vec![coef];
            adj
        } else {
            self.exog_coeffs = vec![];
            series.to_vec()
        };

        // 2. Calculate seasonal factors
        self.seasonal_factors = calculate_seasonal_factors(&adjusted_series, self.seasonal_period);

        // 3. Deseasonalize
        let deseasonalized = deseasonalize(&adjusted_series, &self.seasonal_factors);

        // 4. Apply differencing
        self.differenced_series = difference(&deseasonalized, self.d);

        // 5. Estimate AR/MA coefficients
        self.intercept = mean(&self.differenced_series);
        let centered: Vec<f64> = self.differenced_series.iter()
            .map(|&x| x - self.intercept)
            .collect();

        // Yule-Walker for AR coefficients
        if self.p > 0 {
            let autocorr = autocorrelation(&centered, self.p);
            self.ar_coeffs = solve_yule_walker(&autocorr);
        } else {
            self.ar_coeffs = vec![];
        }

        // Calculate residuals for MA estimation
        self.residuals = calculate_residuals(&centered, &self.ar_coeffs);

        // Estimate MA coefficients from residual autocorrelation
        if self.q > 0 {
            self.ma_coeffs = estimate_ma_coefficients(&self.residuals, self.q);
        } else {
            self.ma_coeffs = vec![];
        }
    }

    /// Generate forecasts
    pub fn forecast(&self, steps: usize) -> Vec<f64> {
        self.forecast_with_exog(steps, None)
    }

    /// Generate forecasts with future exogenous values
    pub fn forecast_with_exog(&self, steps: usize, future_exog: Option<&[f64]>) -> Vec<f64> {
        // Work with differenced series
        let mut extended = self.differenced_series.clone();
        let mut extended_residuals = self.residuals.clone();

        // 1. Forecast differenced series
        for _ in 0..steps {
            let mut prediction = self.intercept;

            // AR component
            for (i, &coef) in self.ar_coeffs.iter().enumerate() {
                if i < extended.len() {
                    prediction += coef * (extended[extended.len() - 1 - i] - self.intercept);
                }
            }

            // MA component (residuals decay to 0 for forecasts)
            for (i, &coef) in self.ma_coeffs.iter().enumerate() {
                let res_idx = extended_residuals.len() - 1 - i;
                if res_idx < self.residuals.len() {
                    prediction += coef * extended_residuals[res_idx];
                }
            }

            extended.push(prediction);
            extended_residuals.push(0.0); // Future residuals are 0
        }

        // Extract forecast values
        let forecast_diff: Vec<f64> = extended[self.differenced_series.len()..].to_vec();

        // 2. Undifference - get deseasonalized original series first
        let deseasonalized: Vec<f64> = self.original_series.iter().enumerate()
            .map(|(i, &v)| {
                let factor = self.seasonal_factors[i % self.seasonal_period];
                if factor > 0.0 { v / factor } else { v }
            })
            .collect();
        let forecast_deseas = undifference(&forecast_diff, &deseasonalized, self.d);

        // 3. Reseasonalize
        let start_month = self.original_series.len() % self.seasonal_period;
        let mut forecast = reseasonalize(&forecast_deseas, &self.seasonal_factors, start_month);

        // 4. Add back exogenous effects for future periods
        if let Some(future_exog_data) = future_exog {
            if !self.exog_coeffs.is_empty() {
                for (s, f) in forecast.iter_mut().enumerate() {
                    if s < future_exog_data.len() {
                        *f += self.exog_coeffs[0] * future_exog_data[s];
                    }
                }
            }
        }

        // Ensure non-negative
        forecast.iter().map(|&x| x.max(0.0)).collect()
    }

    /// Calculate confidence intervals for forecasts
    pub fn confidence_intervals(&self, steps: usize, confidence: f64) -> (Vec<f64>, Vec<f64>) {
        let forecast = self.forecast(steps);

        // Estimate standard error from residuals
        let se = (self.residuals.iter().map(|&r| r * r).sum::<f64>() / self.residuals.len().max(1) as f64).sqrt();

        // Z-score for confidence level
        let z = if (confidence - 0.99).abs() < 0.001 {
            2.576
        } else if (confidence - 0.95).abs() < 0.001 {
            1.96
        } else if (confidence - 0.90).abs() < 0.001 {
            1.645
        } else if (confidence - 0.80).abs() < 0.001 {
            1.28
        } else {
            1.96
        };

        let lower: Vec<f64> = forecast.iter().enumerate().map(|(i, &f)| {
            // Error grows with forecast horizon
            let horizon_se = se * (1.0 + i as f64 * 0.1).sqrt();
            // Scale by seasonal factor for proper interval width
            let seasonal_scale = self.seasonal_factors[(self.original_series.len() + i) % self.seasonal_period];
            let interval = z * horizon_se * seasonal_scale;
            (f - interval).max(0.0)
        }).collect();

        let upper: Vec<f64> = forecast.iter().enumerate().map(|(i, &f)| {
            let horizon_se = se * (1.0 + i as f64 * 0.1).sqrt();
            let seasonal_scale = self.seasonal_factors[(self.original_series.len() + i) % self.seasonal_period];
            let interval = z * horizon_se * seasonal_scale;
            f + interval
        }).collect();

        (lower, upper)
    }

    /// Get the estimated exogenous coefficients
    pub fn exog_coefficients(&self) -> &[f64] {
        &self.exog_coeffs
    }
}

// ============================================================================
// Helper functions - implement these first, they're used by Arima
// ============================================================================

/// Calculate mean of a slice
fn mean(data: &[f64]) -> f64 {
    if data.is_empty() {
        return 0.0;
    }
    data.iter().sum::<f64>() / data.len() as f64
}

/// Regress out exogenous variables using mean-difference approach
///
/// For sparse binary exogenous variables (like Easter), this is more stable
/// than standard OLS within ARIMAX.
///
/// Returns (adjusted_series, coefficient)
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

/// Calculate multiplicative seasonal factors
fn calculate_seasonal_factors(series: &[f64], period: usize) -> Vec<f64> {
    let mut sums = vec![0.0; period];
    let mut counts = vec![0usize; period];

    // Only include positive values
    let overall_mean = series.iter().filter(|&&x| x > 0.0).sum::<f64>()
        / series.iter().filter(|&&x| x > 0.0).count().max(1) as f64;

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

/// Deseasonalize series by dividing by seasonal factors
fn deseasonalize(series: &[f64], factors: &[f64]) -> Vec<f64> {
    series.iter().enumerate().map(|(i, &value)| {
        let factor = factors[i % factors.len()];
        if factor > 0.0 { value / factor } else { value }
    }).collect()
}

/// Reseasonalize series by multiplying by seasonal factors
fn reseasonalize(series: &[f64], factors: &[f64], start_idx: usize) -> Vec<f64> {
    series.iter().enumerate().map(|(i, &value)| {
        let factor_idx = (start_idx + i) % factors.len();
        value * factors[factor_idx]
    }).collect()
}

/// Apply d-order differencing
fn difference(series: &[f64], d: usize) -> Vec<f64> {
    let mut result = series.to_vec();
    
    for _ in 0..d {
        let temp: Vec<f64> = (1..result.len())
            .map(|i| result[i] - result[i - 1])
            .collect();
        result = temp;
    }
    
    result
}

/// Undo differencing to get back to original scale
fn undifference(differenced: &[f64], original: &[f64], d: usize) -> Vec<f64> {
    if d == 0 {
        return differenced.to_vec();
    }

    let mut result = differenced.to_vec();

    for ord in 0..d {
        let last_value = original[original.len() - 1 - (d - ord - 1)];
        let mut cumsum = vec![last_value + result[0]];

        for i in 1..result.len() {
            cumsum.push(cumsum[i - 1] + result[i]);
        }
        result = cumsum;
    }

    result
}

/// Calculate autocorrelation function up to max_lag
fn autocorrelation(series: &[f64], max_lag: usize) -> Vec<f64> {
    let n = series.len();
    let variance = series.iter().map(|&x| x * x).sum::<f64>() / n as f64;

    if variance < 1e-10 {
        let mut result = vec![0.0; max_lag + 1];
        result[0] = 1.0;
        return result;
    }

    (0..=max_lag).map(|lag| {
        let sum: f64 = (lag..n)
            .map(|i| series[i] * series[i - lag])
            .sum();
        sum / (n as f64 * variance)
    }).collect()
}

/// Solve Yule-Walker equations using Levinson-Durbin algorithm
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

/// Calculate residuals from AR model
fn calculate_residuals(series: &[f64], ar_coeffs: &[f64]) -> Vec<f64> {
    series.iter().enumerate().map(|(i, &value)| {
        let predicted: f64 = ar_coeffs.iter().enumerate()
            .filter(|(j, _)| i > *j)
            .map(|(j, &coef)| coef * series[i - j - 1])
            .sum();
        value - predicted
    }).collect()
}

/// Estimate MA coefficients from residual autocorrelation
fn estimate_ma_coefficients(residuals: &[f64], q: usize) -> Vec<f64> {
    if q == 0 {
        return vec![];
    }

    let autocorr = autocorrelation(residuals, q);

    // Simplified: MA coefficient ≈ 0.5 × residual autocorrelation
    (1..=q).map(|i| autocorr[i] * 0.5).collect()
}

// ============================================================================
// High-level convenience function
// ============================================================================

/// Fit ARIMA model and generate forecast in one call
///
/// This is the main entry point for the WASM interface.
pub fn fit_and_forecast(
    series: &[f64],
    start_year: i32,
    start_month: u32,
    forecast_months: usize,
    use_easter: bool,
) -> ForecastResult {
    let mut model = Arima::new(2, 1, 1, 12);
    
    let (easter_coef, adjusted_series) = if use_easter {
        let regressor = create_easter_regressor(start_year, start_month, series.len());
        let (adj, coef) = regress_out_exogenous(series, &regressor);
        (coef, adj)
    } else {
        (0.0, series.to_vec())
    };
    
    model.fit(&adjusted_series);
    
    // Generate future Easter regressor if needed
    let forecast = if use_easter {
        let last_idx = series.len();
        let last_month = ((start_month as usize - 1 + last_idx) % 12 + 1) as u32;
        let years_elapsed = (start_month as usize - 1 + last_idx) / 12;
        let last_year = start_year + years_elapsed as i32;
        
        let future_easter = create_easter_regressor(last_year, last_month + 1, forecast_months);
        model.forecast_with_exog(forecast_months, Some(&future_easter))
    } else {
        model.forecast(forecast_months)
    };
    
    let (lower, upper) = model.confidence_intervals(forecast_months, 0.80);
    
    ForecastResult {
        forecast,
        lower,
        upper,
        seasonal_factors: model.seasonal_factors.clone(),
        easter_coefficient: easter_coef,
        ar_coefficients: model.ar_coeffs.clone(),
        ma_coefficients: model.ma_coeffs.clone(),
        intercept: model.intercept,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_mean() {
        assert!((mean(&[1.0, 2.0, 3.0, 4.0, 5.0]) - 3.0).abs() < 1e-10);
        assert!(mean(&[]).abs() < 1e-10);
    }

    #[test]
    fn test_deseasonalize() {
        let series = vec![100.0, 120.0, 90.0, 110.0];
        let factors = vec![1.0, 1.2, 0.9, 1.1];
        let result = deseasonalize(&series, &factors);
        
        // Each value should become 100.0 after deseasonalization
        for &val in &result {
            assert!((val - 100.0).abs() < 1e-10);
        }
    }

    #[test]
    fn test_difference() {
        let series = vec![10.0, 12.0, 15.0, 14.0, 18.0];
        let diff = difference(&series, 1);
        
        // First differences: [2, 3, -1, 4]
        assert_eq!(diff.len(), 4);
        assert!((diff[0] - 2.0).abs() < 1e-10);
        assert!((diff[1] - 3.0).abs() < 1e-10);
        assert!((diff[2] - (-1.0)).abs() < 1e-10);
        assert!((diff[3] - 4.0).abs() < 1e-10);
    }

    // TODO: Add more tests as functions are implemented
}
