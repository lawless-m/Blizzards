//! Easter date calculation using the Anonymous Gregorian algorithm (Computus)
//! 
//! This is used to create the Easter regressor for ARIMAX models.
//! Easter-related sales show up 3 months before Easter (invoice lag).

use std::collections::HashSet;

/// Calculate Easter Sunday for a given year using the Anonymous Gregorian algorithm
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

/// Get the invoice month for Easter (3 months before)
/// 
/// Returns (year, month) for when Easter-related orders are placed.
pub fn easter_invoice_month(easter_year: i32) -> (i32, u32) {
    let (month, _day) = easter_sunday(easter_year);
    
    // Subtract 3 months, handling year boundary
    if month <= 3 {
        (easter_year - 1, month + 9)  // Jan-Mar → Oct-Dec previous year
    } else {
        (easter_year, month - 3)
    }
}

/// Create Easter regressor array for a time series
/// 
/// Returns a vector of 1.0 for months that are Easter invoice months, 0.0 otherwise.
/// 
/// # Arguments
/// * `start_year` - First year of the time series
/// * `start_month` - First month of the time series (1-12)
/// * `length` - Number of months in the time series
pub fn create_easter_regressor(start_year: i32, start_month: u32, length: usize) -> Vec<f64> {
    let mut regressor = vec![0.0; length];

    // Pre-calculate Easter invoice months for relevant years
    let end_year = start_year + (length as i32 / 12) + 3;
    let mut easter_invoice_months: HashSet<(i32, u32)> = HashSet::new();
    
    for year in start_year..=end_year {
        easter_invoice_months.insert(easter_invoice_month(year));
    }

    // Fill regressor array
    let mut current_year = start_year;
    let mut current_month = start_month;

    for i in 0..length {
        if easter_invoice_months.contains(&(current_year, current_month)) {
            regressor[i] = 1.0;
        }

        current_month += 1;
        if current_month > 12 {
            current_month = 1;
            current_year += 1;
        }
    }

    regressor
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_easter_dates() {
        // Known Easter dates from published sources
        assert_eq!(easter_sunday(2019), (4, 21));
        assert_eq!(easter_sunday(2020), (4, 12));
        assert_eq!(easter_sunday(2021), (4, 4));
        assert_eq!(easter_sunday(2022), (4, 17));
        assert_eq!(easter_sunday(2023), (4, 9));
        assert_eq!(easter_sunday(2024), (3, 31));
        assert_eq!(easter_sunday(2025), (4, 20));
        assert_eq!(easter_sunday(2026), (4, 5));
        assert_eq!(easter_sunday(2027), (3, 28));
    }

    #[test]
    fn test_easter_invoice_months() {
        // Easter 2024 is March 31 → invoice month is December 2023
        assert_eq!(easter_invoice_month(2024), (2023, 12));
        
        // Easter 2025 is April 20 → invoice month is January 2025
        assert_eq!(easter_invoice_month(2025), (2025, 1));
        
        // Easter 2026 is April 5 → invoice month is January 2026
        assert_eq!(easter_invoice_month(2026), (2026, 1));
    }

    #[test]
    fn test_easter_regressor() {
        // Create regressor for 2024-2025 (24 months starting Jan 2024)
        let regressor = create_easter_regressor(2024, 1, 24);
        
        // Should have 1.0 at position 0 (Jan 2024) for Easter 2024 (Mar 31)
        // Actually Dec 2023 is the invoice month, so Jan 2024 won't have it
        // Easter 2025 (Apr 20) → invoice Jan 2025 → position 12
        assert_eq!(regressor[12], 1.0);
        
        // Most other months should be 0.0
        assert_eq!(regressor[6], 0.0);  // Jul 2024
    }
}
