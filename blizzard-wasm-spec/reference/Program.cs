using System.IO.Compression;
using System.Text.Json;
using Parquet;
using Parquet.Data;

namespace Blizzard;

class Program
{
    const string ParquetPath = "/mnt/prod02_ri_services/Outputs/Parquets/em/analysis.parquet";
    const string GeoParquetPath = "/mnt/prod02_ri_services/Outputs/Parquets/em/rigeographic.parquet";
    const string CustomerParquetPath = "/mnt/prod02_ri_services/Outputs/Parquets/em/customer.parquet";
    const string ProdGrpParquetPath = "/mnt/prod02_ri_services/Outputs/Parquets/em/prodgrp.parquet";
    const int ForecastMonths = 12;

    static async Task Main(string[] args)
    {
        // Parse optional cutoff date for backtesting
        DateTime? cutoffDate = null;
        bool backtestMode = false;
        bool cgiMode = false;

        // Check for CGI mode (GATEWAY_INTERFACE env var or --cgi flag)
        if (Environment.GetEnvironmentVariable("GATEWAY_INTERFACE") != null || args.Contains("--cgi"))
        {
            cgiMode = true;
        }

        // Parse query string in CGI mode
        var queryString = Environment.GetEnvironmentVariable("QUERY_STRING") ?? "";
        var queryParams = ParseQueryString(queryString);

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--validate")
            {
                // Run ARIMAX validation tests
                ValidateArimax.VerifyEasterDates();
                Console.WriteLine();
                ValidateArimax.RunSyntheticTest();
                return;
            }
            if ((args[i] == "--cutoff" || args[i] == "-c") && i + 1 < args.Length)
            {
                if (DateTime.TryParse(args[i + 1], out var date))
                {
                    cutoffDate = new DateTime(date.Year, date.Month, 1);
                    backtestMode = true;
                }
            }
        }

        // Also check query string for cutoff
        if (queryParams.TryGetValue("cutoff", out var cutoffStr) && DateTime.TryParse(cutoffStr, out var qdate))
        {
            cutoffDate = new DateTime(qdate.Year, qdate.Month, 1);
            backtestMode = true;
        }

