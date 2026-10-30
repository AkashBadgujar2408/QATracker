import { defineConfig } from 'vitest/config';

// Loaded by `ng test` (angular.json runnerConfig). The first test of a file also pays for the jsdom set-up, which
// passed the 5 s default on a busy machine and failed random tests.
export default defineConfig({
  test: {
    testTimeout: 15_000,
  },
});
