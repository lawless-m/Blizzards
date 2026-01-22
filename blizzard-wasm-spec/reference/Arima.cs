using System;
using System.Collections.Generic;
using System.Linq;

namespace Blizzard;

/// <summary>
/// Easter date calculation using the Anonymous Gregorian algorithm (Computus)
/// </summary>
public static class EasterCalculator
{
    /// <summary>
    /// Calculate Easter Sunday for a given year
    /// </summary>
    public static DateTime GetEasterSunday(int year)
    {
        // Anonymous Gregorian algorithm
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;

        return new DateTime(year, month, day);
    }

    /// <summary>
    /// Get the invoice month for Easter (3 months before)
    /// </summary>
    public static (int year, int month) GetEasterInvoiceMonth(int easterYear)
    {
        var easter = GetEasterSunday(easterYear);
        var invoiceDate = easter.AddMonths(-3);
        return (invoiceDate.Year, invoiceDate.Month);
    }

    /// <summary>
    /// Create Easter regressor array for a time series
    /// Returns 1.0 for months that are Easter invoice months, 0.0 otherwise
    /// </summary>
    public static double[] CreateEasterRegressor(int startYear, int startMonth, int length)
    {
        var regressor = new double[length];

        // Pre-calculate Easter invoice months for relevant years
        var easterInvoiceMonths = new HashSet<(int year, int month)>();
        for (int year = startYear; year <= startYear + (length / 12) + 2; year++)
        {
            easterInvoiceMonths.Add(GetEasterInvoiceMonth(year));
        }

        // Fill regressor array
        int currentYear = startYear;
        int currentMonth = startMonth;

        for (int i = 0; i < length; i++)
        {
            regressor[i] = easterInvoiceMonths.Contains((currentYear, currentMonth)) ? 1.0 : 0.0;

            currentMonth++;
            if (currentMonth > 12)
            {
                currentMonth = 1;
                currentYear++;
            }
        }

        return regressor;
    }
}

/// <summary>
/// ARIMA(p,d,q) model implementation with seasonal support and optional exogenous variables (ARIMAX)
/// </summary>
public class Arima
{
    public int P { get; } // AR order
    public int D { get; } // Differencing order
    public int Q { get; } // MA order
    public int SeasonalPeriod { get; } // Seasonal period (12 for monthly)

    private double[] _arCoeffs = Array.Empty<double>();
    private double[] _maCoeffs = Array.Empty<double>();
    private double[] _seasonalFactors = Array.Empty<double>();
    private double _intercept;
    private double[] _originalSeries = Array.Empty<double>();
    private double[] _differencedSeries = Array.Empty<double>();
    private double[] _residuals = Array.Empty<double>();

    // ARIMAX support
    private double[] _exogCoeffs = Array.Empty<double>();
    private double[]? _exogData;
    private int _exogCount;

    /// <summary>
    /// Exogenous variable coefficients (for ARIMAX)
    /// </summary>
    public double[] ExogCoefficients => _exogCoeffs;

    public Arima(int p = 2, int d = 1, int q = 1, int seasonalPeriod = 12)
    {
        P = p;
        D = d;
        Q = q;
        SeasonalPeriod = seasonalPeriod;
    }

    /// <summary>
    /// Fit ARIMA model without exogenous variables
    /// </summary>
    public void Fit(double[] series)
    {
        Fit(series, null);
    }

