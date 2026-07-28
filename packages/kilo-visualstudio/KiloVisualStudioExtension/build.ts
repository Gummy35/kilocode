#!/usr/bin/env bun

/**
 * Build script for Kilo Visual Studio extension.
 * 
 * Usage:
 *   bun run build.ts           # Development build
 *   bun run build.ts --release  # Release build
 */

import { execSync } from 'child_process';
import { existsSync, copyFileSync, mkdirSync, readdirSync, rmSync } from 'fs';
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

// Step 2: Build webview from VS Code extension
const webviewDir = join(projectDir, 'webview');
const vscodeDistDir = join(rootDir, 'packages', 'kilo-vscode', 'dist');
const webviewJsSource = join(vscodeDistDir, 'webview.js');

// Clean existing webview files (except index.html template)
if (existsSync(webviewDir)) {
  const files = readdirSync(webviewDir);
  for (const file of files) {
    if (file !== 'index.html') {
      const filePath = join(webviewDir, file);
      rmSync(filePath, { recursive: true, force: true });
    }
  }
} else {
  mkdirSync(webviewDir, { recursive: true });
}

if (existsSync(webviewJsSource)) {
  console.log('Copying webview from VS Code extension build...');
  copyFileSync(webviewJsSource, join(webviewDir, 'webview.js'));
  
  // Copy sourcemap if available
  const sourcemapSource = join(vscodeDistDir, 'webview.js.map');
  if (existsSync(sourcemapSource)) {
    copyFileSync(sourcemapSource, join(webviewDir, 'webview.js.map'));
  }
  
  // Copy CSS file
  const cssSource = join(vscodeDistDir, 'webview.css');
  if (existsSync(cssSource)) {
    copyFileSync(cssSource, join(webviewDir, 'webview.css'));
    const cssMapSource = join(vscodeDistDir, 'webview.css.map');
    if (existsSync(cssMapSource)) {
      copyFileSync(cssMapSource, join(webviewDir, 'webview.css.map'));
    }
  }
  
  // Copy KaTeX fonts from VS Code dist
  const vscodeFontsDir = join(vscodeDistDir, 'fonts');
  if (existsSync(vscodeFontsDir)) {
    const fontFiles = readdirSync(vscodeFontsDir);
    for (const file of fontFiles) {
      copyFileSync(join(vscodeFontsDir, file), join(webviewDir, file));
    }
  }
  
  console.log('Webview copied successfully');
} else {
  console.log('⚠️  No VS Code webview build found. Running esbuild...');
  try {
    execSync('bun run esbuild', {
      cwd: join(rootDir, 'packages', 'kilo-vscode'),
      stdio: 'inherit'
    });
    
    if (existsSync(webviewJsSource)) {
      copyFileSync(webviewJsSource, join(webviewDir, 'webview.js'));
      const sourcemapSource = join(vscodeDistDir, 'webview.js.map');
      if (existsSync(sourcemapSource)) {
        copyFileSync(sourcemapSource, join(webviewDir, 'webview.js.map'));
      }
      console.log('Webview built and copied successfully');
    }
  } catch (error) {
    console.warn('⚠️  Webview build failed. Using placeholder webview...');
  }
}

// Step 3: Build the VSIX
console.log(`Building VSIX (${configuration})...`);

try {
  execSync(`dotnet build -c ${configuration}`, {
    cwd: projectDir,
    stdio: 'inherit'
  });
  
  const vsixPath = join(
    projectDir,
    'bin',
    configuration,
    'net481',
    'KiloVisualStudioExtension.vsix'
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
