# Blizzard WASM Scenario Builder - Project Review

**Reviewer**: Claude Code
**Date**: 2026-01-22
**Branch**: claude/review-project-spec-Cr18N
**Status**: Specification Review Complete

---

## Executive Summary

This project aims to extend an existing sales forecasting system with a client-side scenario builder powered by WebAssembly. The core value proposition is combining statistical baseline forecasts (ARIMA) with sales team domain knowledge to create more accurate "what if" scenarios.

**Complexity Level**: High
**Estimated Scope**: 3 phases, ~6-8 weeks of development
**Primary Risk**: Mathematical accuracy - WASM must produce identical results to C# implementation

---

## Project Overview

### Goals

1. **Phase 1**: Port C# ARIMA(2,1,1) implementation to Rust/WASM with exact replication
2. **Phase 2**: Build browser-based scenario builder UI
3. **Phase 3**: Integrate with existing dashboard, add offline support

### Key Features

- **WASM-powered forecasting**: Run complex statistical models entirely in browser
- **Scenario adjustments**: Scale customers, remove customers, add new business
- **Offline capability**: Cache baseline data in IndexedDB
- **Real-time recalculation**: Instant feedback without server round-trips

---

## Technical Architecture Review

### Core Components

```
┌─────────────────────────────────────────────────────┐
│ Browser                                             │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────┐ │
│  │ Scenario UI  │  │  D3 Charts   │  │ IndexedDB │ │
│  └──────┬───────┘  └──────▲───────┘  └─────▲─────┘ │
│         │                 │                 │       │
│         └─────────────────┴─────────────────┘       │
│                           │                         │
│         ┌─────────────────┴──────────────────┐      │
│         │  Rust WASM ARIMA Module            │      │
│         │  - Yule-Walker / Levinson-Durbin   │      │
│         │  - Seasonal decomposition          │      │
│         │  - Easter regressor (ARIMAX)       │      │
│         └────────────────────────────────────┘      │
└─────────────────────────────────────────────────────┘
```

### Technology Stack

| Component | Technology | Justification |
|-----------|-----------|---------------|
| Forecasting Engine | Rust + WASM | Performance, memory safety, exact math control |
| UI Framework | Vanilla JS + D3 | Existing dashboard uses this, minimal dependencies |
| Storage | IndexedDB | Handles large datasets, offline support |
| Build Tool | wasm-pack | Standard Rust→WASM toolchain |
| Serialization | JSON | Simple WASM boundary, human-readable |

---

## Phase 1: ARIMA Port - Detailed Analysis

### Success Criteria

✅ **Primary**: WASM forecasts match C# within 0.01 absolute error
✅ **Secondary**: WASM binary < 500KB
✅ **Validation**: Easter coefficient estimation within 20% on synthetic data

### Implementation Complexity

| Component | Complexity | Risk Level | Notes |
|-----------|-----------|------------|-------|
| Easter calculation | Low | ✅ Low | Algorithm well-documented (Computus) |
| Seasonal decomposition | Medium | ⚠️ Medium | Multiplicative method, need exact replication |
| Yule-Walker solver | High | 🔴 High | Levinson-Durbin algorithm, numerical stability critical |
| MA estimation | Medium | ⚠️ Medium | Simplified approach from residual autocorrelation |
| Confidence intervals | Medium | ⚠️ Medium | Horizon-adjusted standard errors |

### Mathematical Pipeline (11 Steps)

The specification provides excellent detail on the ARIMA pipeline:

1. **Regress out Easter effect** - Mean-difference approach for sparse binary regressor
2. **Calculate seasonal factors** - Multiplicative, 12 monthly factors
3. **Deseasonalize** - Divide by factors
4. **Apply differencing** - First-order (d=1)
5. **Estimate AR coefficients** - Yule-Walker via Levinson-Durbin
6. **Calculate residuals** - Subtract AR predictions
7. **Estimate MA coefficients** - Simplified from residual autocorrelation
8. **Generate forecasts** - Predict in differenced space
9. **Undifference** - Cumulative sum
10. **Reseasonalize** - Multiply by factors
11. **Add back Easter** - For forecast months with Easter

**Assessment**: The spec provides pseudo-code for each step. This is well-structured and implementable.

### Critical Code Path: Levinson-Durbin Algorithm

