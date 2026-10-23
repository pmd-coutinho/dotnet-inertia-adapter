const fs = require('node:fs')
const os = require('node:os')
const path = require('node:path')
const { spawnSync } = require('node:child_process')
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'pathfinder-tsconfigs-'))
const compiler = path.join(__dirname, 'node_modules/typescript/bin/tsc')
function check(config) {
  const result = spawnSync(process.execPath, [compiler, '--noEmit', '-p', config], { stdio: 'inherit' })
  if (result.error) throw result.error
  if (result.status !== 0) throw new Error(`Type check failed: ${config}`)
}
try {
  check(path.join(__dirname, 'tsconfig.json'))
  // Each feature profile is a separate application. Their distinct core module
  // augmentations must be checked in separate TypeScript programs.
  for (const variant of ['without-models', 'without-enums', 'routes-only', 'bigint']) {
    const config = path.join(root, `${variant}.json`)
    fs.writeFileSync(config, JSON.stringify({
      extends: path.join(__dirname, 'tsconfig.json'),
      include: [path.join(__dirname, 'generated', variant, '**/*.ts'), ...(variant === 'bigint' ? [path.join(__dirname, 'contract-bigint.ts')] : [])], exclude: [],
    }))
    check(config)
  }
} finally {
  fs.rmSync(root, { recursive: true, force: true })
}
