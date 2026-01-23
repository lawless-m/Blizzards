/**
 * Scenario data model and adjustment application logic
 * Part of Phase 2: Scenario Builder UI
 */

/**
 * Create a new scenario
 * @param {string} name - Scenario name
 * @returns {Object} New scenario object
 */
export function createScenario(name) {
    return {
        id: crypto.randomUUID(),
        name: name,
        created: new Date().toISOString(),
        modified: new Date().toISOString(),
        adjustments: []
    };
}

/**
 * Create an adjustment
 * @param {string} type - Adjustment type: 'scale', 'remove', or 'new_business'
 * @param {Object} params - Parameters specific to adjustment type
 * @returns {Object} Adjustment object
 */
export function createAdjustment(type, params) {
    const base = {
        id: crypto.randomUUID(),
        type: type,
        note: params.note || ''
    };

    if (type === 'scale' || type === 'remove') {
        return {
            ...base,
            target_type: params.target_type,       // 'customer', 'product_group', or 'geography'
            target_key: params.target_key,         // Identifier (customer name, etc.)
            factor: type === 'remove' ? 0 : params.factor  // 0 for remove, scale factor for scale
        };
    } else if (type === 'new_business') {
        return {
            ...base,
            product_group: params.product_group,
            geography: params.geography,
            start_month: params.start_month,       // ISO format "2025-09"
            year1_value: params.year1_value,
            year2_value: params.year2_value,
            year3_value: params.year3_value
        };
    }

    throw new Error(`Unknown adjustment type: ${type}`);
}

/**
 * Apply all adjustments to baseline data
 * @param {Object} baselineData - Original baseline data from server
 * @param {Array} adjustments - Array of adjustment objects
 * @returns {Object} Adjusted data
 */
export function applyAdjustments(baselineData, adjustments) {
    // Deep copy to avoid mutating original
    let adjusted = JSON.parse(JSON.stringify(baselineData));

    for (const adj of adjustments) {
        if (adj.type === 'scale') {
            adjusted = applyScaleAdjustment(adjusted, adj);
        } else if (adj.type === 'remove') {
            adjusted = applyRemoveAdjustment(adjusted, adj);
        } else if (adj.type === 'new_business') {
            adjusted = applyNewBusinessAdjustment(adjusted, adj);
        }
    }

    return adjusted;
}

/**
 * Apply a scale adjustment to data
 * Multiplies historical values for a specific customer/product/geography by a factor
 * @param {Object} data - Current data
 * @param {Object} adj - Scale adjustment
 * @returns {Object} Modified data
 */
function applyScaleAdjustment(data, adj) {
    const { target_type, target_key, factor } = adj;

    console.log(`Applying scale adjustment: ${target_type}=${target_key}, factor=${factor}`);

    // Simplified implementation for Phase 3:
    // Apply the adjustment as a proportional scaling to the overall series
    // In production with full data breakdowns, this would:
    // 1. Identify which rows in historical data belong to the target
    // 2. Scale those specific rows
    // 3. Recalculate the overall total

    if (data.overall && data.overall.historical && data.overall.historical.rows) {
        // Estimate the impact: assume the target represents some portion of the total
        // For a customer, product, or geography adjustment, apply a proportional change
        // This is a simplified heuristic: (factor - 1) * assumed_contribution

        // For demonstration, assume the target represents 10% of total sales
        const assumedContribution = 0.10;
        const overallFactor = 1 + (factor - 1) * assumedContribution;

        // Apply the overall factor to all historical rows
        data.overall.historical.rows = data.overall.historical.rows.map(row => {
            return [row[0], row[1] * overallFactor];
        });

        console.log(`  Applied overall factor ${overallFactor.toFixed(3)} (target factor: ${factor})`);
    }

    // Store metadata for reference
    if (!data.adjustments) {
        data.adjustments = [];
    }
    data.adjustments.push({
        type: 'scale',
        target_type,
        target_key,
        factor
    });

    return data;
}

/**
 * Apply a remove adjustment (scale by 0)
 * @param {Object} data - Current data
 * @param {Object} adj - Remove adjustment
 * @returns {Object} Modified data
 */
function applyRemoveAdjustment(data, adj) {
    // Remove is just scaling by 0
    return applyScaleAdjustment(data, { ...adj, factor: 0 });
}

/**
 * Apply a new business adjustment
 * Creates a synthetic series with ramp profile based on year 1/2/3 values
 * @param {Object} data - Current data
 * @param {Object} adj - New business adjustment
 * @returns {Object} Modified data
 */
function applyNewBusinessAdjustment(data, adj) {
    const { product_group, geography, start_month, year1_value, year2_value, year3_value } = adj;

    console.log(`Applying new business: ${product_group} in ${geography}, starting ${start_month}`);

    // Parse start month
    const [startYear, startMonthNum] = start_month.split('-').map(Number);

    // Generate monthly values with ramp profile
    const monthlyValues = generateRampProfile(
        year1_value,
        year2_value,
        year3_value,
        startYear,
        startMonthNum
    );

    // Apply seasonal pattern (if we have product_group data to reference)
    const seasonalPattern = getSeasonalPattern(data, product_group, geography);
    const seasonalizedValues = applySeasonalPattern(monthlyValues, seasonalPattern, startMonthNum - 1);

    // Add the new business values to existing historical data
    if (data.overall && data.overall.historical && data.overall.historical.rows) {
        const rows = data.overall.historical.rows;

        // Parse the dates to find where to start adding new business
        // For simplicity in Phase 3, extend the historical series with new business values
        // In production, this would integrate more carefully with the timeline

        // Calculate the date range of existing data
        if (rows.length > 0) {
            const lastRow = rows[rows.length - 1];
            const [lastDate] = lastRow;
            const [lastYear, lastMonth] = lastDate.split('-').map(Number);

            // Add new business starting from the last historical month
            let currentYear = lastYear;
            let currentMonth = lastMonth;

            for (let i = 0; i < Math.min(seasonalizedValues.length, 12); i++) {
                currentMonth++;
                if (currentMonth > 12) {
                    currentMonth = 1;
                    currentYear++;
                }

                // Extend the historical series (this will affect the forecast)
                const newDate = `${currentYear}-${String(currentMonth).padStart(2, '0')}`;
                rows.push([newDate, seasonalizedValues[i]]);
            }

            console.log(`  Added ${Math.min(seasonalizedValues.length, 12)} months of new business data`);
        }
    }

    // Store metadata for reference
    if (!data.adjustments) {
        data.adjustments = [];
    }
    data.adjustments.push({
        type: 'new_business',
        product_group,
        geography,
        start_month,
        monthly_values: seasonalizedValues
    });

    return data;
}