```rust
// From ARIMA_ALGORITHM.md lines 298-339
fn solve_yule_walker(autocorr: &[f64]) -> Vec<f64> {
    // This is the most numerically sensitive part
    // Key risks:
    // 1. Division by near-zero v (variance)
    // 2. Reflection coefficient stability
    // 3. Matching C# floating-point behavior exactly
}
```

**Recommendation**:
- Implement comprehensive unit tests with known AR(2) processes
- Compare intermediate values (not just final output) with C# debug output
- Consider fixed-point arithmetic if float precision causes divergence

### Validation Strategy

The spec includes excellent validation guidance:

1. **Synthetic data test** (lines 548-580):
   - Known Easter coefficient (500.0)
   - 7 years of data with trend + seasonality + noise
   - Estimate should be within 20%

2. **Real data comparison**:
   - Run C# code to generate test fixtures
   - Use same inputs in Rust
   - Assert outputs match within 1e-6

**Assessment**: Validation strategy is sound. The synthetic test is particularly valuable for debugging Easter coefficient estimation.

---

## Phase 2: Scenario Builder UI - Detailed Analysis

### Data Model

The scenario data model is well-designed:

```typescript
interface Scenario {
  id: string;                    // UUID
  name: string;                  // User-friendly name
  created: string;               // ISO timestamp
  modified: string;
  adjustments: Adjustment[];
}

interface Adjustment {
  type: 'scale' | 'remove' | 'new_business';

  // Scale/Remove
  target_type?: 'customer' | 'product_group' | 'geography';
  target_key?: string;
  factor?: number;               // 0 = remove, 1.15 = +15%

  // New Business
  product_group?: string;
  geography?: string;
  start_month?: string;          // ISO month "2025-09"
  year1_value?: number;
  year2_value?: number;
  year3_value?: number;

  note: string;                  // Business justification
}
```

**Assessment**:
- ✅ Clear separation of adjustment types
- ✅ Flexible enough for common scenarios
- ✅ Includes business context (note field)
- ⚠️ May need validation rules (e.g., factor >= 0)

### UI Complexity Assessment

| Feature | Complexity | Dependencies |
|---------|-----------|-------------|
| Scenario CRUD | Low | IndexedDB wrapper |
| Adjustment list/edit | Medium | Form validation, cascading dropdowns |
| New business ramp | High | Seasonal pattern interpolation |
| Comparison chart | Medium | D3.js (already in stack) |
| Search/autocomplete | Medium | Customer/product lists from baseline data |

### New Business Ramp Profile

This is the most complex scenario type:

```
Year 1: £50k  → Monthly profile based on product seasonality, ramping up
Year 2: £100k → Full year with seasonal pattern
Year 3: £150k → Growth continues
```

**Implementation approach** (lines 160-174):
1. Take year 1/2/3 annual totals from user
2. Apply seasonal pattern from similar existing products in that geography
3. Interpolate monthly values with ramp-up curve
4. Add to baseline data before running ARIMA

**Risks**:
- Finding "similar products" - needs business logic to define similarity
- Ramp-up curve shape - linear? sigmoid? needs product owner input
- Edge case: What if no similar products exist in that geography?

**Recommendation**: Start with simple linear ramp, add sophistication later based on user feedback.

---

## Phase 3: Integration - Detailed Analysis

### Offline Support Architecture

```
Initial Load:
  1. Fetch from /cgi-bin/blizzard
  2. Store in IndexedDB with Last-Modified timestamp
  3. Cache WASM module (browser handles this)

Subsequent Loads:
  1. Read from IndexedDB
  2. HEAD request to check Last-Modified
  3. Refresh if stale

Fully Offline:
  1. Work from IndexedDB cache
  2. All ARIMA runs client-side
  3. Scenarios persist locally
```

**Assessment**:
- ✅ Solid progressive enhancement strategy
- ✅ Graceful degradation when offline
- ⚠️ Need to handle cache invalidation edge cases

### Integration with Existing Dashboard

The spec shows the existing dashboard has 4 tabs:
- Overall
- Products
- Customers
- Geography

New: **Scenarios** tab (5th tab)

**Concerns**:
1. How much of `blizzard.html` needs modification?
2. Does the existing D3 charting code need to be refactored?
3. Are there shared state management patterns to follow?