    /// <summary>
    /// Fit ARIMAX model with optional exogenous variables
    /// </summary>
    /// <param name="series">The time series data</param>
    /// <param name="exog">Optional exogenous variables (one array per variable, stacked)</param>
    public void Fit(double[] series, double[][]? exog)
    {
        if (series.Length < P + D + Q + SeasonalPeriod)
            throw new ArgumentException("Series too short for specified ARIMA parameters");

        _originalSeries = series.ToArray();
        _exogCount = exog?.Length ?? 0;
        _exogCoeffs = new double[_exogCount];

        // Store exog data for later use
        if (exog != null && exog.Length > 0)
        {
            _exogData = exog[0].ToArray();
        }

        // IMPORTANT: Regress out exogenous effects FIRST (before deseasonalizing)
        // The exog effect (e.g., Easter spike) is additive on the original scale
        double[] adjustedSeries = series.ToArray();
        if (exog != null && exog.Length > 0)
        {
            adjustedSeries = RegressOutExogenous(adjustedSeries, exog);
        }

        // Calculate seasonal factors on exog-adjusted series
        _seasonalFactors = CalculateSeasonalFactors(adjustedSeries);

        // Deseasonalize
        var deseasonalized = Deseasonalize(adjustedSeries, _seasonalFactors);

        // Apply differencing to deseasonalized series
        _differencedSeries = Difference(deseasonalized, D);

        // Estimate AR and MA coefficients using Yule-Walker and residuals
        EstimateCoefficients(_differencedSeries);
    }

    /// <summary>
    /// Regress out exogenous variables using simple mean-difference approach
    /// For sparse binary variables (like Easter), this is more accurate than
    /// standard OLS within ARIMAX which gets confused by differencing.
    ///
    /// Coefficient = mean(Y when X=1) - mean(Y when X=0)
    /// </summary>
    private double[] RegressOutExogenous(double[] series, double[][] exog)
    {
        int n = series.Length;
        int k = exog.Length;

        var residuals = series.ToArray();

        for (int j = 0; j < k; j++)
        {
            var x = exog[j];
            if (x.Length != n)
                throw new ArgumentException($"Exogenous variable {j} length mismatch");

            // For sparse binary exogenous variables, use mean difference
            // This is equivalent to OLS for binary X but more numerically stable
            var withExog = new List<double>();
            var withoutExog = new List<double>();

            for (int i = 0; i < n; i++)
            {
                if (x[i] > 0.5)
                    withExog.Add(residuals[i]);
                else
                    withoutExog.Add(residuals[i]);
            }

            if (withExog.Count > 0 && withoutExog.Count > 0)
            {
                double meanWith = withExog.Average();
                double meanWithout = withoutExog.Average();
                _exogCoeffs[j] = meanWith - meanWithout;

                // Remove exogenous effect from series (only from affected points)
                for (int i = 0; i < n; i++)
                {
                    if (x[i] > 0.5)
                    {
                        residuals[i] -= _exogCoeffs[j];
                    }
                }
            }
            else
            {
                _exogCoeffs[j] = 0;
            }
        }

        return residuals;
    }

    private double[] CalculateSeasonalFactors(double[] series)
    {
        var factors = new double[SeasonalPeriod];
        var counts = new int[SeasonalPeriod];
        var overall = series.Where(x => x > 0).Average();

        for (int i = 0; i < series.Length; i++)
        {
            if (series[i] > 0)
            {
                factors[i % SeasonalPeriod] += series[i];
                counts[i % SeasonalPeriod]++;
            }
        }

        for (int i = 0; i < SeasonalPeriod; i++)
        {
            factors[i] = counts[i] > 0 ? (factors[i] / counts[i]) / overall : 1.0;
        }

        return factors;
    }

    private double[] Deseasonalize(double[] series, double[] factors)
    {
        var result = new double[series.Length];
        for (int i = 0; i < series.Length; i++)
        {
            var factor = factors[i % SeasonalPeriod];
            result[i] = factor > 0 ? series[i] / factor : series[i];
        }
        return result;
    }

    private double[] Reseasonalize(double[] series, double[] factors, int startIndex)
    {
        var result = new double[series.Length];
        for (int i = 0; i < series.Length; i++)
        {
            result[i] = series[i] * factors[(startIndex + i) % SeasonalPeriod];
        }
        return result;
    }

    private double[] Difference(double[] series, int order)
    {
        var result = series.ToArray();
        for (int d = 0; d < order; d++)
        {
            var temp = new double[result.Length - 1];
            for (int i = 1; i < result.Length; i++)
            {
                temp[i - 1] = result[i] - result[i - 1];
            }
            result = temp;
        }
        return result;
    }

