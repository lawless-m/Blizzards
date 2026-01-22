# Scenario Examples

This document shows example scenarios and their expected behavior.

## Example 1: Scale Existing Customer

**Scenario:** "ACME Corp is losing shelf space to a competitor"

**Adjustment:**
```json
{
  "type": "scale",
  "target_type": "customer",
  "target_key": "ACME Corporation (AC001)",
  "factor": 0.85,
  "note": "Losing shelf space to competitor"
}
```

**Effect:**
- Find all historical data for customer "ACME Corporation (AC001)"
- Multiply all values by 0.85 (15% reduction)
- Re-run ARIMA forecast with modified data
- The forecast will show reduced contribution from this customer

**Before/After:**
```
Customer: ACME Corporation
Historical monthly average: £50,000
Adjusted monthly average: £42,500

Overall forecast impact:
- Baseline 2026: £2,400,000
- Scenario 2026: £2,340,000 (-2.5%)
```

## Example 2: Remove Customer

**Scenario:** "We're losing BigRetail entirely"

**Adjustment:**
```json
{
  "type": "scale",
  "target_type": "customer",
  "target_key": "BigRetail Ltd (BR002)",
  "factor": 0.0,
  "note": "Contract terminated, switching to competitor"
}
```

**Effect:**
- Same as scale, but factor=0 removes all contribution
- Historical data for this customer becomes zeros
- Forecast reflects complete loss

## Example 3: New Business - Chilled in Middle East

**Scenario:** "Dubai distributor confirmed for Chilled products"

**Adjustment:**
```json
{
  "type": "new_business",
  "product_group": "S5",
  "geography": "MEAEDU",
  "start_month": "2025-09",
  "year1_value": 50000,
  "year2_value": 100000,
  "year3_value": 150000,
  "note": "Dubai distributor confirmed"
}
```

**How the system generates monthly values:**

1. **Get seasonal profile:** Look up existing Chilled (S5) sales in similar geography (e.g., UK) to get monthly distribution:
   ```
   Jan: 6%   Apr: 10%  Jul: 8%   Oct: 9%
   Feb: 7%   May: 11%  Aug: 7%   Nov: 8%
   Mar: 9%   Jun: 9%   Sep: 8%   Dec: 8%
   ```

2. **Apply ramp-up curve:** First year ramps up over 4 months:
   ```
   Year 1 (Sep-Aug): 
   - Sep-Dec 2025: 20% of Y1 (ramping)
   - Jan-Aug 2026: 80% of Y1 (full)
   
   Year 2: Full £100k
   Year 3: Full £150k
   ```

3. **Generate monthly values:**
   ```
   Sep 2025: £2,000  (ramp-up, 8% of £10k)
   Oct 2025: £2,700  (ramp-up, 9% of £10k)
   Nov 2025: £2,400  (ramp-up, 8% of £10k)
   Dec 2025: £2,400  (ramp-up, 8% of £10k)
   Jan 2026: £4,800  (full, 6% of £40k)
   Feb 2026: £5,600  (full, 7% of £40k)
   ... etc
   ```

4. **Add to baseline:** These values are added to the overall forecast

**Visual representation:**
```
£k
│
│                                    ┌─ Year 3
150│                              ╭────
│                            ╭──╯
100│                      ╭────╯
│                  ╭────╯      Year 2
50│            ╭────╯
│      ╭───────╯  Year 1
│  ────╯
0└──────┬─────┬─────┬─────┬─────┬─────────
    2025  │    2026  │    2027  │    2028
          Sep       Jan       Jan
          ↑
          Start
```

## Example 4: Product Group Growth

**Scenario:** "Organic range expanding, expect 20% growth"

**Adjustment:**
```json
{
  "type": "scale",
  "target_type": "product_group",
  "target_key": "S520",
  "factor": 1.20,
  "note": "Organic range expansion in major retailers"
}
```

**Effect:**
- All historical sales for product group S520 multiplied by 1.20
- ARIMA sees higher baseline, projects proportionally higher future

## Example 5: Geography Contraction

**Scenario:** "European distribution restructure - losing German region"

**Adjustment:**
```json
{
  "type": "scale",
  "target_type": "geography",
  "target_key": "EUDEDE",
  "factor": 0.0,
  "note": "German distributor contract not renewed"
}
```

## Example 6: Combined Scenario

**Scenario:** "Conservative 2026 outlook"

Multiple adjustments:
```json
{
  "name": "Conservative 2026",
  "adjustments": [
    {
      "type": "scale",
      "target_type": "customer",
      "target_key": "ACME Corporation (AC001)",
      "factor": 0.85,
      "note": "Competitive pressure"
    },
    {
      "type": "scale",
      "target_type": "product_group",
      "target_key": "S52020",
      "factor": 0.90,
      "note": "Raw material cost increases"
    }
  ]
}
```

**Processing order:**
1. Apply all adjustments to baseline data
2. Adjustments to same data points multiply together
3. Run single ARIMA forecast on combined adjusted data

## Scenario Comparison Display

When viewing a scenario, show both forecasts overlaid:

```
£k
│
│                              ╭──── Baseline
400│                         ╭──╯
│                      ╭──╯────── Scenario
350│                 ╭──╯   ╭──╯
│              ╭──╯  ╭──╯
300│         ╭──╯╭──╯
│      ╭──╯╭──╯
250│  ────╯╯
│  ════════════════════════════════════
200│  ▪ Historical
│
150└─┬─────┬─────┬─────┬─────┬─────┬─────
   2023  2024  2025  2026  2027
                     ↑
                     Forecast starts

Legend:
─── Baseline forecast
═══ Scenario forecast
▪▪▪ Historical data
```

## Summary Table Format

```
┌─────────────────────────────────────────────────────────────┐
│  Scenario: Conservative 2026                                │
├─────────────────────────────────────────────────────────────┤
│                    │ Baseline  │ Scenario  │ Difference    │
├────────────────────┼───────────┼───────────┼───────────────┤
│  2026 Total        │ £2.40M    │ £2.21M    │ -£190k (-8%)  │
│  2027 Total        │ £2.52M    │ £2.32M    │ -£200k (-8%)  │
│  2028 Total        │ £2.65M    │ £2.44M    │ -£210k (-8%)  │
└────────────────────┴───────────┴───────────┴───────────────┘

Adjustments Applied:
• ACME Corporation: -15% (competitive pressure)
• Product S52020: -10% (raw material costs)
```

## Edge Cases

### Adjustment to Non-Existent Target
If user tries to adjust a customer/product/geography that doesn't exist in baseline:
- Show warning: "No historical data found for [target]"
- Don't block scenario creation (might be intentional for sensitivity analysis)

### Overlapping Adjustments
If multiple adjustments affect same data:
- Scale factors multiply: 0.85 × 1.20 = 1.02
- Display warning: "Multiple adjustments affect the same data"

### New Business Start in Past
If start_month is before current date:
- Only add values from today forward
- Show warning: "Start date is in the past, values added from [current month]"

### Negative Forecast
If adjustments result in negative forecast values:
- Floor at zero
- Show warning: "Adjustments result in negative values, floored at zero"