        if (cgiMode)
        {
            // CGI mode - output gzipped JSON to stdout
            Console.WriteLine("Content-Type: application/json");
            Console.WriteLine("Content-Encoding: gzip");
            Console.WriteLine("Access-Control-Allow-Origin: *");
            Console.WriteLine();

            try
            {
                // Cache key includes date and cutoff parameter
                var cacheKey = backtestMode ? $"{DateTime.Today:yyyyMMdd}_cutoff_{cutoffDate:yyyyMM}" : $"{DateTime.Today:yyyyMMdd}";
                var cachePath = $"/var/tmp/blizzard_cache_{cacheKey}.gz";

                byte[] gzipped;

                // Return cached if exists
                if (File.Exists(cachePath))
                {
                    gzipped = await File.ReadAllBytesAsync(cachePath);
                }
                else
                {
                    // Generate and cache
                    var cgiResults = await GenerateForecasts(cutoffDate, backtestMode, silent: true);
                    var cgiJson = JsonSerializer.Serialize(cgiResults, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    using var ms = new MemoryStream();
                    using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    using (var writer = new StreamWriter(gz))
                        await writer.WriteAsync(cgiJson);

                    gzipped = ms.ToArray();
                    await File.WriteAllBytesAsync(cachePath, gzipped);
                }

                using var stdout = Console.OpenStandardOutput();
                await stdout.WriteAsync(gzipped);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
            return;
        }

        // CLI mode
        if (backtestMode)
        {
            Console.WriteLine($"BACKTEST MODE: Training on data up to {cutoffDate:yyyy-MM}");
            Console.WriteLine("Will compare predictions against actual future values\n");
        }

        var results = await GenerateForecasts(cutoffDate, backtestMode, silent: false);

        // Save results
        var outputPath = Path.Combine(AppContext.BaseDirectory, "forecast_results.json");
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine($"\nResults saved to {outputPath}");

        // Also save to working directory for easy access
        var workingPath = "/home/matt/Git/Blizzard/forecast_results.json";
        await File.WriteAllTextAsync(workingPath, json);
        Console.WriteLine($"Results also saved to {workingPath}");

        // Summary
        Console.WriteLine($"\nForecast Summary:");
        Console.WriteLine($"  Overall: {results.Overall.Historical.Rows.Count} months history, {ForecastMonths} months forecast");
        Console.WriteLine($"  Product Groups: {results.ProductGroups.Count}");
        Console.WriteLine($"  Customers: {results.Customers.Count}");
        Console.WriteLine($"  Geography: {results.Geography.Forecasts.Count} forecasts, {results.Geography.Hierarchy.Count} locations");
    }

    static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
                result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
        }
        return result;
    }

    static async Task<ForecastResults> GenerateForecasts(DateTime? cutoffDate, bool backtestMode, bool silent)
    {
        var currentYear = DateTime.Now.Year;
        var mutableYears = new HashSet<int> { currentYear, currentYear - 1 }; // Current and previous year are mutable

        if (!silent) Console.WriteLine("Loading yearly shards...");

        // Determine year range (2019 to current)
        var years = Enumerable.Range(2019, currentYear - 2019 + 1).ToList();
        var shards = new List<YearShard>();
        var yearsToLoad = new List<int>();

        // Try to load cached shards for immutable years
        foreach (var year in years)
        {
            if (!mutableYears.Contains(year))
            {
                var cached = await YearShard.LoadFromCache(year);
                if (cached != null)
                {
                    shards.Add(cached);
                    if (!silent) Console.WriteLine($"  {year}: loaded from cache");
                    continue;
                }
            }
            yearsToLoad.Add(year);
        }

        // Load parquet data only for years we need
        if (yearsToLoad.Count > 0)
        {
            if (!silent) Console.WriteLine($"Loading parquet for years: {string.Join(", ", yearsToLoad)}...");
            var allSalesData = await LoadSalesData();

            // Exclude current month data (partial month would skew forecast)
            // Data is always 1 day old, so on 1st we see end of previous month
            var currentMonthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var originalCount = allSalesData.Count;
            allSalesData = allSalesData.Where(r => r.Date < currentMonthStart).ToList();
            if (!silent) Console.WriteLine($"  Excluded {originalCount - allSalesData.Count:N0} records from current month ({currentMonthStart:MMM yyyy})");

            foreach (var year in yearsToLoad)
            {
                var shard = YearShard.BuildFromRecords(year, allSalesData);
                shards.Add(shard);
                if (!silent) Console.WriteLine($"  {year}: built from parquet ({shard.Overall.Values.Sum(m => m.Count):N0} records)");

                // Cache immutable years
                if (!mutableYears.Contains(year))
                {
                    await shard.SaveToCache();
                    if (!silent) Console.WriteLine($"  {year}: saved to cache");
                }
            }
        }

        // Sort shards by year
        shards = shards.OrderBy(s => s.Year).ToList();

        // For backtest mode, filter shards and build future data
        List<YearShard> trainingShards;
        List<YearShard> futureShards;

        if (backtestMode)
        {
            var cutoffYear = cutoffDate!.Value.Year;
            var cutoffMonth = cutoffDate!.Value.Month;

            trainingShards = new List<YearShard>();
            futureShards = new List<YearShard>();

            foreach (var shard in shards)
            {
                if (shard.Year < cutoffYear)
                {
                    trainingShards.Add(shard);
                }
                else if (shard.Year == cutoffYear)
                {
                    // Split the cutoff year
                    var trainShard = new YearShard { Year = shard.Year };
                    var futureShard = new YearShard { Year = shard.Year };

                    foreach (var (month, data) in shard.Overall)
                    {
                        if (month <= cutoffMonth) trainShard.Overall[month] = data;
                        else futureShard.Overall[month] = data;
                    }
                    foreach (var (key, months) in shard.ByGroup)
                    {
                        trainShard.ByGroup[key] = new Dictionary<int, MonthlyData>();
                        futureShard.ByGroup[key] = new Dictionary<int, MonthlyData>();
                        foreach (var (month, data) in months)
                        {
                            if (month <= cutoffMonth) trainShard.ByGroup[key][month] = data;
                            else futureShard.ByGroup[key][month] = data;
                        }
                    }
                    foreach (var (key, months) in shard.ByCustomer)
                    {
                        trainShard.ByCustomer[key] = new Dictionary<int, MonthlyData>();
                        futureShard.ByCustomer[key] = new Dictionary<int, MonthlyData>();
                        foreach (var (month, data) in months)
                        {
                            if (month <= cutoffMonth) trainShard.ByCustomer[key][month] = data;
                            else futureShard.ByCustomer[key][month] = data;
                        }
                    }
                    foreach (var (key, months) in shard.ByTerritory)
                    {
                        trainShard.ByTerritory[key] = new Dictionary<int, MonthlyData>();
                        futureShard.ByTerritory[key] = new Dictionary<int, MonthlyData>();
                        foreach (var (month, data) in months)
                        {
                            if (month <= cutoffMonth) trainShard.ByTerritory[key][month] = data;
                            else futureShard.ByTerritory[key][month] = data;
                        }
                    }

                    if (trainShard.Overall.Count > 0) trainingShards.Add(trainShard);
                    if (futureShard.Overall.Count > 0) futureShards.Add(futureShard);
                }
                else
                {
                    futureShards.Add(shard);
                }
            }

            if (!silent)
            {
                Console.WriteLine($"Training shards: {trainingShards.Count} years (up to {cutoffDate:yyyy-MM})");
                Console.WriteLine($"Future shards: {futureShards.Count} years for validation");
            }
        }
        else
        {
            trainingShards = shards;
            futureShards = new List<YearShard>();
        }

        var results = new ForecastResults { BacktestMode = backtestMode, CutoffDate = cutoffDate?.ToString("yyyy-MM") };

        // In backtest mode, extend forecast to current month + 12 months (standard forecast period)
        // This shows both the backtest comparison AND future forecast
        int? extendedMonths = null;
        if (backtestMode && cutoffDate.HasValue)
        {
            var currentMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var monthsFromCutoff = ((currentMonth.Year - cutoffDate.Value.Year) * 12) + (currentMonth.Month - cutoffDate.Value.Month);
            extendedMonths = monthsFromCutoff + ForecastMonths;
            if (!silent) Console.WriteLine($"Extended forecast: {extendedMonths} months (to {cutoffDate.Value.AddMonths(extendedMonths.Value - 1):yyyy-MM})");
        }

        if (!silent) Console.WriteLine("\nForecasting overall sales...");
        var overallSeries = YearShard.CombineOverall(trainingShards);
        var overallFuture = backtestMode ? YearShard.CombineOverall(futureShards) : null;
        results.Overall = GenerateForecast("Overall", overallSeries, overallFuture, extendedMonths);

        if (!silent) Console.WriteLine("Forecasting by product group...");
        var allProdGroupNames = await LoadProductGroupNames();
        var productGroups = YearShard.GetAllKeysWithMinMonths(trainingShards, s => s.ByGroup, 24);
        if (!silent) Console.WriteLine($"  Found {productGroups.Count} product groups with 24+ months of data");
        results.ProductGroups = new Dictionary<string, ForecastData>();
        foreach (var group in productGroups)
        {
            var series = YearShard.CombineByKey(trainingShards, group, s => s.ByGroup);
            var future = backtestMode ? YearShard.CombineByKey(futureShards, group, s => s.ByGroup) : null;
            if (series.Count >= 24)
            {
                try
                {
                    results.ProductGroups[group] = GenerateForecast(group, series, future, extendedMonths);
                }
                catch { /* Skip groups with insufficient continuous data */ }
            }
        }
        // Only include names for product groups that have forecasts
        results.ProductGroupNames = results.ProductGroups.Keys
            .Where(k => allProdGroupNames.ContainsKey(k))
            .ToDictionary(k => k, k => allProdGroupNames[k]);

        if (!silent) Console.WriteLine("Forecasting by customer...");
        var customerNames = await LoadCustomerNames();
        var allCustomers = YearShard.GetAllKeysWithMinMonths(trainingShards, s => s.ByCustomer, 24);
        if (!silent) Console.WriteLine($"  Found {allCustomers.Count} customers with 24+ months of data");
        results.Customers = new Dictionary<string, ForecastData>();
        foreach (var cust in allCustomers)
        {
            var series = YearShard.CombineByKey(trainingShards, cust, s => s.ByCustomer);
            var future = backtestMode ? YearShard.CombineByKey(futureShards, cust, s => s.ByCustomer) : null;
            if (series.Count >= 24)
            {
                try
                {
                    var displayName = customerNames.TryGetValue(cust, out var name) ? $"{name} ({cust})" : cust;
                    results.Customers[displayName] = GenerateForecast(displayName, series, future, extendedMonths);
                }
                catch { /* Skip customers with insufficient continuous data */ }
            }
        }

        if (!silent) Console.WriteLine("Forecasting by geography...");
        var geoHierarchy = await LoadGeographyHierarchy();
        results.Geography = new GeographyData { Hierarchy = geoHierarchy };

        // Get all territory codes (6-char) with sufficient data
        var allTerrCodes = YearShard.GetAllKeysWithMinMonths(trainingShards, s => s.ByTerritory, 24);
        if (!silent) Console.WriteLine($"  Found {allTerrCodes.Count} geography codes with 24+ months of data");

        // Build forecasts for each geography code
        foreach (var code in allTerrCodes)
        {
            if (code.Length != 6) continue;

            var series = YearShard.CombineByKey(trainingShards, code, s => s.ByTerritory);
            var future = backtestMode ? YearShard.CombineByKey(futureShards, code, s => s.ByTerritory) : null;

            if (series.Count >= 24)
            {
                try
                {
                    results.Geography.Forecasts[code] = GenerateForecast(code, series, future, extendedMonths);
                }
                catch { /* Skip codes with insufficient continuous data */ }
            }
        }

        // Also build aggregated forecasts for territory and country levels
        // Territory level (aggregate all countries in a territory)
        var territoryAggregates = allTerrCodes
            .Where(c => c.Length == 6)
            .GroupBy(c => c.Substring(0, 2))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (terrCode, codes) in territoryAggregates)
        {
            var aggKey = $"{terrCode}__ALL";
            var series = YearShard.CombineMultipleKeys(trainingShards, codes, s => s.ByTerritory);
            var future = backtestMode ? YearShard.CombineMultipleKeys(futureShards, codes, s => s.ByTerritory) : null;

            if (series.Count >= 24)
            {
                try
                {
                    results.Geography.Forecasts[aggKey] = GenerateForecast(aggKey, series, future, extendedMonths);
                }
                catch { }
            }
        }

        // Country level (aggregate all regions in a country)
        var countryAggregates = allTerrCodes
            .Where(c => c.Length == 6)
            .GroupBy(c => c.Substring(0, 4))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (countryKey, codes) in countryAggregates)
        {
            var aggKey = $"{countryKey}__ALL";
            var series = YearShard.CombineMultipleKeys(trainingShards, codes, s => s.ByTerritory);
            var future = backtestMode ? YearShard.CombineMultipleKeys(futureShards, codes, s => s.ByTerritory) : null;

            if (series.Count >= 24)
            {
                try
                {
                    results.Geography.Forecasts[aggKey] = GenerateForecast(aggKey, series, future, extendedMonths);
                }
                catch { }
            }
        }

        if (!silent) Console.WriteLine($"  Generated {results.Geography.Forecasts.Count} geography forecasts");

        return results;
    }

    static async Task<List<GeoHierarchyItem>> LoadGeographyHierarchy()
    {
        var items = new List<GeoHierarchyItem>();
        var seen = new HashSet<string>();

        using var stream = File.OpenRead(GeoParquetPath);
        using var reader = await ParquetReader.CreateAsync(stream);

        for (int rg = 0; rg < reader.RowGroupCount; rg++)
        {
            using var rowGroupReader = reader.OpenRowGroupReader(rg);

            var regionCodeCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "regioncode"))).Data;
            var countryCodeCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "countrycode"))).Data;
            var terrCodeCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "riterritorycode"))).Data;
            var terrDescCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "riterritorydesc"))).Data;
            var countryNameCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "countryname"))).Data;
            var regionDescCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "regiondesc"))).Data;

            var regionCodes = regionCodeCol as string[] ?? Array.Empty<string>();
            var countryCodes = countryCodeCol as string[] ?? Array.Empty<string>();
            var terrCodes = terrCodeCol as string[] ?? Array.Empty<string>();
            var terrDescs = terrDescCol as string[] ?? Array.Empty<string>();
            var countryNames = countryNameCol as string[] ?? Array.Empty<string>();
            var regionDescs = regionDescCol as string[] ?? Array.Empty<string>();

            // Country codes to exclude (incorrectly in data - should be regions)
            var excludedCountryCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DU" }; // Dubai should be under UAE (AE)

            for (int i = 0; i < regionCodes.Length; i++)
            {
                var fullCode = regionCodes[i];
                if (string.IsNullOrEmpty(fullCode) || fullCode.Length != 6 || seen.Contains(fullCode))
                    continue;

                // Skip entries with excluded country codes
                var countryCode = fullCode.Substring(2, 2);
                if (excludedCountryCodes.Contains(countryCode))
                    continue;

                seen.Add(fullCode);

                // Parse the 6-char code: Territory (0-1), Country (2-3), Region (4-5)
                var terrCode = fullCode.Substring(0, 2);
                // countryCode already extracted above for filtering
                var regionCode = fullCode.Substring(4, 2);

                items.Add(new GeoHierarchyItem
                {
                    FullCode = fullCode,
                    TerritoryCode = terrCode,
                    TerritoryName = i < terrDescs.Length && !string.IsNullOrEmpty(terrDescs[i]) ? terrDescs[i] : terrCode,
                    CountryCode = countryCode,
                    CountryName = i < countryNames.Length && !string.IsNullOrEmpty(countryNames[i]) ? countryNames[i] : countryCode,
                    RegionCode = regionCode,
                    RegionName = regionCode == "AA" ? "All" : (i < regionDescs.Length && !string.IsNullOrEmpty(regionDescs[i]) ? regionDescs[i] : regionCode)
                });
            }
        }

        return items;
    }

    static async Task<Dictionary<string, string>> LoadCustomerNames()
    {
        var customers = new Dictionary<string, string>();

        using var stream = File.OpenRead(CustomerParquetPath);
        using var reader = await ParquetReader.CreateAsync(stream);

        for (int rg = 0; rg < reader.RowGroupCount; rg++)
        {
            using var rowGroupReader = reader.OpenRowGroupReader(rg);

            var codeCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "code"))).Data;
            var nameCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "cpyname"))).Data;

            var codes = codeCol as string[] ?? Array.Empty<string>();
            var names = nameCol as string[] ?? Array.Empty<string>();

            for (int i = 0; i < codes.Length; i++)
            {
                if (!string.IsNullOrEmpty(codes[i]) && !customers.ContainsKey(codes[i]))
                {
                    customers[codes[i]] = names.Length > i && !string.IsNullOrEmpty(names[i]) ? names[i].Trim() : codes[i];
                }
            }
        }

        return customers;
    }

    static async Task<Dictionary<string, string>> LoadProductGroupNames()
    {
        var prodGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var stream = File.OpenRead(ProdGrpParquetPath);
        using var reader = await ParquetReader.CreateAsync(stream);

        for (int rg = 0; rg < reader.RowGroupCount; rg++)
        {
            using var rowGroupReader = reader.OpenRowGroupReader(rg);

            var codeCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "code"))).Data;
            var descCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "desc"))).Data;

            var codes = codeCol as string[] ?? Array.Empty<string>();
            var descs = descCol as string[] ?? Array.Empty<string>();

            for (int i = 0; i < codes.Length; i++)
            {
                if (!string.IsNullOrEmpty(codes[i]) && !prodGroups.ContainsKey(codes[i]))
                {
                    prodGroups[codes[i]] = descs.Length > i && !string.IsNullOrEmpty(descs[i]) ? descs[i].Trim() : codes[i];
                }
            }
        }

        return prodGroups;
    }

    static async Task<List<SalesRecord>> LoadSalesData()
    {
        var records = new List<SalesRecord>();

        using var stream = File.OpenRead(ParquetPath);
        using var reader = await ParquetReader.CreateAsync(stream);

        for (int rg = 0; rg < reader.RowGroupCount; rg++)
        {
            using var rowGroupReader = reader.OpenRowGroupReader(rg);

            var dateCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "sadate"))).Data;
            var valueCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "saval"))).Data;
            var qtyCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "saqty"))).Data;
            var groupCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "sagroup"))).Data;
            var custCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "sacust"))).Data;
            var terrCol = (await rowGroupReader.ReadColumnAsync(reader.Schema.GetDataFields().First(f => f.Name == "saterr"))).Data;

            // Handle various date formats from parquet
            var dates = ConvertToDateTimeArray(dateCol);
            var values = ConvertToDoubleArray(valueCol);
            var qtys = ConvertToDoubleArray(qtyCol);
            var groups = groupCol as string[] ?? Array.Empty<string>();
            var custs = custCol as string[] ?? Array.Empty<string>();
            var terrs = terrCol as string[] ?? Array.Empty<string>();

            for (int i = 0; i < dates.Length; i++)
            {
                if (dates[i].HasValue)
                {
                    records.Add(new SalesRecord
                    {
                        Date = dates[i]!.Value,
                        Value = values.Length > i ? values[i] : 0,
                        Qty = qtys.Length > i ? qtys[i] : 0,
                        Group = groups.Length > i ? groups[i] : null,
                        Customer = custs.Length > i ? custs[i] : null,
                        Territory = terrs.Length > i ? terrs[i] : null
                    });
                }
            }
        }

        return records;
    }

    static DateTime?[] ConvertToDateTimeArray(Array data)
    {
        if (data is DateTime?[] dta) return dta;
        if (data is DateTime[] dt) return dt.Cast<DateTime?>().ToArray();
        if (data is DateTimeOffset?[] dto) return dto.Select(d => d?.DateTime).ToArray();
        if (data is DateTimeOffset[] dtoArr) return dtoArr.Select(d => (DateTime?)d.DateTime).ToArray();
        if (data is long[] ticks) return ticks.Select(t => (DateTime?)new DateTime(1970, 1, 1).AddTicks(t / 100)).ToArray(); // nanoseconds
        if (data is long?[] ticksN) return ticksN.Select(t => t.HasValue ? (DateTime?)new DateTime(1970, 1, 1).AddTicks(t.Value / 100) : null).ToArray();

        // Fallback: try to convert element by element
        var result = new DateTime?[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            var val = data.GetValue(i);
            if (val == null) continue;
            if (val is DateTime dt2) result[i] = dt2;
            else if (val is DateTimeOffset dto2) result[i] = dto2.DateTime;
            else if (val is long ticks2) result[i] = new DateTime(1970, 1, 1).AddTicks(ticks2 / 100);
        }
        return result;
    }

    static double[] ConvertToDoubleArray(Array data)
    {
        if (data is double[] d) return d;
        if (data is double?[] dn) return dn.Select(x => x ?? 0).ToArray();
        if (data is float[] f) return f.Select(x => (double)x).ToArray();
        if (data is float?[] fn) return fn.Select(x => (double)(x ?? 0)).ToArray();
        if (data is decimal[] dec) return dec.Select(x => (double)x).ToArray();
        if (data is decimal?[] decn) return decn.Select(x => (double)(x ?? 0)).ToArray();

        var result = new double[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            var val = data.GetValue(i);
            if (val != null) result[i] = Convert.ToDouble(val);
        }
        return result;
    }

    static SortedDictionary<DateTime, MonthlyData> AggregateByMonth(IEnumerable<SalesRecord> records)
    {
        var monthly = new SortedDictionary<DateTime, MonthlyData>();

        foreach (var rec in records)
        {
            var monthKey = new DateTime(rec.Date.Year, rec.Date.Month, 1);
            if (!monthly.ContainsKey(monthKey))
            {
                monthly[monthKey] = new MonthlyData();
            }
            monthly[monthKey].Value += rec.Value;
            monthly[monthKey].Qty += rec.Qty;
            monthly[monthKey].Count++;
        }

        return monthly;
    }

    static ForecastData GenerateForecast(string name, SortedDictionary<DateTime, MonthlyData> monthlyData,
        SortedDictionary<DateTime, MonthlyData>? futureData = null, int? extendedForecastMonths = null)
    {
        var months = monthlyData.Keys.ToList();
        var values = monthlyData.Values.Select(v => v.Value).ToArray();

        // Use extended forecast period if provided (backtest mode extending into future)
        var forecastPeriod = extendedForecastMonths ?? ForecastMonths;

        // Create Easter regressor for historical period
        var startDate = months.First();
        var easterRegressor = EasterCalculator.CreateEasterRegressor(startDate.Year, startDate.Month, values.Length);

        // Create Easter regressor for forecast period
        var lastDate = months.Last();
        var futureEasterRegressor = EasterCalculator.CreateEasterRegressor(
            lastDate.AddMonths(1).Year, lastDate.AddMonths(1).Month, forecastPeriod);

        // Fit ARIMA model with Easter regressor
        var arima = new Arima(p: 2, d: 1, q: 1, seasonalPeriod: 12);
        arima.Fit(values, new[] { easterRegressor });

        // Generate forecast with future Easter dates
        var forecast = arima.Forecast(forecastPeriod, new[] { futureEasterRegressor });
        var (lower, upper) = arima.ForecastConfidenceInterval(forecastPeriod, confidence: 0.80);

        // Build result with columnar format
        var result = new ForecastData { Name = name };

        // Historical data
        for (int i = 0; i < months.Count; i++)
            result.Historical.Add(months[i].ToString("yyyy-MM"), values[i]);

        // Forecast with confidence intervals
        result.Forecast = DataTable.ForForecast();
        var lastMonth = months.Last();
        for (int i = 0; i < forecastPeriod; i++)
        {
            var forecastMonth = lastMonth.AddMonths(i + 1);
            result.Forecast.AddForecast(forecastMonth.ToString("yyyy-MM"), forecast[i], lower[i], upper[i]);

            // Add actual values if in backtest mode (only for months we have data)
            if (futureData != null && futureData.TryGetValue(forecastMonth, out var actualData))
                result.Actual.Add(forecastMonth.ToString("yyyy-MM"), actualData.Value);
        }

        // Calculate backtest metrics
        if (futureData != null && result.Actual.Rows.Count > 0)
        {
            var actualValues = result.Actual.Rows.Select(r => (double)r[1]).ToArray();
            var forecastValues = result.Forecast.Rows.Take(result.Actual.Rows.Count).Select(r => (double)r[1]).ToArray();
            result.BacktestMetrics = arima.CalculateMetrics(actualValues, forecastValues);
        }

        // Calculate in-sample metrics using last 12 months as validation
        if (values.Length > 12)
        {
            var trainValues = values.Take(values.Length - 12).ToArray();
            var testValues = values.Skip(values.Length - 12).ToArray();

            // Easter regressors for validation
            var trainEaster = EasterCalculator.CreateEasterRegressor(startDate.Year, startDate.Month, trainValues.Length);
            var validationStartDate = months[values.Length - 12];
            var testEaster = EasterCalculator.CreateEasterRegressor(validationStartDate.Year, validationStartDate.Month, 12);

            var validationArima = new Arima(p: 2, d: 1, q: 1, seasonalPeriod: 12);
            validationArima.Fit(trainValues, new[] { trainEaster });
            var testForecast = validationArima.Forecast(12, new[] { testEaster });

            result.Metrics = validationArima.CalculateMetrics(testValues, testForecast);
        }

        return result;
    }
}

