using System;
using System.Linq;

namespace Blizzard;

/// <summary>
/// Validation tests for ARIMAX implementation
/// </summary>
public static class ValidateArimax
{
    /// <summary>
    /// Run synthetic data validation test
    /// Uses a random sparse exogenous pattern to avoid seasonal correlation
    /// </summary>
    public static void RunSyntheticTest()
    {
        Console.WriteLine("=== ARIMAX Synthetic Data Validation ===\n");

        // Known parameters
        const double baseValue = 1000.0;
        const double exogEffect = 500.0;  // Known exogenous coefficient
        const int months = 84; // 7 years

        // Generate a sparse exogenous regressor (random months, not correlated with seasonality)
        var random = new Random(42);
        var exogRegressor = new double[months];
        var spikeMonths = new List<int> { 3, 17, 29, 41, 55, 67, 79 }; // Scattered across different months of year

        Console.WriteLine("Exogenous spike months (indices):");
        foreach (var idx in spikeMonths)
        {
            exogRegressor[idx] = 1.0;
            int monthOfYear = (idx % 12) + 1;
            Console.WriteLine($"  Index {idx} (month {monthOfYear} of year)");
        }

        // Generate synthetic data: Y = base + exog*500 + seasonal + noise
        var series = new double[months];

        for (int i = 0; i < months; i++)
        {
            // Base value with slight trend
            double value = baseValue + (i * 2); // Slight upward trend

            // Add seasonal pattern (simplified)
            int monthOfYear = (i % 12);
            double seasonal = 1.0 + 0.1 * Math.Sin(2 * Math.PI * monthOfYear / 12);
            value *= seasonal;

            // Add known exogenous effect
            value += exogEffect * exogRegressor[i];

            // Add noise
            value += random.NextDouble() * 100 - 50;

            series[i] = value;
        }

        Console.WriteLine($"\nGenerated {months} months of synthetic data");
        Console.WriteLine($"True exogenous coefficient: {exogEffect}");

        // Fit ARIMAX model
        var model = new Arima(p: 2, d: 1, q: 1, seasonalPeriod: 12);
        model.Fit(series, new[] { exogRegressor });

        double estimatedCoeff = model.ExogCoefficients[0];
        double error = Math.Abs(estimatedCoeff - exogEffect);
        double errorPct = (error / exogEffect) * 100;

        Console.WriteLine($"\nEstimated exog coefficient: {estimatedCoeff:F2}");
        Console.WriteLine($"Error: {error:F2} ({errorPct:F1}%)");

        // Validation threshold: within 20% of true value
        bool passed = errorPct < 20;
        Console.WriteLine($"\nValidation: {(passed ? "PASSED" : "FAILED")} (threshold: 20%)");

        // Also output data for Python comparison
        Console.WriteLine("\n=== Data for Python comparison ===");
        Console.WriteLine("Copy this to validate_arimax.py:\n");
        Console.WriteLine("series = [");
        for (int i = 0; i < series.Length; i++)
        {
            Console.Write($"    {series[i]:F2}");
            if (i < series.Length - 1) Console.Write(",");
            if ((i + 1) % 6 == 0) Console.WriteLine();
        }
        Console.WriteLine("\n]");

        Console.WriteLine("\nexog = [");
        for (int i = 0; i < exogRegressor.Length; i++)
        {
            Console.Write($"    {exogRegressor[i]:F1}");
            if (i < exogRegressor.Length - 1) Console.Write(",");
            if ((i + 1) % 12 == 0) Console.WriteLine();
        }
        Console.WriteLine("\n]");
    }

    /// <summary>
    /// Verify Easter date calculation against known dates
    /// </summary>
    public static void VerifyEasterDates()
    {
        Console.WriteLine("=== Easter Date Verification ===\n");

        // Known Easter dates (from published sources)
        var knownDates = new[]
        {
            (2019, 4, 21),
            (2020, 4, 12),
            (2021, 4, 4),
            (2022, 4, 17),
            (2023, 4, 9),
            (2024, 3, 31),
            (2025, 4, 20),
            (2026, 4, 5),
            (2027, 3, 28),
        };

        bool allPassed = true;
        foreach (var (year, month, day) in knownDates)
        {
            var calculated = EasterCalculator.GetEasterSunday(year);
            var expected = new DateTime(year, month, day);
            bool match = calculated == expected;
            allPassed &= match;

            var invoiceMonth = EasterCalculator.GetEasterInvoiceMonth(year);

            Console.WriteLine($"  {year}: Expected {expected:yyyy-MM-dd}, Got {calculated:yyyy-MM-dd} " +
                            $"[{(match ? "OK" : "FAIL")}] → Invoice: {invoiceMonth.year}-{invoiceMonth.month:D2}");
        }

        Console.WriteLine($"\nEaster calculation: {(allPassed ? "ALL PASSED" : "SOME FAILED")}");
    }
}
