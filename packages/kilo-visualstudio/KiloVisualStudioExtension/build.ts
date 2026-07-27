#!/usr/bin/env bun

/**
 * Build script for Kilo Visual Studio extension.
 * 
 * Usage:
 *   bun run build.ts           # Development build
 *   bun run build.ts --release  # Release build
 */

import { execSync } from 'child_process';
import { existsSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const rootDir = join(__dirname, '..', '..');
const projectDir = join(__dirname);

const isRelease = process.argv.includes('--release');
const configuration = isRelease ? 'Release' : 'Debug';

console.log(`Building Kilo Visual Studio extension (${configuration})...`);

// Step 1: Build CLI backend if needed
const cliDistDir = join(rootDir, 'packages', 'opencode', 'dist');
if (!existsSync(cliDistDir)) {
  console.log('Building CLI backend...');
  try {
    execSync('bun run build', {
      cwd: join(rootDir, 'packages', 'opencode'),
      stdio: 'inherit',
      timeout: 300000
    });
  } catch (error) {
    console.warn('⚠️  CLI backend build failed. Continuing with placeholder...');
    console.warn('   To build the CLI manually, run: cd packages/opencode && bun run build');
  }
} else {
  console.log('CLI backend already built, skipping...');
}

// Step 2: Build webview (optional - uses existing build if available)
const webviewDir = join(projectDir, 'webview');
const storybookStatic = join(rootDir, 'packages', 'kilo-vscode', 'storybook-static');

if (existsSync(storybookStatic)) {
  console.log('Copying webview from storybook-static...');
  // In a real build, you'd copy files here
  // For now, the placeholder index.html is used
} else {
  console.log('No storybook build found, using placeholder webview...');
}

// Step 3: Build the VSIX
console.log(`Building VSIX (${configuration})...`);

try {
  execSync(`dotnet pack -c ${configuration}`, {
    cwd: projectDir,
    stdio: 'inherit'
  });
  
  const vsixPath = join(
    projectDir,
    'bin',
    configuration,
    `KiloVisualStudio.${isRelease ? '1.0.0' : '1.0.0-alpha'}.vsix`
  );
  
  if (existsSync(vsixPath)) {
    console.log(`\n✅ Build successful!`);
    console.log(`   VSIX: ${vsixPath}`);
    console.log(`\nTo install:`);
    console.log(`   1. Double-click the .vsix file`);
    console.log(`   2. Launch Visual Studio`);
    console.log(`   3. View > Other Windows > Kilo Code`);
  } else {
    console.log('⚠️  Build completed but VSIX not found at expected location');
  }
} catch (error) {
  console.error('❌ Build failed:', error);
  process.exit(1);
}
