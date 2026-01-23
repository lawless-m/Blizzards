/* tslint:disable */
/* eslint-disable */

/**
 * Main WASM entry point for forecasting
 *
 * Takes JSON input, returns JSON output.
 *
 * # Example
 *
 * ```javascript
 * const input = {
 *   series: [1000, 1200, 1100, ...],  // 60+ months of data
 *   start_year: 2019,
 *   start_month: 1,
 *   forecast_months: 12,
 *   use_easter_regressor: true
 * };
 *
 * const result = JSON.parse(forecast(JSON.stringify(input)));
 * console.log(result.forecast);  // [1500, 1600, ...]
 * ```
 */
export function forecast(input_json: string): string;

/**
 * Get Easter dates for a range of years (utility function)
 *
 * Returns JSON array of objects with year, easter_month, easter_day, invoice_month
 */
export function get_easter_dates(start_year: number, end_year: number): string;

/**
 * Version information
 */
export function version(): string;

export type InitInput = RequestInfo | URL | Response | BufferSource | WebAssembly.Module;

export interface InitOutput {
    readonly memory: WebAssembly.Memory;
    readonly forecast: (a: number, b: number) => [number, number];
    readonly get_easter_dates: (a: number, b: number) => [number, number];
    readonly version: () => [number, number];
    readonly __wbindgen_externrefs: WebAssembly.Table;
    readonly __wbindgen_malloc: (a: number, b: number) => number;
    readonly __wbindgen_realloc: (a: number, b: number, c: number, d: number) => number;
    readonly __wbindgen_free: (a: number, b: number, c: number) => void;
    readonly __wbindgen_start: () => void;
}

export type SyncInitInput = BufferSource | WebAssembly.Module;

/**
 * Instantiates the given `module`, which can either be bytes or
 * a precompiled `WebAssembly.Module`.
 *
 * @param {{ module: SyncInitInput }} module - Passing `SyncInitInput` directly is deprecated.
 *
 * @returns {InitOutput}
 */
export function initSync(module: { module: SyncInitInput } | SyncInitInput): InitOutput;

/**
 * If `module_or_path` is {RequestInfo} or {URL}, makes a request and
 * for everything else, calls `WebAssembly.instantiate` directly.
 *
 * @param {{ module_or_path: InitInput | Promise<InitInput> }} module_or_path - Passing `InitInput` directly is deprecated.
 *
 * @returns {Promise<InitOutput>}
 */
export default function __wbg_init (module_or_path?: { module_or_path: InitInput | Promise<InitInput> } | InitInput | Promise<InitInput>): Promise<InitOutput>;
