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
const projectDir = __dirname;
const rootDir = join(projectDir, '..', '..', '..');

const isRelease = process.argv.includes('--release');
const configuration = isRelease ? 'Release' : 'Debug';

console.log(`Building Kilo Visual Studio extension (${configuration})...`);

// Step 1: Copy webview files from source (no CLI or webview builds)
const webviewDir = join(projectDir, 'webview');
const webviewSourceDir = join(rootDir, 'packages', 'kilo-vscode', 'dist');

// Clean existing webview files (except index.html and vscode-api.js)
if (existsSync(webviewDir)) {
  const files = readdirSync(webviewDir);
  for (const file of files) {
    if (file !== 'index.html' && file !== 'vscode-api.js' && file !== 'vscode-theme.css') {
      const filePath = join(webviewDir, file);
      rmSync(filePath, { recursive: true, force: true });
    }
  }
} else {
  mkdirSync(webviewDir, { recursive: true });
}

// Copy webview.js from VS Code dist if available
const webviewJsSource = join(webviewSourceDir, 'webview.js');
if (existsSync(webviewJsSource)) {
  console.log('Copying webview.js from VS Code extension build...');
  copyFileSync(webviewJsSource, join(webviewDir, 'webview.js'));
  
  // Copy sourcemap if available
  const sourcemapSource = join(webviewSourceDir, 'webview.js.map');
  if (existsSync(sourcemapSource)) {
    copyFileSync(sourcemapSource, join(webviewDir, 'webview.js.map'));
  }
  
  // Copy CSS file
  const cssSource = join(webviewSourceDir, 'webview.css');
  const cssTarget = join(webviewDir, 'webview.css');
  if (existsSync(cssSource)) {
    copyFileSync(cssSource, cssTarget);
    const cssMapSource = join(webviewSourceDir, 'webview.css.map');
    if (existsSync(cssMapSource)) {
      copyFileSync(cssMapSource, join(webviewDir, 'webview.css.map'));
    }
  }
  
  // Copy KaTeX fonts from VS Code dist (they're in the root dist folder, not a fonts subfolder)
  const fontFiles = readdirSync(webviewSourceDir).filter(f => f.startsWith('KaTeX_'));
  for (const file of fontFiles) {
    copyFileSync(join(webviewSourceDir, file), join(webviewDir, file));
  }
  
  console.log('Webview files copied successfully');
} else {
  console.log('⚠️  No VS Code webview build found at', webviewJsSource);
  console.log('   Webview will not be available until VS Code extension is built.');
}

// Copy assets folder (icons) from VS Code extension
const assetsSourceDir = join(rootDir, 'packages', 'kilo-vscode', 'assets');
const assetsTargetDir = join(webviewDir, 'assets');
if (existsSync(assetsSourceDir)) {
  console.log('Copying assets folder from VS Code extension...');
  if (!existsSync(assetsTargetDir)) {
    mkdirSync(assetsTargetDir, { recursive: true });
  }
  
  const iconSourceDir = join(assetsSourceDir, 'icons');
  const iconTargetDir = join(assetsTargetDir, 'icons');
  if (existsSync(iconSourceDir)) {
    if (!existsSync(iconTargetDir)) {
      mkdirSync(iconTargetDir, { recursive: true });
    }
    const iconFiles = readdirSync(iconSourceDir);
    for (const file of iconFiles) {
      copyFileSync(join(iconSourceDir, file), join(iconTargetDir, file));
    }
    console.log('Assets copied successfully');
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