/**
 * Generate ramp profile for new business
 * Distributes annual values across months with ramp-up curve
 * @param {number} year1 - Year 1 total
 * @param {number} year2 - Year 2 total
 * @param {number} year3 - Year 3 total
 * @param {number} startYear - Start year
 * @param {number} startMonth - Start month (1-12)
 * @returns {Array} Monthly values for 36 months
 */
function generateRampProfile(year1, year2, year3, startYear, startMonth) {
    const values = [];

    // Year 1: Linear ramp from 0 to target monthly average
    const year1Monthly = year1 / 12;
    const monthsInYear1 = 13 - startMonth; // Partial first year
    for (let i = 0; i < monthsInYear1; i++) {
        // Ramp from 50% to 100% of target over first year
        const rampFactor = 0.5 + (0.5 * i / monthsInYear1);
        values.push(year1Monthly * rampFactor);
    }

    // Year 2: Full year at target
    const year2Monthly = year2 / 12;
    for (let i = 0; i < 12; i++) {
        values.push(year2Monthly);
    }

    // Year 3: Full year at higher target
    const year3Monthly = year3 / 12;
    const remainingMonths = 36 - values.length;
    for (let i = 0; i < remainingMonths; i++) {
        values.push(year3Monthly);
    }

    return values;
}

/**
 * Get seasonal pattern from existing data for a product/geography combination
 * @param {Object} data - Baseline data
 * @param {string} productGroup - Product group code
 * @param {string} geography - Geography code
 * @returns {Array} 12-element seasonal factor array
 */
function getSeasonalPattern(data, productGroup, geography) {
    // Try to find matching historical data to extract seasonal pattern
    // For now, return default pattern (1.0 for all months)
    // In production, this would analyze historical data for similar products

    // Check if we have product-level data with seasonal factors
    if (data.products && data.products[productGroup]) {
        const productData = data.products[productGroup];
        if (productData.seasonal_factors) {
            return productData.seasonal_factors;
        }
    }

    // Default: no seasonality (all 1.0)
    return Array(12).fill(1.0);
}

/**
 * Apply seasonal pattern to monthly values
 * @param {Array} values - Monthly values
 * @param {Array} pattern - 12-element seasonal factor array
 * @param {number} startMonth - Starting month index (0-11)
 * @returns {Array} Seasonalized values
 */
function applySeasonalPattern(values, pattern, startMonth) {
    return values.map((value, index) => {
        const monthIndex = (startMonth + index) % 12;
        return value * pattern[monthIndex];
    });
}

/**
 * Get adjustment summary for display
 * @param {Object} adjustment - Adjustment object
 * @returns {string} Human-readable summary
 */
export function getAdjustmentSummary(adjustment) {
    if (adjustment.type === 'scale') {
        const percent = (adjustment.factor * 100).toFixed(0);
        return `Scale ${adjustment.target_key} to ${percent}%`;
    } else if (adjustment.type === 'remove') {
        return `Remove ${adjustment.target_key}`;
    } else if (adjustment.type === 'new_business') {
        const year1k = (adjustment.year1_value / 1000).toFixed(0);
        return `New business: ${adjustment.product_group} in ${adjustment.geography} (Y1: £${year1k}k)`;
    }
    return 'Unknown adjustment';
}

/**
 * Validate adjustment parameters
 * @param {string} type - Adjustment type
 * @param {Object} params - Parameters to validate
 * @returns {Object} { valid: boolean, errors: string[] }
 */
export function validateAdjustment(type, params) {
    const errors = [];

    if (type === 'scale') {
        if (!params.target_type) errors.push('Target type is required');
        if (!params.target_key) errors.push('Target selection is required');
        if (params.factor === undefined || params.factor < 0) {
            errors.push('Scale factor must be non-negative');
        }
    } else if (type === 'remove') {
        if (!params.target_type) errors.push('Target type is required');
        if (!params.target_key) errors.push('Target selection is required');
    } else if (type === 'new_business') {
        if (!params.product_group) errors.push('Product group is required');
        if (!params.geography) errors.push('Geography is required');
        if (!params.start_month) errors.push('Start month is required');
        if (!params.year1_value || params.year1_value <= 0) {
            errors.push('Year 1 value must be positive');
        }
        if (!params.year2_value || params.year2_value <= 0) {
            errors.push('Year 2 value must be positive');
        }
        if (!params.year3_value || params.year3_value <= 0) {
            errors.push('Year 3 value must be positive');
        }
    } else {
        errors.push(`Unknown adjustment type: ${type}`);
    }

    return {
        valid: errors.length === 0,
        errors
    };
}