class SalesRecord
{
    public DateTime Date { get; set; }
    public double Value { get; set; }
    public double Qty { get; set; }
    public string? Group { get; set; }
    public string? Customer { get; set; }
    public string? Territory { get; set; }
}

class MonthlyData
{
    public double Value { get; set; }
    public double Qty { get; set; }
    public int Count { get; set; }
}

class ForecastResults
{
    public bool BacktestMode { get; set; }
    public string? CutoffDate { get; set; }
    public ForecastData Overall { get; set; } = new();
    public Dictionary<string, ForecastData> ProductGroups { get; set; } = new();
    public Dictionary<string, string> ProductGroupNames { get; set; } = new();
    public Dictionary<string, ForecastData> Customers { get; set; } = new();
    public GeographyData Geography { get; set; } = new();
}

class GeographyData
{
    public List<GeoHierarchyItem> Hierarchy { get; set; } = new();
    public Dictionary<string, ForecastData> Forecasts { get; set; } = new();
}

class GeoHierarchyItem
{
    public string TerritoryCode { get; set; } = "";
    public string TerritoryName { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string CountryName { get; set; } = "";
    public string RegionCode { get; set; } = "";
    public string RegionName { get; set; } = "";
    public string FullCode { get; set; } = "";  // 6-char code for lookup
}

class ForecastData
{
    public string Name { get; set; } = "";
    public DataTable Historical { get; set; } = new();
    public DataTable Forecast { get; set; } = new();
    public DataTable Actual { get; set; } = new();
    public ForecastMetrics? Metrics { get; set; }
    public ForecastMetrics? BacktestMetrics { get; set; }
}

class DataTable
{
    public List<string> Headers { get; set; } = new() { "month", "value" };
    public List<List<object>> Rows { get; set; } = new();