**Recommendation**:
- Extract existing blizzard.html and study its structure (included in reference/)
- Keep scenario builder as isolated module with clear API boundary
- Use event-driven architecture to minimize coupling

---

## Risk Assessment

### High Risks 🔴

1. **Mathematical accuracy divergence**
   - **Impact**: If Rust WASM doesn't match C# exactly, users will lose trust
   - **Mitigation**: Comprehensive test fixtures, compare intermediate values, consider bit-exact float operations
   - **Contingency**: If exact match impossible, document acceptable tolerance and validate business impact

2. **Performance with large datasets**
   - **Impact**: ARIMA on 7+ years of monthly data (84+ points) in browser
   - **Mitigation**: Profile early, optimize hot paths, consider Web Workers for non-blocking
   - **Contingency**: Add progress indicators, consider server-side option for very large datasets

### Medium Risks ⚠️

3. **IndexedDB browser compatibility**
   - **Impact**: Older browsers, private mode, storage limits
   - **Mitigation**: Feature detection, graceful degradation, clear error messages
   - **Contingency**: Fall back to localStorage for metadata, server-side for large data

4. **Scenario UX complexity**
   - **Impact**: Sales team finds UI too complex, doesn't adopt feature
   - **Mitigation**: User testing early, progressive disclosure, guided workflows
   - **Contingency**: Simplify to "preset scenarios" if custom building proves too complex

5. **WASM binary size**
   - **Impact**: Slow initial load on poor connections
   - **Mitigation**: Aggressive optimization (opt-level="s", LTO), gzip compression
   - **Contingency**: Lazy-load WASM only when Scenarios tab opened

### Low Risks ✅

6. **Easter coefficient estimation accuracy**
   - Well-documented algorithm, synthetic test included

7. **Deployment complexity**
   - Simple static file deployment alongside existing dashboard

---

## Implementation Roadmap

### Phase 1: ARIMA Port (Weeks 1-3)

#### Week 1: Foundation
- [ ] Set up Rust project with wasm-pack
- [ ] Implement Easter calculation (already scaffolded)
- [ ] Implement helper functions: mean, autocorrelation, difference
- [ ] Write unit tests for each helper

#### Week 2: Core ARIMA
- [ ] Implement seasonal decomposition
- [ ] Implement Levinson-Durbin (Yule-Walker solver)
- [ ] Implement MA coefficient estimation
- [ ] Implement forecast generation

#### Week 3: Validation
- [ ] Port C# synthetic data test
- [ ] Generate real test fixtures from C# code
- [ ] Validate exact match (within tolerance)
- [ ] Document any divergences and root causes

**Milestone**: WASM module produces forecasts matching C# within 0.01

### Phase 2: Scenario Builder (Weeks 4-5)

#### Week 4: Data Layer
- [ ] IndexedDB wrapper for scenarios and baseline data
- [ ] Scenario CRUD operations
- [ ] Baseline data caching with staleness checks

#### Week 5: UI
- [ ] Add Scenarios tab to dashboard
- [ ] Implement adjustment list/edit UI
- [ ] Build scale/remove adjustment types
- [ ] Build new business adjustment with simple linear ramp
- [ ] Comparison chart (baseline vs scenario)

**Milestone**: User can create scenarios, apply adjustments, see results

### Phase 3: Integration & Polish (Weeks 6-7)

#### Week 6: Integration
- [ ] Wire up WASM forecasting to scenario adjustments
- [ ] Implement offline mode
- [ ] Cache management and invalidation
- [ ] Performance profiling and optimization

#### Week 7: Testing & Documentation
- [ ] End-to-end testing with real data
- [ ] User acceptance testing with sales team
- [ ] Documentation for users and developers
- [ ] Deployment to production

**Milestone**: Feature deployed, sales team trained

### Buffer Week 8: Contingency
- Address discovered issues
- Performance tuning
- UX refinements based on early feedback

---

## Technical Recommendations

### 1. Start with Exact Replication
Don't optimize or "improve" the ARIMA algorithm initially. Match the C# code exactly, even if it seems suboptimal. Optimize only after validation passes.

### 2. Test Fixtures from C# Code
Run the C# implementation to generate JSON test fixtures:
```bash
dotnet run --validate > test_fixtures.json
```
Use these for regression testing the Rust port.

