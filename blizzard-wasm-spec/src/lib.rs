//! Blizzard WASM - ARIMA forecasting for web browsers
//!
//! This crate provides ARIMA(2,1,1) time series forecasting with:
//! - Seasonal decomposition (period 12 for monthly data)
//! - Easter regressor support (ARIMAX)
//! - 80% confidence intervals
//!
//! The interface uses JSON for input/output to keep the WASM boundary simple.

use wasm_bindgen::prelude::*;
use serde::{Deserialize, Serialize};

mod arima;
mod easter;

/// Input structure for forecast requests
#[derive(Deserialize)]
pub struct ForecastInput {
    /// Time series values
    pub series: Vec<f64>,
    /// Start year of the series
    pub start_year: i32,
    /// Start month of the series (1-12)
    pub start_month: u32,
    /// Number of months to forecast
    pub forecast_months: usize,
    /// AR order (default: 2)
    #[serde(default = "default_p")]
    pub p: usize,
    /// Differencing order (default: 1)
    #[serde(default = "default_d")]
    pub d: usize,
    /// MA order (default: 1)
    #[serde(default = "default_q")]
    pub q: usize,
    /// Seasonal period (default: 12)
    #[serde(default = "default_seasonal_period")]
    pub seasonal_period: usize,
    /// Whether to use Easter regressor (default: true)
    #[serde(default = "default_use_easter")]
    pub use_easter_regressor: bool,
}

fn default_p() -> usize { 2 }
fn default_d() -> usize { 1 }
fn default_q() -> usize { 1 }
fn default_seasonal_period() -> usize { 12 }
fn default_use_easter() -> bool { true }

/// Output structure for forecast results
#[derive(Serialize)]
pub struct ForecastOutput {
    /// Point forecasts
    pub forecast: Vec<f64>,
    /// Lower bound of confidence interval
    pub lower: Vec<f64>,
    /// Upper bound of confidence interval
    pub upper: Vec<f64>,
    /// Seasonal factors (12 values)
    pub seasonal_factors: Vec<f64>,
    /// Easter coefficient (if ARIMAX)
    pub easter_coefficient: f64,
    /// AR coefficients
    pub ar_coefficients: Vec<f64>,
    /// MA coefficients
    pub ma_coefficients: Vec<f64>,
    /// Model intercept
    pub intercept: f64,
}

/// Main WASM entry point for forecasting
///
/// Takes JSON input, returns JSON output.
///
/// # Example
///
/// ```javascript
/// const input = {
///   series: [1000, 1200, 1100, ...],  // 60+ months of data
///   start_year: 2019,
///   start_month: 1,
///   forecast_months: 12,
///   use_easter_regressor: true
/// };
///
/// const result = JSON.parse(forecast(JSON.stringify(input)));
/// console.log(result.forecast);  // [1500, 1600, ...]
/// ```
#[wasm_bindgen]
pub fn forecast(input_json: &str) -> String {
    // Parse input
    let input: ForecastInput = match serde_json::from_str(input_json) {
        Ok(i) => i,
        Err(e) => {
            return serde_json::to_string(&ErrorOutput {
                error: format!("Failed to parse input: {}", e),
            }).unwrap_or_else(|_| r#"{"error":"Failed to serialize error"}"#.to_string());
        }
    };

    // Validate input
    if input.series.len() < input.p + input.d + input.q + input.seasonal_period {
        return serde_json::to_string(&ErrorOutput {
            error: "Series too short for specified ARIMA parameters".to_string(),
        }).unwrap_or_else(|_| r#"{"error":"Series too short"}"#.to_string());
    }

    // Run forecast
    let result = arima::fit_and_forecast(
        &input.series,
        input.start_year,
        input.start_month,
        input.forecast_months,
        input.use_easter_regressor,
    );

    // Convert to output format
    let output = ForecastOutput {
        forecast: result.forecast,
        lower: result.lower,
        upper: result.upper,
        seasonal_factors: result.seasonal_factors,
        easter_coefficient: result.easter_coefficient,
        ar_coefficients: result.ar_coefficients,
        ma_coefficients: result.ma_coefficients,
        intercept: result.intercept,
    };

    // Serialize output
    serde_json::to_string(&output)
        .unwrap_or_else(|_| r#"{"error":"Failed to serialize output"}"#.to_string())
}

/// Error output structure
#[derive(Serialize)]
struct ErrorOutput {
    error: String,
}

/// Get Easter dates for a range of years (utility function)
///
/// Returns JSON array of objects with year, easter_month, easter_day, invoice_month
#[wasm_bindgen]
pub fn get_easter_dates(start_year: i32, end_year: i32) -> String {
    #[derive(Serialize)]
    struct EasterDate {
        year: i32,
        easter_month: u32,
        easter_day: u32,
        invoice_year: i32,
        invoice_month: u32,
    }

    let dates: Vec<EasterDate> = (start_year..=end_year)
        .map(|year| {
            let (month, day) = easter::easter_sunday(year);
            let (inv_year, inv_month) = easter::easter_invoice_month(year);
            EasterDate {
                year,
                easter_month: month,
                easter_day: day,
                invoice_year: inv_year,
                invoice_month: inv_month,
            }
        })
        .collect();

    serde_json::to_string(&dates)
        .unwrap_or_else(|_| "[]".to_string())
}

/// Version information
#[wasm_bindgen]
pub fn version() -> String {
    env!("CARGO_PKG_VERSION").to_string()
}

// ============================================================================
// Tests
// ============================================================================

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_forecast_input_parsing() {
        let json = r#"{
            "series": [100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210,
                       220, 230, 240, 250, 260, 270, 280, 290, 300, 310, 320, 330],
            "start_year": 2022,
            "start_month": 1,
            "forecast_months": 12
        }"#;

        let input: ForecastInput = serde_json::from_str(json).unwrap();
        assert_eq!(input.series.len(), 24);
        assert_eq!(input.start_year, 2022);
        assert_eq!(input.p, 2);  // default
        assert!(input.use_easter_regressor);  // default
    }

    #[test]
    fn test_get_easter_dates() {
        let result = get_easter_dates(2024, 2026);
        assert!(result.contains("2024"));
        assert!(result.contains("2025"));
        assert!(result.contains("2026"));
    }
}