    public void Add(string month, double value) => Rows.Add(new List<object> { month, Math.Round(value, 2) });
    public void AddForecast(string month, double value, double lower, double upper) =>
        Rows.Add(new List<object> { month, Math.Round(value, 2), Math.Round(lower, 2), Math.Round(upper, 2) });

    public static DataTable ForForecast() => new() { Headers = new() { "month", "value", "lower", "upper" } };
}

// Yearly aggregation shard - stores monthly totals for one year
class YearShard
{
    public int Year { get; set; }
    public Dictionary<int, MonthlyData> Overall { get; set; } = new();
    public Dictionary<string, Dictionary<int, MonthlyData>> ByGroup { get; set; } = new();
    public Dictionary<string, Dictionary<int, MonthlyData>> ByCustomer { get; set; } = new();
    public Dictionary<string, Dictionary<int, MonthlyData>> ByTerritory { get; set; } = new();

    public static string GetShardPath(int year) => $"/var/tmp/blizzard_shard_{year}.json";

    // Extract product group hierarchy levels from a code
    // E.g., "S52020" returns ["S52020", "S520", "S5"]
    private static List<string> GetProductGroupHierarchy(string code)
    {
        var levels = new List<string> { code };

        // Product group hierarchy pattern: 2 chars → 4 chars → 6+ chars
        // S5 (top) → S520 (mid) → S52020 (leaf)
        if (code.Length > 4)
            levels.Add(code.Substring(0, 4)); // Mid level (e.g., S520)
        if (code.Length > 2)
            levels.Add(code.Substring(0, 2)); // Top level (e.g., S5)

        return levels.Distinct().ToList();
    }

