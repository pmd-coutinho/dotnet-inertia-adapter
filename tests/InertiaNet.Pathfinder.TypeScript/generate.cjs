const { spawnSync } = require('node:child_process')
const path = require('node:path')
for (const variant of [
  { directory: '', flags: [] },
  { directory: 'without-models', flags: ['--models', 'false'] },
  { directory: 'without-enums', flags: ['--enums', 'false'] },
  { directory: 'routes-only', flags: ['--models', 'false', '--enums', 'false', '--props', 'false', '--wolverine', 'false'] },
  { directory: 'bigint', flags: ['--contracts', path.join(__dirname, 'fixtures/bigint-contracts.json')] },
]) {
  const result = spawnSync('dotnet', [
    'run', ...(variant.directory ? ['--no-build'] : []), '--framework', 'net10.0', '--project', path.resolve(__dirname, '../../src/InertiaNet.Pathfinder'),
    '--', '--project', path.resolve(__dirname, 'fixtures'),
    '--output', path.resolve(__dirname, 'generated', variant.directory), '--quiet', ...variant.flags,
  ], { stdio: 'inherit', env: { ...process.env, DOTNET_ROLL_FORWARD: 'Major' } })
  if (result.error) throw result.error
  if (result.status !== 0) process.exit(result.status ?? 1)
}
