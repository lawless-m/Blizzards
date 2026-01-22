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
        
        // TODO: Implement the fitting procedure
        // 1. Regress out exogenous effects (if present)
        // 2. Calculate seasonal factors
        // 3. Deseasonalize
        // 4. Apply differencing
        // 5. Estimate AR/MA coefficients
        
        unimplemented!("Port from C# Arima.cs - see reference/Arima.cs")
    }

    /// Generate forecasts
    pub fn forecast(&self, steps: usize) -> Vec<f64> {
        self.forecast_with_exog(steps, None)
    }

    /// Generate forecasts with future exogenous values
    pub fn forecast_with_exog(&self, steps: usize, future_exog: Option<&[f64]>) -> Vec<f64> {
        // TODO: Implement forecast generation
        // 1. Forecast differenced series
        // 2. Undifference
        // 3. Reseasonalize
        // 4. Add back exogenous effects
        
        unimplemented!("Port from C# Arima.cs - see reference/Arima.cs")
    }

    /// Calculate confidence intervals for forecasts
    pub fn confidence_intervals(&self, steps: usize, confidence: f64) -> (Vec<f64>, Vec<f64>) {
        // TODO: Implement confidence interval calculation
        unimplemented!("Port from C# Arima.cs - see reference/Arima.cs")
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
    // TODO: Implement
    // coefficient = mean(Y where X=1) - mean(Y where X=0)
    // Then subtract effect from affected observations
    unimplemented!()
}

/// Calculate multiplicative seasonal factors
fn calculate_seasonal_factors(series: &[f64], period: usize) -> Vec<f64> {
    // TODO: Implement
    // 1. Calculate average value for each month (only positive values)
    // 2. Calculate overall average
    // 3. Factor = month_average / overall_average
    unimplemented!()
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
    // TODO: Implement
    // Cumulative sum starting from last original value
    unimplemented!()
}

/// Calculate autocorrelation function up to max_lag
fn autocorrelation(series: &[f64], max_lag: usize) -> Vec<f64> {
    // TODO: Implement
    unimplemented!()
}

/// Solve Yule-Walker equations using Levinson-Durbin algorithm
fn solve_yule_walker(autocorr: &[f64]) -> Vec<f64> {
    // TODO: Implement Levinson-Durbin
    // This is the key algorithm for AR coefficient estimation
    unimplemented!()
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
    // TODO: Implement
    // Simplified: MA coefficient ≈ 0.5 × residual autocorrelation
    unimplemented!()
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