### 3. Incremental Integration
Don't wait until Phase 3 to integrate. Add scenario tab early (even if non-functional) to validate HTML/CSS/JS integration patterns.

### 4. Progressive Enhancement
Build the scenario builder to enhance the existing dashboard, not replace it. If WASM fails to load, the baseline forecast should still work.

### 5. Performance Budget
- Initial load (with WASM): < 2 seconds on 3G
- Scenario recalculation: < 500ms for typical dataset
- WASM binary: < 500KB gzipped

### 6. Code Organization
```
blizzard-wasm/
├── Cargo.toml
├── src/
│   ├── lib.rs              # WASM entry points
│   ├── arima.rs            # Core ARIMA implementation
│   ├── easter.rs           # Easter calculation
│   ├── seasonal.rs         # Seasonal decomposition
│   ├── stats.rs            # Statistical helpers
│   └── types.rs            # Data structures
├── tests/
│   ├── validation.rs       # Compare against C# outputs
│   ├── unit_tests.rs       # Unit tests for components
│   └── fixtures/           # Test data from C# implementation
│       ├── input_001.json
│       ├── output_001.json
│       └── ...
└── web/
    ├── index.html          # Updated blizzard.html
    ├── scenario-builder.js # Scenario UI module
    ├── indexeddb-cache.js  # Storage wrapper
    └── styles.css          # Scenario-specific styles
```

---

## Questions for Product Owner / Stakeholders

### Business Logic
1. **New business ramp curve**: Linear, sigmoid, or custom profile?
2. **Similar product matching**: What defines "similar" for seasonal pattern borrowing?
3. **Scenario approval workflow**: Do scenarios need review/approval before use in planning?
4. **Historical what-if**: Do users need to backtest scenarios (apply to historical periods)?

### Technical Constraints
5. **Browser support**: What's the minimum browser version? (affects WASM, IndexedDB features)
6. **Data size limits**: What's the maximum dataset size to support? (affects caching strategy)
7. **Concurrent users**: How many sales people will use this simultaneously? (affects server load for baseline data)

### Deployment
8. **Deployment schedule**: Can this be deployed incrementally (Phase 1, then 2, then 3)?
9. **Training plan**: Who will train the sales team on the new feature?
10. **Rollback plan**: If adoption is low, can we disable the Scenarios tab without affecting baseline forecasts?

---

## Conclusion

### Strengths of the Specification

✅ **Excellent detail on ARIMA algorithm** - Pseudo-code provided for all steps
✅ **Clear validation strategy** - Synthetic and real test cases defined
✅ **Well-designed data model** - Scenario/Adjustment structure is flexible
✅ **Realistic scope** - Phased approach allows incremental delivery
✅ **Reference implementation** - C# code available for comparison

### Areas Needing Clarification

⚠️ **New business ramp algorithm** - Needs product owner input on curve shape
⚠️ **Existing dashboard integration** - Need to study blizzard.html structure
⚠️ **Performance requirements** - No specific SLAs defined
⚠️ **Browser support matrix** - Minimum versions not specified

### Recommendation

**Proceed with implementation** with the following approach:

1. **Phase 1 first, validate thoroughly** - Don't move to Phase 2 until ARIMA port is proven accurate
2. **Build scenario UI in parallel with Phase 1** - But don't integrate until Phase 1 complete
3. **User testing early and often** - Sales team should see prototypes in Week 3-4
4. **Document divergences** - If exact C# match is impossible, document why and impact

### Success Metrics

- **Technical**: WASM forecasts match C# within 0.01 error
- **Performance**: Scenario recalculation < 500ms
- **Business**: Sales team creates 10+ scenarios per quarter
- **Adoption**: 80% of forecasts use at least one scenario adjustment

---

## Next Steps

1. **Review this document** with project stakeholders
2. **Answer open questions** (see "Questions for Product Owner" section)
3. **Set up development environment** (Rust, wasm-pack, etc.)
4. **Extract and study** existing blizzard.html to understand integration points
5. **Begin Phase 1 implementation** starting with Easter calculation and helpers

---

**Prepared by**: Claude Code
**Review Date**: 2026-01-22
**Specification Version**: As provided in blizzard-wasm-spec.zip
**Status**: ✅ Ready for Development