    public static async Task<YearShard?> LoadFromCache(int year)
    {
        var path = GetShardPath(year);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<YearShard>(json);
    }

    public async Task SaveToCache()
    {
        var path = GetShardPath(Year);
        var json = JsonSerializer.Serialize(this);
        await File.WriteAllTextAsync(path, json);
    }

    public static YearShard BuildFromRecords(int year, IEnumerable<SalesRecord> records)
    {
        var shard = new YearShard { Year = year };
        var yearRecords = records.Where(r => r.Date.Year == year).ToList();

        // Overall by month
        foreach (var rec in yearRecords)
        {
            var month = rec.Date.Month;
            if (!shard.Overall.ContainsKey(month))
                shard.Overall[month] = new MonthlyData();
            shard.Overall[month].Value += rec.Value;
            shard.Overall[month].Qty += rec.Qty;
            shard.Overall[month].Count++;
        }

        // By group - aggregate to both leaf level and parent levels
        // Product group hierarchy: S5 (2 chars) → S520 (4 chars) → S52020 (6 chars)
        foreach (var rec in yearRecords.Where(r => !string.IsNullOrEmpty(r.Group)))
        {
            var month = rec.Date.Month;
            var groupCode = rec.Group!;

            // Get all hierarchy levels to aggregate to (leaf + parents)
            var levels = GetProductGroupHierarchy(groupCode);

            foreach (var level in levels)
            {
                if (!shard.ByGroup.ContainsKey(level))
                    shard.ByGroup[level] = new Dictionary<int, MonthlyData>();
                if (!shard.ByGroup[level].ContainsKey(month))
                    shard.ByGroup[level][month] = new MonthlyData();
                shard.ByGroup[level][month].Value += rec.Value;
                shard.ByGroup[level][month].Qty += rec.Qty;
                shard.ByGroup[level][month].Count++;
            }
        }

        // By customer
        foreach (var cust in yearRecords.Where(r => !string.IsNullOrEmpty(r.Customer)).GroupBy(r => r.Customer!))
        {
            shard.ByCustomer[cust.Key] = new Dictionary<int, MonthlyData>();
            foreach (var rec in cust)
            {
                var month = rec.Date.Month;
                if (!shard.ByCustomer[cust.Key].ContainsKey(month))
                    shard.ByCustomer[cust.Key][month] = new MonthlyData();
                shard.ByCustomer[cust.Key][month].Value += rec.Value;
                shard.ByCustomer[cust.Key][month].Qty += rec.Qty;
                shard.ByCustomer[cust.Key][month].Count++;
            }
        }

        // By territory
        foreach (var terr in yearRecords.Where(r => !string.IsNullOrEmpty(r.Territory)).GroupBy(r => r.Territory!))
        {
            shard.ByTerritory[terr.Key] = new Dictionary<int, MonthlyData>();
            foreach (var rec in terr)
            {
                var month = rec.Date.Month;
                if (!shard.ByTerritory[terr.Key].ContainsKey(month))
                    shard.ByTerritory[terr.Key][month] = new MonthlyData();
                shard.ByTerritory[terr.Key][month].Value += rec.Value;
                shard.ByTerritory[terr.Key][month].Qty += rec.Qty;
                shard.ByTerritory[terr.Key][month].Count++;
            }
        }

        return shard;
    }