    private double[] Undifference(double[] diffSeries, double[] originalSeries, int order)
    {
        if (order == 0) return diffSeries;

        var result = diffSeries.ToArray();
        for (int d = 0; d < order; d++)
        {
            var temp = new double[result.Length];
            double lastValue = originalSeries[originalSeries.Length - 1 - (order - d - 1)];

            for (int i = 0; i < result.Length; i++)
            {
                temp[i] = result[i] + lastValue;
                lastValue = temp[i];
            }
            result = temp;
        }
        return result;
    }

    private void EstimateCoefficients(double[] series)
    {
        _intercept = series.Average();
        var centered = series.Select(x => x - _intercept).ToArray();

        // Yule-Walker for AR coefficients
        _arCoeffs = new double[P];
        if (P > 0)
        {
            var autocorr = CalculateAutocorrelation(centered, P);
            _arCoeffs = SolveYuleWalker(autocorr);
        }

        // Calculate residuals for MA estimation
        _residuals = CalculateResiduals(centered, _arCoeffs);

        // Estimate MA coefficients from residual autocorrelation
        _maCoeffs = new double[Q];
        if (Q > 0)
        {
            var resAutocorr = CalculateAutocorrelation(_residuals, Q);
            for (int i = 0; i < Q; i++)
            {
                _maCoeffs[i] = resAutocorr[i + 1] * 0.5; // Simplified MA estimation
            }
        }
    }

    private double[] CalculateAutocorrelation(double[] series, int maxLag)
    {
        var result = new double[maxLag + 1];
        double variance = series.Select(x => x * x).Average();

        if (variance < 1e-10)
        {
            result[0] = 1.0;
            return result;
        }

        for (int lag = 0; lag <= maxLag; lag++)
        {
            double sum = 0;
            for (int i = lag; i < series.Length; i++)
            {
                sum += series[i] * series[i - lag];
            }
            result[lag] = sum / (series.Length * variance);
        }

        return result;
    }

    private double[] SolveYuleWalker(double[] autocorr)
    {
        // Levinson-Durbin algorithm
        int p = autocorr.Length - 1;
        if (p == 0) return Array.Empty<double>();

        var phi = new double[p];
        var phiPrev = new double[p];

        phi[0] = autocorr[1];
        double v = 1 - phi[0] * phi[0];

        for (int i = 1; i < p; i++)
        {
            Array.Copy(phi, phiPrev, p);

            double num = autocorr[i + 1];
            for (int j = 0; j < i; j++)
            {
                num -= phiPrev[j] * autocorr[i - j];
            }

            phi[i] = num / v;

            for (int j = 0; j < i; j++)
            {
                phi[j] = phiPrev[j] - phi[i] * phiPrev[i - 1 - j];
            }

            v *= (1 - phi[i] * phi[i]);
            if (v < 1e-10) break;
        }

        return phi;
    }

    private double[] CalculateResiduals(double[] series, double[] arCoeffs)
    {
        var residuals = new double[series.Length];

        for (int i = 0; i < series.Length; i++)
        {
            double predicted = 0;
            for (int j = 0; j < arCoeffs.Length && i - j - 1 >= 0; j++)
            {
                predicted += arCoeffs[j] * series[i - j - 1];
            }
            residuals[i] = series[i] - predicted;
        }

        return residuals;
    }

    /// <summary>
    /// Forecast without exogenous variables
    /// </summary>
    public double[] Forecast(int steps)
    {
        return Forecast(steps, null);
    }

