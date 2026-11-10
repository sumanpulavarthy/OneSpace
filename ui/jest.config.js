const { createCjsPreset } = require('jest-preset-angular/presets');

module.exports = {
  ...createCjsPreset(),
  setupFilesAfterEnv: ['<rootDir>/setup-jest.ts'],
  testEnvironment: 'jsdom',
  testPathIgnorePatterns: ['/node_modules/', '/e2e/', '/dist/'],
  coverageDirectory: '<rootDir>/../artifacts/coverage/ui',
  coverageReporters: ['cobertura', 'lcov', 'text-summary'],
  reporters: [
    'default',
    ['jest-junit', { outputDirectory: '<rootDir>/../artifacts/test-results/ui', outputName: 'junit.xml' }],
  ],
};