    // Combine multiple shards into aggregated time series
    public static SortedDictionary<DateTime, MonthlyData> CombineOverall(IEnumerable<YearShard> shards)
    {
        var result = new SortedDictionary<DateTime, MonthlyData>();
        foreach (var shard in shards)
        {
            foreach (var (month, data) in shard.Overall)
            {
                var key = new DateTime(shard.Year, month, 1);
                result[key] = data;
            }
        }
        return result;
    }

    public static SortedDictionary<DateTime, MonthlyData> CombineByKey(IEnumerable<YearShard> shards,
        string key, Func<YearShard, Dictionary<string, Dictionary<int, MonthlyData>>> selector)
    {
        var result = new SortedDictionary<DateTime, MonthlyData>();
        foreach (var shard in shards)
        {
            var dict = selector(shard);
            if (dict.TryGetValue(key, out var months))
            {
                foreach (var (month, data) in months)
                {
                    var dateKey = new DateTime(shard.Year, month, 1);
                    result[dateKey] = data;
                }
            }
        }
        return result;
    }

    public static SortedDictionary<DateTime, MonthlyData> CombineMultipleKeys(IEnumerable<YearShard> shards,
        IEnumerable<string> keys, Func<YearShard, Dictionary<string, Dictionary<int, MonthlyData>>> selector)
    {
        var result = new SortedDictionary<DateTime, MonthlyData>();
        var keySet = keys.ToHashSet();

        foreach (var shard in shards)
        {
            var dict = selector(shard);
            foreach (var key in keySet)
            {
                if (dict.TryGetValue(key, out var months))
                {
                    foreach (var (month, data) in months)
                    {
                        var dateKey = new DateTime(shard.Year, month, 1);
                        if (!result.ContainsKey(dateKey))
                            result[dateKey] = new MonthlyData();
                        result[dateKey].Value += data.Value;
                        result[dateKey].Qty += data.Qty;
                        result[dateKey].Count += data.Count;
                    }
                }
            }
        }
        return result;
    }