    /// <summary>
    /// Forecast with optional exogenous variables for future periods
    /// </summary>
    /// <param name="steps">Number of periods to forecast</param>
    /// <param name="futureExog">Exogenous values for forecast periods (one array per variable)</param>
    public double[] Forecast(int steps, double[][]? futureExog)
    {
        // Work with deseasonalized differenced series
        var extended = _differencedSeries.ToList();
        var extendedResiduals = _residuals.ToList();

        for (int s = 0; s < steps; s++)
        {
            double prediction = _intercept;

            // AR component
            for (int i = 0; i < P && i < extended.Count; i++)
            {
                prediction += _arCoeffs[i] * (extended[extended.Count - 1 - i] - _intercept);
            }

            // MA component (residuals decay to 0 for forecasts)
            for (int i = 0; i < Q && i < extendedResiduals.Count; i++)
            {
                if (extended.Count - 1 - i < _differencedSeries.Length)
                {
                    prediction += _maCoeffs[i] * extendedResiduals[extendedResiduals.Count - 1 - i];
                }
            }

            extended.Add(prediction);
            extendedResiduals.Add(0); // Future residuals are 0
        }

        // Extract forecast values
        var forecastDiff = extended.Skip(_differencedSeries.Length).ToArray();

        // Undifference
        var deseasonalized = _originalSeries.Select((v, i) =>
            _seasonalFactors[i % SeasonalPeriod] > 0 ? v / _seasonalFactors[i % SeasonalPeriod] : v).ToArray();
        var forecastDeseas = Undifference(forecastDiff, deseasonalized, D);

        // Reseasonalize
        int startMonth = _originalSeries.Length % SeasonalPeriod;
        var forecast = Reseasonalize(forecastDeseas, _seasonalFactors, startMonth);

        // Add back exogenous effects for future periods
        if (futureExog != null && _exogCoeffs.Length > 0)
        {
            for (int s = 0; s < steps && s < forecast.Length; s++)
            {
                for (int j = 0; j < _exogCoeffs.Length && j < futureExog.Length; j++)
                {
                    if (s < futureExog[j].Length)
                    {
                        // Add exogenous effect (coefficient * future value)
                        forecast[s] += _exogCoeffs[j] * futureExog[j][s];
                    }
                }
            }
        }

        // Ensure non-negative
        return forecast.Select(x => Math.Max(0, x)).ToArray();
    }

    public (double[] Lower, double[] Upper) ForecastConfidenceInterval(int steps, double confidence = 0.95)
    {
        var forecast = Forecast(steps);

        // Estimate standard error from residuals
        double se = Math.Sqrt(_residuals.Select(r => r * r).Average());

        // Z-score for confidence level
        double z = confidence switch
        {
            0.99 => 2.576,
            0.95 => 1.96,
            0.90 => 1.645,
            0.80 => 1.28,
            _ => 1.96
        };

        var lower = new double[steps];
        var upper = new double[steps];

        for (int i = 0; i < steps; i++)
        {
            // Error grows with forecast horizon
            double horizon_se = se * Math.Sqrt(1 + i * 0.1);
            // Scale by seasonal factor for proper interval width
            double seasonalScale = _seasonalFactors[(_originalSeries.Length + i) % SeasonalPeriod];
            double interval = z * horizon_se * seasonalScale;

            lower[i] = Math.Max(0, forecast[i] - interval);
            upper[i] = forecast[i] + interval;
        }

        return (lower, upper);
    }

    public ForecastMetrics CalculateMetrics(double[] actual, double[] predicted)
    {
        int n = Math.Min(actual.Length, predicted.Length);

        double mse = 0, mae = 0, mape = 0;
        int mapeCount = 0;

        for (int i = 0; i < n; i++)
        {
            double error = actual[i] - predicted[i];
            mse += error * error;
            mae += Math.Abs(error);
            if (actual[i] != 0)
            {
                mape += Math.Abs(error / actual[i]);
                mapeCount++;
            }
        }

        return new ForecastMetrics
        {
            MSE = mse / n,
            RMSE = Math.Sqrt(mse / n),
            MAE = mae / n,
            MAPE = mapeCount > 0 ? (mape / mapeCount) * 100 : 0
        };
    }
}

public class ForecastMetrics
{
    public double MSE { get; set; }
    public double RMSE { get; set; }
    public double MAE { get; set; }
    public double MAPE { get; set; }
}