    public static List<string> GetTopKeys(IEnumerable<YearShard> shards,
        Func<YearShard, Dictionary<string, Dictionary<int, MonthlyData>>> selector, int count)
    {
        var totals = new Dictionary<string, double>();
        foreach (var shard in shards)
        {
            foreach (var (key, months) in selector(shard))
            {
                if (!totals.ContainsKey(key)) totals[key] = 0;
                totals[key] += months.Values.Sum(m => m.Value);
            }
        }
        return totals.OrderByDescending(kv => kv.Value).Take(count).Select(kv => kv.Key).ToList();
    }

    public static List<string> GetAllKeysWithMinMonths(IEnumerable<YearShard> shards,
        Func<YearShard, Dictionary<string, Dictionary<int, MonthlyData>>> selector, int minMonths)
    {
        // Count unique months per key across all shards
        var monthCounts = new Dictionary<string, HashSet<(int year, int month)>>();
        var totals = new Dictionary<string, double>();

        foreach (var shard in shards)
        {
            foreach (var (key, months) in selector(shard))
            {
                if (!monthCounts.ContainsKey(key))
                {
                    monthCounts[key] = new HashSet<(int, int)>();
                    totals[key] = 0;
                }
                foreach (var month in months.Keys)
                {
                    monthCounts[key].Add((shard.Year, month));
                }
                totals[key] += months.Values.Sum(m => m.Value);
            }
        }

        // Return keys with sufficient months, sorted by total value
        return monthCounts
            .Where(kv => kv.Value.Count >= minMonths)
            .OrderByDescending(kv => totals[kv.Key])
            .Select(kv => kv.Key)
            .ToList();
    }
}
